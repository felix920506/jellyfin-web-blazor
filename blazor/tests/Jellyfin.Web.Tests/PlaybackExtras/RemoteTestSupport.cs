using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.PlaybackExtras;

/// <summary>Records what a remote control asks of the page.</summary>
internal sealed class FakeRemoteInput : IRemoteInput
{
    public List<string> Calls { get; } = [];

    public void MoveFocus(string direction) => Calls.Add("move " + direction);

    public void Select() => Calls.Add("select");

    public void SendText(string text) => Calls.Add("text " + text);
}

/// <summary>The other sessions of the scripted server, as <c>/Sessions</c> and the web socket describe them.</summary>
internal static class RemoteSessions
{
    public const string TvId = "5e5510000000000000000000000000a1";
    public const string PhoneId = "5e5510000000000000000000000000a2";

    /// <summary>What a Jellyfin Web session reports as its commands.</summary>
    public static readonly string[] WebCommands =
    [
        "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "PageUp", "PageDown", "PreviousLetter", "NextLetter", "ToggleOsd",
        "ToggleContextMenu", "Select", "Back", "SendKey", "SendString", "GoHome", "GoToSettings", "VolumeUp", "VolumeDown",
        "Mute", "Unmute", "ToggleMute", "SetVolume", "SetAudioStreamIndex", "SetSubtitleStreamIndex", "DisplayContent",
        "GoToSearch", "DisplayMessage", "SetRepeatMode", "SetShuffleQueue", "ChannelUp", "ChannelDown", "PlayMediaSource",
        "PlayTrailers",
    ];

    public static JsonObject Session(
        string id = TvId,
        string deviceName = "Living Room TV",
        string deviceId = "tv-device",
        string client = "Jellyfin Web",
        string[]? commands = null,
        string? deviceType = null) => new()
    {
        ["Id"] = id,
        ["DeviceId"] = deviceId,
        ["DeviceName"] = deviceName,
        ["DeviceType"] = deviceType,
        ["Client"] = client,
        ["UserId"] = PlaybackServer.UserId,
        ["UserName"] = "tester",
        ["PlayableMediaTypes"] = new JsonArray("Audio", "Video"),
        ["Capabilities"] = new JsonObject
        {
            ["PlayableMediaTypes"] = new JsonArray("Audio", "Video"),
            ["SupportedCommands"] = new JsonArray((commands ?? WebCommands).Select(c => (JsonNode?)c).ToArray()),
            ["SupportsMediaControl"] = true,
        },
        ["PlayState"] = new JsonObject { ["CanSeek"] = false, ["IsPaused"] = false, ["IsMuted"] = false, ["RepeatMode"] = "RepeatNone", ["PlaybackOrder"] = "Default" },
        ["NowPlayingQueue"] = new JsonArray(),
        ["LastActivityDate"] = "2026-01-01T10:00:00.0000000Z",
    };

    /// <summary>A session that plays <paramref name="item"/>; the queue entries get the playlist ids "playlistItem0", "playlistItem1", ...</summary>
    public static JsonObject Playing(
        BaseItemDto item,
        long positionTicks = 0,
        bool paused = false,
        int volume = 100,
        bool muted = false,
        string repeatMode = "RepeatNone",
        string playbackOrder = "Default",
        BaseItemDto[]? queue = null,
        JsonObject? session = null)
    {
        session ??= Session();
        queue ??= [item];
        var index = Math.Max(0, Array.FindIndex(queue, i => i.Id == item.Id));
        session["NowPlayingItem"] = PlaybackServer.ToJson(item);
        session["PlaylistItemId"] = "playlistItem" + index;
        session["NowPlayingQueue"] = new JsonArray(queue
            .Select((entry, i) => (JsonNode?)new JsonObject { ["Id"] = entry.Id!.Value.ToString("N"), ["PlaylistItemId"] = "playlistItem" + i })
            .ToArray());
        session["PlayState"] = new JsonObject
        {
            ["PositionTicks"] = positionTicks,
            ["CanSeek"] = true,
            ["IsPaused"] = paused,
            ["IsMuted"] = muted,
            ["VolumeLevel"] = volume,
            ["RepeatMode"] = repeatMode,
            ["PlaybackOrder"] = playbackOrder,
            ["PlayMethod"] = "DirectPlay",
            ["MediaSourceId"] = item.Id!.Value.ToString("N"),
        };
        return session;
    }

    /// <summary>Answers <c>GET /Sessions</c> with the sessions and accepts the commands sent to them.</summary>
    public static void Script(FakeHttp server, params JsonObject[] sessions)
    {
        server.OnGet("/Sessions", _ => new JsonArray(sessions.Select(s => s.DeepClone()).ToArray()).ToJsonString());
        foreach (var session in sessions)
        {
            var id = session["Id"]!.GetValue<string>();
            server.OnPost($"/Sessions/{id}/Playing");
            server.OnPost($"/Sessions/{id}/Command");
            foreach (var command in new[] { "PlayPause", "Pause", "Unpause", "stop", "nextTrack", "previousTrack", "seek" })
            {
                server.OnPost($"/Sessions/{id}/Playing/{command}");
            }
        }
    }

    /// <summary>Chooses a session as the play target, as the cast menu does.</summary>
    public static async Task<PlayTarget> ConnectAsync(PlaybackManager manager, string id = TvId)
    {
        var target = (await manager.GetTargetsAsync()).Single(t => t.Id == id);
        await manager.TrySetActivePlayerAsync(target.PlayerName, target);
        return target;
    }

    /// <summary>A "Sessions" message of the web socket.</summary>
    public static void Dispatch(IServiceProvider services, params JsonObject[] sessions) =>
        services.GetRequiredService<ServerEvents>().Dispatch("Sessions", Element(new JsonArray(sessions.Select(s => s.DeepClone()).ToArray())));

    public static JsonElement Element(JsonNode node) => JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();

    /// <summary>The requests sent to a session (play, play state and general commands), in order.</summary>
    public static List<RecordedRequest> Sent(FakeHttp server, string id = TvId)
    {
        lock (server.Requests)
        {
            return server.Requests
                .Where(r => r.Method == HttpMethod.Post && r.Path.Contains($"/Sessions/{id}/", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    /// <summary>The general commands sent to a session, as "Name" or "Name {arguments}".</summary>
    public static List<string> Commands(FakeHttp server, string id = TvId) =>
        Sent(server, id)
            .Where(r => r.Path.EndsWith("/Command", StringComparison.OrdinalIgnoreCase))
            .Select(r => JsonNode.Parse(r.Body ?? "{}")!.AsObject())
            .Select(c => c["Arguments"] is { } arguments ? $"{c["Name"]} {arguments.ToJsonString()}" : $"{c["Name"]}")
            .ToList();

    /// <summary>The play state commands sent to a session: the last path segment with its query.</summary>
    public static List<string> PlayStateCommands(FakeHttp server, string id = TvId) =>
        Sent(server, id)
            .Where(r => r.Path.Contains("/Playing/", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Path[(r.Path.LastIndexOf('/') + 1)..] + r.Uri.Query)
            .ToList();
}
