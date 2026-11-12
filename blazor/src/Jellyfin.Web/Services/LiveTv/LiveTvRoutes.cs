namespace Jellyfin.Web.Services;

/// <summary>
/// One row of the Live TV section views (<c>Section</c> of <c>utils/sections.ts</c>,
/// <c>getProgramSections</c>): what it asks the server for, how its cards look and where its
/// title links to.
/// </summary>
/// <param name="Type">The original's <c>SectionType</c> value; also the <c>data-section</c> of the row.</param>
/// <param name="Name">Translation key of the title.</param>
/// <param name="Path">The request path (<c>SectionApiMethod</c>).</param>
/// <param name="Query">The parameters besides the ones every request of that path gets.</param>
/// <param name="Cards">The card options.</param>
/// <param name="List">The <c>/list</c> page the title links to.</param>
public sealed record LiveTvSection(
    string Type,
    string Name,
    string Path,
    IReadOnlyList<(string Key, object? Value)> Query,
    CardOptions Cards,
    ListRouteOptions List);

/// <summary>The sections of the Programs, Recordings and Schedule views (<c>constants/views/liveTvTabContent</c>).</summary>
public static class LiveTvSections
{
    public const string RecommendedPrograms = "LiveTv/Programs/Recommended";
    public const string Programs = "LiveTv/Programs";
    public const string Recordings = "LiveTv/Recordings";
    public const string RecordingFolders = "LiveTv/Recordings/Folders";

    /// <summary>The options every programme row shares (<c>cardOptions</c> of <c>getProgramSections</c>).</summary>
    private static readonly CardOptions ProgramCards = new()
    {
        InheritThumb = false,
        Shape = CardShape.AutoOverflow,
        DefaultShape = CardShape.OverflowBackdrop,
        CenterText = true,
        CoverImage = true,
        OverlayText = false,
        ShowAirTime = true
    };

    private static readonly CardOptions UpcomingCards = ProgramCards with
    {
        ShowParentTitle = true,
        ShowTitle = true,
        OverlayPlayButton = false,
        PreferThumb = CardToggle.Auto,
        ShowAirDateTime = true
    };

    private static readonly CardOptions RecordingCards = new()
    {
        Shape = CardShape.AutoOverflow,
        DefaultShape = CardShape.OverflowBackdrop,
        ShowTitle = true,
        CoverImage = true,
        CardLayout = false,
        CenterText = true,
        PreferThumb = CardToggle.Auto,
        OverlayText = false
    };

    public static readonly LiveTvSection ActivePrograms = new(
        "activeprograms",
        "HeaderOnNow",
        RecommendedPrograms,
        [("isAiring", true)],
        ProgramCards with
        {
            ShowParentTitle = true,
            ShowTitle = true,
            ShowAirDateTime = false,
            ShowAirEndTime = true,
            OverlayPlayButton = true,
            PreferThumb = CardToggle.Auto
        },
        new ListRouteOptions { ItemTypes = "Programs", IsAiring = true });

    public static readonly LiveTvSection UpcomingEpisodes = new(
        "upcomingepisodes",
        "Shows",
        Programs,
        [("hasAired", false), ("isMovie", false), ("isSports", false), ("isKids", false), ("isNews", false), ("isSeries", true)],
        UpcomingCards,
        new ListRouteOptions { ItemTypes = "Programs", IsSeries = true });

    public static readonly LiveTvSection UpcomingMovies = new(
        "upcomingmovies",
        "Movies",
        Programs,
        [("hasAired", false), ("isMovie", true)],
        UpcomingCards with { PreferThumb = CardToggle.Unset, ShowParentTitle = false },
        new ListRouteOptions { ItemTypes = "Programs", IsMovie = true });

    public static readonly LiveTvSection UpcomingSports = new(
        "upcomingsports",
        "Sports",
        Programs,
        [("hasAired", false), ("isSports", true)],
        UpcomingCards,
        new ListRouteOptions { ItemTypes = "Programs", IsSports = true });

    public static readonly LiveTvSection UpcomingKids = new(
        "upcomingkids",
        "HeaderForKids",
        Programs,
        [("hasAired", false), ("isKids", true)],
        UpcomingCards,
        new ListRouteOptions { ItemTypes = "Programs", IsKids = true });

    public static readonly LiveTvSection UpcomingNews = new(
        "upcomingnews",
        "News",
        Programs,
        [("hasAired", false), ("isNews", true)],
        UpcomingCards with { ShowParentTitleOrTitle = true, ShowTitle = false, ShowParentTitle = false },
        new ListRouteOptions { ItemTypes = "Programs", IsNews = true });

    public static readonly LiveTvSection LatestRecordings = new(
        "latestrecordings",
        "HeaderLatestRecordings",
        Recordings,
        [("limit", 12), ("imageTypeLimit", 1)],
        RecordingCards with { ShowYear = true, Lines = 2, ShowParentTitle = true },
        new ListRouteOptions { ItemTypes = "Recordings" });

    public static readonly LiveTvSection AllRecordings = new(
        "recordingfolders",
        "HeaderAllRecordings",
        RecordingFolders,
        [],
        RecordingCards with { ShowYear = false, ShowParentTitle = false },
        new ListRouteOptions { ItemTypes = "Recordings" });

    public static readonly LiveTvSection ActiveRecordings = new(
        "activerecordings",
        "HeaderActiveRecordings",
        Recordings,
        [("isInProgress", true)],
        RecordingCards with
        {
            DefaultShape = CardShape.Backdrop,
            ShowParentTitle = false,
            ShowParentTitleOrTitle = true,
            ShowAirTime = true,
            ShowAirEndTime = true,
            ShowChannelName = true,
            OverlayMoreButton = true
        },
        new ListRouteOptions { ItemTypes = "Recordings" });

    /// <summary>The cards of the upcoming recordings of the Schedule view (<c>ProgramsSectionView.tsx</c>).</summary>
    public static readonly CardOptions UpcomingRecordingCards = new()
    {
        Shape = CardShape.OverflowBackdrop,
        ShowTitle = true,
        ShowParentTitleOrTitle = true,
        ShowAirTime = true,
        ShowAirEndTime = true,
        ShowChannelName = false,
        CardLayout = true,
        CenterText = false,
        Action = ItemAction.Edit,
        CardFooterAside = "none",
        PreferThumb = true,
        CoverImage = true,
        OverlayText = false,
        ShowChannelLogo = true
    };

    /// <summary>The rows of a view, in the order they are shown; none for any other view.</summary>
    public static IReadOnlyList<LiveTvSection> For(string viewType) => viewType switch
    {
        LibraryTab.Programs => [ActivePrograms, UpcomingEpisodes, UpcomingMovies, UpcomingSports, UpcomingKids, UpcomingNews],
        LibraryTab.Recordings => [LatestRecordings, AllRecordings],
        LibraryTab.Schedule => [ActiveRecordings],
        _ => []
    };

    /// <summary>Whether the view lists the upcoming recordings below its rows (<c>isLiveTvUpcomingRecordings</c>).</summary>
    public static bool HasUpcomingRecordings(string viewType) => viewType == LibraryTab.Schedule;
}

/// <summary>
/// The Live TV page of the user (<c>LibraryRoute</c> "livetv" of <c>constants/libraryRoutes.ts</c> with the views
/// of <c>constants/views/liveTvTabContent</c>): Programs, Guide, Channels, Recordings, Schedule and Series.
/// </summary>
public static class LiveTvRoutes
{
    public const string Collection = "livetv";

    public static readonly LibraryRouteConfig LiveTv = new()
    {
        Path = "/livetv",
        PageId = "liveTvPage",
        CollectionType = Collection,
        Views =
        [
            new(0, "Programs", new()
            {
                ViewType = LibraryTab.Programs,
                CollectionType = Collection,
                Component = typeof(Components.LiveTvSectionsView)
            }, true),
            new(1, "Guide", new()
            {
                ViewType = LibraryTab.Guide,
                CollectionType = Collection,
                Component = typeof(Components.LiveTvGuideView)
            }),
            new(2, "Channels", new()
            {
                ViewType = LibraryTab.Channels,
                CollectionType = Collection,
                IsBtnGridListEnabled = false,
                IsBtnSortEnabled = false,
                IsAlphabetPickerEnabled = false,
                Request = ChannelsRequest
            }),
            new(3, "Recordings", new()
            {
                ViewType = LibraryTab.Recordings,
                CollectionType = Collection,
                Component = typeof(Components.LiveTvSectionsView)
            }),
            new(4, "Schedule", new()
            {
                ViewType = LibraryTab.Schedule,
                CollectionType = Collection,
                Component = typeof(Components.LiveTvSectionsView)
            }),
            new(5, "Series", new()
            {
                ViewType = LibraryTab.SeriesTimers,
                CollectionType = Collection,
                IsPaginationEnabled = false,
                IsBtnFilterEnabled = false,
                IsBtnGridListEnabled = false,
                IsBtnSortEnabled = false,
                IsAlphabetPickerEnabled = false,
                Component = typeof(Components.LiveTvSeriesTimersView)
            })
        ]
    };

    /// <summary>
    /// The channels of one page (<c>getLiveTvChannels</c> of <c>useItemsViewByType.ts</c>). The original sends no
    /// limit although its pager steps by the page size; the port sends the page size so the pages do not overlap.
    /// </summary>
    public static LibraryRequest ChannelsRequest(LibraryQueryContext context)
    {
        var favorite = context.Settings.Filters?.Status?.Contains("IsFavorite") == true;
        return new LibraryRequest(
            "LiveTv/Channels",
            [
                ("userId", context.UserId),
                ("fields", "PrimaryImageAspectRatio"),
                ("startIndex", context.Settings.StartIndex),
                ("limit", context.PageSize > 0 ? context.PageSize : (object?)null),
                ("isFavorite", favorite ? true : (object?)null),
                ("enableImageTypes", "Primary")
            ]);
    }
}
