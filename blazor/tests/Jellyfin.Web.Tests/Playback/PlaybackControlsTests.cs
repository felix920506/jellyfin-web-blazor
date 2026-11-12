using System.Text.Json.Nodes;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>Transport, volume, rate, aspect ratio, tracks and quality: what the playback UI calls, and what is remembered.</summary>
public class PlaybackControlsTests
{
    [Fact]
    public async Task Seeking_a_seekable_stream_moves_the_element()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();

        await harness.Manager.SeekAsync(250_000_000);
        Assert.Equal("seek video 25", harness.Player.Calls[^1]);
        Assert.Equal(250_000_000, harness.Manager.CurrentTimeTicks);

        await harness.Manager.SeekPercentAsync(50);
        Assert.Equal("seek video 30", harness.Player.Calls[^1]);

        await harness.Manager.SeekMsAsync(1500);
        Assert.Equal("seek video 1.5", harness.Player.Calls[^1]);

        // No new stream is needed for any of it.
        Assert.Single(harness.Player.Loads);
        Assert.Single(PlaybackServer.PlaybackInfoRequests(harness.Server));
    }

    [Fact]
    public async Task Skipping_uses_the_users_skip_lengths_and_stops_at_the_beginning()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 15);

        // Forward by 30 seconds, back by 10: the defaults of the user's settings.
        await harness.Manager.FastForwardAsync();
        Assert.Equal("seek video 45", harness.Player.Calls[^1]);

        await harness.Manager.RewindAsync();
        Assert.Equal("seek video 35", harness.Player.Calls[^1]);

        harness.Player.Tick("video", 4);
        await harness.Manager.RewindAsync();
        Assert.Equal("seek video 0", harness.Player.Calls[^1]);
    }

    [Fact]
    public async Task The_volume_is_set_on_a_cubic_scale_and_remembered_for_the_next_start()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        Assert.Equal(100, harness.Manager.Volume);

        harness.Manager.SetVolume(50);
        Assert.Equal("volume video 0.125", harness.Player.Calls[^1]);
        Assert.Equal(50, harness.Manager.Volume);
        Assert.Equal("0.125", harness.App.Browser.LocalStorage["volume"]);

        harness.Manager.VolumeUp();
        Assert.Equal(52, harness.Manager.Volume);
        harness.Manager.VolumeDown();
        harness.Manager.VolumeDown();
        Assert.Equal(48, harness.Manager.Volume);

        harness.Manager.SetVolume(50);
        harness.Manager.SetMute(true);
        Assert.Equal("mute video True", harness.Player.Calls[^1]);
        Assert.True(harness.Manager.IsMuted);
        harness.Manager.ToggleMute();
        Assert.Equal("mute video False", harness.Player.Calls[^1]);
        Assert.False(harness.Manager.IsMuted);

        // The next thing that plays starts with the saved volume, the audio player included.
        await harness.Manager.StopAsync();
        await harness.PlaySongsAsync();
        Assert.Equal("audio", harness.Player.Loads[^1].Kind);
        Assert.Equal(0.125, harness.Player.LastLoad.Volume);
        Assert.Equal(50, harness.Manager.Volume);
    }

    [Theory]
    [InlineData(1, 100)]
    [InlineData(0.125, 50)]
    [InlineData(0.001, 10)]
    [InlineData(0, 0)]
    public void The_element_volume_maps_to_a_level_by_its_cube_root(double elementVolume, int level) =>
        Assert.Equal(level, HtmlMediaPlayer.ToVolume(elementVolume));

    [Fact]
    public async Task The_playback_rate_is_kept_for_the_browser_session_and_stepped_through_the_menu_values()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        Assert.Equal(1, harness.Manager.PlaybackRate);

        harness.Manager.SetPlaybackRate(1.5);
        Assert.Equal("rate video 1.5", harness.Player.Calls[^1]);
        Assert.Equal(1.5, harness.Manager.PlaybackRate);
        Assert.Equal("1.5", harness.App.Browser.SessionStorage["playbackRateSpeed"]);

        harness.Manager.IncreasePlaybackRate();
        Assert.Equal(1.75, harness.Manager.PlaybackRate);
        harness.Manager.DecreasePlaybackRate();
        harness.Manager.DecreasePlaybackRate();
        Assert.Equal(1.25, harness.Manager.PlaybackRate);

        harness.Manager.SetPlaybackRate(4);
        harness.Manager.IncreasePlaybackRate();
        Assert.Equal(4, harness.Manager.PlaybackRate);
        Assert.Equal("4", harness.App.Browser.SessionStorage["playbackRateSpeed"]);
    }

    [Fact]
    public async Task The_aspect_ratio_cycles_and_is_saved()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        Assert.Equal("auto", harness.Manager.AspectRatio);

        harness.Manager.ToggleAspectRatio();
        Assert.Equal("aspect cover", harness.Player.Calls[^1]);
        Assert.Equal("cover", harness.Manager.AspectRatio);
        Assert.Equal("cover", harness.App.Browser.LocalStorage["aspectRatio"]);

        harness.Manager.ToggleAspectRatio();
        Assert.Equal("fill", harness.Manager.AspectRatio);
        harness.Manager.ToggleAspectRatio();
        Assert.Equal("auto", harness.Manager.AspectRatio);
    }

    [Fact]
    public async Task Fullscreen_and_picture_in_picture_follow_the_element()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        Assert.False(harness.Manager.IsFullscreen);

        harness.Manager.ToggleFullscreen();
        Assert.Equal("fullscreen", harness.Player.Calls[^1]);
        Assert.True(harness.Manager.IsFullscreen);

        harness.Manager.TogglePictureInPicture();
        Assert.Equal("pip True", harness.Player.Calls[^1]);
        Assert.True(harness.Manager.IsPictureInPictureEnabled);
        harness.Manager.TogglePictureInPicture();
        Assert.Equal("pip False", harness.Player.Calls[^1]);
        Assert.False(harness.Manager.IsPictureInPictureEnabled);
    }

    [Fact]
    public async Task The_supported_commands_depend_on_the_player()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        Assert.DoesNotContain("PlaybackRate", harness.Manager.GetSupportedCommands());
        Assert.Contains("ToggleFullscreen", harness.Manager.GetSupportedCommands());

        await harness.PlayMovieAsync();
        var video = harness.Manager.GetSupportedCommands();
        Assert.Contains("SetAspectRatio", video);
        Assert.Contains("PlaybackRate", video);
        Assert.Contains("PictureInPicture", video);
        Assert.Contains("SetMaxStreamingBitrate", video);
        Assert.DoesNotContain("AirPlay", video);
        Assert.DoesNotContain("SetBrightness", video);

        await harness.Manager.StopAsync();
        await harness.PlaySongsAsync();
        var audio = harness.Manager.GetSupportedCommands();
        Assert.Contains("PlaybackRate", audio);
        Assert.DoesNotContain("SetAspectRatio", audio);
        Assert.DoesNotContain("PictureInPicture", audio);
    }

    [Fact]
    public async Task The_tracks_of_the_source_are_listed_for_the_menus()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        Assert.Empty(harness.Manager.AudioTracks());

        await harness.PlayMovieAsync();
        Assert.Equal(new int?[] { 2, 3 }, harness.Manager.AudioTracks().Select(t => t.Index));
        Assert.Equal(new int?[] { 0 }, harness.Manager.SubtitleTracks().Select(t => t.Index));
        Assert.Equal(2, harness.Manager.AudioStreamIndex);
        Assert.True(harness.Manager.CanSeek());
    }

    [Fact]
    public async Task Another_audio_track_needs_a_new_stream_when_the_browser_cannot_switch_tracks()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 12);
        var events = new List<string>();
        harness.Manager.Event += events.Add;

        await harness.Manager.SetAudioStreamIndexAsync(3);

        var requests = PlaybackServer.PlaybackInfoRequests(harness.Server);
        Assert.Equal(2, requests.Count);
        Assert.Equal(3, (int?)requests[1]["AudioStreamIndex"]);
        Assert.Equal(120_000_000, (long?)requests[1]["StartTimeTicks"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)requests[1]["MediaSourceId"]);
        Assert.Equal(2, harness.Player.Loads.Count);
        Assert.Equal(12, harness.Player.LastLoad.StartSeconds);
        Assert.DoesNotContain(harness.Player.Calls, c => c.StartsWith("audiotrack", StringComparison.Ordinal));
        Assert.Equal(3, harness.Manager.AudioStreamIndex);
        Assert.Contains(PlaybackEvents.MediaStreamsChange, events);

        // The stream changed, playback did not stop: no stop report, and the progress carries the track.
        Assert.Empty(harness.StopReports);
        harness.Player.Tick("video", 30);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Any(r => (int?)r["AudioStreamIndex"] == 3), "a report with the new track");
    }

    [Fact]
    public async Task The_next_audio_track_wraps_around()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();

        await harness.Manager.ChangeAudioStreamAsync();
        Assert.Equal(3, harness.Manager.AudioStreamIndex);
        await harness.Manager.ChangeAudioStreamAsync();
        Assert.Equal(2, harness.Manager.AudioStreamIndex);
    }

    [Fact]
    public async Task An_external_text_subtitle_is_fetched_as_track_events_and_shown_by_the_player()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet("/Subtitles/0/0/Stream.js", new JsonObject
        {
            ["TrackEvents"] = new JsonArray(
                new JsonObject { ["StartPositionTicks"] = 10_000_000, ["EndPositionTicks"] = 30_000_000, ["Text"] = @"{\an8}Hello\Nworld" },
                new JsonObject { ["StartPositionTicks"] = 40_000_000, ["EndPositionTicks"] = 55_000_000, ["Text"] = "Second" }),
        });
        await harness.PlayMovieAsync();

        // Nothing is shown at the start: the source has no default subtitle.
        Assert.Null(Assert.Single(harness.Player.SubtitleCues));

        await harness.Manager.SetSubtitleStreamIndexAsync(0);
        await PlaybackHarness.Eventually(() => harness.Player.SubtitleCues.Count == 2, "the cues of the track");
        var cues = harness.Player.SubtitleCues[1]!;
        Assert.Equal(
            new[] { new SubtitleCue(1, 3, "‎Hello\n‎world"), new SubtitleCue(4, 5.5, "‎Second") },
            cues);
        Assert.Equal(0, harness.Manager.SubtitleStreamIndex);
        var request = Assert.Single(harness.Server.RequestsTo("/Subtitles/0/0/Stream.js"));
        Assert.Equal("test-token", request.Query("api_key"));

        // The file is shown by the element: no new stream.
        Assert.Single(harness.Player.Loads);
        Assert.Single(PlaybackServer.PlaybackInfoRequests(harness.Server));

        await harness.Manager.SetSubtitleStreamIndexAsync(-1);
        Assert.Equal(3, harness.Player.SubtitleCues.Count);
        Assert.Null(harness.Player.SubtitleCues[2]);
        Assert.Equal(-1, harness.Manager.SubtitleStreamIndex);
    }

    [Fact]
    public async Task Secondary_external_subtitles_and_offset_rebuild_native_cues()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var source = PlaybackServer.MediaSource();
        source["MediaStreams"]!.AsArray().Add(new JsonObject
        {
            ["Index"] = 5, ["Type"] = "Subtitle", ["Codec"] = "subrip", ["Language"] = "deu",
            ["IsExternal"] = true, ["IsTextSubtitleStream"] = true, ["SupportsExternalStream"] = true,
            ["DeliveryMethod"] = "External", ["DeliveryUrl"] = "/Videos/movie/Subtitles/5/0/Stream.vtt",
            ["DisplayTitle"] = "German - SUBRIP - External",
        });
        PlaybackServer.ScriptPlaybackInfo(harness.Server, direct: source);
        harness.Server.OnGet("/Subtitles/0/0/Stream.js", new JsonObject
        {
            ["TrackEvents"] = new JsonArray(new JsonObject
            {
                ["StartPositionTicks"] = 10_000_000,
                ["EndPositionTicks"] = 30_000_000,
                ["Text"] = "Hello",
            }),
        });
        harness.Server.OnGet("/Subtitles/5/0/Stream.js", new JsonObject
        {
            ["TrackEvents"] = new JsonArray(new JsonObject
            {
                ["StartPositionTicks"] = 10_000_000,
                ["EndPositionTicks"] = 30_000_000,
                ["Text"] = "Hello",
            }),
        });

        await harness.PlayMovieAsync();
        await harness.Manager.SetSubtitleStreamIndexAsync(0);
        await PlaybackHarness.Eventually(() => harness.Player.SubtitleCues.Count == 2, "the primary cues");

        await harness.Manager.SetSecondarySubtitleStreamIndexAsync(5);
        await PlaybackHarness.Eventually(() => harness.Player.SecondarySubtitleCues.LastOrDefault() is not null, "the secondary cues");
        Assert.NotEmpty(harness.Server.RequestsTo("/Subtitles/5/0/Stream.js"));
        Assert.Equal(new SubtitleCue(1, 3, "‎Hello"), Assert.Single(harness.Player.SecondarySubtitleCues.Last(cues => cues is not null)!));

        harness.Manager.SetSubtitleOffset(2);
        await PlaybackHarness.Eventually(() => harness.Player.SubtitleCues.Count == 3, "the offset primary cues");
        Assert.Equal(new SubtitleCue(0, 1, "‎Hello"), Assert.Single(harness.Player.SubtitleCues.Last(cues => cues is not null)!));
        Assert.Equal(2, harness.Manager.SubtitleOffset);
    }

    [Fact]
    public async Task The_next_subtitle_track_is_followed_by_off()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet("/Subtitles/0/0/Stream.js", new JsonObject { ["TrackEvents"] = new JsonArray() });
        await harness.PlayMovieAsync();

        await harness.Manager.ChangeSubtitleStreamAsync();
        Assert.Equal(0, harness.Manager.SubtitleStreamIndex);
        await harness.Manager.ChangeSubtitleStreamAsync();
        Assert.Equal(-1, harness.Manager.SubtitleStreamIndex);
    }

    [Fact]
    public async Task A_subtitle_that_must_be_burned_in_needs_a_new_stream()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var source = PlaybackServer.MediaSource(PlaybackServer.MovieId, directPlay: true, directStream: true, transcodingUrl: null);
        source["MediaStreams"]!.AsArray().Add(new JsonObject
        {
            ["Index"] = 4,
            ["Type"] = "Subtitle",
            ["Codec"] = "pgssub",
            ["Language"] = "eng",
            ["IsExternal"] = false,
            ["DeliveryMethod"] = "Encode",
        });
        PlaybackServer.ScriptPlaybackInfo(harness.Server, direct: source);
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 8);

        await harness.Manager.SetSubtitleStreamIndexAsync(4);

        var requests = PlaybackServer.PlaybackInfoRequests(harness.Server);
        Assert.Equal(2, requests.Count);
        Assert.Equal(4, (int?)requests[1]["SubtitleStreamIndex"]);
        Assert.Equal(80_000_000, (long?)requests[1]["StartTimeTicks"]);
        Assert.Equal(2, harness.Player.Loads.Count);
        Assert.Equal(4, harness.Manager.SubtitleStreamIndex);

        // Taking it out of the picture again needs another one.
        await harness.Manager.SetSubtitleStreamIndexAsync(-1);
        requests = PlaybackServer.PlaybackInfoRequests(harness.Server);
        Assert.Equal(3, requests.Count);
        Assert.Equal(-1, (int?)requests[2]["SubtitleStreamIndex"]);
    }

    [Theory]
    [InlineData(@"{\an8}Hello\Nworld", "‎Hello\n‎world")]
    [InlineData("One\r\nTwo", "‎One\n‎Two")]
    [InlineData(@"lower\nbreak", "‎lower\n‎break")]
    [InlineData("Plain", "‎Plain")]
    public void Track_event_text_loses_its_ass_tags_and_keeps_its_line_breaks(string text, string expected) =>
        Assert.Equal(expected, HtmlVideoPlayer.NormalizeTrackEventText(text));

    [Fact]
    public async Task Choosing_a_quality_saves_it_and_restarts_the_stream_with_it()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet("/System/Endpoint", new { IsLocal = false, IsInNetwork = false });
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 20);
        Assert.Equal(PlaybackServer.Bitrate, harness.Manager.GetMaxStreamingBitrate());
        Assert.False(harness.Manager.EnableAutomaticBitrateDetection());

        await harness.Manager.SetMaxStreamingBitrateAsync(false, 500_000);

        Assert.Equal("500000", harness.App.Browser.LocalStorage["maxbitrate-Video-false"]);
        Assert.Equal("false", harness.App.Browser.LocalStorage["enableautobitratebitrate-Video-false"]);
        var requests = PlaybackServer.PlaybackInfoRequests(harness.Server);
        Assert.Equal(2, requests.Count);
        Assert.Equal(500_000, (int?)requests[1]["MaxStreamingBitrate"]);
        Assert.Equal(200_000_000, (long?)requests[1]["StartTimeTicks"]);

        // The scripted server transcodes below 1 Mbps.
        Assert.Equal(PlayMethods.Transcode, harness.Manager.PlayMethod);
        Assert.Equal(TestApp.ServerUrl + PlaybackServer.TranscodeUrl, harness.Player.LastLoad.Url);
        Assert.Equal(500_000, harness.Manager.GetMaxStreamingBitrate());
        Assert.Equal(200_000_000, harness.Manager.CurrentTimeTicks);
        Assert.Empty(harness.StopReports);
    }

    [Fact]
    public async Task The_bitrate_settings_are_kept_per_media_type_and_network()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var manager = harness.Manager;
        var storage = harness.App.Browser.LocalStorage;

        // Nothing saved: automatic, with the default limit.
        Assert.True(manager.EnableAutomaticBitrateDetection(false, "Video"));
        Assert.Equal(1_500_000, manager.MaxStreamingBitrateSetting(false, "Video"));

        manager.SetMaxStreamingBitrateSetting(false, "Video", 4_000_000);
        manager.SetEnableAutomaticBitrateDetection(false, "Video", false);
        Assert.Equal("4000000", storage["maxbitrate-Video-false"]);
        Assert.Equal("false", storage["enableautobitratebitrate-Video-false"]);
        Assert.Equal(4_000_000, manager.MaxStreamingBitrateSetting(false, "Video"));
        Assert.False(manager.EnableAutomaticBitrateDetection(false, "Video"));

        // In the network and for audio the values are their own.
        Assert.Equal(1_500_000, manager.MaxStreamingBitrateSetting(true, "Video"));
        Assert.Equal(1_500_000, manager.MaxStreamingBitrateSetting(false, "Audio"));
        manager.SetMaxStreamingBitrateSetting(true, "Video", 20_000_000);
        Assert.Equal("20000000", storage["maxbitrate-Video-true"]);

        // Audio inside the network is never limited: always automatic, and no limit is saved for it.
        var inNetworkAudio = manager.MaxStreamingBitrateSetting(true, "Audio");
        manager.SetMaxStreamingBitrateSetting(true, "Audio", 128_000);
        manager.SetEnableAutomaticBitrateDetection(true, "Audio", true);
        Assert.Equal(inNetworkAudio, manager.MaxStreamingBitrateSetting(true, "Audio"));
        Assert.True(manager.EnableAutomaticBitrateDetection(true, "Audio"));
        Assert.DoesNotContain("maxbitrate-Audio-true", storage.Keys);
        manager.SetEnableAutomaticBitrateDetection(true, "Audio", false);
        Assert.Equal("true", storage["enableautobitratebitrate-Audio-true"]);
    }

    [Fact]
    public async Task Without_a_player_the_controls_do_nothing()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var manager = harness.Manager;

        await manager.PauseAsync();
        await manager.PlayPauseAsync();
        await manager.SeekAsync(10);
        await manager.NextTrackAsync();
        await manager.PreviousTrackAsync();
        await manager.StopAsync();
        await manager.SetAudioStreamIndexAsync(1);
        await manager.SetSubtitleStreamIndexAsync(1);
        manager.SetVolume(10);
        manager.ToggleMute();
        manager.SetPlaybackRate(2);
        manager.ToggleAspectRatio();
        manager.ToggleFullscreen();

        Assert.Empty(harness.Player.Calls);
        Assert.DoesNotContain(harness.Server.Requests, r => r.Method == HttpMethod.Post);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Equal(0, harness.Manager.CurrentTimeTicks);
        Assert.Null(harness.Manager.DurationTicks);
        Assert.DoesNotContain("playbackRateSpeed", harness.App.Browser.SessionStorage.Keys);
    }
}
