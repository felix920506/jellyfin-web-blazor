using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Shared part of the two local players. Port of the element-independent logic of
/// plugins/htmlVideoPlayer/plugin.js and plugins/htmlAudioPlayer/plugin.js; the elements themselves
/// live in wwwroot/js/playback/player.js behind <see cref="IPlayerInterop"/>.
/// </summary>
public abstract partial class HtmlMediaPlayer : IMediaPlayer
{
    private readonly string _kind;
    private PlayerSnapshot _snapshot = new() { Paused = true };
    private double? _currentTime;
    private double _subtitleOffset;

    protected HtmlMediaPlayer(
        string kind,
        IPlayerInterop interop,
        IMediaCapabilityProbe probe,
        DeviceProfileBuilder profiles,
        AppSettings appSettings,
        AppHost appHost,
        IBrowser browser)
    {
        _kind = kind;
        Interop = interop;
        Probe = probe;
        Profiles = profiles;
        AppSettings = appSettings;
        AppHost = appHost;
        Browser = browser;
        interop.PlayerEvent += OnInteropEvent;
    }

    public event Action<IMediaPlayer, PlayerEvent>? Event;

    public abstract string Name { get; }

    public abstract string Id { get; }

    public bool IsLocalPlayer => true;

    public int Priority => 1;

    public string? CurrentSrc { get; private set; }

    public double? CurrentTimeMs
    {
        get
        {
            if (CurrentSrc is null)
            {
                return null;
            }

            return _currentTime is > 0 ? _currentTime * 1000 : (_snapshot.CurrentTime ?? 0) * 1000;
        }
    }

    public double? DurationMs => CurrentSrc is not null && _snapshot.Duration is > 0 ? _snapshot.Duration * 1000 : null;

    public bool? Seekable => CurrentSrc is null ? null : _snapshot.Seekable;

    public bool Paused => CurrentSrc is not null && _snapshot.Paused;

    public double PlaybackRate => _snapshot.PlaybackRate;

    public int Volume => ToVolume(CurrentSrc is null ? SavedVolume : _snapshot.Volume);

    public bool IsMuted => CurrentSrc is not null && _snapshot.Muted;

    public bool IsFullscreen => _snapshot.Fullscreen;

    public bool IsPictureInPictureEnabled => _snapshot.PictureInPicture;

    public virtual string AspectRatio => "auto";

    public virtual bool CanSetAudioStreamIndex => false;

    public IReadOnlyList<BufferedRange> BufferedRanges
    {
        get
        {
            var offset = CurrentOptions?.TranscodingOffsetTicks ?? 0;
            var buffered = _snapshot.Buffered;
            var ranges = new List<BufferedRange>(buffered.Length / 2);
            for (var i = 0; i + 1 < buffered.Length; i += 2)
            {
                ranges.Add(new BufferedRange((long)(buffered[i] * 10000000) + offset, (long)(buffered[i + 1] * 10000000) + offset));
            }

            return ranges;
        }
    }

    /// <summary>What is being played (the original's _currentPlayOptions).</summary>
    protected StreamInfo? CurrentOptions { get; private set; }

    protected IPlayerInterop Interop { get; }

    protected IMediaCapabilityProbe Probe { get; }

    protected DeviceProfileBuilder Profiles { get; }

    protected AppSettings AppSettings { get; }

    protected AppHost AppHost { get; }

    protected IBrowser Browser { get; }

    protected JsonObject? LastProfile { get; private set; }

    protected PlaybackBrowser BrowserInfo => PlaybackBrowser.Parse(Browser.Environment.UserAgent, AppHost.Browser);

    /// <summary>Port of htmlMediaHelper.getSavedVolume(): the element volume (0..1) kept in appSettings.</summary>
    protected double SavedVolume =>
        double.TryParse(AppSettings.Get("volume"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? Math.Min(value, 1)
            : 1;

    public abstract bool CanPlayMediaType(string? mediaType);

    public abstract bool Supports(string feature);

    public async Task<JsonObject> GetDeviceProfileAsync(BaseItemDto? item, bool isRetry = false)
    {
        var caps = await Probe.GetAsync();
        var profile = Profiles.GetDeviceProfile(caps, item?.RunTimeTicks, item?.MediaType.ToApiString(), item is not null);
        LastProfile = profile;
        return profile;
    }

    public async Task PlayAsync(StreamInfo streamInfo)
    {
        _currentTime = null;
        if (streamInfo.ResetSubtitleOffset)
        {
            _subtitleOffset = 0;
        }

        var request = await BuildLoadRequestAsync(streamInfo);
        CurrentOptions = streamInfo;
        CurrentSrc = request.Url;
        var result = await Interop.LoadAsync(_kind, request);
        if (!result.Ok)
        {
            throw new PlayerException(result.ErrorType, result.ErrorName);
        }
    }

    public virtual Task StopAsync(bool destroyPlayer) => Interop.StopAsync(_kind, destroyPlayer, fade: false);

    public virtual void Destroy()
    {
        Interop.Destroy(_kind);
        _currentTime = null;
        CurrentSrc = null;
        CurrentOptions = null;
    }

    public void SetCurrentTime(double milliseconds) => Interop.SetCurrentTime(_kind, milliseconds / 1000);

    public void Pause() => Interop.Pause(_kind);

    public void Unpause() => Interop.Unpause(_kind);

    public void SetPlaybackRate(double value) => Interop.SetPlaybackRate(_kind, value);

    public void SetVolume(int value) => Interop.SetVolume(_kind, Math.Pow(Math.Clamp(value, 0, 100) / 100d, 3));

    public void SetMute(bool mute) => Interop.SetMute(_kind, mute);

    public virtual void SetAudioStreamIndex(int index)
    {
    }

    public virtual void SetSubtitleStreamIndex(int index)
    {
    }

    public virtual int? SecondarySubtitleStreamIndex => null;

    public virtual void SetSecondarySubtitleStreamIndex(int index)
    {
    }

    public virtual double SubtitleOffset => _subtitleOffset;

    public virtual void SetSubtitleOffset(double seconds)
    {
        _subtitleOffset = seconds;
    }

    public virtual bool SupportsSubtitleOffset => false;

    public virtual void SetAspectRatio(string value)
    {
    }

    public void ToggleFullscreen() => Interop.ToggleFullscreen();

    public virtual void SetPictureInPictureEnabled(bool enabled)
    {
    }

    public Task<PlayerStats?> GetStatsAsync() => Interop.GetStatsAsync(_kind);

    public virtual void OnNavigatedToOsd()
    {
    }

    /// <summary>Port of getVolume(): the element volume on the cubic 0..100 scale.</summary>
    public static int ToVolume(double elementVolume) => (int)Math.Min(Math.Round(Math.Cbrt(elementVolume) * 100), 100);

    protected abstract Task<PlayerLoadRequest> BuildLoadRequestAsync(StreamInfo streamInfo);

    /// <summary>Called for the first "playing" event of a source.</summary>
    protected virtual void OnFirstPlaying()
    {
    }

    protected void Raise(string name, string? errorType = null) => Event?.Invoke(this, new PlayerEvent(name, errorType));

    private void OnInteropEvent(string kind, string name, PlayerSnapshot state, string? errorType)
    {
        if (kind != _kind)
        {
            return;
        }

        switch (name)
        {
            case "stopped":
                // The element has been reset already: keep the last known time for the stop report.
                _snapshot = _snapshot with { Paused = true, Fullscreen = state.Fullscreen, PictureInPicture = state.PictureInPicture };
                var stoppedOptions = CurrentOptions;
                Raise("stopped");

                // A handler may have started the next item already: only forget what has stopped.
                if (ReferenceEquals(CurrentOptions, stoppedOptions))
                {
                    _currentTime = null;
                    CurrentSrc = null;
                    CurrentOptions = null;
                }

                break;
            case "timeupdate":
                _snapshot = state;
                _currentTime = state.CurrentTime;
                Raise(name);
                break;
            case "volumechange":
                _snapshot = state;
                if (state.Volume > 0)
                {
                    AppSettings.Set("volume", state.Volume.ToString("R", CultureInfo.InvariantCulture));
                }

                Raise(name);
                break;
            case "firstplaying":
                _snapshot = state;
                OnFirstPlaying();
                break;
            case "error":
                Raise(name, errorType);
                break;
            default:
                _snapshot = state;
                Raise(name);
                break;
        }
    }

    [GeneratedRegex(@"\{\\.*?\}")]
    protected static partial Regex AssTagRegex();
}

/// <summary>The HTML video player. Port of plugins/htmlVideoPlayer/plugin.js.</summary>
/// <remarks>
/// Subtitles: text tracks the server delivers externally (converted to WebVTT) are fetched as track
/// events and shown through a native text track. ASS/SSA, PGS and VobSub renderers are not ported;
/// those formats are not advertised in the device profile, so the server burns them into a transcode.
/// </remarks>
[AppService(typeof(IMediaPlayer))]
public sealed class HtmlVideoPlayer(
    IPlayerInterop interop,
    IMediaCapabilityProbe probe,
    DeviceProfileBuilder profiles,
    AppSettings appSettings,
    AppHost appHost,
    IBrowser browser,
    JellyfinApi api,
    AppRouter router,
    LoadingService loading)
    : HtmlMediaPlayer("video", interop, probe, profiles, appSettings, appHost, browser)
{
    private int _subtitleTrackIndexToSetOnPlaying = -1;
    private int _secondarySubtitleTrackIndexToSetOnPlaying = -1;
    private int? _secondarySubtitleTrackIndex;
    private int? _audioTrackIndexToSetOnPlaying;
    private int _subtitleRequest;
    private int _secondarySubtitleRequest;

    public override string Name => "Html Video Player";

    public override string Id => "htmlvideoplayer";

    public override string AspectRatio => AppSettings.Get("aspectRatio") is { Length: > 0 } value ? value : "auto";

    public override bool CanSetAudioStreamIndex =>
        CurrentSrc is not null && Probe.Current is { AudioTracks: true } && !AppHost.Browser.Firefox;

    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);

    public override bool Supports(string feature) => feature switch
    {
        "PictureInPicture" => Probe.Current?.PictureInPicture == true,
        "AirPlay" => AppHost.Browser.Safari || AppHost.Browser.Ios,
        "PlaybackRate" or "SetAspectRatio" => true,

        // SetBrightness is still a touch-gesture feature; secondary subtitle tracks are supported
        // through the same external cue path as the primary track.
        "SecondarySubtitles" => SupportsSecondarySubtitles,
        _ => false,
    };

    private bool SupportsSecondarySubtitles => CurrentSrc is not null;

    public override int? SecondarySubtitleStreamIndex => _secondarySubtitleTrackIndex;

    public override bool SupportsSubtitleOffset => CurrentSrc is not null;

    public override void SetSubtitleOffset(double seconds)
    {
        base.SetSubtitleOffset(seconds);
        _ = SetCurrentTrackElementAsync(_subtitleTrackIndexToSetOnPlaying, secondary: false);
        _ = SetCurrentTrackElementAsync(_secondarySubtitleTrackIndex ?? -1, secondary: true);
    }

    public override void SetAspectRatio(string value)
    {
        AppSettings.Set("aspectRatio", value);
        Interop.SetAspectRatio(value);
    }

    public override void SetPictureInPictureEnabled(bool enabled) => Interop.SetPictureInPicture(enabled);

    public override void SetAudioStreamIndex(int index)
    {
        var streams = GetSupportedAudioStreams();
        if (streams.Count < 2)
        {
            return;
        }

        var position = streams.FindIndex(s => s.Index == index);
        if (position >= 0)
        {
            Interop.SetAudioTrack(position);
        }
    }

    public override void SetSubtitleStreamIndex(int index)
    {
        _subtitleTrackIndexToSetOnPlaying = index;
        _ = SetCurrentTrackElementAsync(index, secondary: false);
    }

    public override void SetSecondarySubtitleStreamIndex(int index)
    {
        _secondarySubtitleTrackIndex = index;
        _ = SetCurrentTrackElementAsync(index, secondary: true);
    }

    public override void OnNavigatedToOsd()
    {
        Interop.NavigatedToOsd();
        OnStartedAndNavigatedToOsd();
    }

    public override async Task StopAsync(bool destroyPlayer)
    {
        _subtitleRequest++;
        await base.StopAsync(destroyPlayer);
    }

    protected override async Task<PlayerLoadRequest> BuildLoadRequestAsync(StreamInfo streamInfo)
    {
        var caps = await Probe.GetAsync();
        var source = streamInfo.MediaSource;
        var browserInfo = BrowserInfo;
        var maxBufferLength = 30;
        if ((browserInfo.Chrome || browserInfo.EdgeChromium || browserInfo.Firefox) && streamInfo.MaxStreamingBitrate >= 25000000)
        {
            // Chrome, Edge and Firefox keep too much in memory with high bitrate streams.
            maxBufferLength = 6;
        }

        _subtitleTrackIndexToSetOnPlaying = source?.DefaultSubtitleStreamIndex ?? -1;
        _secondarySubtitleTrackIndexToSetOnPlaying = streamInfo.SecondarySubtitleStreamIndex ?? -1;
        _secondarySubtitleTrackIndex = _secondarySubtitleTrackIndexToSetOnPlaying;
        if (_subtitleTrackIndexToSetOnPlaying >= 0)
        {
            var streams = source?.MediaStreams ?? [];
            var initial = _subtitleTrackIndexToSetOnPlaying < streams.Count ? streams[_subtitleTrackIndexToSetOnPlaying] : null;
            if (initial is null || initial.DeliveryMethod == MediaStream_DeliveryMethod.Encode)
            {
                _subtitleTrackIndexToSetOnPlaying = -1;
            }
        }

        _audioTrackIndexToSetOnPlaying = streamInfo.PlayMethod == PlayMethods.Transcode ? null : source?.DefaultAudioStreamIndex;

        return new PlayerLoadRequest
        {
            Url = streamInfo.Url ?? string.Empty,
            MimeType = streamInfo.MimeType,
            StartSeconds = (streamInfo.PlayerStartPositionTicks ?? 0) / 10000000d,
            UseHlsJs = source is not null
                && PlaybackManager.IsHls(source)
                && DeviceProfileBuilder.EnableHlsJsPlayer(caps, browserInfo, source.RunTimeTicks, "Video"),
            MaxBufferLength = maxBufferLength,
            Fullscreen = streamInfo.Fullscreen,
            Volume = SavedVolume,
            AspectRatio = streamInfo.AspectRatio ?? AspectRatio,
            CrossOrigin = source?.IsRemote == true ? null : "anonymous",
            HasRunTime = source is null || source.RunTimeTicks is > 0,
            TranscodingOffsetSeconds = streamInfo.TranscodingOffsetTicks / 10000000d,
        };
    }

    protected override void OnFirstPlaying()
    {
        loading.Hide();
        if (CurrentOptions?.Fullscreen == true)
        {
            router.ShowVideoOsd();
            OnNavigatedToOsd();
        }
        else
        {
            OnStartedAndNavigatedToOsd();
        }
    }

    private void OnStartedAndNavigatedToOsd()
    {
        _ = SetCurrentTrackElementAsync(_subtitleTrackIndexToSetOnPlaying, secondary: false);
        _ = SetCurrentTrackElementAsync(_secondarySubtitleTrackIndexToSetOnPlaying, secondary: true);
        if (_audioTrackIndexToSetOnPlaying is { } audioIndex && CanSetAudioStreamIndex)
        {
            SetAudioStreamIndex(audioIndex);
        }
    }

    private List<MediaStream> GetSupportedAudioStreams()
    {
        var source = CurrentOptions?.MediaSource;
        if (source is null)
        {
            return [];
        }

        var container = (source.Container ?? string.Empty).ToLowerInvariant();
        return (source.MediaStreams ?? [])
            .Where(s => s.Type == MediaStream_Type.Audio)
            .Where(s => IsAudioStreamSupported(s, LastProfile, container))
            .ToList();
    }

    private static bool IsAudioStreamSupported(MediaStream stream, JsonObject? profile, string container)
    {
        var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
        if (codec.Length == 0 || profile is null)
        {
            return true;
        }

        return (profile["DirectPlayProfiles"] as JsonArray ?? []).Any(p =>
            (string?)p?["Type"] == "Video"
            && PlaybackManager.IncludesAny(((string?)p["Container"] ?? string.Empty).ToLowerInvariant(), container)
            && PlaybackManager.IncludesAny(((string?)p["AudioCodec"] ?? string.Empty).ToLowerInvariant(), codec));
    }

    /// <summary>Port of setCurrentTrackElement() for externally delivered text tracks.</summary>
    private async Task SetCurrentTrackElementAsync(int streamIndex, bool secondary)
    {
        var request = secondary ? ++_secondarySubtitleRequest : ++_subtitleRequest;
        var options = CurrentOptions;
        var track = streamIndex < 0
            ? null
            : options?.MediaSource?.MediaStreams?.FirstOrDefault(s => s.Type == MediaStream_Type.Subtitle && s.Index == streamIndex);
        if (options is null || track is null || track.DeliveryMethod != MediaStream_DeliveryMethod.External || string.IsNullOrEmpty(track.DeliveryUrl))
        {
            if (secondary)
            {
                Interop.SetSecondarySubtitleCues(null);
            }
            else
            {
                Interop.SetSubtitleCues(null);
            }
            return;
        }

        try
        {
            var url = track.IsExternalUrl == true ? track.DeliveryUrl : api.GetUrl(track.DeliveryUrl);
            using var response = await api.SendAsync(HttpMethod.Get, url.Replace(".vtt", ".js", StringComparison.Ordinal));
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (request != (secondary ? _secondarySubtitleRequest : _subtitleRequest))
            {
                return;
            }

            var offset = options.TranscodingOffsetTicks / 10000000d;
            var cues = new List<SubtitleCue>();
            if (document.RootElement.TryGetProperty("TrackEvents", out var events) && events.ValueKind == JsonValueKind.Array)
            {
                foreach (var trackEvent in events.EnumerateArray())
                {
                    var start = Math.Max(0, (trackEvent.GetProperty("StartPositionTicks").GetInt64() / 10000000d) - offset - SubtitleOffset);
                    var end = Math.Max(start, (trackEvent.GetProperty("EndPositionTicks").GetInt64() / 10000000d) - offset - SubtitleOffset);
                    if (end <= 0)
                    {
                        continue;
                    }

                    var text = trackEvent.TryGetProperty("Text", out var textElement) ? textElement.GetString() ?? string.Empty : string.Empty;
                    cues.Add(new SubtitleCue(Math.Max(start, 0), end, NormalizeTrackEventText(text)));
                }
            }

            if (secondary)
            {
                Interop.SetSecondarySubtitleCues(cues);
            }
            else
            {
                Interop.SetSubtitleCues(cues);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            AppLog.Warn($"[playback] subtitle track {streamIndex} could not be loaded: {ex.Message}");
            if (secondary)
            {
                Interop.SetSecondarySubtitleCues(null);
            }
            else
            {
                Interop.SetSubtitleCues(null);
            }
        }
    }

    /// <summary>Port of normalizeTrackEventText(text, false).</summary>
    internal static string NormalizeTrackEventText(string text)
    {
        var result = text.Replace("\\N", "\n", StringComparison.OrdinalIgnoreCase).Replace("\r", string.Empty, StringComparison.Ordinal);
        result = AssTagRegex().Replace(result, string.Empty);
        return string.Join('\n', result.Split('\n').Select(line => "‎" + line));
    }
}

/// <summary>The HTML audio player. Port of plugins/htmlAudioPlayer/plugin.js.</summary>
[AppService(typeof(IMediaPlayer))]
public sealed class HtmlAudioPlayer(
    IPlayerInterop interop,
    IMediaCapabilityProbe probe,
    DeviceProfileBuilder profiles,
    AppSettings appSettings,
    AppHost appHost,
    IBrowser browser,
    JellyfinApi api,
    UserSettings userSettings)
    : HtmlMediaPlayer("audio", interop, probe, profiles, appSettings, appHost, browser)
{
    public override string Name => "Html Audio Player";

    public override string Id => "htmlaudioplayer";

    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase);

    public override bool Supports(string feature) => feature switch
    {
        "PlaybackRate" => true,
        "AirPlay" => AppHost.Browser.Safari || AppHost.Browser.Ios,
        _ => false,
    };

    /// <summary>Stopping for good fades the track out, as the original does.</summary>
    public override Task StopAsync(bool destroyPlayer) => Interop.StopAsync("audio", destroyPlayer, fade: true);

    protected override async Task<PlayerLoadRequest> BuildLoadRequestAsync(StreamInfo streamInfo)
    {
        var caps = await Probe.GetAsync();
        var url = streamInfo.Url ?? string.Empty;
        var source = streamInfo.MediaSource;

        double? normalizationGain = null;
        if (!AppHost.Browser.Ios)
        {
            // Key and default of userSettings.selectAudioNormalization().
            var mode = userSettings.Get("selectAudioNormalization", false) is { Length: > 0 } value ? value : "TrackGain";
            if (mode == "TrackGain")
            {
                normalizationGain = streamInfo.Item.NormalizationGain ?? streamInfo.Source?.AlbumNormalizationGain ?? 0;
            }
            else if (mode == "AlbumGain")
            {
                normalizationGain = streamInfo.Source?.AlbumNormalizationGain ?? streamInfo.Item.NormalizationGain ?? 0;
            }
        }

        return new PlayerLoadRequest
        {
            Url = url,
            MimeType = streamInfo.MimeType,
            StartSeconds = (streamInfo.PlayerStartPositionTicks ?? 0) / 10000000d,
            UseHlsJs = await EnableHlsPlayerAsync(caps, url, source),
            Volume = SavedVolume,
            CrossOrigin = source?.IsRemote == true ? null : "anonymous",
            NormalizationGain = normalizationGain,
        };
    }

    /// <summary>Port of enableHlsPlayer(): the universal endpoint may answer with an HLS playlist.</summary>
    private async Task<bool> EnableHlsPlayerAsync(MediaCapabilities caps, string url, MediaSourceInfo? source)
    {
        if (!DeviceProfileBuilder.EnableHlsJsPlayer(caps, BrowserInfo, source?.RunTimeTicks, "Audio"))
        {
            return false;
        }

        if (url.Contains(".m3u8", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await api.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var contentType = (response.Content.Headers.ContentType?.MediaType ?? string.Empty).ToLowerInvariant();
            return contentType is "application/vnd.apple.mpegurl" or "application/x-mpegurl";
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}
