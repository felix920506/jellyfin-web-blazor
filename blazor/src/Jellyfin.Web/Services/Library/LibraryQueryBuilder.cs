using System.Globalization;

namespace Jellyfin.Web.Services;

/// <summary>An entry of the sort menu: its translation key and the <c>sortBy</c> values it stands for.</summary>
public sealed record LibrarySortOption(string Label, IReadOnlyList<string> Value);

/// <summary>A checkbox of the filter menu: the <see cref="LibraryFilters"/> group it belongs to and its value.</summary>
public sealed record LibraryFilterOption(string Group, string Value, string Label, bool Translate = true);

/// <summary>An accordion of the filter menu.</summary>
public sealed record LibraryFilterSection(string Id, string Title, IReadOnlyList<LibraryFilterOption> Options);

/// <summary>The filter values the server offers for a library (<c>Items/Filters</c>, <c>Items/Filters2</c>, <c>Studios</c>).</summary>
public sealed record LibraryFilterValues
{
    public static readonly LibraryFilterValues Empty = new();

    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> OfficialRatings { get; init; } = [];
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<int> Years { get; init; } = [];
    public IReadOnlyList<(string Id, string Name)> Studios { get; init; } = [];
    public IReadOnlyList<(string Value, string Name)> AudioLanguages { get; init; } = [];
    public IReadOnlyList<(string Value, string Name)> SubtitleLanguages { get; init; } = [];
}

/// <summary>
/// The requests, sort options and filter groups of the library views: ports of <c>useItemsViewByType.ts</c>,
/// <c>utils/items.ts</c>, <c>SortButton.tsx</c> and the visibility rules of <c>filter/*.tsx</c>.
/// </summary>
public static class LibraryQueryBuilder
{
    /// <summary><c>VIDEO_PERSON_KINDS</c>.</summary>
    public static readonly IReadOnlyList<string> VideoPersonKinds = ["Actor", "Director", "Writer", "Producer", "GuestStar", "Unknown"];

    private static readonly string[] LegacyFilterViews =
    [
        LibraryTab.Movies, LibraryTab.Series, LibraryTab.Albums, LibraryTab.AlbumArtists, LibraryTab.Artists, LibraryTab.Songs,
        LibraryTab.Episodes, LibraryTab.Books, LibraryTab.Folders, LibraryTab.MusicVideos, LibraryTab.Videos, LibraryTab.Collections,
        LibraryTab.Playlists, LibraryTab.Mixed
    ];

    private static readonly string[] StudioFilterViews =
    [
        LibraryTab.Movies, LibraryTab.Series, LibraryTab.Books, LibraryTab.Albums, LibraryTab.AlbumArtists, LibraryTab.Artists, LibraryTab.Songs
    ];

    private static readonly string[] FavoriteOnlyViews =
    [
        LibraryTab.Albums, LibraryTab.Artists, LibraryTab.AlbumArtists, LibraryTab.Songs, LibraryTab.Channels, LibraryTab.PhotoAlbums,
        LibraryTab.Authors, LibraryTab.Persons, LibraryTab.Photos, LibraryTab.Studios
    ];

    private static readonly string[] FeatureViews = [LibraryTab.Movies, LibraryTab.Series, LibraryTab.Episodes];
    private static readonly string[] LanguageViews = [LibraryTab.Movies, LibraryTab.Series, LibraryTab.Episodes, LibraryTab.Mixed];

    private static readonly LibrarySortOption Name = new("Name", ["SortName"]);
    private static readonly LibrarySortOption Random = new("OptionRandom", ["Random"]);
    private static readonly LibrarySortOption CommunityRating = new("OptionCommunityRating", ["CommunityRating", "SortName"]);
    private static readonly LibrarySortOption CriticRating = new("OptionCriticRating", ["CriticRating", "SortName"]);
    private static readonly LibrarySortOption DateAdded = new("OptionDateAdded", ["DateCreated", "SortName"]);
    private static readonly LibrarySortOption DatePlayed = new("OptionDatePlayed", ["DatePlayed", "SortName"]);
    private static readonly LibrarySortOption ParentalRating = new("OptionParentalRating", ["OfficialRating", "SortName"]);
    private static readonly LibrarySortOption PlayCount = new("OptionPlayCount", ["PlayCount", "SortName"]);
    private static readonly LibrarySortOption ReleaseDate = new("OptionReleaseDate", ["ProductionYear", "PremiereDate", "SortName"]);
    private static readonly LibrarySortOption Runtime = new("Runtime", ["Runtime", "SortName"]);
    private static readonly LibrarySortOption DateEpisodeAdded = new("OptionDateEpisodeAdded", ["DateLastContentAdded", "SortName"]);

    /// <summary>True for the views whose items a request of <see cref="Build"/> lists.</summary>
    public static bool IsPersonsView(string viewType) =>
        viewType is LibraryTab.AlbumArtists or LibraryTab.Artists or LibraryTab.Authors or LibraryTab.Persons;

    /// <summary>The request of one page of a view (<c>fetchGetItemsViewByType</c>).</summary>
    public static LibraryRequest Build(LibraryQueryContext context)
    {
        if (context.Content.Request is { } custom)
        {
            return custom(context);
        }

        var settings = context.Settings;
        var viewType = context.Content.ViewType;
        var filters = settings.Filters;
        var query = new List<(string Key, object? Value)>
        {
            ("userId", context.UserId),
            ("parentId", context.ParentId)
        };

        void AddPaging()
        {
            query.Add(("limit", context.PageSize > 0 ? context.PageSize : (object?)null));
            AddAlphabet(query, settings.Alphabet);
            query.Add(("startIndex", settings.StartIndex));
        }

        switch (viewType)
        {
            case LibraryTab.AlbumArtists:
            case LibraryTab.Artists:
                query.Add(("enableImageTypes", new[] { settings.ImageType, "Backdrop" }));
                query.Add(("fields", Fields(viewType, settings)));
                query.AddRange(FiltersQuery(viewType, filters));
                AddPaging();
                query.Add(("personTypes", viewType == LibraryTab.AlbumArtists ? "AlbumArtist" : "Artist"));
                return new LibraryRequest("Persons", query);

            case LibraryTab.Authors:
            case LibraryTab.Persons:
                query.Add(("enableImageTypes", new[] { settings.ImageType, "Backdrop" }));
                query.Add(("fields", "PrimaryImageAspectRatio"));
                query.Add(("filters", filters?.Status));
                AddPaging();
                IReadOnlyList<string> personTypes = viewType == LibraryTab.Authors
                    ? ["Author"]
                    : filters?.PersonKinds is { Count: > 0 } kinds ? kinds : VideoPersonKinds;
                query.Add(("personTypes", personTypes));
                return new LibraryRequest("Persons", query);

            case LibraryTab.Studios:
                query.Add(("fields", Fields(viewType, settings)));
                AddPaging();
                query.Add(("includeItemTypes", context.Content.ItemTypes));
                query.Add(("isFavorite", filters?.Status?.Contains("IsFavorite") == true ? true : (object?)null));
                query.Add(("enableImageTypes", "Thumb"));
                return new LibraryRequest("Studios", query);

            default:
                query.Add(("recursive", viewType != LibraryTab.Folders));
                query.Add(("imageTypeLimit", 1));
                query.Add(("enableImageTypes", new[] { settings.ImageType, "Backdrop" }));
                query.Add(("fields", Fields(viewType, settings)));
                query.AddRange(FiltersQuery(viewType, filters));
                AddPaging();
                if (viewType == LibraryTab.Favorites)
                {
                    query.Add(("isFavorite", true));
                }

                query.Add(("sortBy", settings.SortBy));
                query.Add(("sortOrder", settings.SortOrder));
                query.Add(("includeItemTypes", context.Content.ItemTypes));
                return new LibraryRequest("Items", query);
        }
    }

    /// <summary><c>getFieldsQuery</c>.</summary>
    public static IReadOnlyList<string> Fields(string viewType, LibraryViewSettings settings)
    {
        var fields = new List<string>();
        if (viewType != LibraryTab.Studios)
        {
            fields.Add("MediaSourceCount");
        }

        if (settings.ImageType == "Primary")
        {
            fields.Add("PrimaryImageAspectRatio");
        }

        if (viewType == LibraryTab.Studios)
        {
            fields.Add("DateCreated");
            fields.Add("PrimaryImageAspectRatio");
        }

        return fields;
    }

    /// <summary><c>getAlphaPickerQuery</c>: "#" lists the names before "A".</summary>
    public static void AddAlphabet(List<(string Key, object? Value)> query, string? alphabet)
    {
        if (string.IsNullOrEmpty(alphabet))
        {
            return;
        }

        query.Add(alphabet == "#" ? ("nameLessThan", "A") : ("nameStartsWith", alphabet));
    }

    /// <summary>
    /// <c>getFiltersQuery</c>. Lists the server splits on "|" (genres, ratings, tags, studios) are joined that
    /// way so that values containing a comma survive.
    /// </summary>
    public static IReadOnlyList<(string Key, object? Value)> FiltersQuery(string viewType, LibraryFilters? filters)
    {
        var query = new List<(string Key, object? Value)>();
        if (filters is null)
        {
            if (viewType == LibraryTab.Episodes)
            {
                query.Add(("isMissing", false));
            }

            return query;
        }

        bool Has(IReadOnlyList<string>? list, string value) => list?.Contains(value) == true;
        void Flag(string key, bool on)
        {
            if (on)
            {
                query.Add((key, true));
            }
        }

        Flag("hasSubtitles", Has(filters.Features, "HasSubtitles"));
        Flag("hasTrailer", Has(filters.Features, "HasTrailer"));
        Flag("hasSpecialFeature", Has(filters.Features, "HasSpecialFeature"));
        Flag("hasThemeSong", Has(filters.Features, "HasThemeSong"));
        Flag("hasThemeVideo", Has(filters.Features, "HasThemeVideo"));

        if (Has(filters.EpisodeFilter, "ParentIndexNumber"))
        {
            query.Add(("parentIndexNumber", 0));
        }

        if (viewType == LibraryTab.Episodes)
        {
            if (Has(filters.EpisodeFilter, "IsMissing"))
            {
                query.Add(("isMissing", true));
                if (!Has(filters.EpisodeFilter, "IsUnaired"))
                {
                    query.Add(("isUnaired", false));
                }
            }
            else if (Has(filters.EpisodeFilter, "IsUnaired"))
            {
                query.Add(("isUnaired", true));
            }
            else
            {
                query.Add(("isMissing", false));
            }
        }

        if (Has(filters.VideoBasicFilter, "IsSD"))
        {
            query.Add(("isHd", false));
        }
        else if (Has(filters.VideoBasicFilter, "IsHD"))
        {
            query.Add(("isHd", true));
        }

        Flag("is4K", Has(filters.VideoBasicFilter, "Is4K"));
        Flag("is3D", Has(filters.VideoBasicFilter, "Is3D"));

        query.Add(("seriesStatus", filters.SeriesStatus));
        query.Add(("videoTypes", filters.VideoTypes));
        query.Add(("filters", filters.Status));
        query.Add(("genres", Pipe(filters.Genres)));
        query.Add(("officialRatings", Pipe(filters.OfficialRatings)));
        query.Add(("tags", Pipe(filters.Tags)));
        query.Add(("years", filters.Years));
        query.Add(("studioIds", Pipe(filters.StudioIds)));
        query.Add(("audioLanguages", filters.AudioLanguages));
        query.Add(("subtitleLanguages", filters.SubtitleLanguages));
        return query.Where(entry => entry.Value is not null).ToList();
    }

    /// <summary>
    /// The query options the play, shuffle and queue buttons hand to the player when a filter narrows the
    /// list: <c>{ ParentId, ...getFiltersQuery, SortBy, SortOrder }</c>.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PlayQueryOptions(
        string viewType, string? parentId, LibraryViewSettings settings, bool shuffle)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(parentId))
        {
            options["ParentId"] = parentId;
        }

        foreach (var (key, value) in FiltersQuery(viewType, settings.Filters))
        {
            if (Text(value) is { Length: > 0 } text)
            {
                options[char.ToUpperInvariant(key[0]) + key[1..]] = text;
            }
        }

        if (shuffle)
        {
            options["SortBy"] = "Random";
        }
        else
        {
            options["SortBy"] = string.Join(',', settings.SortBy);
            options["SortOrder"] = settings.SortOrder;
        }

        return options;
    }

    /// <summary>The query options of "play all" without filters: the sort order of the view.</summary>
    public static IReadOnlyDictionary<string, string> SortQueryOptions(LibraryViewSettings settings) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SortBy"] = string.Join(',', settings.SortBy),
            ["SortOrder"] = settings.SortOrder
        };

    /// <summary>The entries of the sort menu of a view (<c>getSortMenuOptions</c>); empty for views without sorting.</summary>
    public static IReadOnlyList<LibrarySortOption> SortOptions(string viewType) => viewType switch
    {
        LibraryTab.Movies or LibraryTab.Favorites =>
            [Name, Random, CommunityRating, CriticRating, DateAdded, DatePlayed, ParentalRating, PlayCount, ReleaseDate, Runtime],
        LibraryTab.Collections => [Name, Random, CommunityRating, DateAdded, ParentalRating, ReleaseDate],
        LibraryTab.Series =>
        [
            Name, Random, CommunityRating, new("OptionDateShowAdded", ["DateCreated", "SortName"]), DateEpisodeAdded,
            new("OptionDatePlayed", ["SeriesDatePlayed", "SortName"]), ParentalRating, ReleaseDate
        ],
        LibraryTab.Episodes =>
        [
            new("Name", ["SeriesSortName"]), Random, CommunityRating, DateAdded, ReleaseDate, DatePlayed, ParentalRating, PlayCount, Runtime
        ],
        LibraryTab.Albums =>
        [
            Name, Random, new("AlbumArtist", ["AlbumArtist", "SortName"]), CommunityRating, CriticRating, ReleaseDate, DateAdded
        ],
        LibraryTab.Books => [Name, Random, ReleaseDate, DateAdded, DatePlayed, new("IndexNumber", ["IndexNumber", "SortName"])],
        LibraryTab.Songs =>
        [
            new("OptionTrackName", ["Name"]), Random, new("Album", ["Album", "AlbumArtist", "SortName"]),
            new("AlbumArtist", ["AlbumArtist", "Album", "SortName"]), new("Artist", ["Artist", "Album", "SortName"]), DateAdded, DatePlayed,
            PlayCount, new("OptionReleaseDate", ["ProductionYear", "PremiereDate", "AlbumArtist", "Album", "SortName"]),
            new("Runtime", ["Runtime", "AlbumArtist", "Album", "SortName"])
        ],
        LibraryTab.Playlists =>
        [
            Name, Random, DateAdded, new("OptionDatePlaylistUpdated", ["DateLastContentAdded", "SortName"]), ReleaseDate, Runtime
        ],
        LibraryTab.PhotoAlbums or LibraryTab.Photos => [Name, Random, DateAdded],
        LibraryTab.Videos or LibraryTab.MusicVideos =>
            [Name, Random, CommunityRating, DateAdded, DatePlayed, ParentalRating, PlayCount, ReleaseDate, Runtime],
        LibraryTab.Folders =>
        [
            Name, Random, CommunityRating, DateAdded, DatePlayed, new("Folders", ["IsFolder", "SortName"]), ParentalRating, PlayCount,
            ReleaseDate, Runtime
        ],
        LibraryTab.Mixed =>
        [
            Name, Random, CommunityRating, CriticRating, DateAdded, DateEpisodeAdded, DatePlayed, ParentalRating, PlayCount, ReleaseDate,
            Runtime
        ],
        _ => []
    };

    /// <summary>The image types offered by the view settings menu; empty where the original hides them.</summary>
    public static IReadOnlyList<string> ImageTypes(string viewType) =>
        viewType is LibraryTab.Episodes or LibraryTab.Artists or LibraryTab.AlbumArtists or LibraryTab.Albums
            ? []
            : ["Primary", "Banner", "Disc", "Logo", "Thumb"];

    /// <summary>Whether the filter menu asks the server for filter values at all (<c>parentId</c> is a library).</summary>
    public static bool UsesServerFilters(string viewType) =>
        LegacyFilterViews.Contains(viewType) || StudioFilterViews.Contains(viewType) || LanguageViews.Contains(viewType);

    /// <summary>The accordions of the filter menu of a view, in the original's order (<c>FilterButton.tsx</c>).</summary>
    public static IReadOnlyList<LibraryFilterSection> FilterSections(string viewType, LibraryFilterValues values)
    {
        var sections = new List<LibraryFilterSection>();

        var status = new List<LibraryFilterOption>();
        if (!FavoriteOnlyViews.Contains(viewType))
        {
            status.Add(new("Status", "IsPlayed", "Played"));
            status.Add(new("Status", "IsUnplayed", "Unplayed"));
        }

        status.Add(new("Status", "IsFavorite", "Favorite"));
        if (!FavoriteOnlyViews.Contains(viewType))
        {
            status.Add(new("Status", "IsResumable", viewType == LibraryTab.Books ? "ContinueReading" : "ContinueWatching"));
        }

        sections.Add(new("filtersStatus", "Filters", status));

        if (viewType == LibraryTab.Series)
        {
            sections.Add(new("filtersSeriesStatus", "HeaderSeriesStatus",
            [
                new("SeriesStatus", "Continuing", "Continuing"),
                new("SeriesStatus", "Ended", "Ended"),
                new("SeriesStatus", "Unreleased", "Unreleased")
            ]));
        }

        if (viewType == LibraryTab.Episodes)
        {
            sections.Add(new("filtersEpisodesStatus", "HeaderEpisodesStatus",
            [
                new("EpisodeFilter", "ParentIndexNumber", "OptionSpecialEpisode"),
                new("EpisodeFilter", "IsMissing", "OptionMissingEpisode"),
                new("EpisodeFilter", "IsUnaired", "OptionUnairedEpisode")
            ]));
        }

        if (FeatureViews.Contains(viewType))
        {
            sections.Add(new("filtersFeatures", "Features",
            [
                new("Features", "HasSubtitles", "Subtitles"),
                new("Features", "HasTrailer", "Trailers"),
                new("Features", "HasSpecialFeature", "Extras"),
                new("Features", "HasThemeSong", "ThemeSongs"),
                new("Features", "HasThemeVideo", "ThemeVideos")
            ]));
            sections.Add(new("filtersVideoTypes", "HeaderVideoType",
            [
                new("VideoBasicFilter", "IsSD", "SD", false),
                new("VideoBasicFilter", "IsHD", "HD", false),
                new("VideoBasicFilter", "Is4K", "4K", false),
                new("VideoBasicFilter", "Is3D", "3D", false),
                new("VideoTypes", "Dvd", "DVD", false),
                new("VideoTypes", "BluRay", "Blu-ray", false),
                new("VideoTypes", "Iso", "ISO", false)
            ]));
        }

        if (LegacyFilterViews.Contains(viewType))
        {
            AddValues(sections, "filtersGenres", "Genres", "Genres", values.Genres.Select(genre => (genre, genre)));
            AddValues(sections, "filtersOfficialRatings", "HeaderParentalRatings", "OfficialRatings",
                values.OfficialRatings.Select(rating => (rating, rating)));
            AddValues(sections, "filtersTags", "Tags", "Tags", values.Tags.Select(tag => (tag, tag)));
            AddValues(sections, "filtersYears", "HeaderYears", "Years",
                values.Years.Select(year => year.ToString(CultureInfo.InvariantCulture)).Select(year => (year, year)));
        }

        if (StudioFilterViews.Contains(viewType))
        {
            AddValues(sections, "filtersStudios", "Studios", "StudioIds", values.Studios);
        }

        if (LanguageViews.Contains(viewType))
        {
            AddValues(sections, "filtersAudioLanguages", "AudioLanguages", "AudioLanguages", values.AudioLanguages);
            AddValues(sections, "filtersSubtitleLanguages", "SubtitleLanguages", "SubtitleLanguages", values.SubtitleLanguages);
        }

        if (viewType == LibraryTab.Persons)
        {
            sections.Add(new("filtersPersonKinds", "Credits",
                VideoPersonKinds.Select(kind => new LibraryFilterOption("PersonKinds", kind, kind)).ToList()));
        }

        return sections;
    }

    /// <summary>
    /// Toggles a checkbox of the filter menu and returns the new filters (null when nothing is left).
    /// Played and unplayed exclude each other; the person kinds are all selected by default and stored only
    /// while some are deselected.
    /// </summary>
    public static LibraryFilters? Toggle(LibraryFilters? filters, LibraryFilterOption option, bool selected)
    {
        var current = filters ?? new LibraryFilters();
        var values = IsDefaultPersonKinds(current, option)
            ? VideoPersonKinds.ToList()
            : current.Get(option.Group).ToList();
        values.Remove(option.Value);
        if (selected)
        {
            values.Add(option.Value);
            if (option.Group == "Status" && option.Value is "IsPlayed" or "IsUnplayed")
            {
                values.Remove(option.Value == "IsPlayed" ? "IsUnplayed" : "IsPlayed");
            }
        }

        if (option.Group == "PersonKinds" && values.Count == VideoPersonKinds.Count)
        {
            values.Clear();
        }

        var result = current.With(option.Group, values);
        return result.HasAny ? result : null;
    }

    /// <summary>Whether a checkbox of the filter menu is ticked.</summary>
    public static bool IsSelected(LibraryFilters? filters, LibraryFilterOption option) =>
        IsDefaultPersonKinds(filters, option) || filters?.Get(option.Group).Contains(option.Value) == true;

    private static bool IsDefaultPersonKinds(LibraryFilters? filters, LibraryFilterOption option) =>
        option.Group == "PersonKinds" && filters?.PersonKinds is not { Count: > 0 };

    private static void AddValues(
        List<LibraryFilterSection> sections, string id, string title, string group, IEnumerable<(string Value, string Name)> values)
    {
        var options = values.Select(value => new LibraryFilterOption(group, value.Value, value.Name, false)).ToList();
        if (options.Count > 0)
        {
            sections.Add(new(id, title, options));
        }
    }

    private static string? Pipe(IReadOnlyList<string>? values) => values is { Count: > 0 } ? string.Join('|', values) : null;

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IEnumerable<string> list => string.Join(',', list),
        IEnumerable<int> list => string.Join(',', list.Select(entry => entry.ToString(CultureInfo.InvariantCulture))),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString()
    };
}
