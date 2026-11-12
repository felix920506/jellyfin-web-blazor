using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// This client as the target of a remote control: the "Play", "Playstate" and "GeneralCommand"
/// messages of the web socket. Port of scripts/serverNotifications.js and of the commands of
/// scripts/inputManager.js they lead to.
/// </summary>
/// <remarks>
/// What this client can be asked to do is announced by
/// <see cref="ServerConnections.ReportCapabilitiesAsync"/>.
/// </remarks>
[AppService]
public sealed class RemoteCommandReceiver : IStartupTask
{
    private readonly PlaybackManager _playback;
    private readonly AppRouter _router;
    private readonly AppDialogs _dialogs;
    private readonly ToastService _toast;
    private readonly JellyfinApi _api;
    private readonly IRemoteInput _input;
    private readonly IPlaybackTimers _timers;
    private long? _lastStopMs;

    public RemoteCommandReceiver(
        ServerEvents events,
        PlaybackManager playback,
        AppRouter router,
        AppDialogs dialogs,
        ToastService toast,
        JellyfinApi api,
        IRemoteInput input,
        IPlaybackTimers timers)
    {
        _playback = playback;
        _router = router;
        _dialogs = dialogs;
        _toast = toast;
        _api = api;
        _input = input;
        _timers = timers;
        events.Subscribe("Play", data => Run(OnPlayAsync(data)));
        events.Subscribe("Playstate", data => Run(OnPlaystateAsync(data)));
        events.Subscribe("GeneralCommand", data => Run(OnGeneralCommandAsync(data)));
    }

    public Task StartAsync() => Task.CompletedTask;

    /// <summary>Port of onPlay().</summary>
    internal Task OnPlayAsync(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return Task.CompletedTask;
        }

        var ids = new List<string>();
        if (data.TryGetProperty("ItemIds", out var itemIds) && itemIds.ValueKind == JsonValueKind.Array)
        {
            ids.AddRange(itemIds.EnumerateArray()
                .Where(id => id.ValueKind == JsonValueKind.String)
                .Select(id => id.GetString()!)
                .Where(id => id.Length > 0));
        }

        return Text(data, "PlayCommand") switch
        {
            "PlayNext" => _playback.QueueNextAsync(new PlayOptions { Ids = ids }),
            "PlayLast" => _playback.QueueAsync(new PlayOptions { Ids = ids }),
            _ => _playback.PlayAsync(new PlayOptions
            {
                Ids = ids,
                StartPositionTicks = Long(data, "StartPositionTicks"),
                MediaSourceId = Text(data, "MediaSourceId"),
                AudioStreamIndex = Int(data, "AudioStreamIndex"),
                SubtitleStreamIndex = Int(data, "SubtitleStreamIndex"),
                StartIndex = Int(data, "StartIndex"),
            }),
        };
    }

    /// <summary>Port of onPlaystate().</summary>
    internal Task OnPlaystateAsync(JsonElement data)
    {
        switch (Text(data, "Command"))
        {
            case "Stop":
                // Port of checkCommandTime(): a second stop within a second is ignored.
                var now = _timers.NowMs;
                if (_lastStopMs is { } last && now - last < 1000)
                {
                    return Task.CompletedTask;
                }

                _lastStopMs = now;
                return _playback.StopAsync();
            case "Pause":
                return _playback.PauseAsync();
            case "Unpause":
                return _playback.UnpauseAsync();
            case "PlayPause":
                return _playback.PlayPauseAsync();
            case "Seek":
                return _playback.SeekAsync(Long(data, "SeekPositionTicks") ?? 0);
            case "NextTrack":
                return _playback.NextTrackAsync();
            case "PreviousTrack":
                return _playback.PreviousTrackAsync();
            case "Rewind":
                return _playback.RewindAsync();
            case "FastForward":
                return _playback.FastForwardAsync();
            default:
                return Task.CompletedTask;
        }
    }

    /// <summary>Port of processGeneralCommand().</summary>
    internal async Task OnGeneralCommandAsync(JsonElement data)
    {
        var name = Text(data, "Name");
        var arguments = data.ValueKind == JsonValueKind.Object && data.TryGetProperty("Arguments", out var value) ? value : default;
        switch (name)
        {
            case "Select":
                _input.Select();
                break;
            case "Back":
                if (_router.CanGoBack)
                {
                    _router.Back();
                }

                break;
            case "MoveUp":
                _input.MoveFocus("up");
                break;
            case "MoveDown":
                _input.MoveFocus("down");
                break;
            case "MoveLeft":
                _input.MoveFocus("left");
                break;
            case "MoveRight":
                _input.MoveFocus("right");
                break;
            case "PlayTrailers":
                if (Text(arguments, "ItemId") is { Length: > 0 } itemId && await _playback.GetItemByIdAsync(itemId) is { } item)
                {
                    await _playback.PlayTrailersAsync(item);
                }

                break;
            case "SetRepeatMode":
                _playback.SetRepeatMode(Text(arguments, "RepeatMode") ?? PlayQueue.RepeatNone);
                break;
            case "SetShuffleQueue":
                _playback.SetQueueShuffleMode(Text(arguments, "ShuffleMode") ?? PlayQueue.Sorted);
                break;
            case "VolumeUp":
                _playback.VolumeUp();
                break;
            case "VolumeDown":
                _playback.VolumeDown();
                break;
            case "Mute":
                _playback.SetMute(true);
                break;
            case "Unmute":
                _playback.SetMute(false);
                break;
            case "ToggleMute":
                _playback.ToggleMute();
                break;
            case "SetVolume":
                if (Int(arguments, "Volume") is { } volume)
                {
                    _playback.SetVolume(volume);
                }

                break;
            case "SetAudioStreamIndex":
                if (Int(arguments, "Index") is { } audioIndex)
                {
                    await _playback.SetAudioStreamIndexAsync(audioIndex);
                }

                break;
            case "SetSubtitleStreamIndex":
                if (Int(arguments, "Index") is { } subtitleIndex)
                {
                    await _playback.SetSubtitleStreamIndexAsync(subtitleIndex);
                }

                break;
            case "ToggleFullscreen":
                _playback.ToggleFullscreen();
                break;
            case "GoHome":
                _router.GoHome();
                break;
            case "GoToSettings":
                _router.ShowSettings();
                break;
            case "GoToSearch":
                _router.ShowSearch();
                break;
            case "DisplayContent":
                // Not while a video plays here: the item page would replace the player.
                if (Text(arguments, "ItemId") is { Length: > 0 } contentId
                    && !(_playback.IsPlayingLocally && (_playback.IsPlayingMediaType("Video") || _playback.IsPlayingMediaType("Book"))))
                {
                    _router.ShowItem(contentId, _api.ServerId);
                }

                break;
            case "DisplayMessage":
                var text = Text(arguments, "Text") ?? string.Empty;
                if (Int(arguments, "TimeoutMs") is > 0)
                {
                    _toast.Show(text);
                }
                else
                {
                    await _dialogs.AlertAsync(text, Text(arguments, "Header"));
                }

                break;
            case "SendString":
                _input.SendText(Text(arguments, "String") ?? string.Empty);
                break;
            default:
                // "PageUp", "PageDown", "ChannelUp", "ChannelDown", "ToggleOsd", "ToggleContextMenu" and
                // "SendKey" do nothing in the original either.
                break;
        }
    }

    private static async void Run(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] a remote command failed: {ex.Message}");
        }
    }

    /// <summary>A value as text; the server sends command arguments as strings, the rest typed.</summary>
    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    private static long? Long(JsonElement element, string name) =>
        long.TryParse(Text(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static int? Int(JsonElement element, string name) =>
        int.TryParse(Text(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;
}
