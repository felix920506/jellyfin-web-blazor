using Jellyfin.Web.Routing;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// What happens around a remote target without the user asking for it: returning to the remembered
/// target after signing in (scripts/autocast.js), showing on the target the item whose page opens
/// here (components/playback/displayMirrorManager.ts), and leaving the target when the server goes
/// away (the "ServerShuttingDown" and "ServerRestarting" handlers of playbackmanager.js).
/// </summary>
[AppService]
public sealed class RemoteTargetWatcher : IStartupTask
{
    private readonly PlaybackManager _playback;
    private readonly JellyfinApi _api;

    public RemoteTargetWatcher(
        PlaybackManager playback,
        ServerEvents events,
        ServerConnections connections,
        AppRouter router,
        JellyfinApi api)
    {
        _playback = playback;
        _api = api;
        events.Subscribe("ServerShuttingDown", _ => playback.SetDefaultPlayerActive());
        events.Subscribe("ServerRestarting", _ => playback.SetDefaultPlayerActive());
        connections.LocalUserSignedIn += _ =>
        {
            Run(RestoreTargetAsync());
            return Task.CompletedTask;
        };
        router.LocationChanged += location => Run(MirrorAsync(location));
    }

    public Task StartAsync()
    {
        if (_api.IsLoggedIn)
        {
            Run(RestoreTargetAsync());
        }

        return Task.CompletedTask;
    }

    /// <summary>Port of autocast's onOpen().</summary>
    internal Task RestoreTargetAsync() => _playback.RestoreAutoCastTargetAsync();

    /// <summary>Port of mirrorIfEnabled(): only pages of an item ("id" and "serverId" in the address) are mirrored.</summary>
    internal async Task MirrorAsync(AppLocation location)
    {
        var itemId = location["id"];
        if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(location["serverId"]) || !_playback.EnableDisplayMirroring)
        {
            return;
        }

        if (_playback.GetPlayerInfo() is not { IsLocalPlayer: false } info || !info.SupportedCommands.Contains("DisplayContent"))
        {
            return;
        }

        if (await _playback.GetItemByIdAsync(itemId) is { } item)
        {
            await _playback.DisplayContentAsync(item.Name, item.Id?.ToString("N"), item.Type?.ToString());
        }
    }

    private static async void Run(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            AppLog.Warn($"[playback] remote target: {ex.Message}");
        }
    }
}
