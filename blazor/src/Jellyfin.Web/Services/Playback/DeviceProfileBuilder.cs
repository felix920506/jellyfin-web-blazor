using System.Globalization;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Options of the profile builder (the <c>options</c> argument of browserDeviceProfile.js).</summary>
public sealed record DeviceProfileOptions
{
    public IReadOnlyCollection<string> DisableVideoAudioCodecs { get; init; } = [];

    public IReadOnlyCollection<string> DisableHlsVideoAudioCodecs { get; init; } = [];

    public bool EnableHls { get; init; } = true;

    /// <summary>Set after a failed attempt: complex subtitle formats are no longer offered for client rendering.</summary>
    public bool IsRetry { get; init; }
}

/// <summary>
/// Builds the device profile sent with PlaybackInfo. Port of scripts/browserDeviceProfile.js and of
/// the wrapper in components/apphost.js (getDeviceProfile), for desktop and mobile browsers.
/// The profile is produced as a JSON object with exactly the property names and value types the
/// original posts (numbers that the original sends as strings stay strings).
/// </summary>
/// <remarks>
/// Not ported: the Tizen, webOS, Xbox, PS4, Vidaa and Edge UWP branches (TV and console shells are
/// not targets of the port), and client-side ASS/SSA and PGS/VobSub rendering: those subtitle
/// formats are never advertised, so the server burns them in.
/// </remarks>
[AppService]
public sealed class DeviceProfileBuilder(AppSettings appSettings, UserSettings userSettings, AppHost appHost, Interop.IBrowser browserInterop)
{
    private static readonly string[] AudioFormats = ["opus", "mp3", "mp2", "aac", "flac", "alac", "webma", "wma", "wav", "ogg", "oga"];

    private PlaybackBrowser Browser => PlaybackBrowser.Parse(browserInterop.Environment.UserAgent, appHost.Browser);

    /// <summary>Port of htmlMediaHelper.enableHlsJsPlayer().</summary>
    public static bool EnableHlsJsPlayer(MediaCapabilities caps, PlaybackBrowser browser, long? runTimeTicks, string? mediaType)
    {
        if (!caps.MediaSource || browser.Ios)
        {
            return false;
        }

        if (caps.CanPlayNativeHls)
        {
            if (browser.Android && mediaType is "Audio" or "Video")
            {
                return true;
            }

            if (browser.Chrome || browser.EdgeChromium || browser.Opera)
            {
                return true;
            }

            if (runTimeTicks is > 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Port of apphost.getBaseProfileOptions().</summary>
    public DeviceProfileOptions GetBaseProfileOptions(MediaCapabilities caps, long? runTimeTicks, string? mediaType, bool hasItem = true)
    {
        var browser = Browser;
        var disable = new List<string>();
        if (hasItem && EnableHlsJsPlayer(caps, browser, runTimeTicks, mediaType))
        {
            if (!browser.EdgeChromium)
            {
                disable.Add("ac3");
                disable.Add("eac3");
            }

            if (!(browser.Chrome || browser.EdgeChromium || browser.Firefox))
            {
                disable.Add("opus");
            }
        }

        return new DeviceProfileOptions { DisableHlsVideoAudioCodecs = disable };
    }

    /// <summary>Port of apphost.getDeviceProfile(item): the browser profile adjusted by the user's settings.</summary>
    public JsonObject GetDeviceProfile(MediaCapabilities caps, long? runTimeTicks, string? mediaType, bool hasItem = true, bool isRetry = false) =>
        ApplySettings(Build(caps, GetBaseProfileOptions(caps, runTimeTicks, mediaType, hasItem) with { IsRetry = isRetry }));

    /// <summary>Port of the second half of apphost.getDeviceProfile().</summary>
    public JsonObject ApplySettings(JsonObject profile)
    {
        var maxVideoWidth = appSettings.MaxVideoWidth;
        var env = browserInterop.Environment;
        var maxTranscodingVideoWidth = maxVideoWidth < 0
            ? Math.Max((int)Math.Floor(env.ScreenWidth * env.DevicePixelRatio), (int)Math.Floor(env.ScreenHeight * env.DevicePixelRatio))
            : maxVideoWidth;
        var transcodingProfiles = profile["TranscodingProfiles"]!.AsArray();

        if (maxTranscodingVideoWidth != 0)
        {
            JsonObject ConditionWidth() => Condition("LessThanEqual", "Width", maxTranscodingVideoWidth.ToString(CultureInfo.InvariantCulture), false);

            if (appSettings.GetBool("limitSupportedVideoResolution", false))
            {
                profile["CodecProfiles"]!.AsArray().Add(new JsonObject
                {
                    ["Type"] = "Video",
                    ["Conditions"] = new JsonArray(ConditionWidth()),
                });
            }

            foreach (var transcodingProfile in transcodingProfiles.OfType<JsonObject>().Where(IsVideo))
            {
                var conditions = new JsonArray();
                foreach (var condition in (transcodingProfile["Conditions"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    if ((string?)condition["Property"] != "Width")
                    {
                        conditions.Add(condition.DeepClone());
                    }
                }

                conditions.Add(ConditionWidth());
                transcodingProfile["Conditions"] = conditions;
            }
        }

        MoveToFront(transcodingProfiles, "VideoCodec", appSettings.Get("preferredTranscodeVideoCodec"));
        MoveToFront(transcodingProfiles, "AudioCodec", appSettings.Get("preferredTranscodeVideoAudioCodec"));
        return profile;
    }

    /// <summary>Port of the default export of browserDeviceProfile.js.</summary>
    public JsonObject Build(MediaCapabilities caps, DeviceProfileOptions? options = null)
    {
        options ??= new DeviceProfileOptions();
        var browser = Browser;
        const int bitrateSetting = 120000000;

        var allowedAudioChannels = ParseInt(userSettings.Get("allowedAudioChannels", false) is { Length: > 0 } channels ? channels : "-1");
        var physicalAudioChannels = GetPhysicalAudioChannels(caps, browser, allowedAudioChannels);
        var channelsText = physicalAudioChannels.ToString(CultureInfo.InvariantCulture);

        var canPlayVp8 = caps.CanPlayVideo("video/webm; codecs=\"vp8\"");
        var canPlayVp9 = caps.CanPlayVideo("video/webm; codecs=\"vp9\"");
        var safariSupportsOpus = browser.Safari && browser.VersionMajor >= 17 && caps.CanPlayAudio("audio/x-caf; codecs=\"opus\"");
        var webmAudioCodecs = new List<string> { "vorbis" };
        var canPlayMkv = CanPlayMkv(caps, browser);

        var directPlayProfiles = new JsonArray();
        var profile = new JsonObject
        {
            ["MaxStreamingBitrate"] = bitrateSetting,
            ["MaxStaticBitrate"] = 100000000,
            ["MusicStreamingTranscodingBitrate"] = Math.Min(bitrateSetting, 384000),
            ["DirectPlayProfiles"] = directPlayProfiles,
        };

        var videoAudioCodecs = new List<string>();
        var mkvAudioCodecs = new List<string>();
        var hlsInTsVideoAudioCodecs = new List<string>();
        var hlsInFmp4VideoAudioCodecs = new List<string>();

        var supportsMp3VideoAudio = caps.CanPlayVideo("video/mp4; codecs=\"avc1.640029, mp4a.69\"")
            || caps.CanPlayVideo("video/mp4; codecs=\"avc1.640029, mp4a.6B\"")
            || caps.CanPlayVideo("video/mp4; codecs=\"avc1.640029, mp3\"");

        var supportsMp2VideoAudio = supportsMp3VideoAudio
            && (browser.Chrome || browser.EdgeChromium || (browser.Firefox && browser.VersionMajor >= 83));
        if (browser.Android)
        {
            supportsMp2VideoAudio = false;
        }

        var canPlayAacVideoAudio = caps.CanPlayVideo("video/mp4; codecs=\"avc1.640029, mp4a.40.2\"");
        var canPlayMp3VideoAudioInHls = caps.CanPlayVideo("application/x-mpegurl; codecs=\"avc1.64001E, mp4a.40.34\"")
            || caps.CanPlayVideo("application/vnd.apple.mpegURL; codecs=\"avc1.64001E, mp4a.40.34\"");
        var canPlayAc3VideoAudio = SupportsAc3(caps, browser);
        var canPlayEac3VideoAudio = SupportsEac3(caps, browser);
        var canPlayAc3VideoAudioInHls = caps.CanPlayVideo("application/x-mpegurl; codecs=\"avc1.42E01E, ac-3\"")
            || caps.CanPlayVideo("application/vnd.apple.mpegURL; codecs=\"avc1.42E01E, ac-3\"");

        // Transcoding codec is the first in hlsVideoAudioCodecs. Prefer AAC, MP3 to other codecs when audio transcoding.
        if (canPlayAacVideoAudio)
        {
            videoAudioCodecs.Add("aac");
            mkvAudioCodecs.Add("aac");
            hlsInTsVideoAudioCodecs.Add("aac");
            hlsInFmp4VideoAudioCodecs.Add("aac");
        }

        if (supportsMp3VideoAudio)
        {
            videoAudioCodecs.Add("mp3");
            mkvAudioCodecs.Add("mp3");
        }

        // Safari supports mp3 with HLS, but only in mpegts container, and supportsMp3VideoAudio will return false.
        if (browser.Safari || supportsMp3VideoAudio)
        {
            hlsInTsVideoAudioCodecs.Add("mp3");
        }

        if (canPlayMp3VideoAudioInHls)
        {
            hlsInFmp4VideoAudioCodecs.Add("mp3");
        }

        // For AC3/EAC3 remuxing. Do not use AC3 for audio transcoding unless AAC and MP3 are not supported.
        if (canPlayAc3VideoAudio)
        {
            videoAudioCodecs.Add("ac3");
            mkvAudioCodecs.Add("ac3");
            if (browser.EdgeChromium)
            {
                hlsInFmp4VideoAudioCodecs.Add("ac3");
            }

            if (canPlayEac3VideoAudio)
            {
                videoAudioCodecs.Add("eac3");
                mkvAudioCodecs.Add("eac3");
                if (browser.EdgeChromium)
                {
                    hlsInFmp4VideoAudioCodecs.Add("eac3");
                }
            }

            if (canPlayAc3VideoAudioInHls)
            {
                hlsInTsVideoAudioCodecs.Add("ac3");
                hlsInFmp4VideoAudioCodecs.Add("ac3");
                if (canPlayEac3VideoAudio)
                {
                    hlsInTsVideoAudioCodecs.Add("eac3");
                    hlsInFmp4VideoAudioCodecs.Add("eac3");
                }
            }
        }

        if (supportsMp2VideoAudio)
        {
            videoAudioCodecs.Add("mp2");
            mkvAudioCodecs.Add("mp2");
            hlsInTsVideoAudioCodecs.Add("mp2");
            hlsInFmp4VideoAudioCodecs.Add("mp2");
        }

        var supportsDts = appSettings.GetBool("enableDts", false)
            || caps.CanPlayVideo("video/mp4; codecs=\"dts-\"")
            || caps.CanPlayVideo("video/mp4; codecs=\"dts+\"");
        if (supportsDts)
        {
            videoAudioCodecs.Add("dca");
            videoAudioCodecs.Add("dts");
            mkvAudioCodecs.Add("dca");
            mkvAudioCodecs.Add("dts");
        }

        if (appSettings.GetBool("enableTrueHd", false))
        {
            videoAudioCodecs.Add("truehd");
            mkvAudioCodecs.Add("truehd");
        }

        if (CanPlayAudioFormat(caps, browser, "opus") || safariSupportsOpus)
        {
            videoAudioCodecs.Add("opus");
            mkvAudioCodecs.Add("opus");
            webmAudioCodecs.Add("opus");
            hlsInFmp4VideoAudioCodecs.Add("opus");
        }

        if (CanPlayAudioFormat(caps, browser, "flac"))
        {
            videoAudioCodecs.Add("flac");
            mkvAudioCodecs.Add("flac");
            hlsInFmp4VideoAudioCodecs.Add("flac");
        }

        if (CanPlayAudioFormat(caps, browser, "alac"))
        {
            videoAudioCodecs.Add("alac");
            mkvAudioCodecs.Add("alac");
            hlsInFmp4VideoAudioCodecs.Add("alac");
        }

        videoAudioCodecs.RemoveAll(options.DisableVideoAudioCodecs.Contains);
        mkvAudioCodecs.RemoveAll(options.DisableVideoAudioCodecs.Contains);
        hlsInTsVideoAudioCodecs.RemoveAll(options.DisableHlsVideoAudioCodecs.Contains);
        hlsInFmp4VideoAudioCodecs.RemoveAll(options.DisableHlsVideoAudioCodecs.Contains);

        var mp4VideoCodecs = new List<string>();
        var webmVideoCodecs = new List<string>();
        var hlsInTsVideoCodecs = new List<string>();
        var hlsInFmp4VideoCodecs = new List<string>();

        var canPlayAv1 = CanPlayAv1(caps);
        var canPlayHevc = CanPlayHevc(caps);
        var canPlayH264 = caps.CanPlayVideo("video/mp4; codecs=\"avc1.42E01E, mp4a.40.2\"");

        // av1 is disabled on non-safari mobile browsers since software decoding can be very slow
        if (canPlayAv1 && (browser.Safari || (!browser.Mobile && (browser.EdgeChromium || browser.Firefox || browser.Chrome || browser.Opera))))
        {
            hlsInFmp4VideoCodecs.Add("av1");
        }

        if (canPlayHevc
            && (browser.EdgeChromium || browser.Safari || (browser.Chrome && (!browser.Android || browser.VersionMajor >= 105))
                || (browser.Opera && !browser.Mobile) || (browser.Firefox && browser.VersionMajor >= 134)))
        {
            hlsInFmp4VideoCodecs.Add("hevc");
        }

        if (canPlayH264)
        {
            mp4VideoCodecs.Add("h264");
            hlsInTsVideoCodecs.Add("h264");
            hlsInFmp4VideoCodecs.Add("h264");
        }

        if (canPlayHevc)
        {
            mp4VideoCodecs.Add("hevc");
        }

        if (caps.CanPlayVideo("video/mp4; codecs=\"vc-1\""))
        {
            mp4VideoCodecs.Add("vc1");
        }

        if (canPlayVp8)
        {
            webmVideoCodecs.Add("vp8");
        }

        var webmReliable = !browser.Safari || browser.VersionMajor is >= 15 and < 17;
        if (canPlayVp9)
        {
            // iOS safari may fail to direct play vp9 in mp4; Firefox on macOS fails to detect HDR there.
            if (!browser.Ios && !(browser.Firefox && browser.Osx))
            {
                mp4VideoCodecs.Add("vp9");
            }

            if (browser.Safari || browser.EdgeChromium || browser.Chrome || browser.Firefox)
            {
                hlsInFmp4VideoCodecs.Add("vp9");
            }

            if (webmReliable)
            {
                webmVideoCodecs.Add("vp9");
            }
        }

        if (canPlayAv1)
        {
            mp4VideoCodecs.Add("av1");
            if (webmReliable)
            {
                webmVideoCodecs.Add("av1");
            }
        }

        if (!browser.Safari && canPlayVp8)
        {
            videoAudioCodecs.Add("vorbis");
            mkvAudioCodecs.Add("vorbis");
        }

        if (webmVideoCodecs.Count > 0)
        {
            directPlayProfiles.Add(VideoDirectPlay("webm", webmVideoCodecs, webmAudioCodecs));
        }

        if (mp4VideoCodecs.Count > 0)
        {
            directPlayProfiles.Add(VideoDirectPlay("mp4,m4v", mp4VideoCodecs, videoAudioCodecs));
        }

        if (canPlayMkv && mp4VideoCodecs.Count > 0)
        {
            directPlayProfiles.Add(VideoDirectPlay("mkv", mp4VideoCodecs, mkvAudioCodecs));
        }

        // Of the containers that cannot be tested for, only mov applies to desktop and mobile browsers.
        if (browser.Safari || browser.Chrome || browser.EdgeChromium)
        {
            directPlayProfiles.Add(VideoDirectPlay("mov", ["h264"], videoAudioCodecs));
        }

        var alwaysRemuxFlac = appSettings.GetBool("alwaysRemuxFlac", false);
        var alwaysRemuxMp3 = appSettings.GetBool("alwaysRemuxMp3", false);
        foreach (var audioFormat in AudioFormats.Where(format => CanPlayAudioFormat(caps, browser, format)))
        {
            // Place container overrides before the direct profile for remux container override.
            if (audioFormat == "mp3" && !canPlayMp3VideoAudioInHls)
            {
                directPlayProfiles.Add(new JsonObject { ["Container"] = "ts", ["AudioCodec"] = "mp3", ["Type"] = "Audio" });
            }

            if (audioFormat == "flac" && alwaysRemuxFlac)
            {
                directPlayProfiles.Add(new JsonObject { ["Container"] = "mp4", ["AudioCodec"] = "flac", ["Type"] = "Audio" });
            }
            else if (audioFormat != "mp3" || !alwaysRemuxMp3)
            {
                directPlayProfiles.Add(new JsonObject { ["Container"] = audioFormat, ["Type"] = "Audio" });
            }

            if (audioFormat is "opus" or "webma")
            {
                directPlayProfiles.Add(new JsonObject { ["Container"] = "webm", ["AudioCodec"] = audioFormat, ["Type"] = "Audio" });
            }

            // aac also appears in the m4a and m4b containers
            if (audioFormat is "aac" or "alac")
            {
                directPlayProfiles.Add(new JsonObject { ["Container"] = "m4a", ["AudioCodec"] = audioFormat, ["Type"] = "Audio" });
                directPlayProfiles.Add(new JsonObject { ["Container"] = "m4b", ["AudioCodec"] = audioFormat, ["Type"] = "Audio" });
            }
        }

        if (safariSupportsOpus)
        {
            directPlayProfiles.Add(new JsonObject { ["Container"] = "mp4", ["AudioCodec"] = "opus", ["Type"] = "Audio" });
        }

        var transcodingProfiles = new JsonArray();
        profile["TranscodingProfiles"] = transcodingProfiles;

        var hlsBreakOnNonKeyFrames = browser.Ios || browser.Osx || !caps.CanPlayNativeHls;
        var enableFmp4Hls = AppSettings.ToBoolean(
            userSettings.Get("preferFmp4HlsContainer", false),
            browser.Safari || browser.Firefox || browser.Chrome || browser.EdgeChromium);
        if (browser.Safari && !((browser.Ios && browser.IosVersion >= 11) || browser.Osx))
        {
            enableFmp4Hls = false;
        }

        // fMP4 in Firefox 149 is largely broken (bad moof::traf::tfdt handling)
        if (browser.Firefox && browser.VersionMajor == 149)
        {
            enableFmp4Hls = false;
        }

        var minSegments = browser.Ios || browser.Osx ? "2" : "1";
        if (caps.CanPlayHls)
        {
            transcodingProfiles.Add(new JsonObject
            {
                ["Container"] = enableFmp4Hls ? "mp4" : "ts",
                ["Type"] = "Audio",
                ["AudioCodec"] = "aac",
                ["Context"] = "Streaming",
                ["Protocol"] = "hls",
                ["MaxAudioChannels"] = channelsText,
                ["MinSegments"] = minSegments,
                ["BreakOnNonKeyFrames"] = hlsBreakOnNonKeyFrames,
                ["EnableAudioVbrEncoding"] = !appSettings.GetBool("disableVbrAudio", false),
            });
        }

        // For streaming, prioritize opus transcoding after mp3/aac; for static it is fine first.
        foreach (var (formats, context) in new[] { (new[] { "aac", "mp3", "opus", "wav" }, "Streaming"), (new[] { "opus", "mp3", "aac", "wav" }, "Static") })
        {
            foreach (var audioFormat in formats.Where(format => CanPlayAudioFormat(caps, browser, format)))
            {
                transcodingProfiles.Add(new JsonObject
                {
                    ["Container"] = audioFormat,
                    ["Type"] = "Audio",
                    ["AudioCodec"] = audioFormat,
                    ["Context"] = context,
                    ["Protocol"] = "http",
                    ["MaxAudioChannels"] = channelsText,
                });
            }
        }

        if (caps.CanPlayHls && options.EnableHls)
        {
            var enableLimitedSegmentLength = userSettings.GetBool("limitSegmentLength", false, false);

            void AddHls(string container, List<string> videoCodecs, List<string> audioCodecs)
            {
                // There is no filter for TS/MP4 in the API: HLS support is specified in general and a
                // direct play error is answered with a retry.
                directPlayProfiles.Add(VideoDirectPlay("hls", videoCodecs, audioCodecs));
                var transcoding = new JsonObject
                {
                    ["Container"] = container,
                    ["Type"] = "Video",
                    ["AudioCodec"] = string.Join(',', audioCodecs),
                    ["VideoCodec"] = string.Join(',', videoCodecs),
                    ["Context"] = "Streaming",
                    ["Protocol"] = "hls",
                    ["MaxAudioChannels"] = channelsText,
                    ["MinSegments"] = minSegments,
                    ["BreakOnNonKeyFrames"] = hlsBreakOnNonKeyFrames,
                };
                if (enableLimitedSegmentLength)
                {
                    transcoding["SegmentLength"] = 1;
                }

                transcodingProfiles.Add(transcoding);
            }

            if (hlsInFmp4VideoCodecs.Count > 0 && hlsInFmp4VideoAudioCodecs.Count > 0 && enableFmp4Hls)
            {
                AddHls("mp4", hlsInFmp4VideoCodecs, hlsInFmp4VideoAudioCodecs);
            }

            if (hlsInTsVideoCodecs.Count > 0 && hlsInTsVideoAudioCodecs.Count > 0)
            {
                AddHls("ts", hlsInTsVideoCodecs, hlsInTsVideoAudioCodecs);
            }
        }

        profile["ContainerProfiles"] = new JsonArray();

        var codecProfiles = new JsonArray();
        profile["CodecProfiles"] = codecProfiles;

        var supportsSecondaryAudio = caps.AudioTracks && !browser.Firefox;

        var aacCodecProfileConditions = new JsonArray();
        if (!caps.CanPlayVideo("video/mp4; codecs=\"avc1.640029, mp4a.40.5\""))
        {
            aacCodecProfileConditions.Add(new JsonObject { ["Condition"] = "NotEquals", ["Property"] = "AudioProfile", ["Value"] = "HE-AAC" });
        }

        if (!supportsSecondaryAudio)
        {
            aacCodecProfileConditions.Add(Condition("Equals", "IsSecondaryAudio", "false", false));
        }

        if (aacCodecProfileConditions.Count > 0)
        {
            codecProfiles.Add(new JsonObject { ["Type"] = "VideoAudio", ["Codec"] = "aac", ["Conditions"] = aacCodecProfileConditions });
        }

        var globalAudioCodecProfileConditions = new JsonArray();
        var globalVideoAudioCodecProfileConditions = new JsonArray();
        if (allowedAudioChannels > 0)
        {
            globalAudioCodecProfileConditions.Add(Condition("LessThanEqual", "AudioChannels", channelsText, false));
            globalVideoAudioCodecProfileConditions.Add(Condition("LessThanEqual", "AudioChannels", channelsText, false));
        }

        if (!supportsSecondaryAudio)
        {
            globalVideoAudioCodecProfileConditions.Add(Condition("Equals", "IsSecondaryAudio", "false", false));
        }

        if (globalAudioCodecProfileConditions.Count > 0)
        {
            codecProfiles.Add(new JsonObject { ["Type"] = "Audio", ["Conditions"] = globalAudioCodecProfileConditions });
        }

        if (globalVideoAudioCodecProfileConditions.Count > 0)
        {
            codecProfiles.Add(new JsonObject { ["Type"] = "VideoAudio", ["Conditions"] = globalVideoAudioCodecProfileConditions });
        }

        if (safariSupportsOpus)
        {
            // Safari doesn't support opus with more than 2 channels: split each video transcoding
            // profile so that opus only applies to stereo audio.
            codecProfiles.Add(new JsonObject
            {
                ["Type"] = "VideoAudio",
                ["Codec"] = "opus",
                ["Conditions"] = new JsonArray(Condition("LessThanEqual", "AudioChannels", "2", false)),
            });

            var opusTranscodingProfiles = new List<JsonObject>();
            foreach (var transcodingProfile in transcodingProfiles.OfType<JsonObject>().Where(IsVideo))
            {
                var audioCodecs = ((string?)transcodingProfile["AudioCodec"] ?? string.Empty).Split(',');
                if (!audioCodecs.Contains("opus"))
                {
                    continue;
                }

                var opusProfile = (JsonObject)transcodingProfile.DeepClone();
                opusProfile["AudioCodec"] = "opus";
                var applyConditions = opusProfile["ApplyConditions"] as JsonArray ?? [];
                applyConditions.Add(Condition("LessThanEqual", "AudioChannels", "2", false));
                opusProfile["ApplyConditions"] = applyConditions;
                opusTranscodingProfiles.Add(opusProfile);
                transcodingProfile["AudioCodec"] = string.Join(',', audioCodecs.Where(codec => codec != "opus"));
            }

            foreach (var opusProfile in opusTranscodingProfiles)
            {
                transcodingProfiles.Add(opusProfile);
            }
        }

        var maxH264Level = 42;
        var h264Profiles = "high|main|baseline|constrained baseline";
        if (caps.CanPlayVideo("video/mp4; codecs=\"avc1.640833\""))
        {
            maxH264Level = 51;
        }

        if (caps.CanPlayVideo("video/mp4; codecs=\"avc1.640834\""))
        {
            maxH264Level = 52;
        }

        // These tests pass in safari, but playback fails
        if (caps.CanPlayVideo("video/mp4; codecs=\"avc1.6e0033\"") && !browser.Safari && !browser.Ios && !browser.Mobile)
        {
            h264Profiles += "|high 10";
        }

        var maxHevcLevel = 120;
        var hevcProfiles = "main";
        if (CanPlayHevcTag(caps, "1.4.L123"))
        {
            maxHevcLevel = 123;
        }

        foreach (var level in new[] { 123, 153, 183, 186 })
        {
            if (CanPlayHevcTag(caps, "2.4.L" + level.ToString(CultureInfo.InvariantCulture)))
            {
                maxHevcLevel = level;
                hevcProfiles = "main|main 10";
            }
        }

        var maxAv1Level = 15;
        const string av1Profiles = "main";
        foreach (var level in new[] { 16, 17, 18, 19 })
        {
            var tag = level.ToString(CultureInfo.InvariantCulture);
            if (caps.CanPlayVideo($"video/mp4; codecs=\"av01.0.{tag}M.08\"") && caps.CanPlayVideo($"video/mp4; codecs=\"av01.0.{tag}M.10\""))
            {
                maxAv1Level = level;
            }
        }

        const string h264VideoRangeTypes = "SDR";
        var hevcVideoRangeTypes = "SDR";
        var vp9VideoRangeTypes = "SDR";
        var av1VideoRangeTypes = "SDR";

        if (SupportsHdr10(browser))
        {
            // HDR10+ videos can be safely played on all HDR10 capable devices, just without the dynamic metadata.
            hevcVideoRangeTypes += "|HDR10|HDR10Plus";
            vp9VideoRangeTypes += "|HDR10|HDR10Plus";
            av1VideoRangeTypes += "|HDR10|HDR10Plus";

            // supportsHlg() falls back to supportsHdr10()
            hevcVideoRangeTypes += "|HLG";
            vp9VideoRangeTypes += "|HLG";
            av1VideoRangeTypes += "|HLG";
        }

        if (browser.Safari && ((browser.Ios && browser.IosVersion >= 13) || browser.Osx))
        {
            if (caps.CanPlayVideo("video/mp4; codecs=\"dvh1.05.06\""))
            {
                hevcVideoRangeTypes += "|DOVI";
            }

            if (caps.CanPlayVideo("video/mp4; codecs=\"dvh1.08.06\""))
            {
                hevcVideoRangeTypes += "|DOVIWithHDR10|DOVIWithHLG|DOVIWithSDR|DOVIWithHDR10Plus";
            }

            if (caps.CanPlayVideo("video/mp4; codecs=\"dav1.10.06\""))
            {
                av1VideoRangeTypes += "|DOVI|DOVIWithHDR10|DOVIWithHLG|DOVIWithSDR|DOVIWithHDR10Plus";
            }
        }

        var h264Conditions = new JsonArray(
            Condition("EqualsAny", "VideoProfile", h264Profiles, false),
            Condition("EqualsAny", "VideoRangeType", h264VideoRangeTypes, false),
            Condition("LessThanEqual", "VideoLevel", maxH264Level.ToString(CultureInfo.InvariantCulture), false));
        var hevcConditions = new JsonArray(
            Condition("EqualsAny", "VideoProfile", hevcProfiles, false),
            Condition("EqualsAny", "VideoRangeType", hevcVideoRangeTypes, false),
            Condition("LessThanEqual", "VideoLevel", maxHevcLevel.ToString(CultureInfo.InvariantCulture), false));
        var vp9Conditions = new JsonArray(Condition("EqualsAny", "VideoRangeType", vp9VideoRangeTypes, false));
        var av1Conditions = new JsonArray(
            Condition("EqualsAny", "VideoProfile", av1Profiles, false),
            Condition("EqualsAny", "VideoRangeType", av1VideoRangeTypes, false),
            Condition("LessThanEqual", "VideoLevel", maxAv1Level.ToString(CultureInfo.InvariantCulture), false));

        var supportsAnamorphicVideo = browser.Chrome || browser.Firefox || browser.Safari || browser.EdgeChromium || browser.Ios || browser.Android;
        if (!supportsAnamorphicVideo)
        {
            h264Conditions.Add(Condition("NotEquals", "IsAnamorphic", "true", false));
            hevcConditions.Add(Condition("NotEquals", "IsAnamorphic", "true", false));
            av1Conditions.Add(Condition("NotEquals", "IsAnamorphic", "true", false));
        }

        h264Conditions.Add(Condition("NotEquals", "IsInterlaced", "true", false));
        hevcConditions.Add(Condition("NotEquals", "IsInterlaced", "true", false));

        // Safari quirks for HEVC direct-play
        if (browser.Safari)
        {
            // Only hvc1 & dvh1 tags are supported
            hevcConditions.Add(Condition("EqualsAny", "VideoCodecTag", "hvc1|dvh1", true));

            // Framerate above 60fps is not supported
            hevcConditions.Add(Condition("LessThanEqual", "VideoFramerate", "60", true));
        }

        // On iOS 12.x, for TS container max h264 level is 4.2
        if (browser.Ios && browser.IosVersion < 13)
        {
            foreach (var container in new[] { "ts", "mp4" })
            {
                var conditions = new JsonArray();
                foreach (var condition in h264Conditions.OfType<JsonObject>().Where(c => (string?)c["Property"] != "VideoLevel"))
                {
                    conditions.Add(condition.DeepClone());
                }

                conditions.Add(Condition("LessThanEqual", "VideoLevel", "42", false));
                codecProfiles.Add(new JsonObject { ["Type"] = "Video", ["Codec"] = "h264", ["Container"] = container, ["Conditions"] = conditions });
            }
        }

        if (browser.Safari && appSettings.GetBool("enableHi10p", false))
        {
            codecProfiles.Add(new JsonObject
            {
                ["Type"] = "Video",
                ["Container"] = "hls",
                ["SubContainer"] = "mp4",
                ["Codec"] = "h264",
                ["Conditions"] = new JsonArray(Condition("EqualsAny", "VideoProfile", h264Profiles + "|high 10", false)),
            });
        }

        codecProfiles.Add(new JsonObject { ["Type"] = "Video", ["Codec"] = "h264", ["Conditions"] = h264Conditions });
        codecProfiles.Add(new JsonObject { ["Type"] = "Video", ["Codec"] = "hevc", ["Conditions"] = hevcConditions });
        codecProfiles.Add(new JsonObject { ["Type"] = "Video", ["Codec"] = "vp9", ["Conditions"] = vp9Conditions });
        codecProfiles.Add(new JsonObject { ["Type"] = "Video", ["Codec"] = "av1", ["Conditions"] = av1Conditions });

        // Subtitle profiles: mirror browserDeviceProfile.js. Complex formats are external when their
        // client renderer is enabled; otherwise the server burns them into the video.
        var subtitleProfiles = new JsonArray();
        profile["SubtitleProfiles"] = subtitleProfiles;
        var subtitleBurnIn = appSettings.Get("subtitleburnin");
        if (subtitleBurnIn != "all")
        {
            if (caps.TextTracks)
            {
                subtitleProfiles.Add(new JsonObject { ["Format"] = "vtt", ["Method"] = "External" });
            }

            if (!options.IsRetry && subtitleBurnIn != "allcomplexformats")
            {
                subtitleProfiles.Add(new JsonObject { ["Format"] = "ass", ["Method"] = "External" });
                subtitleProfiles.Add(new JsonObject { ["Format"] = "ssa", ["Method"] = "External" });
            }

            if (caps.Canvas2D && !options.IsRetry && appSettings.Get("subtitlerenderpgs") == "true"
                && subtitleBurnIn is not "allcomplexformats" and not "onlyimageformats")
            {
                subtitleProfiles.Add(new JsonObject { ["Format"] = "pgssub", ["Method"] = "External" });
            }
        }

        profile["ResponseProfiles"] = new JsonArray(new JsonObject { ["Type"] = "Video", ["Container"] = "m4v", ["MimeType"] = "video/mp4" });
        return profile;
    }

    /// <summary>Port of canPlayAudioFormat().</summary>
    public static bool CanPlayAudioFormat(MediaCapabilities caps, PlaybackBrowser browser, string format) => format switch
    {
        "opus" => caps.CanPlayAudio("audio/ogg; codecs=\"opus\""),
        "alac" => browser.Ios || (browser.Osx && browser.Safari) || caps.CanPlayAudio("audio/alac"),
        "mp2" => false,
        "webma" => caps.CanPlayAudio("audio/webm"),
        _ => caps.CanPlayAudio("audio/" + format),
    };

    private static bool IsVideo(JsonObject transcodingProfile) => (string?)transcodingProfile["Type"] == "Video";

    private static JsonObject Condition(string condition, string property, string value, bool isRequired) => new()
    {
        ["Condition"] = condition,
        ["Property"] = property,
        ["Value"] = value,
        ["IsRequired"] = isRequired,
    };

    private static JsonObject VideoDirectPlay(string container, IEnumerable<string> videoCodecs, IEnumerable<string> audioCodecs) => new()
    {
        ["Container"] = container,
        ["Type"] = "Video",
        ["VideoCodec"] = string.Join(',', videoCodecs),
        ["AudioCodec"] = string.Join(',', audioCodecs),
    };

    private static void MoveToFront(JsonArray transcodingProfiles, string property, string? preferred)
    {
        if (string.IsNullOrEmpty(preferred))
        {
            return;
        }

        foreach (var transcodingProfile in transcodingProfiles.OfType<JsonObject>().Where(IsVideo))
        {
            var codecs = ((string?)transcodingProfile[property] ?? string.Empty).Split(',').ToList();
            if (codecs.Remove(preferred))
            {
                codecs.Insert(0, preferred);
                transcodingProfile[property] = string.Join(',', codecs);
            }
        }
    }

    private static int ParseInt(string? text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static bool CanPlayHevc(MediaCapabilities caps) =>
        caps.CanPlayVideo("video/mp4; codecs=\"hvc1.1.L120\"") || caps.CanPlayVideo("video/mp4; codecs=\"hev1.1.L120\"")
        || caps.CanPlayVideo("video/mp4; codecs=\"hvc1.1.0.L120\"") || caps.CanPlayVideo("video/mp4; codecs=\"hev1.1.0.L120\"");

    private static bool CanPlayHevcTag(MediaCapabilities caps, string tag) =>
        caps.CanPlayVideo($"video/mp4; codecs=\"hvc1.{tag}\"") || caps.CanPlayVideo($"video/mp4; codecs=\"hev1.{tag}\"");

    private static bool CanPlayAv1(MediaCapabilities caps) =>
        caps.CanPlayVideo("video/mp4; codecs=\"av01.0.15M.08\"") && caps.CanPlayVideo("video/mp4; codecs=\"av01.0.15M.10\"");

    private static bool SupportsAc3(MediaCapabilities caps, PlaybackBrowser browser) =>
        !(browser.Ios && browser.IosVersion < 11) && caps.CanPlayVideo("audio/mp4; codecs=\"ac-3\"");

    private static bool SupportsEac3(MediaCapabilities caps, PlaybackBrowser browser) =>
        !(browser.Ios && browser.IosVersion < 11) && caps.CanPlayVideo("audio/mp4; codecs=\"ec-3\"");

    private static bool CanPlayMkv(MediaCapabilities caps, PlaybackBrowser browser)
    {
        // Firefox's mkv support forces preloading the whole file before playback starts.
        if (browser.Firefox)
        {
            return false;
        }

        if (caps.CanPlayVideo("video/x-matroska") || caps.CanPlayVideo("video/mkv"))
        {
            return true;
        }

        return browser.EdgeChromium && browser.Windows;
    }

    private static bool SupportsHdr10(PlaybackBrowser browser) =>
        (browser.Safari && ((browser.Ios && browser.IosVersion >= 11) || browser.Osx))
        || (browser.EdgeChromium && browser.VersionMajor >= 121)
        || (browser.Chrome && !browser.Mobile)
        || (browser.Firefox && browser.Osx && !browser.Ios && browser.VersionMajor >= 100);

    private static int GetPhysicalAudioChannels(MediaCapabilities caps, PlaybackBrowser browser, int allowedAudioChannels)
    {
        if (allowedAudioChannels > 0)
        {
            return allowedAudioChannels;
        }

        var isSurroundSoundSupportedBrowser = browser.Safari || browser.Chrome || browser.EdgeChromium || browser.Firefox;
        var isAc3Eac3Supported = SupportsAc3(caps, browser) || SupportsEac3(caps, browser);
        var speakerCount = caps.SpeakerCount;

        // AC3/EAC3 hinted that the device is able to play dolby surround sound.
        if (isAc3Eac3Supported && isSurroundSoundSupportedBrowser)
        {
            return speakerCount > 6 ? speakerCount : 6;
        }

        if (speakerCount > 2)
        {
            return isSurroundSoundSupportedBrowser ? speakerCount : 2;
        }

        if (speakerCount > 0)
        {
            return speakerCount;
        }

        return isSurroundSoundSupportedBrowser ? 6 : 2;
    }
}
