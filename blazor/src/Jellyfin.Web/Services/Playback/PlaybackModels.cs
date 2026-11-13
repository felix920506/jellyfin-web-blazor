using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>The play methods reported to the server.</summary>
public static class PlayMethods
{
    public const string DirectPlay = "DirectPlay";
    public const string DirectStream = "DirectStream";
    public const string Transcode = "Transcode";
}

/// <summary>Port of utils/mediaError.ts: the values are the suffixes of the "PlaybackError.*" strings.</summary>
public static class MediaErrors
{
    public const string AssRenderError = "ASS_RENDER_ERROR";
    public const string PgsRenderError = "PGS_RENDER_ERROR";
    public const string FatalHlsError = "FATAL_HLS_ERROR";
    public const string MediaDecodeError = "MEDIA_DECODE_ERROR";
    public const string MediaNotSupported = "MEDIA_NOT_SUPPORTED";
    public const string NetworkError = "NETWORK_ERROR";
    public const string NoMediaError = "NO_MEDIA_ERROR";
    public const string PlayerError = "PLAYER_ERROR";
    public const string ServerError = "SERVER_ERROR";
}

/// <summary>Names of the events <see cref="PlaybackManager.Event"/> raises (the original's event names).</summary>
public static class PlaybackEvents
{
    public const string PlaybackStart = "playbackstart";
    public const string PlaybackStop = "playbackstop";
    public const string PlaybackError = "playbackerror";
    public const string PlaybackCancelled = "playbackcancelled";
    public const string PlayerChange = "playerchange";
    public const string TimeUpdate = "timeupdate";
    public const string Pause = "pause";
    public const string Unpause = "unpause";
    public const string Playing = "playing";
    public const string Waiting = "waiting";
    public const string VolumeChange = "volumechange";
    public const string RepeatModeChange = "repeatmodechange";
    public const string ShuffleQueueModeChange = "shufflequeuemodechange";
    public const string PlaylistItemMove = "playlistitemmove";
    public const string PlaylistItemRemove = "playlistitemremove";
    public const string PlaylistItemAdd = "playlistitemadd";
    public const string FullscreenChange = "fullscreenchange";
    public const string MediaStreamsChange = "mediastreamschange";
    public const string PlaybackRateChange = "playbackratechange";
    public const string Click = "click";
    public const string DblClick = "dblclick";

    /// <summary>Raised by a remote player when its state changed in a way the other events do not cover.</summary>
    public const string StateChange = "statechange";
}

/// <summary>
/// A media source chosen for playback: the server's <see cref="MediaSourceInfo"/> plus the fields the
/// original client adds to the object (enableDirectPlay, StreamUrl, hasAlternateVersions,
/// albumNormalizationGain).
/// </summary>
public sealed class PlaybackSource(MediaSourceInfo info)
{
    public MediaSourceInfo Info { get; } = info;

    /// <summary>The server playback session returned by PlaybackInfo.</summary>
    public string? PlaySessionId { get; set; }

    /// <summary>The source's own path can be given to the player (remote http sources).</summary>
    public bool EnableDirectPlay { get; set; }

    /// <summary>A ready stream URL (the universal audio endpoint).</summary>
    public string? StreamUrl { get; set; }

    public bool HasAlternateVersions { get; set; }

    public float? AlbumNormalizationGain { get; set; }
}

/// <summary>An external subtitle track the player can show itself.</summary>
public sealed record TextTrackInfo(string Url, string Language, bool IsDefault, int Index, string? Format);

/// <summary>A server-defined media segment that can be skipped in the OSD.</summary>
public sealed record PlaybackMediaSegment(string Type, long StartTicks, long? EndTicks);

/// <summary>What a player is asked to play. Port of the object createStreamInfo() builds.</summary>
public sealed class StreamInfo
{
    public string? Url { get; set; }
    public string? MimeType { get; set; }
    public long TranscodingOffsetTicks { get; set; }
    public string PlayMethod { get; set; } = PlayMethods.Transcode;
    public long? PlayerStartPositionTicks { get; set; }
    public required BaseItemDto Item { get; set; }
    public PlaybackSource? Source { get; set; }
    public MediaSourceInfo? MediaSource => Source?.Info;
    public IReadOnlyList<TextTrackInfo> TextTracks { get; set; } = [];
    public string? MediaType { get; set; }
    public string? LiveStreamId { get; set; }
    public string? PlaySessionId { get; set; }
    public string? Title { get; set; }
    public bool Fullscreen { get; set; } = true;
    public string? AspectRatio { get; set; }
    public bool ResetSubtitleOffset { get; set; } = true;
    public double SubtitleOffset { get; set; }
    public int? SecondarySubtitleStreamIndex { get; set; }
    public bool Started { get; set; }
    public bool Ended { get; set; }
    public long PlaybackStartTimeTicks { get; set; }

    /// <summary>The last time live-stream media information was requested.</summary>
    public long LastMediaInfoQuery { get; set; }

    /// <summary>The max bitrate in effect, for the player's buffer sizing.</summary>
    public int? MaxStreamingBitrate { get; set; }
}

/// <summary>A buffered range in ticks, transcoding offset included.</summary>
public readonly record struct BufferedRange(long Start, long End);

/// <summary>
/// State of a media element, pushed by the JS player with every event so that the C# getters stay
/// synchronous. Times are seconds as the element reports them.
/// </summary>
public sealed record PlayerSnapshot
{
    public double? CurrentTime { get; init; }
    public double? Duration { get; init; }
    public bool Paused { get; init; }
    public double Volume { get; init; } = 1;
    public bool Muted { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public double[] Buffered { get; init; } = [];
    public bool Seekable { get; init; }
    public bool Fullscreen { get; init; }
    public bool PictureInPicture { get; init; }
}

/// <summary>What the JS player is told to load.</summary>
public sealed record PlayerLoadRequest
{
    public string Url { get; init; } = string.Empty;
    public string? MimeType { get; init; }
    public double StartSeconds { get; init; }
    public bool UseHlsJs { get; init; }
    public int MaxBufferLength { get; init; } = 30;
    public bool Fullscreen { get; init; }
    public double? Volume { get; init; }
    public string? AspectRatio { get; init; }
    public string? CrossOrigin { get; init; }
    public bool HasRunTime { get; init; }
    public double TranscodingOffsetSeconds { get; init; }
    public double? NormalizationGain { get; init; }
    public bool Fade { get; init; }
}

/// <summary>Result of a load: <see cref="Ok"/> or the error the element or hls.js reported.</summary>
public sealed record PlayerLoadResult
{
    public bool Ok { get; init; }
    public string? ErrorType { get; init; }
    public string? ErrorName { get; init; }
}

/// <summary>A cue of a text subtitle track (seconds).</summary>
public sealed record SubtitleCue(double Start, double End, string Text);

/// <summary>Configuration for a client-rendered graphical subtitle track.</summary>
public sealed record SubtitleRendererRequest
{
    public string Codec { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public bool Secondary { get; init; }
    public double TimeOffset { get; init; }
    public IReadOnlyList<string> Attachments { get; init; } = [];
    public string? FallbackConfigUrl { get; init; }
    public string? FallbackFontsUrl { get; init; }
    public string? FallbackFontUrl { get; init; }
    public string AspectMode { get; init; } = "contain";
    public double TargetFps { get; init; } = 24;
}

/// <summary>One line of the stats overlay.</summary>
public sealed record PlayerStat(string Label, string Value);

/// <summary>Stats the element knows about (the "media", "video" and "audio" categories of getStats()).</summary>
public sealed record PlayerStats
{
    public bool Hls { get; init; }
    public string? Protocol { get; init; }
    public int ViewWidth { get; init; }
    public int ViewHeight { get; init; }
    public int VideoWidth { get; init; }
    public int VideoHeight { get; init; }
    public int? DroppedFrames { get; init; }
    public int? CorruptedFrames { get; init; }
    public string? SinkId { get; init; }
}

/// <summary>Metadata for the Media Session API.</summary>
public sealed record MediaSessionInfo
{
    public string Title { get; init; } = string.Empty;
    public string Artist { get; init; } = string.Empty;
    public string Album { get; init; } = string.Empty;
    public MediaSessionArtwork[] Artwork { get; init; } = [];
    public bool Paused { get; init; }
    public double? DurationSeconds { get; init; }
    public double? PositionSeconds { get; init; }
    public double PlaybackRate { get; init; } = 1;
}

public sealed record MediaSessionArtwork(string Src, string Sizes);

/// <summary>An error a player reports when a source cannot be played.</summary>
public sealed class PlayerException(string? errorType, string? errorName = null)
    : Exception(errorType ?? errorName ?? "player error")
{
    /// <summary>One of <see cref="MediaErrors"/>, when the player knows.</summary>
    public string? ErrorType { get; } = errorType;

    /// <summary>The DOMException name ("NotSupportedError", ...), when there is one.</summary>
    public string? ErrorName { get; } = errorName;
}

/// <summary>An event of a player: the original's event name plus the error type for "error".</summary>
public sealed record PlayerEvent(string Name, string? ErrorType = null);

/// <summary>
/// A player the <see cref="PlaybackManager"/> can drive. The local HTML players implement it over
/// <see cref="IPlayerInterop"/>; players for other targets (remote sessions, cast) can be added later
/// with <see cref="PlaybackManager.RegisterPlayer"/>.
/// </summary>
public interface IMediaPlayer
{
    string Name { get; }
    string Id { get; }
    bool IsLocalPlayer { get; }
    int Priority { get; }

    /// <summary>"timeupdate", "pause", "unpause", "playing", "waiting", "volumechange", "stopped", "error", "fullscreenchange", "click", "dblclick".</summary>
    event Action<IMediaPlayer, PlayerEvent>? Event;

    bool CanPlayMediaType(string? mediaType);
    Task<JsonObject> GetDeviceProfileAsync(BaseItemDto? item, bool isRetry = false);

    /// <summary>Starts playing; throws <see cref="PlayerException"/> when the source cannot be played.</summary>
    Task PlayAsync(StreamInfo streamInfo);
    Task StopAsync(bool destroyPlayer);
    void Destroy();

    /// <summary>The URL being played, or null when nothing is loaded (the original's currentSrc()).</summary>
    string? CurrentSrc { get; }
    double? CurrentTimeMs { get; }
    void SetCurrentTime(double milliseconds);
    double? DurationMs { get; }

    /// <summary>Whether the element can seek; null when the player cannot tell.</summary>
    bool? Seekable { get; }
    bool Paused { get; }
    void Pause();
    void Unpause();
    double PlaybackRate { get; }
    void SetPlaybackRate(double value);

    /// <summary>Volume 0..100.</summary>
    int Volume { get; }
    void SetVolume(int value);
    bool IsMuted { get; }
    void SetMute(bool mute);
    IReadOnlyList<BufferedRange> BufferedRanges { get; }
    bool Supports(string feature);
    bool CanSetAudioStreamIndex { get; }
    void SetAudioStreamIndex(int index);
    void SetSubtitleStreamIndex(int index);
    int? SecondarySubtitleStreamIndex { get; }
    void SetSecondarySubtitleStreamIndex(int index);
    double SubtitleOffset { get; }
    void SetSubtitleOffset(double seconds);
    bool SupportsSubtitleOffset { get; }
    string AspectRatio { get; }
    void SetAspectRatio(string value);
    bool IsFullscreen { get; }
    void ToggleFullscreen();
    bool IsPictureInPictureEnabled { get; }
    void SetPictureInPictureEnabled(bool enabled);
    Task<PlayerStats?> GetStatsAsync();

    /// <summary>The video OSD has been shown: the player may drop its on-top state and apply tracks.</summary>
    void OnNavigatedToOsd();
}

/// <summary>
/// The JS boundary of the local players (wwwroot/js/playback/player.js). <c>kind</c> is "video" or
/// "audio". Tests replace it with a fake.
/// </summary>
public interface IPlayerInterop
{
    /// <summary>Raised for every element event: kind, event name, element state, error type.</summary>
    event Action<string, string, PlayerSnapshot, string?>? PlayerEvent;

    /// <summary>Raised for Media Session actions: action name and seek time in seconds, if any.</summary>
    event Action<string, double?>? MediaSessionAction;

    Task<PlayerLoadResult> LoadAsync(string kind, PlayerLoadRequest request);
    Task StopAsync(string kind, bool destroy, bool fade);
    void Destroy(string kind);
    void Pause(string kind);
    void Unpause(string kind);
    void SetCurrentTime(string kind, double seconds);
    void SetVolume(string kind, double elementVolume);
    void SetMute(string kind, bool mute);
    void SetPlaybackRate(string kind, double rate);
    void SetAspectRatio(string value);
    void SetAudioTrack(int position);
    void SetSubtitleCues(IReadOnlyList<SubtitleCue>? cues);
    void SetSecondarySubtitleCues(IReadOnlyList<SubtitleCue>? cues);
    void SetSubtitleRenderer(SubtitleRendererRequest? request, bool secondary);
    void SetSubtitleRendererOffset(double seconds, bool secondary);
    void ToggleFullscreen();
    void SetPictureInPicture(bool enabled);
    void NavigatedToOsd();
    Task<PlayerStats?> GetStatsAsync(string kind);
    void SetMediaSession(MediaSessionInfo? info);
}

/// <summary>Clock and timers of the playback code; a manual fake drives them in tests.</summary>
public interface IPlaybackTimers
{
    /// <summary>Milliseconds since the Unix epoch (Date.now()).</summary>
    long NowMs { get; }

    /// <summary>Calls <paramref name="callback"/> every <paramref name="milliseconds"/> until disposed.</summary>
    IDisposable SetInterval(Func<Task> callback, int milliseconds);

    Task Delay(int milliseconds);
}

[AppService(typeof(IPlaybackTimers))]
public sealed class SystemPlaybackTimers : IPlaybackTimers
{
    public long NowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public IDisposable SetInterval(Func<Task> callback, int milliseconds)
    {
        var cancellation = new CancellationTokenSource();
        _ = RunAsync(callback, milliseconds, cancellation.Token);
        return new Handle(cancellation);
    }

    public Task Delay(int milliseconds) => Task.Delay(milliseconds);

    private sealed class Handle(CancellationTokenSource cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }

    private static async Task RunAsync(Func<Task> callback, int milliseconds, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(milliseconds, cancellation);
                try
                {
                    await callback();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    AppLog.Warn($"[playback] timer callback failed: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopped.
        }
    }
}
