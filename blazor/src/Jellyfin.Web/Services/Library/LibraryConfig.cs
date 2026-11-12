namespace Jellyfin.Web.Services;

/// <summary>The views of the library pages (<c>types/libraryTab.ts</c>); the values are the original strings.</summary>
public static class LibraryTab
{
    public const string Albums = "albums";
    public const string AlbumArtists = "albumartists";
    public const string Artists = "artists";
    public const string Authors = "authors";
    public const string Persons = "persons";
    public const string Channels = "channels";
    public const string Collections = "collections";
    public const string Episodes = "episodes";
    public const string Favorites = "favorites";
    public const string Genres = "genres";
    public const string Guide = "guide";
    public const string Movies = "movies";
    public const string Playlists = "playlists";
    public const string Programs = "programs";
    public const string Recordings = "recordings";
    public const string Schedule = "schedule";
    public const string Series = "series";
    public const string SeriesTimers = "seriestimers";
    public const string Songs = "songs";
    public const string Studios = "studios";
    public const string Suggestions = "suggestions";
    public const string Upcoming = "upcoming";
    public const string PhotoAlbums = "photoalbums";
    public const string Photos = "photos";
    public const string Videos = "videos";
    public const string Books = "books";
    public const string MusicVideos = "musicvideos";
    public const string Folders = "folders";
    public const string Mixed = "mixed";

    /// <summary>Views without an item list of their own: the toolbar shows only the view menu (<c>MENU_ONLY_VIEWS</c>).</summary>
    public static readonly IReadOnlySet<string> MenuOnly = new HashSet<string>(StringComparer.Ordinal)
    {
        Genres, Guide, Suggestions, Programs, Recordings, Schedule, Upcoming
    };
}

/// <summary>The suggestion sections of <c>types/sections.ts</c>.</summary>
public static class LibrarySectionType
{
    public const string ContinueWatchingMixed = "continuewatchingmixed";
    public const string ContinueWatchingMovies = "continuewatchingmovies";
    public const string ContinueReading = "continuereading";
    public const string LatestBooks = "latestbooks";
    public const string LatestMixed = "latestmixed";
    public const string LatestMovies = "latestmovies";
    public const string ContinueWatchingEpisode = "continuewatchingepisode";
    public const string LatestEpisode = "latestepisode";
    public const string NextUp = "nextUp";
    public const string LatestMusic = "latestmusic";
    public const string RecentlyPlayedMusic = "recentlyplayedmusic";
    public const string FrequentlyPlayedMusic = "frequentlyplayedmusic";
    public const string LatestMusicVideos = "latestmusicvideos";
    public const string RecentlyPlayedMusicVideos = "recentlyplayedmusicvideos";
    public const string FrequentlyPlayedMusicVideos = "frequentlyplayedmusicvideos";
}

/// <summary>What a view asks the server for: the path and the query of one page of items.</summary>
public sealed record LibraryRequest(string Path, IReadOnlyList<(string Key, object? Value)> Query);

/// <summary>What a view's request is built from.</summary>
public sealed record LibraryQueryContext(
    LibraryTabContent Content,
    string? ParentId,
    LibraryViewSettings Settings,
    int PageSize,
    string? UserId);

/// <summary>
/// One view of a library page (<c>LibraryTabContent</c> of <c>types/libraryTabContent.ts</c>): what it lists
/// and which toolbar buttons it has. The defaults are the original's <c>defaultViewContent</c>.
/// </summary>
public sealed record LibraryTabContent
{
    /// <summary>A <see cref="LibraryTab"/>.</summary>
    public required string ViewType { get; init; }

    /// <summary>The item types listed (<c>includeItemTypes</c>).</summary>
    public IReadOnlyList<string>? ItemTypes { get; init; }

    /// <summary>The collection type handed to cards and links as their context; null for mixed libraries.</summary>
    public string? CollectionType { get; init; }

    /// <summary>The <see cref="LibrarySectionType"/>s of a suggestions view.</summary>
    public IReadOnlyList<string>? SuggestionSections { get; init; }

    public bool IsMovieRecommendations { get; init; }
    public bool IsPaginationEnabled { get; init; } = true;
    public bool IsBtnPlayAllEnabled { get; init; }
    public bool IsBtnQueueEnabled { get; init; }
    public bool IsBtnShuffleEnabled { get; init; }
    public bool IsBtnSortEnabled { get; init; } = true;
    public bool IsBtnFilterEnabled { get; init; } = true;
    public bool IsBtnNewCollectionEnabled { get; init; }
    public bool IsBtnNewPlaylistEnabled { get; init; }
    public bool IsBtnGridListEnabled { get; init; } = true;
    public bool IsAlphabetPickerEnabled { get; init; } = true;

    /// <summary>Translation key of the text shown when the view has no items; "MessageNoItemsAvailable" when null.</summary>
    public string? NoItemsMessage { get; init; }

    // --- extension points for views this workstream does not own (Live TV) ---

    /// <summary>
    /// A component that draws the whole view instead of the built-in ones (a guide, programme sections…).
    /// It receives the parameters <c>Content</c> (<see cref="LibraryTabContent"/>) and <c>ParentId</c> (string).
    /// </summary>
    public Type? Component { get; init; }

    /// <summary>Builds the item request of the view instead of <see cref="LibraryQueryBuilder.Build"/>.</summary>
    public Func<LibraryQueryContext, LibraryRequest>? Request { get; init; }

    /// <summary>Adjusts the card options of the view after the built-in rules were applied.</summary>
    public Func<CardOptions, LibraryViewSettings, CardOptions>? ConfigureCards { get; init; }

    /// <summary>True for views whose items come from a request (not sections, genres, upcoming or a custom component).</summary>
    public bool HasItemList => Component is null && !LibraryTab.MenuOnly.Contains(ViewType);
}

/// <summary>An entry of the view menu of a library page (<c>LibraryTabTitle</c> plus its content).</summary>
public sealed record LibraryViewDefinition(int Index, string Label, LibraryTabContent Content, bool IsDefault = false)
{
    public string View => Content.ViewType;
}

/// <summary>
/// A library page: its route and its views (<c>LibraryRoute</c> of <c>constants/libraryRoutes.ts</c> together with
/// the per-page values of <c>LibraryPage.tsx</c>). A page of another workstream (Live TV) defines its own instance
/// and renders <c>LibraryPage</c> with it; nothing has to be registered.
/// </summary>
public sealed record LibraryRouteConfig
{
    /// <summary>The route path, e.g. "/movies".</summary>
    public required string Path { get; init; }

    /// <summary>The element id of the page.</summary>
    public required string PageId { get; init; }

    /// <summary>The collection type of the libraries the page shows; null for mixed content.</summary>
    public string? CollectionType { get; init; }

    /// <summary>The item types whose backdrops the page rotates.</summary>
    public IReadOnlyList<string> BackdropTypes { get; init; } = [];

    public required IReadOnlyList<LibraryViewDefinition> Views { get; init; }

    /// <summary>The view at an index, or the first one for an unknown index.</summary>
    public LibraryViewDefinition ViewAt(int index) => Views.FirstOrDefault(view => view.Index == index) ?? Views[0];

    /// <summary>
    /// The view shown without <c>?tab=</c> (<c>getDefaultViewIndex</c>): the user's landing view of the library,
    /// else the default view.
    /// </summary>
    public int GetDefaultViewIndex(string? libraryId, UserSettings settings)
    {
        if (string.IsNullOrEmpty(libraryId))
        {
            return 0;
        }

        var landing = settings.Get("landing-" + libraryId, false);
        if (!string.IsNullOrEmpty(landing) && Views.FirstOrDefault(view => view.View == landing) is { } landingView)
        {
            return landingView.Index;
        }

        return Views.FirstOrDefault(view => view.IsDefault)?.Index ?? 0;
    }
}

/// <summary>
/// The library pages of this port (<c>LibraryRoutes</c> without Live TV, which has its own workstream) with the
/// view contents of <c>constants/views/*.ts</c>.
/// </summary>
public static class LibraryRoutes
{
    private const string NoCollections = "MessageNoCollectionsAvailable";

    private static readonly string[] BoxSet = ["BoxSet"];
    private static readonly string[] BookTypes = ["AudioBook", "Book"];

    public static readonly LibraryRouteConfig Books = new()
    {
        Path = "/books",
        PageId = "booksPage",
        CollectionType = "books",
        Views =
        [
            new(0, "Folders", new() { ViewType = LibraryTab.Folders, CollectionType = "books", ItemTypes = ["Folder", "AudioBook", "Book"] }, true),
            new(1, "Books", new() { ViewType = LibraryTab.Books, CollectionType = "books", ItemTypes = BookTypes }),
            new(2, "Authors", new() { ViewType = LibraryTab.Authors, CollectionType = "books", IsBtnSortEnabled = false }),
            new(3, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                CollectionType = "books",
                SuggestionSections = [LibrarySectionType.LatestBooks, LibrarySectionType.ContinueReading]
            }),
            new(4, "Genres", new() { ViewType = LibraryTab.Genres, CollectionType = "books", ItemTypes = BookTypes }),
            new(5, "Collections", Collections("books")),
            new(6, "Favorites", new() { ViewType = LibraryTab.Favorites, CollectionType = "books", ItemTypes = BookTypes })
        ]
    };

    public static readonly LibraryRouteConfig BoxSets = new()
    {
        Path = "/boxsets",
        PageId = "boxsetsPage",
        CollectionType = "boxsets",
        BackdropTypes = BoxSet,
        Views =
        [
            new(0, "Collections", new() { ViewType = LibraryTab.Collections, CollectionType = "boxsets", ItemTypes = BoxSet, IsBtnNewCollectionEnabled = true }, true),
            new(1, "Favorites", new() { ViewType = LibraryTab.Favorites, CollectionType = "boxsets", ItemTypes = BoxSet }),
            new(2, "Genres", new() { ViewType = LibraryTab.Genres, CollectionType = "boxsets", ItemTypes = BoxSet })
        ]
    };

    public static readonly LibraryRouteConfig Movies = new()
    {
        Path = "/movies",
        PageId = "moviesPage",
        CollectionType = "movies",
        BackdropTypes = ["Movie"],
        Views =
        [
            new(0, "Movies", new() { ViewType = LibraryTab.Movies, CollectionType = "movies", ItemTypes = ["Movie"], IsBtnPlayAllEnabled = true, IsBtnShuffleEnabled = true }, true),
            new(1, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                CollectionType = "movies",
                SuggestionSections = [LibrarySectionType.ContinueWatchingMovies, LibrarySectionType.LatestMovies],
                IsMovieRecommendations = true
            }),
            new(2, "Favorites", new() { ViewType = LibraryTab.Favorites, CollectionType = "movies", ItemTypes = ["Movie"] }),
            new(3, "Collections", Collections("movies")),
            new(4, "Genres", new() { ViewType = LibraryTab.Genres, CollectionType = "movies", ItemTypes = ["Movie"] }),
            new(5, "Studios", new() { ViewType = LibraryTab.Studios, CollectionType = "movies", ItemTypes = ["Movie"], IsBtnGridListEnabled = false, IsBtnSortEnabled = false }),
            new(6, "Playlists", Playlists("movies")),
            new(7, "People", Persons("movies"))
        ]
    };

    public static readonly LibraryRouteConfig Music = new()
    {
        Path = "/music",
        PageId = "musicPage",
        CollectionType = "music",
        BackdropTypes = ["MusicArtist"],
        Views =
        [
            new(0, "Albums", new() { ViewType = LibraryTab.Albums, CollectionType = "music", ItemTypes = ["MusicAlbum"], IsBtnPlayAllEnabled = true, IsBtnShuffleEnabled = true }, true),
            new(1, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                CollectionType = "music",
                SuggestionSections = [LibrarySectionType.LatestMusic, LibrarySectionType.FrequentlyPlayedMusic, LibrarySectionType.RecentlyPlayedMusic]
            }),
            new(2, "HeaderAlbumArtists", new() { ViewType = LibraryTab.AlbumArtists, CollectionType = "music", IsBtnSortEnabled = false }),
            new(3, "Artists", new() { ViewType = LibraryTab.Artists, CollectionType = "music", IsBtnSortEnabled = false }),
            new(4, "Playlists", Playlists("music")),
            new(5, "Songs", new()
            {
                ViewType = LibraryTab.Songs,
                CollectionType = "music",
                ItemTypes = ["Audio"],
                IsBtnShuffleEnabled = true,
                IsBtnGridListEnabled = false,
                IsAlphabetPickerEnabled = false
            }),
            new(6, "Genres", new() { ViewType = LibraryTab.Genres, CollectionType = "music", ItemTypes = ["MusicAlbum"] }),
            new(7, "Collections", Collections("music"))
        ]
    };

    public static readonly LibraryRouteConfig TvShows = new()
    {
        Path = "/tv",
        PageId = "tvshowsPage",
        CollectionType = "tvshows",
        BackdropTypes = ["Series"],
        Views =
        [
            new(0, "Shows", new() { ViewType = LibraryTab.Series, CollectionType = "tvshows", ItemTypes = ["Series"], IsBtnPlayAllEnabled = true, IsBtnShuffleEnabled = true }, true),
            new(1, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                CollectionType = "tvshows",
                SuggestionSections = [LibrarySectionType.ContinueWatchingEpisode, LibrarySectionType.LatestEpisode, LibrarySectionType.NextUp]
            }),
            new(2, "TabUpcoming", new() { ViewType = LibraryTab.Upcoming, CollectionType = "tvshows" }),
            new(3, "Genres", new() { ViewType = LibraryTab.Genres, CollectionType = "tvshows", ItemTypes = ["Series"] }),
            new(4, "TabNetworks", new() { ViewType = LibraryTab.Studios, CollectionType = "tvshows", ItemTypes = ["Series"], IsBtnGridListEnabled = false, IsBtnSortEnabled = false }),
            new(5, "Episodes", new()
            {
                ViewType = LibraryTab.Episodes,
                CollectionType = "tvshows",
                ItemTypes = ["Episode"],
                IsAlphabetPickerEnabled = false,
                IsBtnPlayAllEnabled = true,
                IsBtnShuffleEnabled = true,
                NoItemsMessage = "MessageNoEpisodesFound"
            }),
            new(6, "Collections", Collections("tvshows")),
            new(7, "Playlists", Playlists("tvshows")),
            new(8, "People", Persons("tvshows"))
        ]
    };

    public static readonly LibraryRouteConfig HomeVideos = new()
    {
        Path = "/homevideos",
        PageId = "homevideos",
        CollectionType = "homevideos",
        BackdropTypes = ["Video", "Photo"],
        Views =
        [
            new(0, "Folders", Playable(LibraryTab.Folders, "homevideos", "Folder", "Photo", "PhotoAlbum", "Video"), true),
            new(1, "Photos", Playable(LibraryTab.Photos, "homevideos", "Photo")),
            new(2, "HeaderPhotoAlbums", Playable(LibraryTab.PhotoAlbums, "homevideos", "PhotoAlbum")),
            new(3, "HeaderVideos", Playable(LibraryTab.Videos, "homevideos", "Video"))
        ]
    };

    public static readonly LibraryRouteConfig MusicVideos = new()
    {
        Path = "/musicvideos",
        PageId = "musicvideos",
        CollectionType = "musicvideos",
        BackdropTypes = ["MusicVideo"],
        Views =
        [
            new(0, "Folders", Playable(LibraryTab.Folders, "musicvideos", "Folder", "MusicVideo"), true),
            new(1, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                CollectionType = "musicvideos",
                SuggestionSections =
                [
                    LibrarySectionType.LatestMusicVideos, LibrarySectionType.FrequentlyPlayedMusicVideos, LibrarySectionType.RecentlyPlayedMusicVideos
                ]
            }),
            new(2, "HeaderVideos", Playable(LibraryTab.MusicVideos, "musicvideos", "MusicVideo")),
            new(3, "Playlists", Playlists("musicvideos"))
        ]
    };

    public static readonly LibraryRouteConfig PlaylistsPage = new()
    {
        Path = "/playlists",
        PageId = "playlistsPage",
        CollectionType = "playlists",
        Views =
        [
            new(0, "Playlists", new() { ViewType = LibraryTab.Playlists, IsBtnNewPlaylistEnabled = true }, true),
            new(1, "Favorites", new() { ViewType = LibraryTab.Favorites, CollectionType = "playlists", ItemTypes = ["Playlist"] })
        ]
    };

    public static readonly LibraryRouteConfig Mixed = new()
    {
        Path = "/mixed",
        PageId = "mixed",
        CollectionType = null,
        BackdropTypes = ["Movie", "Series"],
        Views =
        [
            new(0, "Folders", Playable(LibraryTab.Folders, null, "Folder", "Movie", "Series"), true),
            new(1, "Suggestions", new()
            {
                ViewType = LibraryTab.Suggestions,
                SuggestionSections = [LibrarySectionType.ContinueWatchingMixed, LibrarySectionType.LatestMixed, LibrarySectionType.NextUp]
            }),
            new(2, "HeaderMedia", Playable(LibraryTab.Mixed, null, "Movie", "Series")),
            new(3, "Collections", Collections(null)),
            new(4, "Playlists", Playlists(null))
        ]
    };

    /// <summary>Every library page of this workstream.</summary>
    public static readonly IReadOnlyList<LibraryRouteConfig> All =
        [Books, BoxSets, Movies, Music, TvShows, HomeVideos, MusicVideos, PlaylistsPage, Mixed];

    /// <summary>The library page at a route path, or null.</summary>
    public static LibraryRouteConfig? Find(string path) =>
        All.FirstOrDefault(route => string.Equals(route.Path, path, StringComparison.OrdinalIgnoreCase));

    private static LibraryTabContent Collections(string? collectionType) => new()
    {
        ViewType = LibraryTab.Collections,
        CollectionType = collectionType,
        ItemTypes = BoxSet,
        IsBtnNewCollectionEnabled = true,
        NoItemsMessage = NoCollections
    };

    private static LibraryTabContent Playlists(string? collectionType) => new()
    {
        ViewType = LibraryTab.Playlists,
        CollectionType = collectionType,
        ItemTypes = ["Playlist"],
        IsBtnFilterEnabled = false,
        IsBtnGridListEnabled = false,
        IsBtnNewPlaylistEnabled = true,
        IsAlphabetPickerEnabled = false
    };

    private static LibraryTabContent Persons(string collectionType) => new()
    {
        ViewType = LibraryTab.Persons,
        CollectionType = collectionType,
        IsBtnSortEnabled = false
    };

    private static LibraryTabContent Playable(string viewType, string? collectionType, params string[] itemTypes) => new()
    {
        ViewType = viewType,
        CollectionType = collectionType,
        ItemTypes = itemTypes,
        IsBtnPlayAllEnabled = true,
        IsBtnShuffleEnabled = true
    };
}
