namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Port of apps/legacy/features/playback/utils/mediaSessionSubscriber.ts: keeps the browser's Media
/// Session (lock screen / media keys) in step with what is playing, and runs its actions.
/// </summary>
[AppService]
public sealed class MediaSessionUpdater : IStartupTask
{
    /// <summary>
    /// The image resolutions offered to the media session. Highest-to-lowest order matters: Firefox
    /// on Linux uses the first one for its MPRIS interface.
    /// </summary>
    private static readonly int[] ImageSizes = [512, 384, 256, 192, 128, 96];

    private readonly PlaybackManager _playback;
    private readonly IPlayerInterop _interop;
    private readonly PlaybackText _text;

    public MediaSessionUpdater(PlaybackManager playback, IPlayerInterop interop, PlaybackText text)
    {
        _playback = playback;
        _interop = interop;
        _text = text;
        playback.Event += OnPlaybackEvent;
        interop.MediaSessionAction += OnAction;
    }

    public Task StartAsync() => Task.CompletedTask;

    /// <summary>Port of onMediaSessionAction().</summary>
    internal void OnAction(string action, double? seekTime)
    {
        _ = action switch
        {
            "pause" => _playback.PauseAsync(),
            "play" => _playback.UnpauseAsync(),
            "stop" => _playback.StopAsync(),
            "seekbackward" => _playback.RewindAsync(),
            "seekforward" => _playback.FastForwardAsync(),
            "seekto" => _playback.SeekMsAsync((long)((seekTime ?? 0) * 1000)),
            "previoustrack" => _playback.PreviousTrackAsync(),
            "nexttrack" => _playback.NextTrackAsync(),
            _ => Task.CompletedTask,
        };
    }

    private void OnPlaybackEvent(string name)
    {
        switch (name)
        {
            case PlaybackEvents.PlaybackStop:
                _interop.SetMediaSession(null);
                break;
            case PlaybackEvents.PlaybackStart:
            case PlaybackEvents.PlayerChange:
            case PlaybackEvents.Pause:
            case PlaybackEvents.Unpause:
                Update();
                break;
        }
    }

    /// <summary>Port of onMediaSessionUpdate().</summary>
    private void Update()
    {
        if (_playback.CurrentPlayer is not { } player || _playback.CurrentItem is not { } item)
        {
            _interop.SetMediaSession(null);
            return;
        }

        // The browser shows its own notification for a local video element.
        if (player.IsLocalPlayer && item.MediaType.ToApiString() == "Video")
        {
            return;
        }

        var lines = PlaybackText.ItemTextLines(item, includeYear: false);
        var line1 = lines.Count > 0 ? lines[0] : string.Empty;
        var line2 = lines.Count > 1 ? lines[1] : null;

        var artwork = new List<MediaSessionArtwork>();
        foreach (var size in ImageSizes)
        {
            if (_text.ImageUrl(item, height: size) is { } url)
            {
                artwork.Add(new MediaSessionArtwork(url, $"{size}x{size}"));
            }
        }

        _interop.SetMediaSession(new MediaSessionInfo
        {
            // The artist is the second line if present, the first line otherwise; the title is the
            // first line when there are two.
            Title = line2 is null ? string.Empty : line1,
            Artist = line2 ?? line1,
            Album = item.Album ?? string.Empty,
            Artwork = [.. artwork],
            Paused = _playback.IsPaused,
            DurationSeconds = _playback.DurationTicks / 10000000d,
            PositionSeconds = _playback.CurrentTimeTicks / 10000000d,
            PlaybackRate = _playback.PlaybackRate,
        });
    }
}
