using System.Text.Json.Nodes;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>The device profile sent to the server, built from what a (faked) browser says it can play.</summary>
public class DeviceProfileTests
{
    private const long Ticks = PlaybackServer.MovieTicks;

    [Fact]
    public async Task Chrome_direct_plays_the_licensed_and_the_open_codecs()
    {
        await using var harness = await PlaybackHarness.CreateAsync();

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");

        Assert.Equal(120000000, (int)profile["MaxStreamingBitrate"]!);
        Assert.Equal(100000000, (int)profile["MaxStaticBitrate"]!);
        Assert.Equal(384000, (int)profile["MusicStreamingTranscodingBitrate"]!);

        Assert.Equal(new[] { "vp8", "vp9", "av1" }, Codecs(DirectPlay(profile, "webm"), "VideoCodec"));
        Assert.Equal(new[] { "vorbis", "opus" }, Codecs(DirectPlay(profile, "webm"), "AudioCodec"));
        var mp4 = DirectPlay(profile, "mp4,m4v");
        Assert.Equal(new[] { "h264", "vp9", "av1" }, Codecs(mp4, "VideoCodec"));
        Assert.Contains("aac", Codecs(mp4, "AudioCodec"));
        Assert.Contains("mp3", Codecs(mp4, "AudioCodec"));
        Assert.Contains("opus", Codecs(mp4, "AudioCodec"));
        Assert.DoesNotContain("ac3", Codecs(mp4, "AudioCodec"));
        Assert.Equal(Codecs(mp4, "VideoCodec"), Codecs(DirectPlay(profile, "mkv"), "VideoCodec"));
        Assert.Equal(new[] { "h264" }, Codecs(DirectPlay(profile, "mov"), "VideoCodec"));

        var audio = Of(profile, "DirectPlayProfiles").Where(p => (string?)p["Type"] == "Audio").Select(p => (string?)p["Container"]).ToList();
        Assert.Contains("mp3", audio);
        Assert.Contains("flac", audio);
        Assert.Contains("opus", audio);
        Assert.Contains("m4a", audio);
    }

    [Fact]
    public async Task Chrome_transcodes_to_hls_in_fmp4_first_and_takes_subtitles_as_webvtt()
    {
        await using var harness = await PlaybackHarness.CreateAsync();

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");

        var video = Of(profile, "TranscodingProfiles").Where(p => (string?)p["Type"] == "Video").ToList();
        Assert.Equal(new[] { "mp4", "ts" }, video.Select(p => (string?)p["Container"]));
        Assert.All(video, p => Assert.Equal("hls", (string?)p["Protocol"]));
        Assert.All(video, p => Assert.Equal("Streaming", (string?)p["Context"]));
        Assert.Equal(new[] { "av1", "h264", "vp9" }, Codecs(video[0], "VideoCodec"));
        Assert.Equal(new[] { "h264" }, Codecs(video[1], "VideoCodec"));
        Assert.Contains("aac", Codecs(video[0], "AudioCodec"));
        Assert.Contains("opus", Codecs(video[0], "AudioCodec"));
        Assert.Equal("2", (string?)video[0]["MaxAudioChannels"]);
        Assert.Equal("1", (string?)video[0]["MinSegments"]);
        Assert.True((bool)video[0]["BreakOnNonKeyFrames"]!);

        // hls.js plays the stream: the codecs it cannot demux are kept out of HLS.
        Assert.All(video, p => Assert.DoesNotContain("ac3", Codecs(p, "AudioCodec")));

        var audio = Of(profile, "TranscodingProfiles").First(p => (string?)p["Type"] == "Audio");
        Assert.Equal("hls", (string?)audio["Protocol"]);
        Assert.Equal("aac", (string?)audio["AudioCodec"]);
        Assert.True((bool)audio["EnableAudioVbrEncoding"]!);

        var subtitles = Of(profile, "SubtitleProfiles").ToList();
        Assert.Contains(subtitles, p => (string?)p["Format"] == "vtt" && (string?)p["Method"] == "External");
        Assert.Contains(subtitles, p => (string?)p["Format"] == "ass" && (string?)p["Method"] == "External");
        Assert.Contains(subtitles, p => (string?)p["Format"] == "ssa" && (string?)p["Method"] == "External");
        Assert.DoesNotContain(subtitles, p => (string?)p["Format"] == "pgssub");

        var codecProfiles = Of(profile, "CodecProfiles").Where(p => (string?)p["Type"] == "Video").Select(p => (string?)p["Codec"]).ToList();
        Assert.All(new[] { "h264", "hevc", "vp9", "av1" }, codec => Assert.Contains(codec, codecProfiles));
    }

    [Fact]
    public async Task Pgs_is_advertised_only_when_image_rendering_is_enabled()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var settings = harness.Get<AppSettings>();
        settings.Set("subtitlerenderpgs", true);

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");
        Assert.Contains(Of(profile, "SubtitleProfiles"), p => (string?)p["Format"] == "pgssub" && (string?)p["Method"] == "External");

        var retry = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video", isRetry: true);
        Assert.DoesNotContain(Of(retry, "SubtitleProfiles"), p => (string?)p["Format"] is "ass" or "ssa" or "pgssub");
    }

    [Fact]
    public async Task A_browser_without_the_licensed_codecs_does_not_claim_h264()
    {
        await using var harness = await PlaybackHarness.CreateAsync();

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.OpenCodecsOnly, Ticks, "Video");

        Assert.Equal(new[] { "vp8", "vp9" }, Codecs(DirectPlay(profile, "webm"), "VideoCodec"));
        Assert.Equal(new[] { "vp9" }, Codecs(DirectPlay(profile, "mp4,m4v"), "VideoCodec"));
        Assert.DoesNotContain("aac", Codecs(DirectPlay(profile, "mp4,m4v"), "AudioCodec"));
        Assert.DoesNotContain(
            Of(profile, "TranscodingProfiles"),
            p => (string?)p["Type"] == "Video" && Codecs(p, "VideoCodec").Contains("h264"));

        // No AAC decoder: audio is not offered as AAC over plain http either.
        Assert.DoesNotContain(
            Of(profile, "TranscodingProfiles"),
            p => (string?)p["Type"] == "Audio" && (string?)p["Protocol"] == "http" && (string?)p["Container"] == "aac");
        Assert.Contains(
            Of(profile, "TranscodingProfiles"),
            p => (string?)p["Type"] == "Audio" && (string?)p["Protocol"] == "http" && (string?)p["Container"] == "mp3");
    }

    [Fact]
    public async Task Safari_plays_hevc_and_ac3_and_uses_its_own_hls()
    {
        await using var harness = await PlaybackHarness.CreateAsync(app =>
            app.Browser.Environment = app.Browser.Environment with { UserAgent = FakeCapabilityProbe.SafariUserAgent });
        var builder = harness.Get<DeviceProfileBuilder>();
        var browser = PlaybackBrowser.Parse(FakeCapabilityProbe.SafariUserAgent, harness.Get<AppHost>().Browser);

        Assert.True(browser.Safari);
        Assert.True(browser.Osx);
        Assert.Equal(17, browser.VersionMajor);
        Assert.False(DeviceProfileBuilder.EnableHlsJsPlayer(FakeCapabilityProbe.Safari, browser, Ticks, "Video"));

        // A stream without a known length (live) goes through hls.js even there.
        Assert.True(DeviceProfileBuilder.EnableHlsJsPlayer(FakeCapabilityProbe.Safari, browser, null, "Video"));

        var profile = builder.GetDeviceProfile(FakeCapabilityProbe.Safari, Ticks, "Video");

        Assert.Null(DirectPlay(profile, "webm"));
        Assert.Null(DirectPlay(profile, "mkv"));
        var mp4 = DirectPlay(profile, "mp4,m4v");
        Assert.Equal(new[] { "h264", "hevc" }, Codecs(mp4, "VideoCodec"));
        Assert.Contains("ac3", Codecs(mp4, "AudioCodec"));
        Assert.Contains("eac3", Codecs(mp4, "AudioCodec"));

        var video = Of(profile, "TranscodingProfiles").Where(p => (string?)p["Type"] == "Video").ToList();
        Assert.Equal("mp4", (string?)video[0]["Container"]);
        Assert.Contains("hevc", Codecs(video[0], "VideoCodec"));
        Assert.Contains("ac3", Codecs(video[0], "AudioCodec"));
        Assert.Equal("6", (string?)video[0]["MaxAudioChannels"]);
        Assert.Equal("2", (string?)video[0]["MinSegments"]);
    }

    [Fact]
    public async Task Firefox_gets_no_matroska_profile()
    {
        await using var harness = await PlaybackHarness.CreateAsync(app =>
            app.Browser.Environment = app.Browser.Environment with { UserAgent = FakeCapabilityProbe.FirefoxUserAgent });

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");

        Assert.Null(DirectPlay(profile, "mkv"));
        Assert.Null(DirectPlay(profile, "mov"));
        Assert.NotNull(DirectPlay(profile, "webm"));
    }

    [Fact]
    public async Task Hls_js_is_used_where_the_browser_has_media_source_extensions()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var chrome = PlaybackBrowser.Parse(harness.App.Browser.Environment.UserAgent, harness.Get<AppHost>().Browser);

        Assert.True(chrome.Chrome);
        Assert.Equal(130, chrome.VersionMajor);
        Assert.True(DeviceProfileBuilder.EnableHlsJsPlayer(FakeCapabilityProbe.Chrome, chrome, Ticks, "Video"));
        Assert.False(DeviceProfileBuilder.EnableHlsJsPlayer(FakeCapabilityProbe.Chrome with { MediaSource = false }, chrome, Ticks, "Video"));
        Assert.False(DeviceProfileBuilder.EnableHlsJsPlayer(FakeCapabilityProbe.Chrome, chrome with { Ios = true }, Ticks, "Video"));
    }

    [Fact]
    public async Task The_settings_of_the_device_change_the_profile()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var settings = harness.Get<AppSettings>();
        var builder = harness.Get<DeviceProfileBuilder>();

        settings.Set("subtitleburnin", "all");
        settings.Set("disableVbrAudio", true);
        settings.Set("alwaysRemuxMp3", true);
        settings.Set("enableDts", true);
        settings.Set("enableTrueHd", true);
        var profile = builder.GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");

        // Burn in everything: no subtitle format is taken as a file.
        Assert.Empty(Of(profile, "SubtitleProfiles"));
        Assert.False((bool)Of(profile, "TranscodingProfiles").First(p => (string?)p["Type"] == "Audio")["EnableAudioVbrEncoding"]!);
        Assert.DoesNotContain(Of(profile, "DirectPlayProfiles"), p => (string?)p["Type"] == "Audio" && (string?)p["Container"] == "mp3");
        Assert.Contains(Of(profile, "DirectPlayProfiles"), p => (string?)p["Type"] == "Audio" && (string?)p["Container"] == "ts" && (string?)p["AudioCodec"] == "mp3");
        Assert.Contains("dts", Codecs(DirectPlay(profile, "mkv"), "AudioCodec"));
        Assert.Contains("truehd", Codecs(DirectPlay(profile, "mkv"), "AudioCodec"));
    }

    [Fact]
    public async Task The_preferred_transcoding_codec_moves_to_the_front()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        harness.Get<AppSettings>().Set("preferredTranscodeVideoCodec", "h264");

        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, Ticks, "Video");

        var video = Of(profile, "TranscodingProfiles").First(p => (string?)p["Type"] == "Video");
        Assert.Equal("h264", Codecs(video, "VideoCodec")[0]);
        Assert.Contains("vp9", Codecs(video, "VideoCodec"));
    }

    [Fact]
    public async Task The_player_asks_the_probe_once_and_builds_the_profile_for_the_item()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        harness.Fakes.Probe.Caps = FakeCapabilityProbe.OpenCodecsOnly;
        var player = harness.Services.GetServices<IMediaPlayer>().First(p => p.CanPlayMediaType("Video"));

        var profile = await player.GetDeviceProfileAsync(PlaybackServer.Movie());
        await player.GetDeviceProfileAsync(PlaybackServer.Movie());

        Assert.Equal(new[] { "vp9" }, Codecs(DirectPlay(profile, "mp4,m4v"), "VideoCodec"));
        Assert.True(harness.Fakes.Probe.Probes >= 1);
    }

    private static IEnumerable<JsonObject> Of(JsonObject profile, string list) => profile[list]!.AsArray().OfType<JsonObject>();

    private static JsonObject? DirectPlay(JsonObject profile, string container) =>
        Of(profile, "DirectPlayProfiles").FirstOrDefault(p => (string?)p["Type"] == "Video" && (string?)p["Container"] == container);

    private static string[] Codecs(JsonObject? profile, string property) =>
        ((string?)profile?[property] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
}
