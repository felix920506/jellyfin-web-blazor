using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Entry for the browser tests (blazor/e2e/playback.py, through testCommand() of player.js): starts
/// playback and reads the manager's state. It exists because playback is otherwise only started from
/// item pages, which other workstreams own; everything else in the tests goes through the UI.
/// </summary>
[AppService]
public sealed class PlaybackTestHook(PlaybackManager manager) : IStartupTask
{
    private static PlaybackTestHook? _current;

    /// <summary>Runs a command; the result is JSON (or null). Called from JS only.</summary>
    [JSInvokable("PlaybackTestCommand")]
    public static async Task<string?> RunAsync(string name, JsonElement arguments)
    {
        if (_current is not { } hook)
        {
            return null;
        }

        try
        {
            return await hook.ExecuteAsync(name, arguments);
        }
        catch (Exception ex)
        {
            return new JsonObject { ["error"] = ex.ToString() }.ToJsonString();
        }
    }

    public Task StartAsync()
    {
        _current = this;
        return Task.CompletedTask;
    }

    internal async Task<string?> ExecuteAsync(string name, JsonElement arguments)
    {
        switch (name)
        {
            case "play":
                await manager.PlayAsync(ToPlayOptions(arguments));
                break;
            case "queue":
                await manager.QueueAsync(ToPlayOptions(arguments));
                break;
            case "queueNext":
                await manager.QueueNextAsync(ToPlayOptions(arguments));
                break;
            case "stop":
                await manager.StopAsync();
                break;
            case "seek":
                await manager.SeekAsync(Long(arguments, "ticks") ?? 0);
                break;
            case "setAudioStreamIndex":
                await manager.SetAudioStreamIndexAsync(Int(arguments, "index") ?? 0);
                break;
            case "setSubtitleStreamIndex":
                await manager.SetSubtitleStreamIndexAsync(Int(arguments, "index") ?? -1);
                break;
            case "setMaxStreamingBitrate":
                await manager.SetMaxStreamingBitrateAsync(enableAutomaticBitrateDetection: false, Int(arguments, "bitrate"));
                break;
            case "state":
                break;
            default:
                return new JsonObject { ["error"] = "unknown command " + name }.ToJsonString();
        }

        return State().ToJsonString();
    }

    private JsonObject State()
    {
        var streamInfo = manager.CurrentStreamInfo;
        return new JsonObject
        {
            ["isPlaying"] = manager.IsPlaying,
            ["isPaused"] = manager.IsPaused,
            ["player"] = manager.CurrentPlayer?.Id,
            ["mediaType"] = streamInfo?.MediaType,
            ["itemId"] = streamInfo?.Item.Id?.ToString("N"),
            ["itemName"] = streamInfo?.Item.Name,
            ["playMethod"] = streamInfo?.PlayMethod,
            ["url"] = streamInfo?.Url,
            ["mimeType"] = streamInfo?.MimeType,
            ["playSessionId"] = streamInfo?.PlaySessionId,
            ["mediaSourceId"] = streamInfo?.MediaSource?.Id,
            ["positionTicks"] = manager.CurrentTimeTicks,
            ["durationTicks"] = manager.DurationTicks,
            ["audioStreamIndex"] = manager.AudioStreamIndex,
            ["subtitleStreamIndex"] = manager.SubtitleStreamIndex,
            ["textTracks"] = streamInfo?.TextTracks.Count ?? 0,
            ["volume"] = manager.Volume,
            ["isMuted"] = manager.IsMuted,
            ["playbackRate"] = manager.PlaybackRate,
            ["repeatMode"] = manager.RepeatMode,
            ["shuffleMode"] = manager.ShuffleMode,
            ["maxStreamingBitrate"] = manager.GetMaxStreamingBitrate(),
            ["queueIndex"] = manager.CurrentPlaylistIndex,
            ["queue"] = new JsonArray(manager.GetPlaylist().Select(i => (JsonNode?)i.Name).ToArray()),
        };
    }

    private static PlayOptions ToPlayOptions(JsonElement arguments) => new()
    {
        Ids = arguments.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array
            ? ids.EnumerateArray().Select(i => i.GetString() ?? string.Empty).ToList()
            : null,
        StartPositionTicks = Long(arguments, "startPositionTicks"),
        StartIndex = Int(arguments, "startIndex"),
        MediaSourceId = arguments.TryGetProperty("mediaSourceId", out var source) ? source.GetString() : null,
        AudioStreamIndex = Int(arguments, "audioStreamIndex"),
        SubtitleStreamIndex = Int(arguments, "subtitleStreamIndex"),
        Shuffle = arguments.TryGetProperty("shuffle", out var shuffle) && shuffle.ValueKind == JsonValueKind.True,
        Fullscreen = !arguments.TryGetProperty("fullscreen", out var fullscreen) || fullscreen.ValueKind != JsonValueKind.False,
    };

    private static int? Int(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;

    private static long? Long(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetInt64()
            : null;
}
