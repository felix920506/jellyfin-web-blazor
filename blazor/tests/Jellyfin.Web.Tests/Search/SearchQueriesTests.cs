using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Search;

/// <summary>The requests and result rows of the search page (apps/legacy/features/search).</summary>
public class SearchQueriesTests
{
    private const string UserId = "user1";

    private static Dictionary<string, string?> Query(LibraryRequest request) =>
        request.Query.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => entry.Value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        });

    private static BaseItemDto Item(int number, string type, string? name = null) =>
        TestItems.Item(new { Id = TestItems.Id(number), Name = name ?? type + " " + number, Type = type });

    [Fact]
    public void Suggestions_are_random_favourites()
    {
        var request = SearchQueries.Suggestions("lib1", UserId);

        Assert.Equal("Items", request.Path);
        var query = Query(request);
        Assert.Equal(UserId, query["userId"]);
        Assert.Equal("IsFavoriteOrLiked,Random", query["sortBy"]);
        Assert.Equal("Movie,Series,MusicArtist", query["includeItemTypes"]);
        Assert.Equal("20", query["limit"]);
        Assert.Equal("true", query["recursive"]);
        Assert.Equal("0", query["imageTypeLimit"]);
        Assert.Equal("false", query["enableImages"]);
        Assert.Equal("lib1", query["parentId"]);
        Assert.Equal("false", query["enableTotalRecordCount"]);
        Assert.False(Query(SearchQueries.Suggestions(null, UserId)).ContainsKey("parentId"));
    }

    [Fact]
    public void A_search_everywhere_asks_all_requests()
    {
        var requests = SearchQueries.Requests(null, null, "  test ", UserId, false);

        Assert.Equal(new[] { "Artists", "Programs", "People", "Studios", "HeaderVideos", null }, requests.Select(request => request.Title));
        Assert.Equal(new[] { "Persons", "Items", "Persons", "Studios", "Items", "Items" }, requests.Select(request => request.Request.Path));
        Assert.All(requests, request =>
        {
            var query = Query(request.Request);
            Assert.Equal("test", query["searchTerm"]);
            Assert.Equal(UserId, query["userId"]);
            Assert.Equal("PrimaryImageAspectRatio,CanDelete,MediaSourceCount", query["fields"]);
            Assert.Equal("false", query["enableTotalRecordCount"]);
            Assert.Equal("1", query["imageTypeLimit"]);
            Assert.False(query.ContainsKey("parentId"));
        });

        Assert.Equal("Artist,AlbumArtist", Query(requests[0].Request)["personTypes"]);
        Assert.Equal("LiveTvProgram", Query(requests[1].Request)["includeItemTypes"]);
        Assert.Equal("Artist,AlbumArtist", Query(requests[2].Request)["excludePersonTypes"]);
        Assert.Equal("100", Query(requests[3].Request)["limit"]);

        var videos = Query(requests[4].Request);
        Assert.Equal("Video", videos["mediaTypes"]);
        Assert.Equal("Movie,Episode,TvChannel", videos["excludeItemTypes"]);
        Assert.Equal("true", videos["recursive"]);

        var items = Query(requests[5].Request);
        Assert.Equal("800", items["limit"]);
        Assert.Equal("true", items["recursive"]);
        Assert.Equal("Movie,Series,Episode,Playlist,MusicAlbum,Audio,TvChannel,PhotoAlbum,Photo,AudioBook,Book,BoxSet", items["includeItemTypes"]);
        Assert.Equal("false", items["isMissing"]);
        Assert.Equal(12, requests[5].ItemTypes!.Count);
    }

    [Fact]
    public void Missing_episodes_are_searched_when_the_user_displays_them()
    {
        var requests = SearchQueries.Requests(null, null, "test", UserId, true);

        Assert.False(Query(requests[^1].Request).ContainsKey("isMissing"));
    }

    [Theory]
    [InlineData("movies", "Artists,People,Studios,", "Movie")]
    [InlineData("tvshows", "Artists,People,Studios,", "Series,Episode")]
    [InlineData("music", "", "Playlist,MusicAlbum,Audio")]
    [InlineData("books", "", "Movie,Series,Episode,Playlist,MusicAlbum,Audio,TvChannel,PhotoAlbum,Photo,AudioBook,Book,BoxSet")]
    public void A_search_within_a_library_is_limited_to_its_types(string collectionType, string titles, string itemTypes)
    {
        var requests = SearchQueries.Requests("lib1", collectionType, "test", UserId, false);

        Assert.Equal(titles, string.Join(',', requests.Select(request => request.Title)));
        var items = Query(requests[^1].Request);
        Assert.Equal(itemTypes, items["includeItemTypes"]);
        Assert.Equal("lib1", items["parentId"]);
        Assert.Equal(itemTypes.Contains("Episode", StringComparison.Ordinal), items.ContainsKey("isMissing"));
        foreach (var request in requests.Where(request => request.Request.Path == "Studios"))
        {
            Assert.Equal("lib1", Query(request.Request)["parentId"]);
        }

        foreach (var request in requests.Where(request => request.Request.Path == "Persons"))
        {
            Assert.False(Query(request.Request).ContainsKey("parentId"));
        }
    }

    [Fact]
    public void A_live_tv_search_asks_for_programs_by_kind()
    {
        var requests = SearchQueries.Requests(null, "livetv", "news", UserId, false);

        Assert.Equal(new[] { "Movies", "Episodes", "Sports", "Kids", "News", "Programs", "Channels" }, requests.Select(request => request.Title));
        Assert.All(requests, request =>
        {
            Assert.Equal("Items", request.Request.Path);
            Assert.Null(request.ItemTypes);
            Assert.Equal("news", Query(request.Request)["searchTerm"]);
            Assert.Equal("100", Query(request.Request)["limit"]);
        });
        Assert.Equal("true", Query(requests[0].Request)["isMovie"]);

        var episodes = Query(requests[1].Request);
        Assert.Equal("LiveTvProgram", episodes["includeItemTypes"]);
        Assert.Equal("false", episodes["isMovie"]);
        Assert.Equal("true", episodes["isSeries"]);
        Assert.Equal("false", episodes["isSports"]);
        Assert.Equal("false", episodes["isKids"]);
        Assert.Equal("false", episodes["isNews"]);

        Assert.Equal("true", Query(requests[2].Request)["isSports"]);
        Assert.Equal("true", Query(requests[3].Request)["isKids"]);
        Assert.Equal("true", Query(requests[4].Request)["isNews"]);
        Assert.Equal("false", Query(requests[5].Request)["isSeries"]);
        Assert.Equal("TvChannel", Query(requests[6].Request)["includeItemTypes"]);

        var movies = requests[0].Cards!(SearchQueries.RowCards);
        Assert.Equal(CardShape.OverflowPortrait, movies.Shape);
        Assert.True(movies.PreferThumb.IsTruthy);
        Assert.True(movies.ShowAirTime);
        Assert.True(movies.ShowChannelName);
        Assert.False(movies.ShowTitle.IsTruthy);
        Assert.Null(requests[6].Cards);
    }

    [Fact]
    public void The_item_search_is_split_into_a_row_per_type()
    {
        var request = SearchQueries.Requests(null, null, "test", UserId, false)[^1];
        var items = new[] { Item(1, "Audio"), Item(2, "Movie"), Item(3, "Episode"), Item(4, "Movie"), Item(5, "BoxSet"), Item(6, "Trailer") };

        var sections = SearchQueries.Sections(request, items).ToList();

        Assert.Equal(new[] { "Movies", "Episodes", "Songs", "Collections" }, sections.Select(section => section.Title));
        Assert.Equal(new[] { "Movie 2", "Movie 4" }, sections[0].Items.Select(item => item.Name));
        Assert.True(sections[0].Cards.ShowYear);
        Assert.Equal(CardShape.AutoOverflow, sections[0].Cards.Shape);
        Assert.True(sections[0].Cards.ShowTitle.IsTruthy);
        Assert.True(sections[0].Cards.CenterText);
        Assert.True(sections[1].Cards.ShowParentTitle);
        Assert.True(sections[1].Cards.CoverImage);
        Assert.Equal(CardShape.OverflowSquare, sections[2].Cards.Shape);
        Assert.True(sections[2].Cards.ShowParentTitle);
        Assert.False(sections[3].Cards.ShowYear);
    }

    [Fact]
    public void Other_requests_fill_one_row_and_empty_rows_are_left_out()
    {
        var requests = SearchQueries.Requests(null, null, "test", UserId, false);
        var people = requests.Single(request => request.Title == "People");
        var studios = requests.Single(request => request.Title == "Studios");

        var section = Assert.Single(SearchQueries.Sections(people, [Item(1, "Person")]));

        Assert.Equal("People", section.Title);
        Assert.True(section.Cards.CoverImage);
        Assert.Equal(CardShape.OverflowSquare, Assert.Single(SearchQueries.Sections(studios, [Item(2, "Studio")])).Cards.Shape);
        Assert.Empty(SearchQueries.Sections(people, []));
        Assert.Empty(SearchQueries.Sections(requests[^1], []));
    }

    [Fact]
    public void Rows_are_shown_in_the_original_order()
    {
        var titles = new[] { "Artists", "Programs", "People", "Studios", "HeaderVideos", "Songs", "Movies", "Collections", "Episodes", "Shows" };

        var sorted = SearchQueries.Sort(titles.Select(title => new SearchSection(title, [], SearchQueries.RowCards)));

        Assert.Equal(
            new[] { "Movies", "Shows", "Episodes", "People", "Studios", "Artists", "Songs", "HeaderVideos", "Programs", "Collections" },
            sorted.Select(section => section.Title));
    }

    [Fact]
    public void Live_tv_rows_keep_their_order_before_the_known_ones()
    {
        var titles = new[] { "Movies", "Episodes", "Sports", "Kids", "News", "Programs", "Channels" };

        var sorted = SearchQueries.Sort(titles.Select(title => new SearchSection(title, [], SearchQueries.RowCards)));

        Assert.Equal(new[] { "Sports", "Kids", "News", "Movies", "Episodes", "Programs", "Channels" }, sorted.Select(section => section.Title));
    }

    [Theory]
    [InlineData("Movie", "Movies")]
    [InlineData("Series", "Shows")]
    [InlineData("Episode", "Episodes")]
    [InlineData("Playlist", "Playlists")]
    [InlineData("MusicAlbum", "Albums")]
    [InlineData("Audio", "Songs")]
    [InlineData("LiveTvProgram", "Programs")]
    [InlineData("TvChannel", "Channels")]
    [InlineData("PhotoAlbum", "HeaderPhotoAlbums")]
    [InlineData("Photo", "Photos")]
    [InlineData("AudioBook", "HeaderAudioBooks")]
    [InlineData("Book", "Books")]
    [InlineData("BoxSet", "Collections")]
    [InlineData("Trailer", "")]
    public void Every_item_type_has_its_row_title(string itemType, string title) =>
        Assert.Equal(title, SearchQueries.TitleOf(itemType));
}

/// <summary>The search requests against a server.</summary>
public class SearchServiceTests
{
    private readonly TestApp _app = new();

    [Fact]
    public async Task Suggestions_come_from_the_library_searched_in()
    {
        _app.SignIn();
        _app.Server.OnGet("/Items", new { Items = new[] { TestItems.Movie(1, "First"), TestItems.Movie(2, "Second") } });

        var items = await _app.Get<SearchService>().GetSuggestionsAsync("lib1");

        Assert.Equal(new[] { "First", "Second" }, items.Select(item => item.Name));
        var request = Assert.Single(_app.Server.Requests);
        Assert.Equal("lib1", request.Query("parentId"));
        Assert.Equal("IsFavoriteOrLiked,Random", request.Query("sortBy"));
        Assert.Equal(TestApp.UserId.ToString("N"), request.Query("userId"));
    }

    [Fact]
    public async Task A_search_runs_every_request_and_orders_the_rows()
    {
        _app.SignIn();
        _app.Server.OnGet("/Persons", request => new
        {
            Items = request.Query("personTypes") is null
                ? new[] { new { Id = TestItems.Id(11), Name = "Actor", Type = "Person" } }
                : new[] { new { Id = TestItems.Id(12), Name = "Singer", Type = "MusicArtist" } }
        });
        _app.Server.OnGet("/Items", request => new
        {
            Items = request.Query("limit") == "800"
                ? new object[] { new { Id = TestItems.Id(3), Name = "Song", Type = "Audio" }, TestItems.Movie(1, "Movie") }
                : []
        });

        var sections = await _app.Get<SearchService>().SearchAsync(null, null, "test");

        // The studios request fails (404): its row is left out, the others still show.
        Assert.Equal(new[] { "Movies", "People", "Artists", "Songs" }, sections.Select(section => section.Title));
        Assert.Equal("Actor", Assert.Single(sections[1].Items).Name);
        Assert.Equal(6, _app.Server.Requests.Count);
        Assert.All(_app.Server.Requests, request => Assert.Equal("test", request.Query("searchTerm")));
        Assert.Equal("false", _app.Server.RequestsTo("/Items").Single(request => request.Query("limit") == "800").Query("isMissing"));
    }

    [Fact]
    public async Task Missing_episodes_follow_the_user_configuration()
    {
        _app.SignIn(configure: user => user.Configuration!.DisplayMissingEpisodes = true);

        await _app.Get<SearchService>().SearchAsync("lib1", "tvshows", "test");

        var request = _app.Server.RequestsTo("/Items").Single(request => request.Query("limit") == "800");
        Assert.Null(request.Query("isMissing"));
        Assert.Equal("Series,Episode", request.Query("includeItemTypes"));
        Assert.Equal("lib1", request.Query("parentId"));
    }
}
