using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Home;

/// <summary>Which sections the home screen shows and what they request (homesections.js, favorites.js).</summary>
public class HomeSectionsTests : ItemsTestContext
{
    private static BaseItemDto View(int number, string? collectionType, string type = "CollectionFolder") =>
        TestItems.Item(new { Id = TestItems.Id(number), Name = "View " + number, Type = type, CollectionType = collectionType, IsFolder = true });

    [Fact]
    public void Default_sections()
    {
        SignIn();

        Assert.Equal(
            new[] { "smalllibrarytiles", "resume", "resumeaudio", "resumebook", "livetv", "nextup", "latestmedia", "none", "none", "none" },
            Get<HomeSections>().GetSections());
        Assert.Equal("smalllibrarytiles", HomeSections.GetDefaultSection(0));
        Assert.Equal(string.Empty, HomeSections.GetDefaultSection(10));
        Assert.Equal(string.Empty, HomeSections.GetDefaultSection(-1));
    }

    [Fact]
    public void Sections_follow_the_users_settings()
    {
        SignIn();
        var settings = Get<UserSettings>();
        settings.Set("homesection0", "latestmedia");
        settings.Set("homesection1", "none");
        settings.Set("homesection2", "folders");
        settings.Set("homesection8", "activerecordings");

        Assert.Equal(
            new[] { "latestmedia", "none", "smalllibrarytiles", "resumebook", "livetv", "nextup", "latestmedia", "none", "activerecordings", "none" },
            Get<HomeSections>().GetSections());
    }

    [Fact]
    public void Latest_sections_skip_excluded_libraries_and_types_without_latest_items()
    {
        var user = SignIn();
        var views = new[]
        {
            View(1, "movies"), View(2, "tvshows"), View(3, "music"), View(4, "playlists"), View(5, "livetv"),
            View(6, "boxsets"), View(7, "folders"), View(8, null), View(9, "books"), View(10, "channels", "Channel")
        };

        // "channels" is no collection type of current servers, so a channel folder is not recognised by it (as in the original).
        Assert.Equal(new[] { "View 1", "View 2", "View 3", "View 8", "View 9", "View 10" }, HomeSections.GetLatestViews(views, user).Select(view => view.Name));

        user.Configuration!.LatestItemsExcludes = [Guid.Parse(TestItems.Id(2)), Guid.Parse(TestItems.Id(8))];

        Assert.Equal(new[] { "View 1", "View 3", "View 9", "View 10" }, HomeSections.GetLatestViews(views, user).Select(view => view.Name));
        Assert.Equal(6, HomeSections.GetLatestViews(views, null).Count);
    }

    [Theory]
    [InlineData("movies", 16)]
    [InlineData("music", 30)]
    [InlineData(null, 16)]
    public void Latest_asks_for_more_music(string? collectionType, int limit)
    {
        Assert.Equal(limit, HomeSections.GetLatestLimit(View(1, collectionType)));
    }

    [Theory]
    [InlineData("movies", "overflowPortrait", true, false, true)]
    [InlineData("tvshows", "overflowPortrait", true, true, true)]
    [InlineData("books", "overflowPortrait", false, false, true)]
    [InlineData("music", "overflowSquare", false, true, true)]
    [InlineData("homevideos", "overflowSquare", false, false, true)]
    [InlineData("photos", "overflowBackdrop", false, false, false)]
    [InlineData(null, "overflowBackdrop", true, true, true)]
    public void Latest_cards_depend_on_the_library_type(string? collectionType, string shape, bool showYear, bool showParentTitle, bool showTitle)
    {
        var options = HomeSections.GetLatestOptions(View(1, collectionType));

        Assert.Equal(shape, options.Shape);
        Assert.Equal(showYear, options.ShowYear);
        Assert.Equal(showParentTitle, options.ShowParentTitle);
        Assert.Equal(showTitle, options.ShowTitle.IsTruthy);
        Assert.Equal(showTitle, options.OverlayPlayButton);
        Assert.Equal(collectionType is not ("movies" or "tvshows" or "music"), options.PreferThumb.IsAuto);
        Assert.True(options.PreferParentPoster);
        Assert.True(options.ShowChildCountIndicator);
        Assert.Equal("home", options.Context);
        Assert.Equal(2, options.Lines);
    }

    [Fact]
    public void Latest_cards_of_a_channel_are_portrait()
    {
        var options = HomeSections.GetLatestOptions(View(1, null, "Channel"));

        Assert.Equal("overflowPortrait", options.Shape);
        Assert.False(options.PreferThumb.IsSet);
    }

    [Fact]
    public void Resume_cards_and_monitors()
    {
        SignIn();
        var sections = Get<HomeSections>();

        var video = sections.GetResumeOptions("Video");
        var book = sections.GetResumeOptions("Book");

        Assert.Equal("overflowBackdrop", video.Shape);
        Assert.Equal("overflowPortrait", book.Shape);
        Assert.True(video.PreferThumb.IsTruthy);
        Assert.True(video.InheritThumb);
        Assert.True(video.ShowTitle.IsTruthy);
        Assert.True(video.ShowParentTitle);
        Assert.True(video.ShowYear);
        Assert.True(video.OverlayPlayButton);
        Assert.Equal(2, video.Lines);
        Assert.Equal("home", video.Context);

        Get<UserSettings>().UseEpisodeImagesInNextUpAndResume = true;

        Assert.False(sections.GetResumeOptions("Video").InheritThumb);
        Assert.False(sections.GetNextUpOptions().InheritThumb);
        Assert.Equal("videoplayback,markplayed", HomeSections.GetResumeMonitor("Video"));
        Assert.Equal("audioplayback,markplayed", HomeSections.GetResumeMonitor("Audio"));
        Assert.Equal("markplayed", HomeSections.GetResumeMonitor("Book"));
    }

    [Fact]
    public void Next_up_looks_back_a_year_unless_configured()
    {
        SignIn();
        var sections = Get<HomeSections>();

        Assert.Equal(Now.AddDays(-365), sections.GetNextUpCutoff(Now));

        Get<UserSettings>().Set("maxDaysForNextUp", "30", false);
        Assert.Equal(Now.AddDays(-30), sections.GetNextUpCutoff(Now));

        Get<UserSettings>().Set("maxDaysForNextUp", "0", false);
        Assert.Null(sections.GetNextUpCutoff(Now));
    }

    [Fact]
    public void Favorite_sections_in_the_original_order()
    {
        var sections = HomeSections.FavoriteSections;

        Assert.Equal(
            new[]
            {
                "Movies", "Shows", "HeaderSeasons", "Episodes", "HeaderVideos", "MusicVideos", "Collections", "Playlists", "Studios",
                "People", "Artists", "Albums", "Songs", "Books", "Channels", "HeaderPhotoAlbums", "Photos"
            },
            sections.Select(section => section.NameKey));
        Assert.Equal(
            new[]
            {
                "Movie", "Series", "Season", "Episode", "Video", "MusicVideo", "BoxSet", "Playlist", "Studio",
                "Person", "MusicArtist", "MusicAlbum", "Audio", "Book", "LiveTVChannel", "PhotoAlbum", "Photo"
            },
            sections.Select(section => section.Types));
        Assert.Equal(new int?[] { 2, 2, 2, 2, 1, 1, 1, 1, 1, 1, 1, 2, 2, 2, 1, 1, 1 }, sections.Select(section => section.Options.Lines));
        Assert.All(sections, section =>
        {
            Assert.True(section.ShowsTitle);
            Assert.Equal(section.Shape, section.Options.Shape);
            Assert.True(section.Options.CenterText);
            Assert.StartsWith("overflow", section.Shape, StringComparison.Ordinal);
        });

        var songs = sections[12];
        Assert.Equal(ItemAction.InstantMix, songs.Options.Action);
        Assert.True(songs.Options.OverlayMoreButton);
        Assert.Null(songs.Options.OverlayPlayButton);
        Assert.Equal("overflowSquare", songs.Shape);
        Assert.Equal("overflowBackdrop", sections[3].Shape);
        Assert.Equal("overflowPortrait", sections[0].Shape);
    }

    [Fact]
    public async Task Favorites_of_an_item_type_come_from_the_items_query()
    {
        SignIn();
        App.Server.OnGet("/Items", new { Items = new[] { TestItems.Movie(1, "Movie") }, TotalRecordCount = 1 });

        var items = await HomeSections.GetFavoritesAsync(Get<ItemQueries>(), Get<JellyfinApi>(), HomeSections.FavoriteSections[0]);

        Assert.Equal("Movie", Assert.Single(items).Name);
        var request = Assert.Single(App.Server.Requests);
        Assert.Equal("/Items", request.Path);
        Assert.Equal(TestApp.UserId.ToString("N"), request.Query("userId"));
        Assert.Equal("SeriesSortName,SortName", request.Query("SortBy"));
        Assert.Equal("Ascending", request.Query("SortOrder"));
        Assert.Equal("IsFavorite", request.Query("Filters"));
        Assert.Equal("true", request.Query("Recursive"));
        Assert.Equal("PrimaryImageAspectRatio", request.Query("Fields"));
        Assert.Equal("false", request.Query("CollapseBoxSetItems"));
        Assert.Equal("Virtual", request.Query("ExcludeLocationTypes"));
        Assert.Equal("false", request.Query("EnableTotalRecordCount"));
        Assert.Equal("20", request.Query("Limit"));
        Assert.Equal("Movie", request.Query("IncludeItemTypes"));
    }

    [Fact]
    public async Task Favorite_studios_artists_and_people_have_their_own_queries()
    {
        SignIn();
        App.Server.OnGet("/Studios", new { Items = Array.Empty<object>(), TotalRecordCount = 0 });
        App.Server.OnGet("/Persons", new { Items = Array.Empty<object>(), TotalRecordCount = 0 });
        var sections = HomeSections.FavoriteSections;
        var queries = Get<ItemQueries>();
        var api = Get<JellyfinApi>();

        await HomeSections.GetFavoritesAsync(queries, api, sections.Single(section => section.Types == "Studio"));
        await HomeSections.GetFavoritesAsync(queries, api, sections.Single(section => section.Types == "MusicArtist"));
        await HomeSections.GetFavoritesAsync(queries, api, sections.Single(section => section.Types == "Person"));

        var requests = App.Server.Requests;
        Assert.Equal(new[] { "/Studios", "/Persons", "/Persons" }, requests.Select(request => request.Path));
        Assert.Equal("true", requests[0].Query("isFavorite"));
        Assert.Equal("Thumb", requests[0].Query("enableImageTypes"));
        Assert.Equal("20", requests[0].Query("limit"));
        Assert.Null(requests[0].Query("IncludeItemTypes"));
        Assert.Equal("Artist", requests[1].Query("PersonTypes"));
        Assert.Equal("IsFavorite", requests[1].Query("Filters"));
        Assert.Null(requests[1].Query("IncludeItemTypes"));
        Assert.Equal("Artist,AlbumArtist", requests[2].Query("ExcludePersonTypes"));
        Assert.All(requests, request => Assert.Equal(TestApp.UserId.ToString("N"), request.Query("userId")));
    }

    [Fact]
    public async Task Home_queries_send_the_original_parameters()
    {
        SignIn();
        var empty = new { Items = Array.Empty<object>(), TotalRecordCount = 0 };
        App.Server.OnGet("/UserItems/Resume", empty);
        App.Server.OnGet("/Shows/NextUp", empty);
        App.Server.OnGet("/Items/Latest", Array.Empty<object>());
        App.Server.OnGet("/LiveTv/Programs/Recommended", empty);
        App.Server.OnGet("/LiveTv/Recordings", empty);
        var queries = Get<ItemQueries>();
        var user = TestApp.UserId.ToString("N");

        await queries.GetResumeItemsAsync("Audio");
        await queries.GetNextUpAsync(Now.AddDays(-365), enableRewatching: false);
        await queries.GetNextUpAsync(null, enableRewatching: true);
        await queries.GetLatestItemsAsync(TestItems.Id(3), 30);
        await queries.GetOnNowProgramsAsync(24);
        await queries.GetActiveRecordingsAsync();

        var requests = App.Server.Requests.Select(request => request.Uri.PathAndQuery).ToArray();
        Assert.Equal(
            new[]
            {
                $"/UserItems/Resume?userId={user}&limit=12&fields=PrimaryImageAspectRatio&imageTypeLimit=1&enableImageTypes=Primary%2CBackdrop%2CThumb&enableTotalRecordCount=false&mediaTypes=Audio",
                $"/Shows/NextUp?userId={user}&limit=24&fields=PrimaryImageAspectRatio%2CDateCreated%2CPath%2CMediaSourceCount&imageTypeLimit=1&enableImageTypes=Primary%2CBackdrop%2CThumb&enableTotalRecordCount=false&nextUpDateCutoff=2023-05-18T12%3A00%3A00.000Z&enableResumable=false&enableRewatching=false",
                $"/Shows/NextUp?userId={user}&limit=24&fields=PrimaryImageAspectRatio%2CDateCreated%2CPath%2CMediaSourceCount&imageTypeLimit=1&enableImageTypes=Primary%2CBackdrop%2CThumb&enableTotalRecordCount=false&enableResumable=false&enableRewatching=true",
                $"/Items/Latest?userId={user}&limit=30&fields=PrimaryImageAspectRatio%2CPath&imageTypeLimit=1&enableImageTypes=Primary%2CBackdrop%2CThumb&parentId={TestItems.Id(3)}",
                $"/LiveTv/Programs/Recommended?userId={user}&isAiring=true&limit=24&imageTypeLimit=1&enableImageTypes=Primary%2CThumb%2CBackdrop&enableTotalRecordCount=false&fields=ChannelInfo%2CPrimaryImageAspectRatio",
                $"/LiveTv/Recordings?userId={user}&limit=12&fields=PrimaryImageAspectRatio&enableTotalRecordCount=false&isInProgress=true"
            },
            requests);
    }
}
