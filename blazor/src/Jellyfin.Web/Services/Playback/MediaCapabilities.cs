using System.Globalization;
using System.Text.RegularExpressions;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// What the browser's media stack reports. Filled once by <see cref="IMediaCapabilityProbe"/> from
/// real <c>canPlayType</c> / <c>MediaSource</c> probing (wwwroot/js/playback/capabilities.js); tests
/// build one by hand.
/// </summary>
public sealed record MediaCapabilities
{
    /// <summary>Type strings a video element answered "maybe" or "probably" for.</summary>
    public IReadOnlyCollection<string> VideoTypes { get; init; } = [];

    /// <summary>Type strings an audio element answered "maybe" or "probably" for.</summary>
    public IReadOnlyCollection<string> AudioTypes { get; init; } = [];

    /// <summary><c>window.MediaSource</c> exists (hls.js can be used).</summary>
    public bool MediaSource { get; init; }

    /// <summary>Video elements have <c>textTracks</c>.</summary>
    public bool TextTracks { get; init; }

    /// <summary>Video elements have <c>audioTracks</c>.</summary>
    public bool AudioTracks { get; init; }

    /// <summary><c>AudioContext.destination.maxChannelCount</c>, or -1 without Web Audio.</summary>
    public int SpeakerCount { get; init; } = -1;

    /// <summary>Picture-in-picture is available for video elements.</summary>
    public bool PictureInPicture { get; init; }

    /// <summary>The document can enter fullscreen.</summary>
    public bool Fullscreen { get; init; }

    /// <summary>AirPlay target picker is available (Safari).</summary>
    public bool AirPlay { get; init; }

    public bool CanPlayVideo(string type) => VideoTypes.Contains(type);

    public bool CanPlayAudio(string type) => AudioTypes.Contains(type);

    /// <summary>Port of canPlayNativeHls().</summary>
    public bool CanPlayNativeHls => CanPlayVideo("application/x-mpegURL") || CanPlayVideo("application/vnd.apple.mpegURL");

    /// <summary>Port of canPlayHls(): native or through Media Source Extensions.</summary>
    public bool CanPlayHls => CanPlayNativeHls || MediaSource;
}

/// <summary>Probes the browser. The JS implementation is replaced by a fake in tests.</summary>
public interface IMediaCapabilityProbe
{
    /// <summary>The cached result of the last probe, or null before the first one.</summary>
    MediaCapabilities? Current { get; }

    Task<MediaCapabilities> GetAsync();
}

/// <summary>The type strings the device profile asks about (the ones browserDeviceProfile.js tests).</summary>
public static class MediaProbeTypes
{
    public static readonly IReadOnlyList<string> Video =
    [
        "video/mp4; codecs=\"avc1.42E01E, mp4a.40.2\"",
        "video/mp4; codecs=\"hvc1.1.L120\"",
        "video/mp4; codecs=\"hev1.1.L120\"",
        "video/mp4; codecs=\"hvc1.1.0.L120\"",
        "video/mp4; codecs=\"hev1.1.0.L120\"",
        "video/mp4; codecs=\"av01.0.15M.08\"",
        "video/mp4; codecs=\"av01.0.15M.10\"",
        "video/webm; codecs=\"vp8\"",
        "video/webm; codecs=\"vp9\"",
        "application/x-mpegURL",
        "application/vnd.apple.mpegURL",
        "audio/mp4; codecs=\"ac-3\"",
        "audio/mp4; codecs=\"ec-3\"",
        "application/x-mpegurl; codecs=\"avc1.42E01E, ac-3\"",
        "application/vnd.apple.mpegURL; codecs=\"avc1.42E01E, ac-3\"",
        "application/x-mpegurl; codecs=\"avc1.64001E, mp4a.40.34\"",
        "application/vnd.apple.mpegURL; codecs=\"avc1.64001E, mp4a.40.34\"",
        "video/mp4; codecs=\"dts-\"",
        "video/mp4; codecs=\"dts+\"",
        "video/mp4; codecs=\"avc1.640029, mp4a.69\"",
        "video/mp4; codecs=\"avc1.640029, mp4a.6B\"",
        "video/mp4; codecs=\"avc1.640029, mp3\"",
        "video/mp4; codecs=\"avc1.640029, mp4a.40.2\"",
        "video/mp4; codecs=\"avc1.640029, mp4a.40.5\"",
        "video/x-matroska",
        "video/mkv",
        "video/mp4; codecs=\"vc-1\"",
        "video/mp4; codecs=\"avc1.640833\"",
        "video/mp4; codecs=\"avc1.640834\"",
        "video/mp4; codecs=\"avc1.6e0033\"",
        "video/mp4; codecs=\"hvc1.1.4.L123\"",
        "video/mp4; codecs=\"hev1.1.4.L123\"",
        "video/mp4; codecs=\"hvc1.2.4.L123\"",
        "video/mp4; codecs=\"hev1.2.4.L123\"",
        "video/mp4; codecs=\"hvc1.2.4.L153\"",
        "video/mp4; codecs=\"hev1.2.4.L153\"",
        "video/mp4; codecs=\"hvc1.2.4.L183\"",
        "video/mp4; codecs=\"hev1.2.4.L183\"",
        "video/mp4; codecs=\"hvc1.2.4.L186\"",
        "video/mp4; codecs=\"hev1.2.4.L186\"",
        "video/mp4; codecs=\"av01.0.16M.08\"",
        "video/mp4; codecs=\"av01.0.16M.10\"",
        "video/mp4; codecs=\"av01.0.17M.08\"",
        "video/mp4; codecs=\"av01.0.17M.10\"",
        "video/mp4; codecs=\"av01.0.18M.08\"",
        "video/mp4; codecs=\"av01.0.18M.10\"",
        "video/mp4; codecs=\"av01.0.19M.08\"",
        "video/mp4; codecs=\"av01.0.19M.10\"",
        "video/mp4; codecs=\"dvh1.05.06\"",
        "video/mp4; codecs=\"dvh1.08.06\"",
        "video/mp4; codecs=\"dav1.10.06\"",
    ];

    public static readonly IReadOnlyList<string> Audio =
    [
        "audio/ogg; codecs=\"opus\"",
        "audio/x-caf; codecs=\"opus\"",
        "audio/webm",
        "audio/mp3",
        "audio/aac",
        "audio/flac",
        "audio/alac",
        "audio/wma",
        "audio/wav",
        "audio/ogg",
        "audio/oga",
    ];
}

/// <summary>
/// Browser facts the playback code branches on. Port of the parts of scripts/browser.js the device
/// profile and the players use (desktop and mobile browsers; TV platforms are not targets of the port).
/// </summary>
public sealed partial record PlaybackBrowser
{
    public bool Chrome { get; init; }
    public bool EdgeChromium { get; init; }
    public bool Firefox { get; init; }
    public bool Opera { get; init; }
    public bool Safari { get; init; }
    public bool Ios { get; init; }
    public bool Android { get; init; }
    public bool Mobile { get; init; }

    /// <summary>The user agent mentions "mac os x" (true on iOS as well, as in the original).</summary>
    public bool Osx { get; init; }
    public bool Windows { get; init; }
    public int VersionMajor { get; init; }

    /// <summary>iOS version as major + minor / 10, or 0.</summary>
    public double IosVersion { get; init; }

    public static PlaybackBrowser Parse(string userAgent, BrowserInfo info)
    {
        var ua = userAgent.ToLowerInvariant();
        var version = VersionRegex().Match(ua) is { Success: true } explicitVersion
            ? explicitVersion.Groups[1].Value
            : BrowserRegex().Match(ua) is { Success: true } browserMatch ? browserMatch.Groups[2].Value : "0";
        _ = int.TryParse(version.Split('.')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var major);

        var iosVersion = 0d;
        if (info.Ios && IosRegex().Match(ua) is { Success: true } ios)
        {
            iosVersion = int.Parse(ios.Groups[1].Value, CultureInfo.InvariantCulture)
                + (int.Parse(ios.Groups[2].Value, CultureInfo.InvariantCulture) / 10d);
        }

        return new PlaybackBrowser
        {
            Chrome = info.Chrome,
            EdgeChromium = info.EdgeChromium,
            Firefox = info.Firefox,
            Opera = info.Opera,
            Safari = info.Safari,
            Ios = info.Ios,
            Android = info.Android,
            Mobile = info.Mobile,
            Osx = ua.Contains("mac os x", StringComparison.Ordinal),
            Windows = ua.Contains("windows", StringComparison.Ordinal),
            VersionMajor = major,
            IosVersion = iosVersion,
        };
    }

    [GeneratedRegex(@"version[ /]([\w.]+)")]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"(edg|opr|opera|chrome|safari|firefox)[ /]([\w.]+)")]
    private static partial Regex BrowserRegex();

    [GeneratedRegex(@"os (\d+)_(\d+)")]
    private static partial Regex IosRegex();
}
