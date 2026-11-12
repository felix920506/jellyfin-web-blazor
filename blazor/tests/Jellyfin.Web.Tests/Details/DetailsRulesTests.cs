using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;

namespace Jellyfin.Web.Tests.Details;

/// <summary>The per-item-type rules of the details page: which buttons and sections an item gets.</summary>
public class DetailsRulesTests
{
    private static readonly DateTimeOffset Now = new(2024, 5, 17, 12, 0, 0, TimeSpan.Zero);

    private static BaseItemDto Item(string type, string? mediaType = null, bool folder = false, long position = 0) => TestItems.Item(new
    {
        Id = TestItems.Id(1),
        Name = "Item",
        Type = type,
        MediaType = mediaType,
        IsFolder = folder,
        UserData = new { PlaybackPositionTicks = position }
    });

    // The server has no item type for a series timer: the client wraps it as one.
    private static BaseItemDto SeriesTimer() => new SeriesTimerInfoDto { Id = "st1", Name = "A recorded series" }.ToItem();

    private static UserDto User(bool admin = false, bool liveTv = false) =>
        new() { Policy = new UserPolicy { IsAdministrator = admin, EnableLiveTvManagement = liveTv } };

    // --- Play buttons -------------------------------------------------------------------------------

    [Fact]
    public void A_movie_gets_the_play_button_only()
    {
        var buttons = DetailsRules.GetPlayButtons(Item("Movie", "Video"), canPlay: true, Now);

        Assert.Equal(new DetailPlayButtons(Play: true, Replay: false, InstantMix: false, Shuffle: false, Resumable: false), buttons);
        Assert.Equal("Play", buttons.PlayTitleKey);
    }

    [Fact]
    public void A_started_movie_resumes_and_can_start_over()
    {
        var buttons = DetailsRules.GetPlayButtons(Item("Movie", "Video", position: 600_000_000), canPlay: true, Now);

        Assert.True(buttons.Play);
        Assert.True(buttons.Replay);
        Assert.True(buttons.Resumable);
        Assert.Equal("ButtonResume", buttons.PlayTitleKey);
    }

    [Fact]
    public void Nothing_plays_what_no_player_can_play()
    {
        Assert.Equal(DetailPlayButtons.None, DetailsRules.GetPlayButtons(Item("Person"), canPlay: false, Now));
    }

    [Theory]
    [InlineData("MusicAlbum", true, true, true)]
    [InlineData("MusicArtist", true, true, true)]
    [InlineData("MusicGenre", false, true, true)]
    [InlineData("Audio", false, true, false)]
    [InlineData("Series", true, false, true)]
    [InlineData("Season", true, false, true)]
    [InlineData("BoxSet", true, false, true)]
    [InlineData("Playlist", true, false, true)]
    [InlineData("Episode", false, false, false)]
    public void Instant_mix_is_for_music_and_shuffle_for_folders_and_music(string type, bool folder, bool instantMix, bool shuffle)
    {
        var buttons = DetailsRules.GetPlayButtons(Item(type, folder: folder), canPlay: true, Now);

        Assert.True(buttons.Play);
        Assert.Equal(instantMix, buttons.InstantMix);
        Assert.Equal(shuffle, buttons.Shuffle);
    }

    [Fact]
    public void A_programme_plays_only_while_it_is_on_air()
    {
        BaseItemDto Program(int startHour, int endHour) => TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Program",
            StartDate = new DateTimeOffset(2024, 5, 17, startHour, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(2024, 5, 17, endHour, 0, 0, TimeSpan.Zero)
        });

        var onAir = DetailsRules.GetPlayButtons(Program(11, 13), canPlay: false, Now);
        var over = DetailsRules.GetPlayButtons(Program(9, 11), canPlay: true, Now);
        var later = DetailsRules.GetPlayButtons(Program(13, 14), canPlay: true, Now);

        Assert.Equal(new DetailPlayButtons(true, false, false, false, false), onAir);
        Assert.Equal(DetailPlayButtons.None, over);
        Assert.Equal(DetailPlayButtons.None, later);
        Assert.True(DetailsRules.ShowMainButtons(Program(11, 13), onAir));
        Assert.False(DetailsRules.ShowMainButtons(Program(9, 11), over));
        Assert.True(DetailsRules.ShowMainButtons(Item("Person"), DetailPlayButtons.None));
    }

    // --- Other buttons ------------------------------------------------------------------------------

    [Fact]
    public void The_trailer_button_needs_a_local_or_remote_trailer()
    {
        Assert.False(DetailsRules.ShowTrailerButton(Item("Movie")));
        Assert.True(DetailsRules.ShowTrailerButton(TestItems.Item(new { Type = "Movie", LocalTrailerCount = 1 })));
        Assert.True(DetailsRules.ShowTrailerButton(TestItems.Item(new { Type = "Movie", RemoteTrailers = new[] { new { Url = "https://example.test/t" } } })));
        Assert.False(DetailsRules.ShowTrailerButton(TestItems.Item(new { Type = "Movie", LocalTrailerCount = 0, RemoteTrailers = Array.Empty<object>() })));
    }

    [Fact]
    public void Split_versions_is_for_administrators_and_grouped_versions()
    {
        var grouped = TestItems.Item(new
        {
            Type = "Movie",
            MediaSources = new object[] { new { Id = "a", Type = "Default" }, new { Id = "b", Type = "Grouping" } }
        });
        var single = TestItems.Item(new { Type = "Movie", MediaSources = new object[] { new { Id = "a", Type = "Default" } } });

        Assert.True(DetailsRules.ShowSplitVersions(grouped, User(admin: true)));
        Assert.False(DetailsRules.ShowSplitVersions(grouped, User()));
        Assert.False(DetailsRules.ShowSplitVersions(grouped, null));
        Assert.False(DetailsRules.ShowSplitVersions(single, User(admin: true)));
        Assert.False(DetailsRules.ShowSplitVersions(Item("Movie"), User(admin: true)));
    }

    [Fact]
    public void Only_books_get_a_download_button()
    {
        Assert.True(DetailsRules.ShowDownload(TestItems.Item(new { Type = "Book", CanDownload = true })));
        Assert.False(DetailsRules.ShowDownload(TestItems.Item(new { Type = "Book", CanDownload = false })));
        Assert.False(DetailsRules.ShowDownload(TestItems.Item(new { Type = "Movie", CanDownload = true })));
    }

    [Fact]
    public void The_live_tv_buttons_are_for_users_who_manage_live_tv()
    {
        var recording = TestItems.Item(new { Type = "Recording", TimerId = "t1", Status = "InProgress" });
        var finished = TestItems.Item(new { Type = "Recording", TimerId = "t1", Status = "Completed" });

        Assert.True(DetailsRules.ShowCancelTimer(recording, User(liveTv: true)));
        Assert.False(DetailsRules.ShowCancelTimer(recording, User()));
        Assert.False(DetailsRules.ShowCancelTimer(finished, User(liveTv: true)));
        Assert.False(DetailsRules.ShowCancelTimer(Item("Recording"), User(liveTv: true)));

        Assert.True(DetailsRules.ShowCancelSeriesTimer(SeriesTimer(), User(liveTv: true)));
        Assert.False(DetailsRules.ShowCancelSeriesTimer(SeriesTimer(), User()));
        Assert.False(DetailsRules.ShowCancelSeriesTimer(Item("Series"), User(liveTv: true)));

        Assert.True(DetailsRules.ShowRecordingFields(Item("Program"), User(liveTv: true)));
        Assert.False(DetailsRules.ShowRecordingFields(Item("Program"), User()));
        Assert.False(DetailsRules.ShowRecordingFields(Item("Movie"), User(liveTv: true)));
    }

    // --- Track selection applies --------------------------------------------------------------------

    [Fact]
    public void Versions_and_tracks_are_chosen_for_library_videos_only()
    {
        BaseItemDto Video(string type = "Movie", string mediaType = "Video", string sourceType = "Default", bool? display = null, string? itemSource = null) =>
            TestItems.Item(new
            {
                Type = type,
                MediaType = mediaType,
                MediaSources = new object[] { new { Id = "a", Type = sourceType } },
                EnableMediaSourceDisplay = display,
                SourceType = itemSource
            });

        Assert.True(DetailsRules.SupportsMediaSourceSelection(Video()));
        Assert.True(DetailsRules.SupportsMediaSourceSelection(Video(itemSource: "Library")));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Video(itemSource: "Channel")));
        Assert.True(DetailsRules.SupportsMediaSourceSelection(Video(itemSource: "Channel", display: true)));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Video(display: false)));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Video(mediaType: "Audio", type: "Audio")));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Video(type: "TvChannel")));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Video(sourceType: "Placeholder")));
        Assert.False(DetailsRules.SupportsMediaSourceSelection(Item("Movie", "Video")));
    }

    // --- Sections -----------------------------------------------------------------------------------

    [Theory]
    [InlineData("Playlist", true, DetailChildrenKind.Playlist)]
    [InlineData("Person", false, DetailChildrenKind.ItemsByName)]
    [InlineData("Studio", false, DetailChildrenKind.ItemsByName)]
    [InlineData("Genre", false, DetailChildrenKind.ItemsByName)]
    [InlineData("MusicGenre", false, DetailChildrenKind.ItemsByName)]
    [InlineData("MusicArtist", true, DetailChildrenKind.ItemsByName)]
    [InlineData("BoxSet", true, DetailChildrenKind.Collection)]
    [InlineData("Series", true, DetailChildrenKind.Children)]
    [InlineData("Season", true, DetailChildrenKind.Children)]
    [InlineData("MusicAlbum", true, DetailChildrenKind.Children)]
    [InlineData("Folder", true, DetailChildrenKind.Children)]
    [InlineData("Movie", false, DetailChildrenKind.None)]
    [InlineData("Episode", false, DetailChildrenKind.None)]
    [InlineData("Audio", false, DetailChildrenKind.None)]
    public void The_children_area_depends_on_the_type(string type, bool folder, DetailChildrenKind expected)
    {
        Assert.Equal(expected, DetailsRules.GetChildrenKind(Item(type, folder: folder)));
    }

    [Theory]
    [InlineData("Series", true, "HeaderSeasons", false)]
    [InlineData("Season", true, "Episodes", true)]
    [InlineData("MusicAlbum", true, "HeaderTracks", true)]
    [InlineData("Playlist", true, "Items", false)]
    [InlineData("Folder", false, "Items", false)]
    public void Children_have_their_place_and_title(string type, bool primaryColumn, string titleKey, bool titleHidden)
    {
        var item = Item(type, folder: true);

        Assert.Equal(primaryColumn, DetailsRules.ChildrenInPrimaryColumn(item));
        Assert.Equal(titleKey, DetailsRules.ChildrenTitleKey(item));
        Assert.Equal(titleHidden, DetailsRules.HideChildrenTitle(item));
    }

    [Theory]
    [InlineData("Movie", true)]
    [InlineData("Trailer", true)]
    [InlineData("Series", true)]
    [InlineData("Program", true)]
    [InlineData("Recording", true)]
    [InlineData("MusicAlbum", true)]
    [InlineData("MusicArtist", true)]
    [InlineData("Playlist", true)]
    [InlineData("Audio", true)]
    [InlineData("Episode", false)]
    [InlineData("Season", false)]
    [InlineData("Person", false)]
    [InlineData("BoxSet", false)]
    public void More_like_this_is_for_some_types(string type, bool expected)
    {
        Assert.Equal(expected, DetailsRules.ShowSimilar(Item(type)));
    }

    [Fact]
    public void Series_sections_are_for_series()
    {
        Assert.True(DetailsRules.ShowNextUp(Item("Series")));
        Assert.True(DetailsRules.ShowSeriesSchedule(Item("Series")));
        Assert.False(DetailsRules.ShowNextUp(Item("Season")));
        Assert.False(DetailsRules.ShowSeriesSchedule(Item("Movie")));
    }

    [Fact]
    public void Counts_decide_about_specials_and_additional_parts()
    {
        Assert.True(DetailsRules.ShowSpecials(TestItems.Item(new { Type = "Movie", SpecialFeatureCount = 2 })));
        Assert.False(DetailsRules.ShowSpecials(TestItems.Item(new { Type = "Movie", SpecialFeatureCount = 0 })));
        Assert.False(DetailsRules.ShowSpecials(Item("Movie")));
        Assert.True(DetailsRules.ShowAdditionalParts(TestItems.Item(new { Type = "Movie", PartCount = 2 })));
        Assert.False(DetailsRules.ShowAdditionalParts(TestItems.Item(new { Type = "Movie", PartCount = 1 })));
        Assert.False(DetailsRules.ShowAdditionalParts(Item("Movie")));
    }

    [Fact]
    public void Music_sections_follow_the_type()
    {
        var song = TestItems.Item(new { Type = "Audio", HasLyrics = true, AlbumArtists = new[] { new { Id = TestItems.Id(7), Name = "Artist" } } });
        var loneSong = TestItems.Item(new { Type = "Audio", AlbumArtists = Array.Empty<object>() });

        Assert.True(DetailsRules.ShowMusicVideos(Item("MusicAlbum")));
        Assert.False(DetailsRules.ShowMusicVideos(Item("Audio")));
        Assert.True(DetailsRules.ShowLyrics(song));
        Assert.False(DetailsRules.ShowLyrics(loneSong));
        Assert.False(DetailsRules.ShowLyrics(TestItems.Item(new { Type = "Movie", HasLyrics = true })));
        Assert.True(DetailsRules.ShowMoreFromArtist(song));
        Assert.False(DetailsRules.ShowMoreFromArtist(loneSong));
        Assert.True(DetailsRules.ShowMoreFromArtist(Item("MusicArtist")));
        Assert.False(DetailsRules.ShowMoreFromArtist(Item("Movie")));
    }

    [Fact]
    public void More_from_season_needs_an_episode_with_its_season_and_series()
    {
        var episode = TestItems.Item(new { Type = "Episode", SeasonId = TestItems.Id(2), SeriesId = TestItems.Id(3) });

        Assert.True(DetailsRules.ShowMoreFromSeason(episode));
        Assert.False(DetailsRules.ShowMoreFromSeason(TestItems.Item(new { Type = "Episode", SeriesId = TestItems.Id(3) })));
        Assert.False(DetailsRules.ShowMoreFromSeason(TestItems.Item(new { Type = "Movie", SeasonId = TestItems.Id(2), SeriesId = TestItems.Id(3) })));
    }

    [Theory]
    [InlineData("Movie", "Video", "HeaderCastAndCrew")]
    [InlineData("Series", null, "HeaderCastAndCrew")]
    [InlineData("Audio", "Audio", "People")]
    [InlineData("MusicAlbum", null, "People")]
    [InlineData("Book", "Book", "People")]
    [InlineData("Photo", "Photo", "People")]
    public void The_people_section_is_titled_by_media_type(string type, string? mediaType, string key)
    {
        Assert.Equal(key, DetailsRules.PeopleHeaderKey(Item(type, mediaType)));
    }

    [Fact]
    public void Scenes_need_an_image_for_the_first_chapter()
    {
        var withImages = TestItems.Item(new { Type = "Movie", Chapters = new[] { new { Name = "One", ImageTag = "c0" }, new { Name = "Two", ImageTag = "c1" } } });
        var without = TestItems.Item(new { Type = "Movie", Chapters = new[] { new { Name = "One" }, new { Name = "Two" } } });

        Assert.Equal(2, DetailsRules.GetScenes(withImages).Count);
        Assert.Empty(DetailsRules.GetScenes(without));
        Assert.Empty(DetailsRules.GetScenes(Item("Movie")));
    }

    [Theory]
    [InlineData("Movie", "movies")]
    [InlineData("BoxSet", "movies")]
    [InlineData("Episode", "tvshows")]
    [InlineData("MusicAlbum", "music")]
    [InlineData("Program", "livetv")]
    [InlineData("Person", null)]
    public void The_context_follows_the_type(string type, string? context)
    {
        Assert.Equal(context, DetailsRules.InferContext(Item(type)));
    }

    // --- People -------------------------------------------------------------------------------------

    [Fact]
    public void People_split_into_cast_and_guest_cast_without_the_artists()
    {
        var item = TestItems.Item(new
        {
            Type = "Episode",
            ServerId = "server1",
            People = new object[]
            {
                new { Id = TestItems.Id(1), Name = "Ann Actor", Role = "Hero", Type = "Actor", PrimaryImageTag = "p1" },
                new { Id = TestItems.Id(2), Name = "Gus Guest", Role = "Visitor", Type = "GuestStar" },
                new { Id = TestItems.Id(3), Name = "Dee Director", Type = "Director" },
                new { Id = TestItems.Id(3), Name = "Dee Director", Type = "Writer" },
                new { Id = TestItems.Id(4), Name = "Al Artist", Type = "Artist" },
                new { Id = TestItems.Id(5), Name = "Al Album", Type = "AlbumArtist" }
            }
        });

        var (cast, guestCast) = DetailsRules.SplitPeople(item);

        Assert.Equal(["Ann Actor", "Dee Director"], cast.Select(person => person.Name));
        Assert.Equal(["Gus Guest"], guestCast.Select(person => person.Name));
        Assert.All(cast, person => Assert.Equal("Person", person.TypeName()));
        Assert.Equal("server1", cast[0].ServerId);
        Assert.Equal("p1", cast[0].ImageTag("Primary"));
        Assert.Equal("Hero", cast[0].EnsureExtras().Role);
        Assert.Equal("Actor", cast[0].EnsureExtras().PersonType);
        Assert.Equal(["Director", "Writer"], cast[1].EnsureExtras().RoleList!.Select(role => role.Type));
        Assert.Equal("GuestStar", guestCast[0].EnsureExtras().PersonType);
    }

    [Fact]
    public void An_item_without_people_has_no_cast()
    {
        var (cast, guestCast) = DetailsRules.SplitPeople(Item("Movie"));

        Assert.Empty(cast);
        Assert.Empty(guestCast);
    }

    // --- Series air time and ages -------------------------------------------------------------------

    [Fact]
    public void A_series_tells_when_it_airs()
    {
        BaseItemDto Series(string status, string[] days, string? time) => TestItems.Item(new { Type = "Series", Status = status, AirDays = days, AirTime = time });
        string[] week = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

        Assert.Equal("Airs Mondays,Thursdays at 20:00", DetailsRules.GetSeriesAirTime(Series("Continuing", ["Monday", "Thursday"], "20:00")));
        Assert.Equal("Aired Fridays", DetailsRules.GetSeriesAirTime(Series("Ended", ["Friday"], null)));
        Assert.Equal("Airs daily at 7:30 PM", DetailsRules.GetSeriesAirTime(Series("Continuing", week, "7:30 PM")));
        Assert.Equal("Airs  at 21:00", DetailsRules.GetSeriesAirTime(Series("Continuing", [], "21:00")));
        Assert.Null(DetailsRules.GetSeriesAirTime(Series("Continuing", [], null)));
        Assert.Null(DetailsRules.GetSeriesAirTime(TestItems.Item(new { Type = "Movie", AirTime = "20:00" })));
    }

    [Theory]
    [InlineData("1980-05-17", "2024-05-17", 44)]
    [InlineData("1980-05-18", "2024-05-17", 43)]
    [InlineData("1980-06-01", "2024-05-17", 43)]
    [InlineData("1980-01-01", "2024-05-17", 44)]
    [InlineData("2024-05-01", "2024-05-17", 0)]
    [InlineData("2025-01-01", "2024-05-17", 0)]
    public void Ages_count_full_years(string start, string end, int years)
    {
        var from = DateTimeOffset.Parse(start + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(end + "T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(years, DetailsRules.YearsBetween(from, to));
    }

    // --- Collections and albums ---------------------------------------------------------------------

    [Fact]
    public void A_collection_sorts_its_items_into_type_sections()
    {
        BaseItemDto[] items =
        [
            TestItems.Item(new { Id = TestItems.Id(1), Type = "Series" }),
            TestItems.Item(new { Id = TestItems.Id(2), Type = "Movie", MediaType = "Video" }),
            TestItems.Item(new { Id = TestItems.Id(3), Type = "MusicVideo", MediaType = "Video" }),
            TestItems.Item(new { Id = TestItems.Id(4), Type = "Movie", MediaType = "Video" }),
            TestItems.Item(new { Id = TestItems.Id(5), Type = "Photo", MediaType = "Photo" }),
            TestItems.Item(new { Id = TestItems.Id(6), Type = "BoxSet" })
        ];

        var sections = DetailsRules.GroupCollectionItems(items);

        Assert.Equal(["Movies", "Series", "HeaderVideos", "Collections", "HeaderOtherItems"], sections.Select(section => section.TitleKey));
        Assert.Equal([TestItems.Id(2), TestItems.Id(4)], sections[0].Items.Select(item => item.IdText()));
        Assert.Equal("Movie", sections[0].Type);
        Assert.Equal([TestItems.Id(3)], sections[2].Items.Select(item => item.IdText()));
        Assert.Equal([TestItems.Id(5)], sections[4].Items.Select(item => item.IdText()));
    }

    [Fact]
    public void An_empty_collection_has_one_empty_section()
    {
        var sections = DetailsRules.GroupCollectionItems([]);

        var section = Assert.Single(sections);
        Assert.Equal("Items", section.TitleKey);
        Assert.Empty(section.Items);
    }

    [Fact]
    public void Tracks_show_their_artists_when_they_differ_from_the_album_artists()
    {
        object Artist(int id) => new { Id = TestItems.Id(id), Name = "Artist " + id };
        var same = TestItems.Item(new { Type = "Audio", ArtistItems = new[] { Artist(1) }, AlbumArtists = new[] { Artist(1) } });
        var guest = TestItems.Item(new { Type = "Audio", ArtistItems = new[] { Artist(1), Artist(2) }, AlbumArtists = new[] { Artist(1) } });

        Assert.False(DetailsRules.ShowTrackArtists([same, same]));
        Assert.True(DetailsRules.ShowTrackArtists([same, guest]));
        Assert.False(DetailsRules.ShowTrackArtists([]));
    }

    [Fact]
    public void Tracks_are_grouped_by_disc_when_there_are_several_or_a_later_one()
    {
        BaseItemDto Track(int? disc) => TestItems.Item(new { Type = "Audio", ParentIndexNumber = disc });

        Assert.False(DetailsRules.ShowDiscs([Track(1), Track(1)]));
        Assert.False(DetailsRules.ShowDiscs([Track(null), Track(null)]));
        Assert.True(DetailsRules.ShowDiscs([Track(1), Track(2)]));
        Assert.True(DetailsRules.ShowDiscs([Track(2), Track(2)]));
        Assert.False(DetailsRules.ShowDiscs([]));
    }
}
