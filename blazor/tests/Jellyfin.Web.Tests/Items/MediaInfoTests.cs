using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Items;

/// <summary>The line of facts under a title (mediainfo.js).</summary>
public class MediaInfoTests : ItemsTestContext
{
    private MediaInfo Info => Get<MediaInfo>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    private static string Text(string value) => $"<div class=\"mediaInfoItem\">{value}</div>";

    [Fact]
    public void A_movie_shows_year_runtime_ratings_subtitles_and_its_end_time()
    {
        var movie = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Movie",
            MediaType = "Video",
            ProductionYear = 2001,
            RunTimeTicks = 72_000_000_000L,
            OfficialRating = "PG-13",
            CommunityRating = 7.4,
            CriticRating = 85,
            HasSubtitles = true
        });

        Assert.Equal(
            Text("2001")
            + Text("2h 0m")
            + "<div class=\"mediaInfoItem mediaInfoText mediaInfoOfficialRating\">PG-13</div>"
            + "<div class=\"starRatingContainer mediaInfoItem\"><span class=\"material-icons starIcon star\" aria-hidden=\"true\"></span>7.4</div>"
            + "<div class=\"mediaInfoItem mediaInfoText closedCaptionMediaInfoText\">CC</div>"
            + "<div class=\"mediaInfoItem mediaInfoCriticRating mediaInfoCriticRatingFresh\">85</div>"
            + "<div class=\"endsAt mediaInfoItem\">Ends at 2024-05-17 14:00:00</div>",
            Info.GetPrimaryHtml(movie));
    }

    [Fact]
    public void Every_part_can_be_switched_off()
    {
        var movie = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Movie",
            MediaType = "Video",
            ProductionYear = 2001,
            RunTimeTicks = 72_000_000_000L,
            OfficialRating = "PG-13",
            CommunityRating = 7.4,
            CriticRating = 40,
            HasSubtitles = true
        });

        var none = new MediaInfoOptions
        {
            Year = false,
            Runtime = false,
            OfficialRating = false,
            StarRating = false,
            CriticRating = false,
            EndsAt = false,
            Subtitles = false
        };

        Assert.Equal(string.Empty, Info.GetPrimaryHtml(movie, none));
        Assert.Equal(
            "<div class=\"mediaInfoItem mediaInfoCriticRating mediaInfoCriticRatingRotten\">40</div>",
            Info.GetPrimaryHtml(movie, none with { CriticRating = true }));
    }

    [Fact]
    public void A_series_shows_its_run_and_no_runtime()
    {
        var running = TestItems.Item(new { Id = TestItems.Id(1), Type = "Series", ProductionYear = 2010, Status = "Continuing", RunTimeTicks = 15_000_000_000L });
        var ended = TestItems.Item(new { Id = TestItems.Id(1), Type = "Series", ProductionYear = 2010, Status = "Ended", EndDate = "2014-06-01T00:00:00.0000000Z" });
        var oneYear = TestItems.Item(new { Id = TestItems.Id(1), Type = "Series", ProductionYear = 2010, Status = "Ended", EndDate = "2010-06-01T00:00:00.0000000Z" });

        Assert.Equal(Text("2010 - Present"), Info.GetPrimaryHtml(running));
        Assert.Equal(Text("2010 - 2014"), Info.GetPrimaryHtml(ended));
        Assert.Equal(Text("2010"), Info.GetPrimaryHtml(oneYear));
    }

    [Fact]
    public void An_episode_shows_its_air_date_instead_of_the_year()
    {
        var episode = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Episode",
            MediaType = "Video",
            ProductionYear = 2020,
            PremiereDate = "2020-03-05T00:00:00.0000000Z",
            RunTimeTicks = 15_000_000_000L,
            OfficialRating = "TV-14"
        });

        Assert.Equal(
            Text("2020-03-05 00:00:00") + Text("25m") + "<div class=\"endsAt mediaInfoItem\">Ends at 2024-05-17 12:25:00</div>",
            Info.GetPrimaryHtml(episode));
    }

    [Fact]
    public void An_album_shows_its_track_count_and_length()
    {
        var album = TestItems.Item(new { Id = TestItems.Id(1), Type = "MusicAlbum", IsFolder = true, SongCount = 12, RunTimeTicks = 18_000_000_000L, ProductionYear = 1999 });
        var boxSet = TestItems.Item(new { Id = TestItems.Id(2), Type = "BoxSet", IsFolder = true, ChildCount = 3 });

        Assert.Equal(Text("12 tracks") + Text("30m") + Text("1999"), Info.GetPrimaryHtml(album));
        Assert.Equal(Text("3 items"), Info.GetPrimaryHtml(boxSet));
    }

    [Fact]
    public void A_song_shows_its_running_time_and_container()
    {
        var song = TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", MediaType = "Audio", RunTimeTicks = 1_850_000_000L, Container = "flac" });

        Assert.Equal(Text("3:05") + Text("flac"), Info.GetPrimaryHtml(song));
        Assert.Equal(Text("3:05"), Info.GetPrimaryHtml(song, new MediaInfoOptions { Container = false }));
    }

    [Fact]
    public void A_programme_shows_its_episode_title_or_original_air_date()
    {
        var episode = TestItems.Item(new { Id = TestItems.Id(1), Type = "Program", Name = "Show", IsSeries = true, EpisodeTitle = "Pilot", IndexNumber = 2, ParentIndexNumber = 1 });
        var film = TestItems.Item(new { Id = TestItems.Id(2), Type = "Program", Name = "Film", IsMovie = true, ProductionYear = 1984 });
        var other = TestItems.Item(new { Id = TestItems.Id(3), Type = "Program", Name = "Docu", PremiereDate = "2020-03-05T00:00:00.0000000Z" });

        Assert.Equal(Text("S1:E2 - Pilot"), Info.GetPrimaryHtml(episode));
        Assert.Equal(Text("Pilot"), Info.GetPrimaryHtml(episode, new MediaInfoOptions { EpisodeTitleIndexNumber = false }));
        Assert.Equal(Text("1984"), Info.GetPrimaryHtml(film));
        Assert.Equal(Text("Original air date: 2020-03-05 00:00:00"), Info.GetPrimaryHtml(other));
    }

    [Fact]
    public void The_programme_line_shows_time_channel_and_timer()
    {
        var program = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Program",
            Name = "News",
            ServerId = TestApp.ServerId,
            StartDate = "2024-05-17T11:30:00.0000000Z",
            EndDate = "2024-05-17T12:30:00.0000000Z",
            ChannelNumber = "5",
            ChannelName = "One",
            ChannelId = TestItems.Id(8),
            TimerId = "timer1"
        });

        Assert.Equal(
            Text("2024-05-17 11:30:00 2024-05-17 11:30:00 - 2024-05-17 12:30:00")
            + Text("CH 5")
            + Text("One")
            + "<span class=\"material-icons mediaInfoItem mediaInfoIconItem mediaInfoTimerIcon fiber_manual_record\" aria-hidden=\"true\"></span>",
            Info.GetSecondaryHtml(program));

        var interactive = Info.GetSecondaryHtml(program, new MediaInfoOptions { Interactive = true, ProgramTime = false, TimerIndicator = false });
        Assert.Equal(
            Text("CH 5")
            + $"<a is=\"emby-linkbutton\" class=\"button-flat mediaInfoItem emby-button\" href=\"#/details?id={TestItems.Id(8)}&amp;serverId={TestApp.ServerId}\">One</a>",
            interactive);
    }

    [Fact]
    public void Only_programmes_have_a_secondary_line()
    {
        var movie = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", ChannelName = "One" });

        Assert.Equal(string.Empty, Info.GetSecondaryHtml(movie));
    }

    [Fact]
    public void The_end_time_counts_from_the_resume_position()
    {
        var movie = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Movie",
            MediaType = "Video",
            RunTimeTicks = 72_000_000_000L,
            UserData = new { PlaybackPositionTicks = 36_000_000_000L }
        });
        var song = TestItems.Item(new { Id = TestItems.Id(2), Type = "Audio", MediaType = "Audio", RunTimeTicks = 72_000_000_000L });

        Assert.Equal("Ends at 2024-05-17 13:00:00", Info.GetEndsAt(movie));
        Assert.Null(Info.GetEndsAt(song));
        Assert.Equal("2024-05-17 12:30:00", Info.GetEndsAtFromPosition(72_000_000_000L, 0, playbackRate: 4, includeText: false));
    }

    [Theory]
    [InlineData(3840, 2160, false, "4K")]
    [InlineData(2560, 1440, false, "1440p")]
    [InlineData(1920, 1080, false, "1080p")]
    [InlineData(1920, 1080, true, "1080i")]
    [InlineData(1920, 800, false, "1080p")]
    [InlineData(1280, 720, false, "720p")]
    [InlineData(720, 480, false, "480p")]
    [InlineData(320, 240, false, null)]
    [InlineData(null, 1080, false, null)]
    public void Resolution_text(int? width, int? height, bool interlaced, string? expected) =>
        Assert.Equal(expected, MediaInfo.GetResolutionText(width, height, interlaced));

    [Theory]
    [InlineData(72_000_000_000L, "2h 0m")]
    [InlineData(57_000_000_000L, "1h 35m")]
    [InlineData(21_000_000_000L, "35m")]
    [InlineData(100L, "1m")]
    public void Display_duration(long ticks, string expected) => Assert.Equal(expected, DateTimeText.GetDisplayDuration(ticks));

    [Theory]
    [InlineData(39_090_000_000L, "1:05:09")]
    [InlineData(3_090_000_000L, "5:09")]
    [InlineData(70_000_000L, "0:07")]
    public void Display_running_time(long ticks, string expected) => Assert.Equal(expected, DateTimeText.GetDisplayRunningTime(ticks));
}

/// <summary>Progress bars and the badges on cards and rows (indicators.js).</summary>
public class IndicatorsTests
{
    private static readonly DateTimeOffset Now = ItemsTestContext.Now;

    [Fact]
    public void A_partly_watched_video_has_a_progress_bar()
    {
        BaseItemDto Video(double percent) => TestItems.Item(new { Type = "Movie", MediaType = "Video", UserData = new { PlayedPercentage = percent } });
        var song = TestItems.Item(new { Type = "Audio", MediaType = "Audio", UserData = new { PlayedPercentage = 50 } });

        Assert.Equal(
            "<div class=\"itemProgressBar\"><div class=\"itemProgressBarForeground\" style=\"width:50%;\"></div></div>",
            Indicators.GetProgressBarHtml(Video(50), Now));
        Assert.Equal(
            "<div class=\"itemProgressBar extra\"><div class=\"itemProgressBarForeground\" style=\"width:12.5%;\"></div></div>",
            Indicators.GetProgressBarHtml(Video(12.5), Now, "extra"));
        Assert.Equal(string.Empty, Indicators.GetProgressBarHtml(Video(0), Now));
        Assert.Equal(string.Empty, Indicators.GetProgressBarHtml(Video(100), Now));
        Assert.Equal(string.Empty, Indicators.GetProgressBarHtml(song, Now));
    }

    [Fact]
    public void A_programme_on_air_shows_the_elapsed_share_of_its_slot()
    {
        var program = TestItems.Item(new { Type = "Program", StartDate = "2024-05-17T11:45:00.0000000Z", EndDate = "2024-05-17T12:45:00.0000000Z" });
        var later = TestItems.Item(new { Type = "Program", StartDate = "2024-05-17T13:00:00.0000000Z", EndDate = "2024-05-17T14:00:00.0000000Z" });
        var recording = TestItems.Item(new { Type = "Program", TimerId = "t1", StartDate = "2024-05-17T11:45:00.0000000Z", EndDate = "2024-05-17T12:45:00.0000000Z" });

        Assert.Equal(
            "<div is=\"emby-progressbar\" data-automode=\"time\" data-starttime=\"1715946300000\" data-endtime=\"1715949900000\" class=\"itemProgressBar\">"
            + "<div class=\"itemProgressBarForeground\" style=\"width:25%;\"></div></div>",
            Indicators.GetProgressBarHtml(program, Now));
        Assert.Equal(string.Empty, Indicators.GetProgressBarHtml(later, Now));
        Assert.Contains("itemProgressBarForeground itemProgressBarForeground-recording", Indicators.GetProgressBarHtml(recording, Now), StringComparison.Ordinal);
    }

    [Fact]
    public void Played_state_and_unplayed_count()
    {
        var played = TestItems.Item(new { Type = "Movie", MediaType = "Video", UserData = new { Played = true } });
        var unplayed = TestItems.Item(new { Type = "Movie", MediaType = "Video", UserData = new { Played = false } });
        var series = TestItems.Item(new { Type = "Series", UserData = new { UnplayedItemCount = 5 } });
        var many = TestItems.Item(new { Type = "Series", UserData = new { UnplayedItemCount = 100 } });
        var song = TestItems.Item(new { Type = "Audio", MediaType = "Audio", UserData = new { Played = true } });

        Assert.Equal(
            "<div class=\"playedIndicator indicator\"><span class=\"material-icons indicatorIcon check\" aria-hidden=\"true\"></span></div>",
            Indicators.GetPlayedIndicatorHtml(played));
        Assert.Equal(string.Empty, Indicators.GetPlayedIndicatorHtml(unplayed));
        Assert.Equal("<div class=\"countIndicator indicator\">5</div>", Indicators.GetPlayedIndicatorHtml(series));
        Assert.Equal("<div class=\"countIndicator indicator\">99+</div>", Indicators.GetPlayedIndicatorHtml(many));
        Assert.Equal(string.Empty, Indicators.GetPlayedIndicatorHtml(song));
    }

    [Fact]
    public void Child_count_above_the_minimum()
    {
        var three = TestItems.Item(new { Type = "Series", ChildCount = 3 });
        var one = TestItems.Item(new { Type = "Series", ChildCount = 1 });

        Assert.Equal("<div class=\"countIndicator indicator\">3</div>", Indicators.GetChildCountIndicatorHtml(three, 1));
        Assert.Equal(string.Empty, Indicators.GetChildCountIndicatorHtml(one, 1));
        Assert.Equal("<div class=\"countIndicator indicator\">1</div>", Indicators.GetChildCountIndicatorHtml(one));
    }

    [Fact]
    public void Timer_indicators()
    {
        var single = TestItems.Item(new { Type = "Program", TimerId = "t1" });
        var series = TestItems.Item(new { Type = "Program", TimerId = "t1", SeriesTimerId = "s1", Status = "New" });
        var cancelled = TestItems.Item(new { Type = "Program", SeriesTimerId = "s1" });
        var seriesTimer = new SeriesTimerInfoDto { Id = "s1", Name = "Show" }.ToItem();
        var plain = TestItems.Item(new { Type = "Program" });

        Assert.Contains("timerIndicator indicatorIcon fiber_manual_record", Indicators.GetTimerIndicator(single), StringComparison.Ordinal);
        Assert.Contains("timerIndicator indicatorIcon fiber_smart_record", Indicators.GetTimerIndicator(series), StringComparison.Ordinal);
        Assert.Contains("timerIndicator timerIndicator-inactive indicatorIcon fiber_smart_record", Indicators.GetTimerIndicator(cancelled), StringComparison.Ordinal);
        Assert.Contains("timerIndicator indicatorIcon fiber_smart_record", Indicators.GetTimerIndicator(seriesTimer), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Indicators.GetTimerIndicator(plain));
    }

    [Theory]
    [InlineData("Video", "videocam")]
    [InlineData("Folder", "folder")]
    [InlineData("PhotoAlbum", "photo_album")]
    [InlineData("Photo", "photo")]
    public void Type_indicators(string type, string icon) =>
        Assert.Equal(
            $"<div class=\"indicator videoIndicator\"><span class=\"material-icons indicatorIcon {icon}\" aria-hidden=\"true\"></span></div>",
            Indicators.GetTypeIndicator(TestItems.Item(new { Type = type })));

    [Fact]
    public void A_movie_has_no_type_indicator() =>
        Assert.Equal(string.Empty, Indicators.GetTypeIndicator(TestItems.Item(new { Type = "Movie" })));

    [Fact]
    public void Episodes_without_a_file_are_missing_or_unaired()
    {
        var missing = TestItems.Item(new { Type = "Episode", LocationType = "Virtual", PremiereDate = "2020-03-05T00:00:00.0000000Z" });
        var unaired = TestItems.Item(new { Type = "Episode", LocationType = "Virtual", PremiereDate = "2030-03-05T00:00:00.0000000Z" });
        var present = TestItems.Item(new { Type = "Episode", LocationType = "FileSystem" });

        Assert.Equal("<div class=\"missingIndicator\">Missing</div>", Indicators.GetMissingIndicator(missing, Now));
        Assert.Equal("<div class=\"unairedIndicator\">Unaired</div>", Indicators.GetMissingIndicator(unaired, Now));
        Assert.Equal(string.Empty, Indicators.GetMissingIndicator(present, Now));
    }

    [Fact]
    public void Sync_indicator()
    {
        var item = TestItems.Item(new { Type = "Movie" });
        Assert.Equal(string.Empty, Indicators.GetSyncIndicator(item));

        item.EnsureExtras().SyncPercent = 100;
        Assert.Contains("fullSyncIndicator", Indicators.GetSyncIndicator(item), StringComparison.Ordinal);

        item.EnsureExtras().SyncPercent = 40;
        Assert.Contains("emptySyncIndicator", Indicators.GetSyncIndicator(item), StringComparison.Ordinal);
    }
}

/// <summary>Display names and what may be done with an item (itemHelper.js).</summary>
public class ItemHelperTests : ItemsTestContext
{
    private Globalize Strings => Get<Globalize>();

    [Fact]
    public void Display_names()
    {
        var episode = TestItems.Item(new { Type = "Episode", Name = "Pilot", IndexNumber = 2, ParentIndexNumber = 1 });
        var doubleEpisode = TestItems.Item(new { Type = "Episode", Name = "Finale", IndexNumber = 9, IndexNumberEnd = 10, ParentIndexNumber = 3 });
        var special = TestItems.Item(new { Type = "Episode", Name = "Behind the Scenes", IndexNumber = 1, ParentIndexNumber = 0 });
        var channel = TestItems.Item(new { Type = "TvChannel", Name = "One", ChannelNumber = "5" });
        var program = TestItems.Item(new { Type = "Program", Name = "Show", EpisodeTitle = "The Episode" });
        var movie = TestItems.Item(new { Type = "Movie", Name = "The Movie", IndexNumber = 1 });

        Assert.Equal("S1:E2 - Pilot", ItemHelper.GetDisplayName(episode, Strings));
        Assert.Equal("2. Pilot", ItemHelper.GetDisplayName(episode, Strings, includeParentInfo: false));
        Assert.Equal("Pilot", ItemHelper.GetDisplayName(episode, Strings, includeIndexNumber: false));
        Assert.Equal("S3:E9-10 - Finale", ItemHelper.GetDisplayName(doubleEpisode, Strings));
        Assert.Equal("Special - Behind the Scenes", ItemHelper.GetDisplayName(special, Strings));
        Assert.Equal("5 One", ItemHelper.GetDisplayName(channel, Strings));
        Assert.Equal("The Episode", ItemHelper.GetDisplayName(program, Strings));
        Assert.Equal("The Movie", ItemHelper.GetDisplayName(movie, Strings));
    }

    [Theory]
    [InlineData("Movie", "Video", true)]
    [InlineData("Episode", "Video", true)]
    [InlineData("Series", null, true)]
    [InlineData("Season", null, true)]
    [InlineData("BoxSet", null, true)]
    [InlineData("Folder", null, true)]
    [InlineData("AudioBook", "Audio", true)]
    [InlineData("Book", "Book", true)]
    [InlineData("Audio", "Audio", false)]
    [InlineData("MusicAlbum", null, false)]
    [InlineData("Program", "Video", false)]
    [InlineData("TvChannel", "Video", false)]
    public void What_can_be_marked_played(string type, string? mediaType, bool expected) =>
        Assert.Equal(expected, ItemHelper.CanMarkPlayed(TestItems.Item(new { Type = type, MediaType = mediaType })));

    [Fact]
    public void Only_items_with_user_data_can_be_rated()
    {
        Assert.True(ItemHelper.CanRate(TestItems.Item(new { Type = "Movie", UserData = new { IsFavorite = false } })));
        Assert.False(ItemHelper.CanRate(TestItems.Item(new { Type = "Movie" })));
        Assert.False(ItemHelper.CanRate(TestItems.Item(new { Type = "Program", UserData = new { IsFavorite = false } })));
        Assert.False(ItemHelper.CanRate(TestItems.Item(new { Type = "CollectionFolder", UserData = new { IsFavorite = false } })));
    }

    [Fact]
    public void Collections_and_playlists_accept_the_right_items()
    {
        var movie = TestItems.Item(new { Type = "Movie", MediaType = "Video" });
        var song = TestItems.Item(new { Type = "Audio", MediaType = "Audio" });
        var library = TestItems.Item(new { Type = "CollectionFolder", CollectionType = "movies", IsFolder = true });
        var program = TestItems.Item(new { Type = "Program", MediaType = "Video" });
        var photo = TestItems.Item(new { Type = "Photo", MediaType = "Photo" });

        Assert.True(ItemHelper.SupportsAddingToCollection(movie));
        Assert.False(ItemHelper.SupportsAddingToCollection(song));
        Assert.False(ItemHelper.SupportsAddingToCollection(library));
        Assert.False(ItemHelper.SupportsAddingToCollection(photo));

        Assert.True(ItemHelper.SupportsAddingToPlaylist(movie));
        Assert.True(ItemHelper.SupportsAddingToPlaylist(song));
        Assert.True(ItemHelper.SupportsAddingToPlaylist(library));
        Assert.False(ItemHelper.SupportsAddingToPlaylist(program));
        Assert.False(ItemHelper.SupportsAddingToPlaylist(photo));
    }

    [Fact]
    public void Editing_needs_an_administrator()
    {
        var admin = new UserDto { Policy = new UserPolicy { IsAdministrator = true } };
        var user = new UserDto { Policy = new UserPolicy { IsAdministrator = false, EnableSubtitleManagement = true } };
        var movie = TestItems.Item(new { Type = "Movie", MediaType = "Video" });
        var program = TestItems.Item(new { Type = "Program", MediaType = "Video" });
        var song = TestItems.Item(new { Type = "Audio", MediaType = "Audio" });

        Assert.True(ItemHelper.CanEdit(admin, movie));
        Assert.False(ItemHelper.CanEdit(user, movie));
        Assert.False(ItemHelper.CanEdit(admin, program));
        Assert.True(ItemHelper.CanIdentify(admin, movie));
        Assert.False(ItemHelper.CanIdentify(admin, song));
        Assert.True(ItemHelper.CanEditImages(admin, movie));
        Assert.True(ItemHelper.CanEditSubtitles(user, movie));
        Assert.False(ItemHelper.CanEditSubtitles(admin, song));
        Assert.True(ItemHelper.CanEditLyrics(admin, song));
        Assert.False(ItemHelper.CanEditLyrics(user, song));
        Assert.True(ItemHelper.CanRefreshMetadata(movie, admin));
        Assert.False(ItemHelper.CanRefreshMetadata(movie, user));
        Assert.False(ItemHelper.CanRefreshMetadata(program, admin));
    }

    [Theory]
    [InlineData("movies", "movie")]
    [InlineData("tvshows", "tv")]
    [InlineData("music", "music_note")]
    [InlineData("livetv", "live_tv")]
    [InlineData("unknown", "folder")]
    [InlineData(null, "ondemand_video")]
    public void Library_icons(string? collectionType, string icon) => Assert.Equal(icon, ItemIcons.ForLibrary(collectionType));

    [Fact]
    public void Item_type_icons_fall_back_to_the_default()
    {
        Assert.Equal("album", ItemIcons.ForItemType("MusicAlbum"));
        Assert.Equal("tv", ItemIcons.ForItemType("Series", "folder"));
        Assert.Equal("folder", ItemIcons.ForItemType("Studio", "folder"));
        Assert.Null(ItemIcons.ForItemType("Studio"));
    }
}
