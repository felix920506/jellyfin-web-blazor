using System.Text.Json.Nodes;
using Jellyfin.Web.Components;
using Jellyfin.Web.Pages.Playback;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>
/// The playback pages and the now-playing bar, rendered against the fake player: what they show of
/// the manager's state and what their controls ask of it.
/// </summary>
public class PlaybackUiTests : PlaybackTestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private Globalize Globalize => Services.GetRequiredService<Globalize>();

    /// <summary>The router follows the (fake) browser only once it has been started.</summary>
    private AppRouter StartRouter(string? at = null)
    {
        var router = Services.GetRequiredService<AppRouter>();
        router.Start();
        if (at is not null)
        {
            router.Navigate(at);
        }

        return router;
    }

    private VideoOsd Osd => Assert.IsType<VideoOsd>(Fakes.Ui.Osd);

    // --- the video OSD -------------------------------------------------------------------------------

    [Fact]
    public async Task The_osd_shows_the_title_the_times_and_the_controls_the_video_has()
    {
        StartRouter("/home");
        await PlayMovieAsync();

        // A video that starts takes over the screen.
        Assert.Equal("/video", App.Browser.Hash);

        var cut = Render<VideoOsd>();
        Assert.Equal("Test Movie (2015)", cut.Find("h3.pageTitle").TextContent);
        Assert.Equal("0:00", cut.Find(".osdPositionText").TextContent);

        // The time that is left, unless the user prefers the run time.
        Assert.Equal("-1:00", cut.Find(".osdDurationText").TextContent);
        Assert.Contains("pause", cut.Find(".btnPause .material-icons").ClassList);

        // One item without chapters: nothing to skip to. Two audio tracks and a subtitle: both menus.
        Assert.Contains("hide", cut.Find(".btnPreviousTrack").ClassList);
        Assert.Contains("hide", cut.Find(".btnNextTrack").ClassList);
        Assert.Contains("hide", cut.Find(".btnPreviousChapter").ClassList);
        Assert.DoesNotContain("hide", cut.Find(".btnAudio").ClassList);
        Assert.DoesNotContain("hide", cut.Find(".btnSubtitles").ClassList);
        Assert.DoesNotContain("hide", cut.Find(".btnFullscreen").ClassList);
        Assert.False(cut.Find(".btnRewind").HasAttribute("disabled"));

        Player.Tick("video", 7);
        cut.WaitForAssertion(() => Assert.Equal("0:07", cut.Find(".osdPositionText").TextContent), Timeout);
        Assert.Equal("-0:53", cut.Find(".osdDurationText").TextContent);

        // A click on it switches to the run time; the choice is kept.
        cut.Find(".osdDurationText").Click();
        cut.WaitForAssertion(() => Assert.Equal("1:00", cut.Find(".osdDurationText").TextContent), Timeout);
        Assert.False(Services.GetRequiredService<UserSettings>().EnableVideoRemainingTime);
    }

    [Fact]
    public async Task The_buttons_and_the_keys_of_the_osd_control_the_player()
    {
        StartRouter();
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);
        var osd = Osd;

        cut.Find(".btnPause").Click();
        cut.WaitForAssertion(() => Assert.Contains("play_arrow", cut.Find(".btnPause .material-icons").ClassList), Timeout);
        Assert.Equal("pause video", Player.Calls[^1]);

        await osd.OnKeyDown("Space", false, false);
        Assert.Equal("unpause video", Player.Calls[^1]);
        cut.WaitForAssertion(() => Assert.Contains("pause", cut.Find(".btnPause .material-icons").ClassList), Timeout);

        await osd.OnKeyDown("ArrowRight", false, false);
        Assert.Equal("seek video 30", Player.Calls[^1]);
        await osd.OnKeyDown("KeyJ", false, false);
        Assert.Equal("seek video 20", Player.Calls[^1]);
        await osd.OnKeyDown("Digit5", false, false);
        Assert.Equal("seek video 30", Player.Calls[^1]);
        await osd.OnKeyDown("Home", false, false);
        Assert.Equal("seek video 0", Player.Calls[^1]);
        cut.Find(".btnFastForward").Click();
        cut.WaitForAssertion(() => Assert.Equal("seek video 30", Player.Calls[^1]), Timeout);

        await osd.OnKeyDown("KeyM", false, false);
        Assert.Equal("mute video True", Player.Calls[^1]);
        cut.WaitForAssertion(() => Assert.Contains("volume_off", cut.Find(".buttonMute .material-icons").ClassList), Timeout);
        cut.Find(".buttonMute").Click();
        Assert.Equal("mute video False", Player.Calls[^1]);

        await osd.OnKeyDown("ArrowDown", false, false);
        Assert.StartsWith("volume video ", Player.Calls[^1], StringComparison.Ordinal);
        Assert.Equal(98, Manager.Volume);
        await osd.OnKeyDown("ArrowUp", false, false);
        Assert.Equal(100, Manager.Volume);
        cut.Find("input.osdVolumeSlider").Change("50");
        Assert.Equal("volume video 0.125", Player.Calls[^1]);

        await osd.OnKeyDown("KeyF", false, false);
        Assert.Equal("fullscreen", Player.Calls[^1]);
        cut.WaitForAssertion(() => Assert.Contains("fullscreen_exit", cut.Find(".btnFullscreen .material-icons").ClassList), Timeout);

        // Shift and the period key speed playback up; without Shift it steps a frame, when paused.
        var seeks = Player.Calls.Count(c => c.StartsWith("seek", StringComparison.Ordinal));
        await osd.OnKeyDown("Period", false, false);
        Assert.Equal(seeks, Player.Calls.Count(c => c.StartsWith("seek", StringComparison.Ordinal)));
        await osd.OnKeyDown("Period", true, false);
        Assert.Equal("rate video 1.25", Player.Calls[^1]);
    }

    [Fact]
    public async Task Subtitle_offset_keys_show_the_slider_and_adjust_the_offset()
    {
        StartRouter();
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        var osd = Osd;

        await osd.OnKeyDown("KeyH", false, false);
        cut.WaitForAssertion(() => Assert.DoesNotContain("hide", cut.Find(".subtitleSyncContainer").ClassList), Timeout);
        Assert.Equal(0.1, Manager.SubtitleOffset, precision: 6);

        await osd.OnKeyDown("KeyG", false, false);
        Assert.Equal(0, Manager.SubtitleOffset, precision: 6);
        cut.Find(".subtitleSync-closeButton").Click();
        cut.WaitForAssertion(() => Assert.Contains("hide", cut.Find(".subtitleSyncContainer").ClassList), Timeout);
    }

    [Fact]
    public async Task The_osd_hides_when_nothing_happens_and_returns_with_the_pointer()
    {
        StartRouter();
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);
        var osd = Osd;
        Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList);
        Assert.DoesNotContain("osdHeader-hidden", cut.Find(".osdHeader").ClassList);

        // While a button is held nothing hides.
        await osd.OnMouseDownChanged(true);
        await Fakes.Timers.AdvanceAsync(3000);
        Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList);

        await osd.OnMouseDownChanged(false);
        await Fakes.Timers.AdvanceAsync(2999);
        Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList);
        await Fakes.Timers.AdvanceAsync(1);
        cut.WaitForAssertion(() => Assert.Contains("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList), Timeout);
        Assert.Contains("osdHeader-hidden", cut.Find(".osdHeader").ClassList);
        Assert.Contains("body:mouseIdle", App.Browser.Classes);

        // The controls leave the layout when they have faded out.
        Assert.DoesNotContain("hide", cut.Find(".videoOsdBottom").ClassList);
        await Fakes.Timers.AdvanceAsync(350);
        cut.WaitForAssertion(() => Assert.Contains("hide", cut.Find(".videoOsdBottom").ClassList), Timeout);

        await osd.OnPointerMoved();
        cut.WaitForAssertion(() => Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList), Timeout);
        Assert.DoesNotContain("hide", cut.Find(".videoOsdBottom").ClassList);
        Assert.DoesNotContain("osdHeader-hidden", cut.Find(".osdHeader").ClassList);
        Assert.DoesNotContain("body:mouseIdle", App.Browser.Classes);

        // Escape hides at once.
        await osd.OnKeyDown("Escape", false, false);
        cut.WaitForAssertion(() => Assert.Contains("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList), Timeout);
        await osd.OnKeyDown("Enter", false, false);
        cut.WaitForAssertion(() => Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList), Timeout);
    }

    [Fact]
    public async Task The_settings_menu_of_the_osd_opens_the_playback_info()
    {
        StartRouter();
        Server.OnGet("/Sessions", new JsonArray(new JsonObject
        {
            ["NowPlayingItem"] = new JsonObject { ["Name"] = "Test Movie" },
            ["PlayState"] = new JsonObject { ["PlayMethod"] = "DirectPlay" },
        }));
        await PlayMovieAsync();
        var dialogs = Render<DialogHost>();
        var cut = Render<VideoOsd>();
        Assert.Contains("hide", cut.Find(".playerStats").ClassList);

        cut.Find(".btnVideoOsdSettings").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll(".actionSheetMenuItem")), Timeout);
        var ids = dialogs.FindAll(".actionSheetMenuItem").Select(e => e.GetAttribute("data-id")).ToList();
        Assert.Equal(new[] { "aspectratio", "playbackrate", "repeatmode", "suboffset", "stats" }, ids);
        Assert.Equal(Globalize["Auto"], dialogs.Find("[data-id=aspectratio] .actionSheetItemAsideText").TextContent);
        Assert.Equal("1x", dialogs.Find("[data-id=playbackrate] .actionSheetItemAsideText").TextContent);

        // An open menu keeps the controls on the screen.
        await Fakes.Timers.AdvanceAsync(3000);
        Assert.DoesNotContain("videoOsdBottom-hidden", cut.Find(".videoOsdBottom").ClassList);

        dialogs.Find("[data-id=stats]").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("hide", cut.Find(".playerStats").ClassList), Timeout);
        cut.WaitForAssertion(
            () => Assert.Contains(cut.FindAll(".playerStats-stat-value"), e => e.TextContent.Contains("1280x720", StringComparison.Ordinal)),
            Timeout);
        Assert.Contains(cut.FindAll(".playerStats-stat-label"), e => e.TextContent == Globalize["LabelPlaybackInfo"]);
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".dialogContainer")), Timeout);

        cut.Find(".playerStats-closeButton").Click();
        cut.WaitForAssertion(() => Assert.Contains("hide", cut.Find(".playerStats").ClassList), Timeout);
    }

    [Fact]
    public async Task Users_who_may_transcode_are_offered_the_quality_menu()
    {
        StartRouter();
        Services.GetRequiredService<JellyfinApi>().User!.Policy!.EnableVideoPlaybackTranscoding = true;
        await PlayMovieAsync();
        var dialogs = Render<DialogHost>();
        var cut = Render<VideoOsd>();

        cut.Find(".btnVideoOsdSettings").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll("[data-id=quality]")), Timeout);

        // The entry names the quality in use: a fixed limit in the tests.
        var options = Services.GetRequiredService<PlayerSettingsMenu>().VideoQualityOptions();
        var selected = Assert.Single(options, o => o.Selected);
        Assert.EndsWith("Mbps", selected.Name, StringComparison.Ordinal);
        Assert.Equal(selected.Name, dialogs.Find("[data-id=quality] .actionSheetItemAsideText").TextContent);

        var lower = options.First(o => o.Name == "1.5 Mbps");
        var lowerId = QualityOptions.IdOf(lower);
        dialogs.Find("[data-id=quality]").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll($"[data-id='{lowerId}']")), Timeout);
        Assert.Equal(options.Select(o => o.Name), dialogs.FindAll(".actionSheetItemText").Select(e => e.TextContent));
        Assert.Equal(Globalize["Auto"], options[0].Name);

        dialogs.Find($"[data-id='{lowerId}']").Click();
        await PlaybackHarness.Eventually(() => PlaybackServer.PlaybackInfoRequests(Server).Count == 2, "the new negotiation");
        Assert.Equal(lower.Bitrate, (int?)PlaybackServer.PlaybackInfoRequests(Server)[1]["MaxStreamingBitrate"]);
    }

    [Fact]
    public async Task The_audio_and_subtitle_menus_of_the_osd_switch_the_tracks()
    {
        StartRouter();
        Server.OnGet("/Subtitles/0/0/Stream.js", new JsonObject
        {
            ["TrackEvents"] = new JsonArray(new JsonObject { ["StartPositionTicks"] = 10_000_000, ["EndPositionTicks"] = 30_000_000, ["Text"] = "Hello" }),
        });
        await PlayMovieAsync();
        var dialogs = Render<DialogHost>();
        var cut = Render<VideoOsd>();

        cut.Find(".btnSubtitles").Click();
        dialogs.WaitForAssertion(() => Assert.Equal(Globalize["Subtitles"], dialogs.Find(".actionSheetTitle").TextContent), Timeout);
        Assert.Equal(new[] { "-1", "0" }, dialogs.FindAll(".actionSheetMenuItem").Select(e => e.GetAttribute("data-id")));
        Assert.Equal(Globalize["Off"], dialogs.Find("[data-id='-1'] .actionSheetItemText").TextContent);
        Assert.Equal("English - SUBRIP - External", dialogs.Find("[data-id='0'] .actionSheetItemText").TextContent);

        dialogs.Find("[data-id='0']").Click();
        await PlaybackHarness.Eventually(() => Manager.SubtitleStreamIndex == 0, "the subtitle track");
        await PlaybackHarness.Eventually(() => Player.Calls.Contains("cues 1"), "the cues of the track");

        cut.Find(".btnAudio").Click();
        dialogs.WaitForAssertion(() => Assert.Equal(Globalize["Audio"], dialogs.Find(".actionSheetTitle").TextContent), Timeout);
        Assert.Equal(
            new[] { "English - AAC - Stereo - Default", "German - AAC - Stereo" },
            dialogs.FindAll(".actionSheetItemText").Select(e => e.TextContent));

        dialogs.Find("[data-id='3']").Click();
        await PlaybackHarness.Eventually(() => Manager.AudioStreamIndex == 3, "the audio track");
    }

    [Fact]
    public async Task The_osd_page_closes_when_the_video_ends()
    {
        StartRouter("/home");
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);
        Assert.Equal("/video", App.Browser.Hash);

        Player.Tick("video", 59);
        Player.End("video");
        cut.WaitForAssertion(() => Assert.Equal("/home", App.Browser.Hash), Timeout);
        await PlaybackHarness.Eventually(() => !Manager.IsPlaying, "the end of playback");
    }

    [Fact]
    public async Task Leaving_the_osd_page_ends_the_video()
    {
        StartRouter("/home");
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);

        cut.Instance.Dispose();
        await PlaybackHarness.Eventually(() => Player.Calls.Contains("stop video destroy=True fade=False"), "the stop of the video");
        Assert.Equal(1, Fakes.Ui.OsdDetached);
        Assert.DoesNotContain("body:mouseIdle", App.Browser.Classes);
    }

    [Fact]
    public async Task Near_the_end_of_an_episode_the_next_one_is_announced()
    {
        const long runtime = 20 * 60 * 10_000_000L;
        const string firstId = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
        const string secondId = "e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2";
        var episodes = new[] { PlaybackServer.Episode(firstId, "Pilot", 1), PlaybackServer.Episode(secondId, "The Second One", 2) };
        foreach (var episode in episodes)
        {
            episode.RunTimeTicks = runtime;
            PlaybackServer.ScriptPlaybackInfo(Server, episode.Id!.Value.ToString("N"), PlaybackServer.MediaSource(episode.Id!.Value.ToString("N"), runTimeTicks: runtime));
        }

        PlaybackServer.ScriptItems(Server, episodes);
        Player.DurationSeconds = 1200;
        StartRouter();
        await Manager.PlayAsync(new PlayOptions { Items = episodes });
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);
        Assert.Equal("Test Show - S1:E1 - Pilot", cut.Find("h3.pageTitle").TextContent);
        Assert.DoesNotContain("hide", cut.Find(".btnNextTrack").ClassList);
        Assert.Contains("hide", cut.Find(".upNextDialog").ClassList);

        // Not while the controls are shown, and not before the last thirty seconds.
        Player.Tick("video", 1175);
        Fakes.Timers.NowMs += 1000;
        await Osd.OnKeyDown("Escape", false, false);
        Player.Tick("video", 1100);
        Fakes.Timers.NowMs += 1000;
        Assert.Contains("upNextDialog-hidden", cut.Find(".upNextDialog").ClassList);

        Player.Tick("video", 1175);
        cut.WaitForAssertion(() => Assert.DoesNotContain("upNextDialog-hidden", cut.Find(".upNextDialog").ClassList), Timeout);
        Assert.Contains("The Second One", cut.Find(".upNextDialog-title").TextContent, StringComparison.Ordinal);
        Assert.Equal(Globalize["HeaderNextEpisode"], cut.Find(".upNextDialog-nextVideoText").TextContent.Trim());

        cut.Find(".btnStartNow").Click();
        await PlaybackHarness.Eventually(() => Manager.CurrentItem?.Name == "The Second One", "the next episode");
        cut.WaitForAssertion(() => Assert.Contains("upNextDialog-hidden", cut.Find(".upNextDialog").ClassList), Timeout);
        cut.WaitForAssertion(() => Assert.Equal("Test Show - S1:E2 - The Second One", cut.Find("h3.pageTitle").TextContent), Timeout);
    }

    [Fact]
    public async Task What_cannot_be_played_is_explained_in_a_dialog()
    {
        StartRouter("/home");
        Server.OnPost(
            $"/Items/{PlaybackServer.MovieId}/PlaybackInfo",
            _ => FakeHttp.Json(new JsonObject { ["MediaSources"] = new JsonArray(), ["ErrorCode"] = "NoCompatibleStream" }));
        var dialogs = Render<DialogHost>();

        await PlayMovieAsync();

        dialogs.WaitForAssertion(() => Assert.Single(dialogs.FindAll(".dialogContainer")), Timeout);
        Assert.Equal(Globalize["HeaderPlaybackError"], dialogs.Find(".formDialogHeaderTitle").TextContent);
        Assert.Equal(Globalize["PlaybackErrorNoCompatibleStream"], dialogs.Find(".dialogContentInner .text").TextContent.Trim());
        Assert.Equal("/home", App.Browser.Hash);

        dialogs.Find("button[data-id=ok]").Click();
        dialogs.WaitForAssertion(() => Assert.Empty(dialogs.FindAll(".dialogContainer")), Timeout);
    }

    // --- the now-playing bar -------------------------------------------------------------------------

    [Fact]
    public async Task The_bar_is_hidden_until_music_plays_and_while_a_video_has_the_screen()
    {
        StartRouter("/home");
        var cut = Render<NowPlayingBar>();
        Assert.Contains("hide", cut.Find(".nowPlayingBar").ClassList);
        Assert.Contains("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList);
        Assert.Empty(cut.FindAll(".nowPlayingBarTop"));

        await PlayMovieAsync();
        Player.Tick("video", 1);
        Assert.Contains("hide", cut.Find(".nowPlayingBar").ClassList);

        await Manager.StopAsync();
        App.Browser.Back();
        Assert.Equal("/home", App.Browser.Hash);
        Assert.Contains("hide", cut.Find(".nowPlayingBar").ClassList);

        await PlaySongsAsync();
        cut.WaitForAssertion(() => Assert.DoesNotContain("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
        Assert.DoesNotContain("hide", cut.Find(".nowPlayingBar").ClassList);
        Assert.Equal(new[] { "Opening", "Test Artist" }, cut.FindAll(".nowPlayingBarText a").Select(e => e.TextContent));
        Assert.Equal("0:00 / 0:30", cut.Find(".nowPlayingBarCurrentTime").TextContent);
        Assert.Contains("pause", cut.Find(".playPauseButton .material-icons").ClassList);

        // Music stays where the user is.
        Assert.Equal("/home", App.Browser.Hash);
    }

    [Fact]
    public async Task The_bar_follows_the_position_and_its_buttons_control_playback()
    {
        StartRouter("/home");
        await PlaySongsAsync();
        var cut = Render<NowPlayingBar>();
        Assert.DoesNotContain("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList);

        Player.Tick("audio", 7);
        cut.WaitForAssertion(() => Assert.Equal("0:07 / 0:30", cut.Find(".nowPlayingBarCurrentTime").TextContent), Timeout);
        Fakes.Timers.NowMs += 700;
        Player.Tick("audio", 9);
        cut.WaitForAssertion(() => Assert.Equal("0:09 / 0:30", cut.Find(".nowPlayingBarCurrentTime").TextContent), Timeout);

        cut.Find(".playPauseButton").Click();
        cut.WaitForAssertion(
            () => Assert.All(cut.FindAll(".playPauseButton .material-icons"), e => Assert.Contains("play_arrow", e.ClassList)),
            Timeout);
        Assert.Equal("pause audio", Player.Calls[^1]);
        cut.Find(".playPauseButton").Click();
        cut.WaitForAssertion(() => Assert.Equal("unpause audio", Player.Calls[^1]), Timeout);

        cut.Find(".muteButton").Click();
        Assert.Equal("mute audio True", Player.Calls[^1]);
        cut.WaitForAssertion(() => Assert.Contains("volume_off", cut.Find(".muteButton .material-icons").ClassList), Timeout);
        cut.Find("input.nowPlayingBarVolumeSlider").Change("50");
        Assert.Equal("volume audio 0.125", Player.Calls[^1]);
        cut.Find("input.nowPlayingBarPositionSlider").Change("50");
        cut.WaitForAssertion(() => Assert.Equal("seek audio 15", Player.Calls[^1]), Timeout);

        // Repeat cycles through all, one and none.
        Assert.DoesNotContain("buttonActive", cut.Find(".toggleRepeatButton").ClassList);
        cut.Find(".toggleRepeatButton").Click();
        cut.WaitForAssertion(() => Assert.Contains("buttonActive", cut.Find(".toggleRepeatButton").ClassList), Timeout);
        Assert.Equal(PlayQueue.RepeatAll, Manager.RepeatMode);
        cut.Find(".toggleRepeatButton").Click();
        cut.WaitForAssertion(() => Assert.Contains("repeat_one", cut.Find(".toggleRepeatButton .material-icons").ClassList), Timeout);
        cut.Find(".toggleRepeatButton").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("buttonActive", cut.Find(".toggleRepeatButton").ClassList), Timeout);
        Assert.Equal(PlayQueue.RepeatNone, Manager.RepeatMode);

        cut.Find(".nextTrackButton").Click();
        cut.WaitForAssertion(() => Assert.Equal("Middle", cut.FindAll(".nowPlayingBarText a")[0].TextContent), Timeout);

        // At the start of a track the button goes to the one before; later it restarts the track.
        cut.Find(".previousTrackButton").Click();
        cut.WaitForAssertion(() => Assert.Equal("Opening", cut.FindAll(".nowPlayingBarText a")[0].TextContent), Timeout);
        await PlaybackHarness.Eventually(() => Manager.IsPlaying && Manager.CurrentItem?.Name == "Opening", "the first song again");
        await Manager.NextTrackAsync();
        Player.Tick("audio", 7);
        cut.Find(".previousTrackButton").Click();
        cut.WaitForAssertion(() => Assert.Equal("seek audio 0", Player.Calls[^1]), Timeout);
        Assert.Equal("Middle", Manager.CurrentItem?.Name);

        // None of the buttons opened the queue page.
        Assert.Equal("/home", App.Browser.Hash);
    }

    [Fact]
    public async Task A_click_on_the_bar_opens_the_queue_page_where_the_bar_slides_out()
    {
        StartRouter("/home");
        await PlaySongsAsync();
        var cut = Render<NowPlayingBar>();

        cut.Find(".nowPlayingBar").Click();
        Assert.Equal("/queue", App.Browser.Hash);
        cut.WaitForAssertion(() => Assert.Contains("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
        Assert.DoesNotContain("hide", cut.Find(".nowPlayingBar").ClassList);

        await Fakes.Timers.AdvanceAsync(350);
        cut.WaitForAssertion(() => Assert.Contains("hide", cut.Find(".nowPlayingBar").ClassList), Timeout);

        App.Browser.Back();
        cut.WaitForAssertion(() => Assert.DoesNotContain("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
        Assert.Equal("Opening", cut.FindAll(".nowPlayingBarText a")[0].TextContent);
    }

    [Fact]
    public async Task The_menu_of_the_bar_stops_playback_and_the_bar_goes_away()
    {
        StartRouter("/home");
        await PlaySongsAsync();
        var dialogs = Render<DialogHost>();
        var cut = Render<NowPlayingBar>();

        cut.Find(".btnToggleContextMenu").Click();
        dialogs.WaitForAssertion(() => Assert.NotEmpty(dialogs.FindAll(".actionSheetMenuItem")), Timeout);
        var menuIds = dialogs.FindAll(".actionSheetMenuItem").Select(e => e.GetAttribute("data-id")).ToList();
        Assert.Equal(new[] { "stopPlayback", "clearQueue" }, menuIds.Take(2));
        Assert.Contains("addtoplaylist", menuIds);
        Assert.Equal("/home", App.Browser.Hash);

        dialogs.Find("[data-id=stopPlayback]").Click();
        await PlaybackHarness.Eventually(() => !Manager.IsPlaying, "the stop");
        cut.WaitForAssertion(() => Assert.Contains("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
        await Fakes.Timers.AdvanceAsync(350);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".nowPlayingBarTop")), Timeout);
        Assert.Contains("hide", cut.Find(".nowPlayingBar").ClassList);
    }

    [Fact]
    public async Task On_a_phone_the_bar_keeps_play_and_next_only()
    {
        Services.GetRequiredService<AppHost>().SetLayout("mobile");
        StartRouter("/home");
        await PlaySongsAsync();
        var cut = Render<NowPlayingBar>();

        Assert.Contains("hide", cut.Find(".nowPlayingBarCenter").ClassList);
        Assert.Empty(cut.FindAll(".nowPlayingBarCenter .nextTrackButton"));
        Assert.Single(cut.FindAll(".nowPlayingBarRight .nextTrackButton"));
        Assert.Empty(cut.FindAll(".btnToggleContextMenu"));
        Assert.Contains("hide", cut.Find(".btnShuffleQueue").ClassList);
    }

    // --- the queue page ------------------------------------------------------------------------------

    [Fact]
    public async Task The_queue_page_shows_what_plays_and_lists_the_queue()
    {
        StartRouter("/queue");
        await PlaySongsAsync();
        var cut = Render<Jellyfin.Web.Pages.Playback.Queue>();

        Assert.Contains("hideVideoButtons", cut.Find("#nowPlayingPage").ClassList);
        Assert.Equal("Opening", cut.Find(".nowPlayingSongName").TextContent);
        Assert.Equal("First Album", cut.Find(".nowPlayingAlbum a").TextContent);
        Assert.Equal("Test Artist", cut.Find(".nowPlayingArtist a").TextContent);
        Assert.Contains("hide", cut.Find(".nowPlayingPageTitle").ClassList);
        Assert.Equal("0:00", cut.Find(".positionTime").TextContent);
        Assert.Equal("0:30", cut.Find(".runtime").TextContent);
        Assert.Contains("pause_circle_filled", cut.Find(".btnPlayPause .material-icons").ClassList);
        Assert.Contains("hide", cut.Find(".btnAudioTracks").ClassList);
        Assert.Contains("hide", cut.Find(".btnSubtitles").ClassList);

        var entries = cut.FindAll("#playlist .listItem");
        Assert.Equal(new[] { "Opening", "Middle", "Closing" }, entries.Select(e => e.QuerySelector(".listItemBodyText")!.TextContent));
        Assert.All(entries, e => Assert.Equal("0:30", e.QuerySelector(".mediaInfoItem")!.TextContent));
        Assert.Equal(Manager.GetPlaylist().Select(i => i.PlaylistItemId), entries.Select(e => e.GetAttribute("data-playlistitemid")));
        Assert.Contains("playlistIndexIndicatorImage", entries[0].QuerySelector(".listItemImage")!.ClassList);
        Assert.DoesNotContain("playlistIndexIndicatorImage", entries[1].QuerySelector(".listItemImage")!.ClassList);

        Player.Tick("audio", 12);
        cut.WaitForAssertion(() => Assert.Equal("0:12", cut.Find(".positionTime").TextContent), Timeout);
    }

    [Fact]
    public async Task The_queue_can_be_reordered_shortened_and_jumped_in()
    {
        StartRouter("/queue");
        await PlaySongsAsync();
        var cut = Render<Jellyfin.Web.Pages.Playback.Queue>();
        Assert.True(cut.FindAll(".jf-playlistMoveUp")[0].HasAttribute("disabled"));
        Assert.True(cut.FindAll(".jf-playlistMoveDown")[2].HasAttribute("disabled"));

        cut.FindAll(".jf-playlistMoveDown")[0].Click();
        cut.WaitForAssertion(
            () => Assert.Equal(
                new[] { "Middle", "Opening", "Closing" },
                cut.FindAll("#playlist .listItem").Select(e => e.QuerySelector(".listItemBodyText")!.TextContent)),
            Timeout);

        // What plays is still marked.
        Assert.Contains("playlistIndexIndicatorImage", cut.FindAll("#playlist .listItemImage")[1].ClassList);
        Assert.Equal("Opening", Manager.CurrentItem?.Name);

        cut.FindAll("#playlist button[data-customaction=remove]")[2].Click();
        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("#playlist .listItem").Count), Timeout);
        Assert.Equal(new[] { "Middle", "Opening" }, Manager.GetPlaylist().Select(i => i.Name));

        cut.FindAll("#playlist .listItemBody")[0].Click();
        await PlaybackHarness.Eventually(() => Manager.IsPlaying && Manager.CurrentItem?.Name == "Middle", "the chosen song");
        cut.WaitForAssertion(() => Assert.Equal("Middle", cut.Find(".nowPlayingSongName").TextContent), Timeout);
        Assert.Contains("playlistIndexIndicatorImage", cut.FindAll("#playlist .listItemImage")[0].ClassList);
    }

    [Fact]
    public async Task The_queue_can_be_reordered_with_native_drag_and_drop()
    {
        StartRouter("/queue");
        await PlaySongsAsync();
        var cut = Render<Jellyfin.Web.Pages.Playback.Queue>();
        cut.FindAll("#playlist .listItem")[2].TriggerEvent("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        cut.FindAll("#playlist .listItem")[0].TriggerEvent("ondrop", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        cut.WaitForAssertion(
            () => Assert.Equal(
                new[] { "Closing", "Opening", "Middle" },
                cut.FindAll("#playlist .listItem").Select(e => e.QuerySelector(".listItemBodyText")!.TextContent)),
            Timeout);
        Assert.Equal(new[] { "Closing", "Opening", "Middle" }, Manager.GetPlaylist().Select(i => i.Name));
    }

    [Fact]
    public async Task The_buttons_of_the_queue_page_control_playback_and_the_page_closes_with_it()
    {
        StartRouter("/home").Navigate("/queue");
        await PlaySongsAsync();
        var cut = Render<Jellyfin.Web.Pages.Playback.Queue>();

        cut.Find(".btnPlayPause").Click();
        cut.WaitForAssertion(() => Assert.Contains("play_circle_filled", cut.Find(".btnPlayPause .material-icons").ClassList), Timeout);
        Assert.Equal("pause audio", Player.Calls[^1]);
        Assert.Contains("playlistIndexIndicatorPausedImage", cut.FindAll("#playlist .listItemImage")[0].ClassList);
        cut.Find(".btnPlayPause").Click();
        cut.WaitForAssertion(() => Assert.Contains("pause_circle_filled", cut.Find(".btnPlayPause .material-icons").ClassList), Timeout);

        Player.Tick("audio", 15);
        cut.Find(".btnRewind").Click();
        cut.WaitForAssertion(() => Assert.Equal("seek audio 5", Player.Calls[^1]), Timeout);
        cut.Find("input.nowPlayingVolumeSlider").Change("50");
        Assert.Equal("volume audio 0.125", Player.Calls[^1]);

        cut.Find(".btnRepeat").Click();
        cut.WaitForAssertion(() => Assert.All(cut.FindAll(".btnRepeat"), e => Assert.Contains("buttonActive", e.ClassList)), Timeout);
        Assert.Equal(PlayQueue.RepeatAll, Manager.RepeatMode);

        cut.Find(".btnNextTrack").Click();
        cut.WaitForAssertion(() => Assert.Equal("Middle", cut.Find(".nowPlayingSongName").TextContent), Timeout);
        await PlaybackHarness.Eventually(() => Manager.IsPlaying, "the second song");

        cut.Find(".btnShuffleQueue").Click();
        cut.WaitForAssertion(() => Assert.All(cut.FindAll(".btnShuffleQueue"), e => Assert.Contains("buttonActive", e.ClassList)), Timeout);
        Assert.Equal(PlayQueue.Shuffled, Manager.ShuffleMode);
        Assert.Equal("Middle", Manager.CurrentItem?.Name);

        cut.Find(".btnStop").Click();
        cut.WaitForAssertion(() => Assert.Equal("/home", App.Browser.Hash), Timeout);
        Assert.Contains(Player.Calls, c => c.StartsWith("stop audio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_video_in_the_queue_page_has_a_title_and_its_track_buttons()
    {
        StartRouter("/home");
        await PlayMovieAsync();
        var cut = Render<Jellyfin.Web.Pages.Playback.Queue>();

        Assert.DoesNotContain("hideVideoButtons", cut.Find("#nowPlayingPage").ClassList);
        Assert.Equal("Test Movie - 2015", cut.Find(".nowPlayingPageTitle").TextContent);
        Assert.DoesNotContain("hide", cut.Find(".nowPlayingPageTitle").ClassList);
        Assert.Equal(string.Empty, cut.Find(".nowPlayingSongName").TextContent);
        Assert.DoesNotContain("hide", cut.Find(".btnAudioTracks").ClassList);
        Assert.DoesNotContain("hide", cut.Find(".btnSubtitles").ClassList);
        Assert.DoesNotContain("hide", cut.Find(".btnToggleFullscreen").ClassList);
        Assert.Equal("1:00", cut.Find(".runtime").TextContent);
    }

    [Fact]
    public void Entries_without_an_image_get_a_colour_from_their_name()
    {
        // The middle letter decides: the sum of the digits of its character code.
        Assert.Equal("defaultCardBackground defaultCardBackground1", Jellyfin.Web.Pages.Playback.Queue.DefaultBackgroundClass(null));
        Assert.Equal("defaultCardBackground defaultCardBackground3", Jellyfin.Web.Pages.Playback.Queue.DefaultBackgroundClass("Opening"));
        Assert.Equal("defaultCardBackground defaultCardBackground2", Jellyfin.Web.Pages.Playback.Queue.DefaultBackgroundClass("Middle"));
    }
}
