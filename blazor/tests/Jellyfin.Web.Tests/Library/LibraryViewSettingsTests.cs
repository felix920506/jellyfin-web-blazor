using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Library;

/// <summary>The library routes with their tabs, and where a library keeps its view settings.</summary>
public class LibraryViewSettingsTests
{
    private readonly TestApp _app = new();

    private static string[] Views(LibraryRouteConfig route) => route.Views.Select(view => view.View).ToArray();

    [Fact]
    public void Every_library_route_is_configured()
    {
        Assert.Equal(
            new[] { "/books", "/boxsets", "/homevideos", "/mixed", "/movies", "/music", "/musicvideos", "/playlists", "/tv" },
            LibraryRoutes.All.Select(route => route.Path).Order());
        Assert.Same(LibraryRoutes.Movies, LibraryRoutes.Find("/movies"));
        Assert.Same(LibraryRoutes.TvShows, LibraryRoutes.Find("/TV"));
        Assert.Null(LibraryRoutes.Find("/livetv"));
        Assert.Equal("moviesPage", LibraryRoutes.Movies.PageId);
        Assert.Equal("tvshows", LibraryRoutes.TvShows.CollectionType);
    }

    [Fact]
    public void The_tabs_follow_the_original_order()
    {
        Assert.Equal(
            new[]
            {
                LibraryTab.Movies, LibraryTab.Suggestions, LibraryTab.Favorites, LibraryTab.Collections, LibraryTab.Genres,
                LibraryTab.Studios, LibraryTab.Playlists, LibraryTab.Persons
            },
            Views(LibraryRoutes.Movies));
        Assert.Equal(
            new[]
            {
                LibraryTab.Series, LibraryTab.Suggestions, LibraryTab.Upcoming, LibraryTab.Genres, LibraryTab.Studios,
                LibraryTab.Episodes, LibraryTab.Collections, LibraryTab.Playlists, LibraryTab.Persons
            },
            Views(LibraryRoutes.TvShows));
        Assert.Equal(
            new[]
            {
                LibraryTab.Albums, LibraryTab.Suggestions, LibraryTab.AlbumArtists, LibraryTab.Artists, LibraryTab.Playlists,
                LibraryTab.Songs, LibraryTab.Genres, LibraryTab.Collections
            },
            Views(LibraryRoutes.Music));
        Assert.Equal(
            new[]
            {
                LibraryTab.Folders, LibraryTab.Books, LibraryTab.Authors, LibraryTab.Suggestions, LibraryTab.Genres,
                LibraryTab.Collections, LibraryTab.Favorites
            },
            Views(LibraryRoutes.Books));
        Assert.Equal(new[] { LibraryTab.Collections, LibraryTab.Favorites, LibraryTab.Genres }, Views(LibraryRoutes.BoxSets));
        Assert.Equal(new[] { LibraryTab.Folders, LibraryTab.Photos, LibraryTab.PhotoAlbums, LibraryTab.Videos }, Views(LibraryRoutes.HomeVideos));
        Assert.Equal(new[] { LibraryTab.Playlists, LibraryTab.Favorites }, Views(LibraryRoutes.PlaylistsPage));
        Assert.Equal("Shows", LibraryRoutes.TvShows.ViewAt(0).Label);
        Assert.Equal("TabNetworks", LibraryRoutes.TvShows.ViewAt(4).Label);
        Assert.Equal("People", LibraryRoutes.Movies.ViewAt(7).Label);
    }

    [Fact]
    public void The_tab_contents_carry_the_toolbar_switches()
    {
        var movies = LibraryRoutes.Movies.ViewAt(0).Content;
        Assert.True(movies.IsBtnPlayAllEnabled);
        Assert.True(movies.IsBtnShuffleEnabled);
        Assert.False(movies.IsBtnQueueEnabled);
        Assert.Equal(new[] { "Movie" }, movies.ItemTypes);

        var songs = LibraryRoutes.Music.ViewAt(5).Content;
        Assert.False(songs.IsBtnPlayAllEnabled);
        Assert.True(songs.IsBtnShuffleEnabled);
        Assert.False(songs.IsBtnGridListEnabled);
        Assert.False(songs.IsAlphabetPickerEnabled);

        var collections = LibraryRoutes.Movies.ViewAt(3).Content;
        Assert.True(collections.IsBtnNewCollectionEnabled);
        Assert.Equal("MessageNoCollectionsAvailable", collections.NoItemsMessage);

        var playlists = LibraryRoutes.Music.ViewAt(4).Content;
        Assert.True(playlists.IsBtnNewPlaylistEnabled);
        Assert.False(playlists.IsBtnFilterEnabled);

        Assert.False(LibraryRoutes.Movies.ViewAt(5).Content.IsBtnSortEnabled);
        Assert.Equal("MessageNoEpisodesFound", LibraryRoutes.TvShows.ViewAt(5).Content.NoItemsMessage);
        Assert.True(LibraryRoutes.Movies.ViewAt(1).Content.IsMovieRecommendations);
        Assert.Equal(
            new[] { LibrarySectionType.ContinueWatchingEpisode, LibrarySectionType.LatestEpisode, LibrarySectionType.NextUp },
            LibraryRoutes.TvShows.ViewAt(1).Content.SuggestionSections);
    }

    [Fact]
    public void An_unknown_tab_index_shows_the_first_tab()
    {
        Assert.Equal(0, LibraryRoutes.Movies.ViewAt(42).Index);
        Assert.Equal(0, LibraryRoutes.Movies.ViewAt(-1).Index);
    }

    [Fact]
    public void The_landing_screen_setting_selects_the_first_tab()
    {
        var settings = _app.Get<UserSettings>();

        Assert.Equal(0, LibraryRoutes.Movies.GetDefaultViewIndex("lib", settings));

        settings.Set("landing-lib", LibraryTab.Genres, false);

        Assert.Equal(4, LibraryRoutes.Movies.GetDefaultViewIndex("lib", settings));
        Assert.Equal(3, LibraryRoutes.TvShows.GetDefaultViewIndex("lib", settings));
        Assert.Equal(0, LibraryRoutes.Movies.GetDefaultViewIndex("other", settings));
        Assert.Equal(0, LibraryRoutes.Movies.GetDefaultViewIndex(null, settings));

        settings.Set("landing-lib", "nonsense", false);

        Assert.Equal(0, LibraryRoutes.Movies.GetDefaultViewIndex("lib", settings));
    }

    [Fact]
    public void Defaults_depend_on_the_view()
    {
        var movies = LibraryViewSettings.Defaults(LibraryTab.Movies);
        Assert.Equal(new[] { "SortName" }, movies.SortBy);
        Assert.Equal("Ascending", movies.SortOrder);
        Assert.Equal("Primary", movies.ImageType);
        Assert.False(movies.IsList);
        Assert.True(movies.ShowTitle);
        Assert.True(movies.ShowYear);
        Assert.False(movies.CardLayout);
        Assert.False(movies.HasFilters);

        Assert.True(LibraryViewSettings.Defaults(LibraryTab.Songs).IsList);
        Assert.Equal("Thumb", LibraryViewSettings.Defaults(LibraryTab.Studios).ImageType);
        Assert.Equal(new[] { "SeriesSortName" }, LibraryViewSettings.Defaults(LibraryTab.Episodes).SortBy);
    }

    [Fact]
    public void Settings_are_stored_per_view_and_library_in_local_storage()
    {
        var store = _app.Get<LibraryViewSettingsStore>();
        var settings = LibraryViewSettings.Defaults(LibraryTab.Movies) with
        {
            SortBy = ["CommunityRating", "SortName"],
            SortOrder = "Descending",
            ImageType = "Banner",
            ViewMode = LibraryViewSettings.List,
            ShowTitle = false,
            CardLayout = true,
            StartIndex = 100,
            Alphabet = "C",
            Filters = new LibraryFilters { Status = ["IsFavorite"], Years = [2019] }
        };

        Assert.Empty(_app.Browser.LocalStorage);
        Assert.Equal(LibraryViewSettings.Defaults(LibraryTab.Movies).SortBy, store.Load(LibraryTab.Movies, "lib").SortBy);
        Assert.Empty(_app.Browser.LocalStorage);

        store.Save(LibraryTab.Movies, "lib", settings);

        Assert.Equal("movies - lib", Assert.Single(_app.Browser.LocalStorage.Keys));
        var json = _app.Browser.LocalStorage["movies - lib"];
        Assert.Contains("\"SortBy\":[\"CommunityRating\",\"SortName\"]", json);
        Assert.Contains("\"SortOrder\":\"Descending\"", json);
        Assert.Contains("\"ImageType\":\"Banner\"", json);
        Assert.Contains("\"ViewMode\":\"list\"", json);
        Assert.Contains("\"Filters\":{\"Status\":[\"IsFavorite\"],\"Years\":[2019]}", json);
        Assert.DoesNotContain("HasFilters", json);

        var loaded = store.Load(LibraryTab.Movies, "lib");
        Assert.Equal(settings.SortBy, loaded.SortBy);
        Assert.Equal("Descending", loaded.SortOrder);
        Assert.Equal("Banner", loaded.ImageType);
        Assert.True(loaded.IsList);
        Assert.False(loaded.ShowTitle);
        Assert.True(loaded.CardLayout);
        Assert.Equal(100, loaded.StartIndex);
        Assert.Equal("C", loaded.Alphabet);
        Assert.Equal(new[] { "IsFavorite" }, loaded.Filters!.Status);
        Assert.Equal(new[] { 2019 }, loaded.Filters.Years);
        Assert.True(loaded.HasFilters);

        // Another library and another view keep their own settings.
        Assert.False(store.Load(LibraryTab.Movies, "other").IsList);
        Assert.False(store.Load(LibraryTab.Favorites, "lib").IsList);
        Assert.Equal("movies - null", LibraryViewSettingsStore.Key(LibraryTab.Movies, null));
        Assert.Equal("channels - livetv", LibraryViewSettingsStore.Key(LibraryTab.Channels, "livetv"));
    }

    [Fact]
    public void Settings_written_by_the_original_client_are_read()
    {
        _app.Browser.LocalStorage["series - lib"] =
            """{"SortBy":["DateCreated","SortName"],"SortOrder":"Descending","StartIndex":0,"CardLayout":false,"ImageType":"Thumb","ViewMode":"grid","ShowTitle":true,"ShowYear":false,"Filters":{"SeriesStatus":["Ended"]}}""";

        var loaded = _app.Get<LibraryViewSettingsStore>().Load(LibraryTab.Series, "lib");

        Assert.Equal(new[] { "DateCreated", "SortName" }, loaded.SortBy);
        Assert.Equal("Thumb", loaded.ImageType);
        Assert.False(loaded.ShowYear);
        Assert.Equal(new[] { "Ended" }, loaded.Filters!.SeriesStatus);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SortBy\":[]}")]
    [InlineData("{\"SortBy\":7}")]
    public void Unreadable_settings_fall_back(string stored)
    {
        _app.Browser.LocalStorage["movies - lib"] = stored;

        var loaded = _app.Get<LibraryViewSettingsStore>().Load(LibraryTab.Movies, "lib");

        Assert.Equal(new[] { "SortName" }, loaded.SortBy);
    }

    [Fact]
    public void Filters_know_whether_anything_is_set()
    {
        var filters = new LibraryFilters().With("Genres", ["Action"]);

        Assert.True(filters.HasAny);
        Assert.Equal(new[] { "Action" }, filters.Get("Genres"));
        Assert.False(filters.With("Genres", []).HasAny);
        Assert.True(new LibraryFilters().With("Years", ["2019"]).HasAny);
        Assert.True((LibraryViewSettings.Defaults(LibraryTab.Movies) with { Alphabet = "A" }).HasFilters);
        Assert.False((LibraryViewSettings.Defaults(LibraryTab.Movies) with { Alphabet = "A" }).HasFilterOptions);
    }

    [Fact]
    public void The_card_options_follow_the_view_settings()
    {
        var movies = LibraryRoutes.Movies.ViewAt(0).Content;
        var settings = LibraryViewSettings.Defaults(LibraryTab.Movies);

        var cards = LibraryViewOptions.Cards(movies, settings);
        Assert.Equal(CardShape.Auto, cards.Shape);
        Assert.Equal("movies", cards.Context);
        Assert.True(cards.CenterText);
        Assert.True(cards.CoverImage);
        Assert.False(cards.OverlayText);
        Assert.True(cards.OverlayPlayButton);

        var banner = LibraryViewOptions.Cards(movies, settings with { ImageType = "Banner", ShowTitle = false, CardLayout = true });
        Assert.Equal(CardShape.Banner, banner.Shape);
        Assert.True(banner.OverlayText);
        Assert.True(banner.CardLayout);

        Assert.Equal(CardShape.Backdrop, LibraryViewOptions.Cards(movies, settings with { ImageType = "Thumb" }).Shape);
        Assert.Equal(CardShape.Square, LibraryViewOptions.Cards(movies, settings with { ImageType = "Disc" }).Shape);
        Assert.True(LibraryViewOptions.Cards(movies, settings with { ImageType = "Logo" }).PreferLogo);

        var series = LibraryViewOptions.Cards(LibraryRoutes.TvShows.ViewAt(0).Content, settings);
        Assert.True(series.OverlayMoreButton);

        var people = LibraryViewOptions.Cards(LibraryRoutes.Movies.ViewAt(7).Content, settings);
        Assert.Equal(1, people.Lines);

        var custom = movies with { ConfigureCards = (options, _) => options with { ShowAirTime = true } };
        Assert.True(LibraryViewOptions.Cards(custom, settings).ShowAirTime);
    }

    [Fact]
    public void The_list_options_follow_the_view()
    {
        var settings = LibraryViewSettings.Defaults(LibraryTab.Songs);

        var songs = LibraryViewOptions.List(LibraryRoutes.Music.ViewAt(5).Content, settings);
        Assert.Equal(ItemAction.PlayAllFromHere, songs.Action);
        Assert.True(songs.ShowParentTitle);
        Assert.True(songs.AddToListButton);

        var albums = LibraryViewOptions.List(LibraryRoutes.Music.ViewAt(0).Content, settings with { SortBy = ["ProductionYear", "SortName"] });
        Assert.Equal("ProductionYear", albums.SortBy);

        var episodes = LibraryViewOptions.List(LibraryRoutes.TvShows.ViewAt(5).Content, settings);
        Assert.True(episodes.ShowParentTitle);
    }
}
