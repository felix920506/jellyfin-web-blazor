using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>The kinds of home screen sections (<c>constants/homeSectionType.ts</c>).</summary>
public static class HomeSectionType
{
    public const string None = "none";
    public const string SmallLibraryTiles = "smalllibrarytiles";
    public const string LibraryButtons = "librarybuttons";
    public const string ActiveRecordings = "activerecordings";
    public const string Resume = "resume";
    public const string ResumeAudio = "resumeaudio";
    public const string LatestMedia = "latestmedia";
    public const string NextUp = "nextup";
    public const string LiveTv = "livetv";
    public const string ResumeBook = "resumebook";
}

/// <summary>One "favorites" section of the home screen's second tab (<c>favorites.js</c>).</summary>
public sealed record FavoriteSection(string NameKey, string Types, string Shape, CardOptions Options)
{
    /// <summary>Whether the section's cards show a title (sections without get extra bottom padding).</summary>
    public bool ShowsTitle => Options.ShowTitle.IsTruthy;
}

/// <summary>
/// Which sections the home screen shows and how their cards look: the logic of
/// <c>components/homesections</c> and <c>components/favoriteitems.js</c> without the markup.
/// </summary>
[AppService]
public sealed class HomeSections(UserSettings settings)
{
    /// <summary>The number of configurable sections (<c>homesection0</c> … <c>homesection9</c>).</summary>
    public const int MaxSections = 10;

    /// <summary>The sections shown when the user has not chosen any.</summary>
    public static readonly IReadOnlyList<string> DefaultSections =
    [
        HomeSectionType.SmallLibraryTiles,
        HomeSectionType.Resume,
        HomeSectionType.ResumeAudio,
        HomeSectionType.ResumeBook,
        HomeSectionType.LiveTv,
        HomeSectionType.NextUp,
        HomeSectionType.LatestMedia,
        HomeSectionType.None,
        HomeSectionType.None,
        HomeSectionType.None
    ];

    private static readonly string[] LatestExcludedViewTypes = ["playlists", "livetv", "boxsets", "channels", "folders"];

    /// <summary>The default section of a slot; empty outside the slots.</summary>
    public static string GetDefaultSection(int index) =>
        index >= 0 && index < DefaultSections.Count ? DefaultSections[index] : string.Empty;

    /// <summary>
    /// The section type of each of the ten slots, from the user's <c>homesection{n}</c> settings
    /// with the defaults filled in. The legacy value "folders" means the default of the first slot.
    /// </summary>
    public IReadOnlyList<string> GetSections()
    {
        var sections = new string[MaxSections];
        for (var i = 0; i < MaxSections; i++)
        {
            var section = settings.Get("homesection" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (string.IsNullOrEmpty(section))
            {
                section = GetDefaultSection(i);
            }

            if (section == "folders")
            {
                section = GetDefaultSection(0);
            }

            sections[i] = section;
        }

        return sections;
    }

    /// <summary>The libraries that get a "Latest …" section: not excluded by the user, and of a type that has one.</summary>
    public static IReadOnlyList<BaseItemDto> GetLatestViews(IEnumerable<BaseItemDto> userViews, UserDto? user)
    {
        var excluded = user?.Configuration?.LatestItemsExcludes ?? [];
        return userViews
            .Where(view => view.Id is not null
                           && !excluded.Contains(view.Id)
                           && !LatestExcludedViewTypes.Contains(view.CollectionTypeName()))
            .ToList();
    }

    /// <summary>How many items a library's "Latest" section asks for.</summary>
    public static int GetLatestLimit(BaseItemDto view) => view.CollectionTypeName() == "music" ? 30 : 16;

    /// <summary>The cards of the "My Media" tiles.</summary>
    public static CardOptions LibraryTilesOptions { get; } = new()
    {
        Shape = CardShape.GetBackdropShape(),
        ShowTitle = true,
        CenterText = true,
        OverlayText = false
    };

    /// <summary>The cards of "Continue Watching / Listening / Reading" (<paramref name="mediaType"/>: Video, Audio, Book).</summary>
    public CardOptions GetResumeOptions(string mediaType) => new()
    {
        PreferThumb = true,
        InheritThumb = !settings.UseEpisodeImagesInNextUpAndResume,
        Shape = mediaType == "Book" ? CardShape.GetPortraitShape() : CardShape.GetBackdropShape(),
        OverlayText = false,
        ShowTitle = true,
        ShowParentTitle = true,
        OverlayPlayButton = true,
        Context = "home",
        CenterText = true,
        CardLayout = false,
        ShowYear = true,
        Lines = 2
    };

    /// <summary>The <c>data-monitor</c> value of a resume section.</summary>
    public static string GetResumeMonitor(string mediaType) => mediaType switch
    {
        "Audio" => "audioplayback,markplayed",
        "Video" => "videoplayback,markplayed",
        _ => "markplayed"
    };

    /// <summary>The cards of "Next Up".</summary>
    public CardOptions GetNextUpOptions() => new()
    {
        PreferThumb = true,
        InheritThumb = !settings.UseEpisodeImagesInNextUpAndResume,
        Shape = CardShape.GetBackdropShape(),
        OverlayText = false,
        ShowTitle = true,
        ShowParentTitle = true,
        OverlayPlayButton = true,
        Context = "home",
        CenterText = true,
        CardLayout = false
    };

    /// <summary>The oldest "date played" a series may have to appear in Next Up; null without a limit.</summary>
    public DateTimeOffset? GetNextUpCutoff(DateTimeOffset now)
    {
        var days = settings.MaxDaysForNextUp;
        return days > 0 ? now.AddDays(-days) : null;
    }

    /// <summary>The cards of a library's "Latest …" section.</summary>
    public static CardOptions GetLatestOptions(BaseItemDto view)
    {
        var viewType = view.CollectionTypeName();
        var itemType = view.TypeName();

        string shape;
        if (itemType == "Channel" || viewType is "movies" or "books" or "tvshows")
        {
            shape = CardShape.GetPortraitShape();
        }
        else if (viewType is "music" or "homevideos")
        {
            shape = CardShape.GetSquareShape();
        }
        else
        {
            shape = CardShape.GetBackdropShape();
        }

        return new CardOptions
        {
            Shape = shape,
            PreferThumb = viewType is not ("movies" or "tvshows" or "music") && itemType != "Channel" ? CardToggle.Auto : CardToggle.Unset,
            PreferParentPoster = true,
            ShowChildCountIndicator = true,
            Context = "home",
            OverlayText = false,
            CenterText = true,
            OverlayPlayButton = viewType != "photos",
            CardLayout = false,
            ShowTitle = viewType != "photos",
            ShowYear = viewType is "movies" or "tvshows" or null,
            ShowParentTitle = viewType is "music" or "tvshows" or null,
            Lines = 2
        };
    }

    /// <summary>The cards of "On Now".</summary>
    public static CardOptions OnNowOptions { get; } = new()
    {
        PreferThumb = CardToggle.Auto,
        InheritThumb = false,
        Shape = CardShape.AutoOverflow,
        ShowParentTitleOrTitle = true,
        ShowTitle = true,
        CenterText = true,
        CoverImage = true,
        OverlayText = false,
        ShowAirTime = true,
        ShowChannelName = false,
        ShowAirDateTime = false,
        ShowAirEndTime = true,
        DefaultShape = CardShape.GetBackdropShape(),
        Lines = 3,
        OverlayPlayButton = true
    };

    /// <summary>The cards of "Active Recordings".</summary>
    public static CardOptions ActiveRecordingsOptions { get; } = new()
    {
        Shape = CardShape.AutoOverflow,
        DefaultShape = CardShape.GetBackdropShape(),
        ShowTitle = true,
        ShowParentTitle = true,
        CoverImage = true,
        CenterText = true,
        OverlayText = false,
        ShowYear = true,
        Lines = 2,
        OverlayPlayButton = false,
        PreferThumb = true,
        CardLayout = false,
        OverlayMoreButton = true,
        Action = ItemAction.None,
        CenterPlayButton = true
    };

    /// <summary>The sections of the favorites tab, in the original order.</summary>
    public static IReadOnlyList<FavoriteSection> FavoriteSections { get; } =
    [
        Favorite("Movies", "Movie", CardShape.GetPortraitShape(), new CardOptions { ShowTitle = true, ShowYear = true, OverlayPlayButton = true }),
        Favorite("Shows", "Series", CardShape.GetPortraitShape(), new CardOptions { ShowTitle = true, ShowYear = true, OverlayPlayButton = true }),
        Favorite("HeaderSeasons", "Season", CardShape.GetPortraitShape(), new CardOptions { ShowTitle = true, ShowParentTitle = true, OverlayPlayButton = true }),
        Favorite("Episodes", "Episode", CardShape.GetBackdropShape(), new CardOptions { PreferThumb = false, ShowTitle = true, ShowParentTitle = true, OverlayPlayButton = true }),
        Favorite("HeaderVideos", "Video", CardShape.GetBackdropShape(), new CardOptions { PreferThumb = true, ShowTitle = true, OverlayPlayButton = true }),
        Favorite("MusicVideos", "MusicVideo", CardShape.GetBackdropShape(), new CardOptions { PreferThumb = true, ShowTitle = true, OverlayPlayButton = true }),
        Favorite("Collections", "BoxSet", CardShape.GetPortraitShape(), new CardOptions { ShowTitle = true, OverlayPlayButton = true }),
        Favorite("Playlists", "Playlist", CardShape.GetSquareShape(), new CardOptions { PreferThumb = false, ShowTitle = true, OverlayPlayButton = true, CoverImage = true }),
        Favorite("Studios", "Studio", CardShape.GetBackdropShape(), new CardOptions { PreferThumb = true, ShowTitle = true, OverlayPlayButton = true }),
        Favorite("People", "Person", CardShape.GetPortraitShape(), new CardOptions { PreferThumb = false, ShowTitle = true, OverlayPlayButton = true, CoverImage = true }),
        Favorite("Artists", "MusicArtist", CardShape.GetSquareShape(), new CardOptions { PreferThumb = false, ShowTitle = true, OverlayPlayButton = true, CoverImage = true }),
        Favorite("Albums", "MusicAlbum", CardShape.GetSquareShape(), new CardOptions { PreferThumb = false, ShowTitle = true, ShowParentTitle = true, OverlayPlayButton = true, CoverImage = true }),
        Favorite("Songs", "Audio", CardShape.GetSquareShape(), new CardOptions { PreferThumb = false, ShowTitle = true, ShowParentTitle = true, OverlayMoreButton = true, Action = ItemAction.InstantMix, CoverImage = true }),
        Favorite("Books", "Book", CardShape.GetPortraitShape(), new CardOptions { ShowTitle = true, ShowYear = true, OverlayPlayButton = true }),
        Favorite("Channels", "LiveTVChannel", CardShape.GetBackdropShape(), new CardOptions { ShowTitle = true, OverlayPlayButton = true }),
        Favorite("HeaderPhotoAlbums", "PhotoAlbum", CardShape.GetBackdropShape(), new CardOptions { ShowTitle = true, OverlayPlayButton = true }),
        Favorite("Photos", "Photo", CardShape.GetBackdropShape(), new CardOptions { ShowTitle = true, OverlayPlayButton = true })
    ];

    /// <summary>The request of a favorites section, as <c>favoriteitems.js</c> sends it.</summary>
    public static Task<IReadOnlyList<BaseItemDto>> GetFavoritesAsync(ItemQueries queries, JellyfinApi api, FavoriteSection section)
    {
        ArgumentNullException.ThrowIfNull(queries);
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(section);

        (string Key, object? Value)[] options =
        [
            ("SortBy", "SeriesSortName,SortName"),
            ("SortOrder", "Ascending"),
            ("Filters", "IsFavorite"),
            ("Recursive", true),
            ("Fields", "PrimaryImageAspectRatio"),
            ("CollapseBoxSetItems", false),
            ("ExcludeLocationTypes", "Virtual"),
            ("EnableTotalRecordCount", false),
            ("Limit", 20)
        ];

        return section.Types switch
        {
            "Studio" => queries.GetItemsAsync(
                "Studios",
                ("userId", api.UserIdText),
                ("isFavorite", true),
                ("fields", "PrimaryImageAspectRatio"),
                ("enableImageTypes", "Thumb"),
                ("enableTotalRecordCount", false),
                ("limit", 20)),
            "MusicArtist" => queries.GetItemsAsync("Persons", [("userId", api.UserIdText), .. options, ("PersonTypes", "Artist")]),
            "Person" => queries.GetItemsAsync("Persons", [("userId", api.UserIdText), .. options, ("ExcludePersonTypes", "Artist,AlbumArtist")]),
            _ => queries.GetUserItemsAsync([.. options, ("IncludeItemTypes", section.Types)])
        };
    }

    private static FavoriteSection Favorite(string nameKey, string types, string shape, CardOptions options)
    {
        var lines = 0;
        if (options.ShowTitle.IsTruthy)
        {
            lines++;
        }

        if (options.ShowYear)
        {
            lines++;
        }

        if (options.ShowParentTitle)
        {
            lines++;
        }

        return new FavoriteSection(nameKey, types, shape, options with
        {
            Shape = shape,
            OverlayText = false,
            CenterText = true,
            CardLayout = false,
            Lines = lines
        });
    }
}
