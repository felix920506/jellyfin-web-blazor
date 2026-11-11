using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>The smaller pieces of the playback UI: quality menu, trickplay tiles, texts, media session, stats.</summary>
public class PlaybackHelperTests
{
    [Fact]
    public void The_video_qualities_end_one_step_above_the_bitrate_of_the_source()
    {
        // An 8.5 Mbps H.264 source: 10 Mbps is offered to allow a remux, then everything below.
        var options = QualityOptions.GetVideoQualityOptions(4_000_000, isAutomaticBitrateEnabled: false, 8_500_000, "h264", enableAuto: true);

        Assert.Equal(
            new[] { "Auto", "10 Mbps", "8 Mbps", "6 Mbps", "4 Mbps", "3 Mbps", "1.5 Mbps", "720 kbps", "420 kbps" },
            options.Select(o => o.Name));
        Assert.Equal("4 Mbps", Assert.Single(options, o => o.Selected).Name);
        Assert.Equal(720, options.Single(o => o.Selected).MaxHeight);
        Assert.Equal("4000000", QualityOptions.IdOf(options.Single(o => o.Selected)));
    }

    [Fact]
    public void Efficient_codecs_are_offered_a_higher_quality_for_the_same_source()
    {
        var h264 = QualityOptions.GetVideoQualityOptions(null, false, 5_000_000, "h264", enableAuto: false);
        var hevc = QualityOptions.GetVideoQualityOptions(null, false, 5_000_000, "hevc", enableAuto: false);

        Assert.Equal("6 Mbps", h264[0].Name);
        Assert.Equal("8 Mbps", hevc[0].Name);
        Assert.DoesNotContain(h264, o => o.Selected);
    }

    [Fact]
    public void With_automatic_quality_the_auto_entry_is_selected_and_names_the_bitrate_in_use()
    {
        var options = QualityOptions.GetVideoQualityOptions(6_500_000, isAutomaticBitrateEnabled: true, null, null, enableAuto: true, autoName: "Automatic");

        Assert.Equal(15, options.Count);
        Assert.Equal("Automatic", options[0].Name);
        Assert.True(options[0].Selected);
        Assert.Equal("6 Mbps", options[0].AutoText);
        Assert.Single(options, o => o.Selected);
    }

    [Fact]
    public void A_limit_below_every_quality_selects_the_lowest()
    {
        var video = QualityOptions.GetVideoQualityOptions(100_000, false, null, null, enableAuto: false);
        Assert.Equal("420 kbps", Assert.Single(video, o => o.Selected).Name);

        var audio = QualityOptions.GetAudioQualityOptions(200_000, false, enableAuto: true);
        Assert.Equal(10, audio.Count);
        Assert.Equal("192 kbps", Assert.Single(audio, o => o.Selected).Name);
    }

    private static BaseItemDto ItemWithTrickplay() => KiotaJson.Parse(
        """
        {
          "Id": "11111111111111111111111111111111",
          "Trickplay": {
            "11111111111111111111111111111111": {
              "160": { "Width": 160, "Height": 90, "TileWidth": 10, "TileHeight": 10, "ThumbnailCount": 300, "Interval": 10000, "Bandwidth": 1000 },
              "320": { "Width": 320, "Height": 180, "TileWidth": 10, "TileHeight": 10, "ThumbnailCount": 300, "Interval": 10000, "Bandwidth": 4000 },
              "640": { "Width": 640, "Height": 360, "TileWidth": 5, "TileHeight": 5, "ThumbnailCount": 300, "Interval": 10000, "Bandwidth": 9000 }
            }
          }
        }
        """,
        BaseItemDto.CreateFromDiscriminatorValue)!;

    [Theory]
    [InlineData(100, 160)]
    [InlineData(320, 320)]
    [InlineData(400, 320)]
    [InlineData(2000, 640)]
    public void The_trickplay_resolution_is_the_widest_that_fits(double maxWidth, int expectedWidth)
    {
        var resolution = Trickplay.Select(ItemWithTrickplay(), "11111111-1111-1111-1111-111111111111", maxWidth);

        Assert.Equal(expectedWidth, resolution?.Width);
    }

    [Fact]
    public void Without_trickplay_data_for_the_source_there_is_no_preview()
    {
        Assert.Null(Trickplay.Select(ItemWithTrickplay(), "22222222222222222222222222222222", 320));
        Assert.Null(Trickplay.Select(ItemWithTrickplay(), null, 320));
        Assert.Null(Trickplay.Select(PlaybackServer.Movie(), PlaybackServer.MovieId, 320));
        Assert.Null(Trickplay.Select(null, PlaybackServer.MovieId, 320));
    }

    [Fact]
    public void A_position_maps_to_a_tile_of_a_sheet()
    {
        var resolution = new TrickplayResolution(320, 180, TileWidth: 10, TileHeight: 10, Interval: 10000);

        Assert.Equal(new TrickplayTile(0, 0, 0), Trickplay.Tile(resolution, 0));
        Assert.Equal(new TrickplayTile(0, -960, 0), Trickplay.Tile(resolution, 350_000_000));
        Assert.Equal(new TrickplayTile(0, -640, -180), Trickplay.Tile(resolution, 1_200_000_000));

        // Tile 1003: the fourth of the eleventh sheet.
        Assert.Equal(new TrickplayTile(10, -960, 0), Trickplay.Tile(resolution, 100_300_000_000));
    }

    [Fact]
    public async Task Running_times_and_durations_read_like_the_originals()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var text = harness.Get<PlaybackText>();

        Assert.Equal("0:00", text.RunningTime(0));
        Assert.Equal("2:03", text.RunningTime(1_230_000_000));
        Assert.Equal("12:00", text.RunningTime(7_200_000_000));
        Assert.Equal("1:02:03", text.RunningTime(37_230_000_000));
        Assert.Equal("0:00", text.RunningTime(-5));

        Assert.Equal("1m", PlaybackText.DisplayDuration(0));
        Assert.Equal("45m", PlaybackText.DisplayDuration(27_000_000_000));
        Assert.Equal("1h 32m", PlaybackText.DisplayDuration(55_200_000_000));

        Assert.Equal("4.2 Mbps", PlaybackText.DisplayBitrate(4_200_000));
        Assert.Equal("320 Kbps", PlaybackText.DisplayBitrate(320_000));
        Assert.Equal("0 Bytes", PlaybackText.ReadableSize(0));
        Assert.Equal("512.0 Bytes", PlaybackText.ReadableSize(512));
        Assert.Equal("1.5 GiB", PlaybackText.ReadableSize(1_610_612_736));
    }

    [Fact]
    public async Task Titles_name_the_series_the_episode_number_and_the_year()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var text = harness.Get<PlaybackText>();
        var episode = PlaybackServer.Episode("e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2", "The Second One", 2);
        var movie = PlaybackServer.Movie();

        Assert.Equal("S1:E2 - The Second One", text.DisplayName(episode));
        Assert.Equal("2. The Second One", text.DisplayName(episode, includeParentInfo: false));
        Assert.Equal("The Second One", text.DisplayName(episode, includeIndexNumber: false));
        Assert.Equal("Test Movie", text.DisplayName(movie));

        Assert.Equal("Test Show - S1:E2 - The Second One", text.OsdTitle(episode));
        Assert.Equal("Test Movie (2015)", text.OsdTitle(movie));

        episode.IndexNumberEnd = 3;
        Assert.Equal("S1:E2-3 - The Second One", text.DisplayName(episode));
    }

    [Fact]
    public void The_two_lines_of_a_playing_item_depend_on_what_it_is()
    {
        Assert.Equal(new[] { "Opening", "Test Artist" }, PlaybackText.ItemTextLines(PlaybackServer.Song()));
        Assert.Equal(new[] { "Test Movie", "2015" }, PlaybackText.ItemTextLines(PlaybackServer.Movie()));
        Assert.Equal(new[] { "Test Movie" }, PlaybackText.ItemTextLines(PlaybackServer.Movie(), includeYear: false));
        Assert.Equal(
            new[] { "Test Show", "1.2 - The Second One" },
            PlaybackText.ItemTextLines(PlaybackServer.Episode("e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2", "The Second One", 2)));

        var song = PlaybackServer.Song();
        song.Artists = [];
        Assert.Equal(new[] { "First Album", "Opening" }, PlaybackText.ItemTextLines(song));
    }

    [Fact]
    public async Task The_image_of_a_playing_item_is_its_own_its_series_or_its_album()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var text = harness.Get<PlaybackText>();

        var song = PlaybackServer.Song();
        Assert.Null(text.ImageUrl(song));

        song.AlbumId = Guid.Parse("abababababababababababababababab");
        song.AlbumPrimaryImageTag = "album-tag";
        Assert.Equal(
            "http://jellyfin.test/Items/abababababababababababababababab/Images/Primary?height=80&tag=album-tag",
            text.ImageUrl(song, height: 80));

        var episode = PlaybackServer.Episode("e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2", "The Second One", 2);
        episode.SeriesPrimaryImageTag = "series-tag";
        Assert.Equal(
            "http://jellyfin.test/Items/5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e/Images/Primary?maxHeight=300&tag=series-tag",
            text.ImageUrl(episode, maxHeight: 300));

        var movie = KiotaJson.Parse(
            """{ "Id": "11111111111111111111111111111111", "Type": "Movie", "ImageTags": { "Primary": "own-tag" } }""",
            BaseItemDto.CreateFromDiscriminatorValue)!;
        Assert.Equal(
            "http://jellyfin.test/Items/11111111111111111111111111111111/Images/Primary?tag=own-tag",
            text.ImageUrl(movie));
    }

    [Fact]
    public void Only_items_with_user_data_can_be_rated()
    {
        Assert.True(PlaybackText.CanRate(PlaybackServer.Song()));
        Assert.False(PlaybackText.CanRate(new BaseItemDto { Type = BaseItemDto_Type.Audio }));
        Assert.False(PlaybackText.CanRate(new BaseItemDto { Type = BaseItemDto_Type.Program, UserData = new UserItemDataDto() }));
    }

    [Fact]
    public async Task The_media_session_shows_the_song_and_follows_pause_and_stop()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Get<MediaSessionUpdater>();
        var sessions = harness.Player.MediaSessions;

        await harness.PlaySongsAsync();

        var session = Assert.IsType<MediaSessionInfo>(sessions[^1]);
        Assert.Equal("Opening", session.Title);
        Assert.Equal("Test Artist", session.Artist);
        Assert.Equal("First Album", session.Album);
        Assert.False(session.Paused);
        Assert.Equal(30, session.DurationSeconds);
        Assert.Equal(0, session.PositionSeconds);
        Assert.Equal(1, session.PlaybackRate);
        Assert.Empty(session.Artwork);

        harness.Player.Tick("audio", 7);
        await harness.Manager.PauseAsync();
        Assert.True(sessions[^1]!.Paused);
        Assert.Equal(7, sessions[^1]!.PositionSeconds);

        await harness.Manager.NextTrackAsync();
        Assert.Equal("Middle", sessions[^1]!.Title);
        Assert.False(sessions[^1]!.Paused);

        await harness.Manager.StopAsync();
        Assert.Null(sessions[^1]);
    }

    [Fact]
    public async Task The_media_session_offers_the_album_cover_in_several_sizes()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Get<MediaSessionUpdater>();
        var song = PlaybackServer.Song();
        song.AlbumId = Guid.Parse("abababababababababababababababab");
        song.AlbumPrimaryImageTag = "album-tag";

        await harness.Manager.PlayAsync(new PlayOptions { Items = [song] });

        var artwork = harness.Player.MediaSessions[^1]!.Artwork;
        Assert.Equal(new[] { "512x512", "384x384", "256x256", "192x192", "128x128", "96x96" }, artwork.Select(a => a.Sizes));
        Assert.Equal("http://jellyfin.test/Items/abababababababababababababababab/Images/Primary?height=512&tag=album-tag", artwork[0].Src);
    }

    [Fact]
    public async Task A_video_leaves_the_media_session_to_the_browser()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Get<MediaSessionUpdater>();

        await harness.PlayMovieAsync();
        await harness.Manager.PauseAsync();

        Assert.DoesNotContain(harness.Player.MediaSessions, s => s is not null);
    }

    [Fact]
    public async Task The_keys_of_the_media_session_control_playback()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Get<MediaSessionUpdater>();
        await harness.PlaySongsAsync();
        harness.Player.Tick("audio", 12);

        harness.Player.RaiseMediaSessionAction("pause");
        Assert.True(harness.Manager.IsPaused);
        harness.Player.RaiseMediaSessionAction("play");
        Assert.False(harness.Manager.IsPaused);

        harness.Player.RaiseMediaSessionAction("seekto", 20.5);
        Assert.Equal("seek audio 20.5", harness.Player.Calls[^1]);
        harness.Player.RaiseMediaSessionAction("seekbackward");
        Assert.Equal("seek audio 10.5", harness.Player.Calls[^1]);
        harness.Player.RaiseMediaSessionAction("seekforward");
        Assert.Equal("seek audio 40.5", harness.Player.Calls[^1]);

        harness.Player.RaiseMediaSessionAction("nexttrack");
        await PlaybackHarness.Eventually(() => harness.Manager.CurrentItem?.Name == "Middle", "the next track");
        harness.Player.RaiseMediaSessionAction("previoustrack");
        await PlaybackHarness.Eventually(() => harness.Manager.CurrentItem?.Name == "Opening", "the previous track");

        harness.Player.RaiseMediaSessionAction("stop");
        await PlaybackHarness.Eventually(() => !harness.Manager.IsPlaying, "the stop");
    }

    [Theory]
    [InlineData("DirectPlay", null, null, null, "DirectPlay")]
    [InlineData("DirectStream", null, null, null, "DirectPlay")]
    [InlineData("Transcode", false, false, "h264", "Transcode")]
    [InlineData("Transcode", true, false, "h264", "DirectStream")]
    [InlineData("Transcode", true, true, "h264", "Remux")]
    [InlineData("Transcode", false, true, "", "Remux")]
    public void The_play_method_shown_tells_a_remux_and_a_direct_stream_from_a_transcode(
        string playMethod, bool? videoDirect, bool? audioDirect, string? videoCodec, string expected)
    {
        var session = new JsonObject
        {
            ["NowPlayingItem"] = new JsonObject { ["Name"] = "Test Movie" },
            ["PlayState"] = new JsonObject { ["PlayMethod"] = playMethod },
        };
        if (videoDirect is not null)
        {
            session["TranscodingInfo"] = new JsonObject
            {
                ["IsVideoDirect"] = videoDirect,
                ["IsAudioDirect"] = audioDirect,
                ["VideoCodec"] = videoCodec,
            };
        }

        var parsed = KiotaJson.Parse(session.ToJsonString(), SessionInfoDto.CreateFromDiscriminatorValue)!;

        Assert.Equal(expected, PlayerStatsBuilder.GetDisplayPlayMethod(parsed));
        Assert.Null(PlayerStatsBuilder.GetDisplayPlayMethod(new SessionInfoDto()));
    }

    [Fact]
    public async Task The_stats_combine_the_player_the_session_and_the_source_and_ask_the_server_every_ten_seconds()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var globalize = harness.Get<Globalize>();
        harness.Server.OnGet("/Sessions", new JsonArray(new JsonObject
        {
            ["NowPlayingItem"] = new JsonObject { ["Name"] = "Test Movie" },
            ["PlayState"] = new JsonObject { ["PlayMethod"] = "DirectPlay" },
        }));
        var builder = harness.Get<PlayerStatsBuilder>();
        Assert.Empty(await builder.GetStatsAsync());

        await harness.PlayMovieAsync();
        var categories = await builder.GetStatsAsync();

        Assert.Equal(globalize["LabelPlaybackInfo"], categories[0].Name);
        var player = Assert.Single(categories[0].Stats);
        Assert.Equal(globalize["LabelPlayer"], player.Label);
        Assert.Contains("(" + globalize["DirectPlaying"] + ")", player.Value, StringComparison.Ordinal);
        Assert.Equal(globalize["LabelOriginalMediaInfo"], categories[^1].Name);
        Assert.NotEmpty(categories[^1].Stats);
        Assert.DoesNotContain(categories, c => c.Name == globalize["LabelTranscodingInfo"]);
        Assert.Contains(categories.SelectMany(c => c.Stats), s => s.Value.Contains("1280x720", StringComparison.Ordinal));

        var request = Assert.Single(harness.Server.RequestsTo("/Sessions"));
        Assert.Equal(harness.Get<JellyfinApi>().DeviceId, request.Query("deviceId"));

        // The session is asked for again only after ten seconds, or after a reset.
        harness.Timers.NowMs += 9_999;
        await builder.GetStatsAsync();
        Assert.Single(harness.Server.RequestsTo("/Sessions"));
        harness.Timers.NowMs += 1;
        await builder.GetStatsAsync();
        Assert.Equal(2, harness.Server.RequestsTo("/Sessions").Count);
        builder.Reset();
        await builder.GetStatsAsync();
        Assert.Equal(3, harness.Server.RequestsTo("/Sessions").Count);
    }
}
