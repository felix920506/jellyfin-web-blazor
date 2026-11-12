using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Routing;

namespace Jellyfin.Web.Services;

/// <summary>The URL parameters of the legacy list page (<c>list.js</c>); the flags keep their text ("true"/"false").</summary>
public sealed record ListParameters
{
    public string? ServerId { get; init; }
    public string? Type { get; init; }
    public string? ParentId { get; init; }
    public string? GenreId { get; init; }
    public string? MusicGenreId { get; init; }
    public string? StudioId { get; init; }
    public string? PersonId { get; init; }
    public string? ArtistId { get; init; }
    public string? Tag { get; init; }
    public string? IsFavorite { get; init; }
    public string? IsMovie { get; init; }
    public string? IsSeries { get; init; }
    public string? IsNews { get; init; }
    public string? IsSports { get; init; }
    public string? IsKids { get; init; }
    public string? IsAiring { get; init; }

    public static ListParameters From(AppLocation location) => new()
    {
        ServerId = Text(location["serverId"]),
        Type = Text(location["type"]),
        ParentId = Text(location["parentId"]),
        GenreId = Text(location["genreId"]),
        MusicGenreId = Text(location["musicGenreId"]),
        StudioId = Text(location["studioId"]),
        PersonId = Text(location["personId"]),
        ArtistId = Text(location["artistId"]),
        Tag = Text(location["tag"]),
        IsFavorite = Text(location["IsFavorite"]),
        IsMovie = Text(location["IsMovie"]),
        IsSeries = Text(location["IsSeries"]),
        IsNews = Text(location["IsNews"]),
        IsSports = Text(location["IsSports"]),
        IsKids = Text(location["IsKids"]),
        IsAiring = Text(location["IsAiring"])
    };

    private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>The saved filters of a list page (<c>getFilters</c>).</summary>
public sealed record ListFilters
{
    public bool IsPlayed { get; init; }
    public bool IsUnplayed { get; init; }
    public bool IsFavorite { get; init; }
    public bool IsResumable { get; init; }
    public bool Is4K { get; init; }
    public bool IsHD { get; init; }
    public bool IsSD { get; init; }
    public bool Is3D { get; init; }
    public string? VideoTypes { get; init; }
    public string? SeriesStatus { get; init; }
    public string? GenreIds { get; init; }
    public bool HasSubtitles { get; init; }
    public bool HasTrailer { get; init; }
    public bool HasSpecialFeature { get; init; }
    public bool HasThemeSong { get; init; }
    public bool HasThemeVideo { get; init; }

    /// <summary>The names of the yes/no filters in the order they are stored.</summary>
    public static readonly IReadOnlyList<string> Flags =
    [
        "IsPlayed", "IsUnplayed", "IsFavorite", "IsResumable", "Is4K", "IsHD", "IsSD", "Is3D",
        "HasSubtitles", "HasTrailer", "HasSpecialFeature", "HasThemeSong", "HasThemeVideo"
    ];

    public bool Get(string flag) => flag switch
    {
        "IsPlayed" => IsPlayed,
        "IsUnplayed" => IsUnplayed,
        "IsFavorite" => IsFavorite,
        "IsResumable" => IsResumable,
        "Is4K" => Is4K,
        "IsHD" => IsHD,
        "IsSD" => IsSD,
        "Is3D" => Is3D,
        "HasSubtitles" => HasSubtitles,
        "HasTrailer" => HasTrailer,
        "HasSpecialFeature" => HasSpecialFeature,
        "HasThemeSong" => HasThemeSong,
        "HasThemeVideo" => HasThemeVideo,
        _ => false
    };

    public ListFilters With(string flag, bool value) => flag switch
    {
        "IsPlayed" => this with { IsPlayed = value },
        "IsUnplayed" => this with { IsUnplayed = value },
        "IsFavorite" => this with { IsFavorite = value },
        "IsResumable" => this with { IsResumable = value },
        "Is4K" => this with { Is4K = value },
        "IsHD" => this with { IsHD = value },
        "IsSD" => this with { IsSD = value },
        "Is3D" => this with { Is3D = value },
        "HasSubtitles" => this with { HasSubtitles = value },
        "HasTrailer" => this with { HasTrailer = value },
        "HasSpecialFeature" => this with { HasSpecialFeature = value },
        "HasThemeSong" => this with { HasThemeSong = value },
        "HasThemeVideo" => this with { HasThemeVideo = value },
        _ => this
    };

    /// <summary>The values of a comma list such as <see cref="VideoTypes"/>.</summary>
    public static IReadOnlyList<string> Split(string? list) =>
        string.IsNullOrEmpty(list) ? [] : list.Split(',', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// True when a filter narrows the list (<c>hasFilters</c> of <c>modifyQueryWithFilters</c>; the series
    /// status is stored by the filter menu but never applied by the list page).
    /// </summary>
    public bool HasAny =>
        IsPlayed || IsUnplayed || IsFavorite || IsResumable || !string.IsNullOrEmpty(VideoTypes) || !string.IsNullOrEmpty(GenreIds)
        || Is4K || IsHD || IsSD || Is3D || HasSubtitles || HasTrailer || HasSpecialFeature || HasThemeSong || HasThemeVideo;
}

/// <summary>The sort values of a list page (<c>getSortValuesLegacy</c>).</summary>
public sealed record ListSort(string SortBy, string SortOrder)
{
    public bool Descending => SortOrder == "Descending";
}

/// <summary>An entry of the sort dialog: the translation key of its name and the sort value.</summary>
public sealed record ListSortOption(string Name, string Value);

/// <summary>The view settings of a list page (<c>getViewSettings</c>).</summary>
public sealed record ListViewSettings(bool ShowTitle, bool ShowYear, string ImageType, string ViewType)
{
    public bool IsList => ImageType == "list";
}

/// <summary>
/// The logic of the legacy list page (<c>controllers/list.js</c>) without its markup: which item the page is
/// about, the settings key, the server request of every parameter combination, the sort, filter and view
/// options, the title, the buttons and the card options.
/// </summary>
public static class ListPageModel
{
    /// <summary>The number of items "play all", "shuffle" and "queue" fetch when they cannot hand over the folder.</summary>
    public const int PlayLimit = 300;

    private static readonly string[] NoItemTypes = ["Recordings", "Programs", "nextup", "tag"];
    private static readonly string[] TitledTypes = ["Audio", "MusicAlbum", "MusicArtist", "Person", "Programs", "Recordings", "nextup", "tag"];

    /// <summary>The id of the item the page shows the children of (<c>getItem</c>), or null.</summary>
    public static string? ItemId(ListParameters parameters) =>
        NoItemTypes.Contains(parameters.Type)
            ? null
            : parameters.GenreId ?? parameters.MusicGenreId ?? parameters.StudioId ?? parameters.PersonId ?? parameters.ParentId;

    /// <summary>The key the page's sort, filter and view settings are stored under (<c>getSettingsKey</c>).</summary>
    public static string SettingsKey(ListParameters parameters)
    {
        var values = new List<string> { "items" };
        if (parameters.Type is not null)
        {
            values.Add(parameters.Type);
        }
        else if (parameters.ParentId is not null)
        {
            values.Add(parameters.ParentId);
        }

        void Flag(string? value, string name)
        {
            if (value is not null)
            {
                values.Add(name);
            }
        }

        Flag(parameters.IsAiring, "IsAiring");
        Flag(parameters.IsMovie, "IsMovie");
        Flag(parameters.IsKids, "IsKids");
        Flag(parameters.IsSports, "IsSports");
        Flag(parameters.IsNews, "IsNews");
        Flag(parameters.IsSeries, "IsSeries");
        Flag(parameters.IsFavorite, "IsFavorite");
        Flag(parameters.GenreId, "Genre");
        Flag(parameters.MusicGenreId, "MusicGenre");
        Flag(parameters.StudioId, "Studio");
        Flag(parameters.PersonId, "Person");
        Flag(parameters.ParentId, "Folder");
        return string.Join('-', values);
    }

    /// <summary>The value of the "Name" sort option (<c>getNameSortOption</c>).</summary>
    public static string NameSortValue(ListParameters parameters) =>
        parameters.Type == "Episode" ? "SeriesSortName,SortName" : "SortName";

    /// <summary>The sort order nothing was saved for (<c>getDefaultSortBy</c>).</summary>
    public static string DefaultSortBy(ListParameters parameters) =>
        parameters.Type is not null ? NameSortValue(parameters) : "IsFolder," + NameSortValue(parameters);

    /// <summary>The entries of the sort dialog (<c>getSortMenuOptions</c>).</summary>
    public static IReadOnlyList<ListSortOption> SortOptions(ListParameters parameters)
    {
        var programs = parameters.Type == "Programs";
        var options = new List<ListSortOption>();
        if (programs)
        {
            options.Add(new ListSortOption("AirDate", "StartDate,SortName"));
        }

        options.Add(new ListSortOption("Name", NameSortValue(parameters)));
        options.Add(new ListSortOption("CommunityRating", "CommunityRating,SortName"));
        if (!programs)
        {
            options.Add(new ListSortOption("CriticRating", "CriticRating,SortName"));
            options.Add(new ListSortOption("DateAdded", "DateCreated,SortName"));
            options.Add(new ListSortOption("DatePlayed", "DatePlayed,SortName"));
        }

        if (parameters.Type is null)
        {
            options.Add(new ListSortOption("Folders", "IsFolder," + NameSortValue(parameters)));
        }

        options.Add(new ListSortOption("ParentalRating", "OfficialRating,SortName"));
        if (!programs)
        {
            options.Add(new ListSortOption("PlayCount", "PlayCount,SortName"));
        }

        options.Add(new ListSortOption("ReleaseDate", "ProductionYear,PremiereDate,SortName"));
        options.Add(new ListSortOption("Runtime", "Runtime,SortName"));
        return options;
    }

    /// <summary>The filters the filter dialog offers (<c>getVisibleFilters</c>).</summary>
    public static IReadOnlyList<string> VisibleFilters(ListParameters parameters)
    {
        if (parameters.Type == "nextup")
        {
            return [];
        }

        if (parameters.Type == "Programs")
        {
            return ["Genres"];
        }

        var filters = new List<string> { "IsUnplayed", "IsPlayed" };
        if (parameters.IsFavorite is null)
        {
            filters.Add("IsFavorite");
        }

        filters.AddRange(["IsResumable", "VideoType", "HasSubtitles", "HasTrailer", "HasSpecialFeature", "HasThemeSong", "HasThemeVideo"]);
        return filters;
    }

    /// <summary>The item types the filter dialog asks the server's filter values for (<c>getItemTypes</c>).</summary>
    public static IReadOnlyList<string> FilterItemTypes(ListParameters parameters) => parameters.Type switch
    {
        "nextup" => ["Episode"],
        "Programs" => ["Program"],
        _ => []
    };

    /// <summary>The view settings the settings dialog offers (<c>getVisibleViewSettings</c>).</summary>
    public static IReadOnlyList<string> VisibleViewSettings(BaseItemDto? item)
    {
        var type = item?.TypeName();
        return type is "PhotoAlbum" or "ChannelFolderItem" ? ["showTitle", "viewType"] : ["showTitle", "imageType", "viewType"];
    }

    /// <summary>The page title (<c>getTitle</c>): a translation key, or literal text.</summary>
    public static (string? Key, string? Text) Title(ListParameters parameters, BaseItemDto? item)
    {
        static (string?, string?) Key(string key) => (key, null);
        switch (parameters.Type)
        {
            case "Recordings":
                return Key("Recordings");
            case "Programs":
                return Key(
                    parameters.IsMovie == "true" ? "Movies"
                    : parameters.IsSports == "true" ? "Sports"
                    : parameters.IsKids == "true" ? "HeaderForKids"
                    : parameters.IsAiring == "true" ? "HeaderOnNow"
                    : parameters.IsSeries == "true" ? "Shows"
                    : parameters.IsNews == "true" ? "News"
                    : "Programs");
            case "nextup":
                return Key("NextUp");
            case "favoritemovies":
                return Key("FavoriteMovies");
        }

        if (item is not null)
        {
            return (null, item.Name);
        }

        return parameters.Type switch
        {
            "Movie" => Key("Movies"),
            "Series" => Key("Shows"),
            "Season" => Key("Seasons"),
            "Episode" => Key("Episodes"),
            "MusicArtist" => Key("Artists"),
            "MusicAlbum" => Key("Albums"),
            "Audio" => Key("Songs"),
            "Video" => Key("Videos"),
            _ => (null, parameters.Tag)
        };
    }

    /// <summary>Whether titles show when the user never chose (<c>getViewSettings</c>).</summary>
    public static bool DefaultShowTitle(ListParameters parameters, BaseItemDto? item) =>
        TitledTypes.Contains(parameters.Type) || (item is not null && item.TypeName() != "PhotoAlbum");

    /// <summary>Whether the "play all" buttons show.</summary>
    public static bool ShowPlay(ListParameters parameters, BaseItemDto? item)
    {
        var itemType = item?.TypeName();
        var collectionType = item?.CollectionTypeName();
        return (itemType == "MusicGenre" || (parameters.Type != "Programs" && itemType != "Channel"))
            && itemType != "UserView"
            && !(itemType == "CollectionFolder" && collectionType != "homevideos" && collectionType != "musicvideos");
    }

    /// <summary>Whether the "shuffle" buttons show.</summary>
    public static bool ShowShuffle(ListParameters parameters, BaseItemDto? item)
    {
        var itemType = item?.TypeName();
        var collectionType = item?.CollectionTypeName();
        return (itemType == "MusicGenre" || (parameters.Type != "Programs" && parameters.Type != "nextup" && itemType != "Channel"))
            && itemType != "UserView"
            && !(itemType == "CollectionFolder" && collectionType != "homevideos" && collectionType != "musicvideos");
    }

    /// <summary>Whether the alphabet picker exists (item pages other than photo albums).</summary>
    public static bool HasAlphaPicker(BaseItemDto? item) => item is not null && item.TypeName() != "PhotoAlbum";

    /// <summary>Whether the alphabet picker shows: only when the list is sorted by name (<c>updateAlphaPickerState</c>).</summary>
    public static bool ShowAlphaPicker(BaseItemDto? item, ListSort sort) =>
        HasAlphaPicker(item) && sort.SortBy.Contains("SortName", StringComparison.Ordinal);

    /// <summary>
    /// The server request of the page (<c>getItems</c> with <c>modifyQueryWithFilters</c>). A null
    /// <paramref name="sortBy"/> applies the saved sort order; <paramref name="recursive"/> reports
    /// <c>queryRecursive</c>, which the filter dialog passes on.
    /// </summary>
    public static LibraryRequest Request(
        ListParameters parameters,
        BaseItemDto? item,
        ListSort sort,
        ListFilters filters,
        string? alphabet,
        bool hasAlphaPicker,
        string? userId,
        bool enableRewatching,
        string? sortBy,
        int startIndex,
        int? limit,
        out bool recursive)
    {
        recursive = false;
        var query = new List<(string Key, object? Value)>();
        string path;
        string? fields;

        void Add(string key, object? value) => query.Add((key, value));

        if (parameters.Type is "Recordings" or "Programs")
        {
            path = parameters.Type == "Recordings"
                ? "LiveTv/Recordings"
                : parameters.IsAiring == "true" ? "LiveTv/Programs/Recommended" : "LiveTv/Programs";
            Add("UserId", userId);
            Add("StartIndex", startIndex);
            fields = "ChannelInfo,PrimaryImageAspectRatio";
            Add("Limit", limit);
            if (parameters.Type == "Recordings")
            {
                Add("IsInProgress", false);
            }
            else
            {
                Add("HasAired", false);
            }

            Add("GenreIds", parameters.GenreId);
            AddFlag(query, "IsMovie", parameters.IsMovie);
            AddFlag(query, "IsSeries", parameters.IsSeries);
            AddFlag(query, "IsNews", parameters.IsNews);
            AddFlag(query, "IsSports", parameters.IsSports);
            AddFlag(query, "IsKids", parameters.IsKids);
            AddFlag(query, "IsAiring", parameters.IsAiring);
        }
        else if (parameters.Type == "nextup")
        {
            path = "Shows/NextUp";
            Add("Limit", limit);
            fields = "PrimaryImageAspectRatio,DateCreated,MediaSourceCount,Chapters,Trickplay";
            Add("UserId", userId);
            Add("EnableImageTypes", "Primary,Backdrop,Thumb");
            Add("EnableTotalRecordCount", false);
            Add("EnableRewatching", enableRewatching);
        }
        else if (item is null)
        {
            recursive = true;
            var persons = parameters.Type is "MusicArtist" or "Person";
            path = persons ? "Persons" : "Items";
            Add("userId", userId);
            Add("StartIndex", startIndex);
            Add("Limit", limit);
            fields = "PrimaryImageAspectRatio,SortName,Chapters,Trickplay";
            Add("IncludeItemTypes", persons ? null : parameters.Type);
            Add("PersonTypes", parameters.Type == "MusicArtist" ? "Artist" : null);
            Add("Recursive", true);
            Add("IsFavorite", parameters.IsFavorite == "true" ? true : (object?)null);
            Add("ArtistIds", parameters.ArtistId);
            Add("Tags", parameters.Tag);
        }
        else if (item.TypeName() is "Genre" or "MusicGenre" or "Studio" or "Person")
        {
            recursive = true;
            var itemType = item.TypeName();
            var collectionType = item.CollectionTypeName();
            path = "Items";
            Add("userId", userId);
            Add("StartIndex", startIndex);
            Add("Limit", limit);
            fields = "PrimaryImageAspectRatio,SortName";
            Add("Recursive", true);
            Add("parentId", parameters.ParentId);
            Add(itemType == "Studio" ? "StudioIds" : itemType == "Person" ? "PersonIds" : "GenreIds", item.IdText());
            Add("IncludeItemTypes", itemType == "MusicGenre" ? "MusicAlbum"
                : collectionType == "movies" ? "Movie"
                : collectionType == "tvshows" ? "Series"
                : itemType == "Genre" ? "Audiobook,Book,BoxSet,Movie,Series,Video"
                : itemType == "Person" ? parameters.Type
                : null);
        }
        else
        {
            path = "Items";
            Add("userId", userId);
            Add("StartIndex", startIndex);
            Add("Limit", limit);
            fields = "PrimaryImageAspectRatio,SortName,Path,ChildCount,MediaSourceCount";
            Add("ParentId", item.IdText());
            if (parameters.Type is not null)
            {
                recursive = true;
                Add("Recursive", true);
                Add("IncludeItemTypes", parameters.Type);
            }
            else if (sortBy == "Random")
            {
                recursive = true;
                Add("IncludeItemTypes", "Video,Movie,Series,Music,MusicVideo");
                Add("Recursive", true);
            }
        }

        // modifyQueryWithFilters
        if (string.IsNullOrEmpty(sortBy))
        {
            Add("SortBy", sort.SortBy);
            Add("SortOrder", sort.SortOrder);
        }
        else
        {
            Add("SortBy", sortBy);
        }

        Add("Fields", fields + ",PrimaryImageAspectRatio");
        Add("ImageTypeLimit", 1);

        var queryFilters = new List<string>();
        if (filters.IsPlayed)
        {
            queryFilters.Add("IsPlayed");
        }

        if (filters.IsUnplayed)
        {
            queryFilters.Add("IsUnplayed");
        }

        if (filters.IsFavorite)
        {
            queryFilters.Add("IsFavorite");
        }

        if (filters.IsResumable)
        {
            queryFilters.Add("IsResumable");
        }

        Add("Filters", queryFilters.Count > 0 ? string.Join(',', queryFilters) : null);
        Add("VideoTypes", filters.VideoTypes);
        if (!string.IsNullOrEmpty(filters.GenreIds))
        {
            // The saved genre filter replaces the genre of the URL, as it does in the original's query object.
            query.RemoveAll(entry => entry.Key == "GenreIds");
            Add("GenreIds", filters.GenreIds);
        }

        Add("Is4K", filters.Is4K ? true : (object?)null);
        Add("IsHD", filters.IsSD ? false : filters.IsHD ? true : (object?)null);
        Add("Is3D", filters.Is3D ? true : (object?)null);
        Add("HasSubtitles", filters.HasSubtitles ? true : (object?)null);
        Add("HasTrailer", filters.HasTrailer ? true : (object?)null);
        Add("HasSpecialFeature", filters.HasSpecialFeature ? true : (object?)null);
        Add("HasThemeSong", filters.HasThemeSong ? true : (object?)null);
        Add("HasThemeVideo", filters.HasThemeVideo ? true : (object?)null);

        if (hasAlphaPicker && !string.IsNullOrEmpty(alphabet))
        {
            if (alphabet == "#")
            {
                Add("NameLessThan", "A");
            }
            else
            {
                Add("NameStartsWith", alphabet);
            }
        }

        return new LibraryRequest(path, query.Where(entry => entry.Value is not null).ToList());
    }

    /// <summary>The card options of the page (<c>getItemsHtml</c>).</summary>
    public static CardOptions Cards(ListParameters parameters, BaseItemDto? item, ListViewSettings settings)
    {
        var shape = CardShape.AutoVertical;
        CardToggle preferThumb = CardToggle.Unset;
        var preferDisc = false;
        var preferLogo = false;
        string? defaultShape = null;
        var movie = parameters.IsMovie == "true";
        switch (settings.ImageType)
        {
            case "banner":
                shape = CardShape.Banner;
                break;
            case "disc":
                shape = CardShape.Square;
                preferDisc = true;
                break;
            case "logo":
                shape = CardShape.Backdrop;
                preferLogo = true;
                break;
            case "thumb":
                shape = CardShape.Backdrop;
                preferThumb = true;
                break;
            default:
                if (parameters.Type == "nextup")
                {
                    shape = CardShape.Backdrop;
                }
                else if (parameters.Type is "Programs" or "Recordings")
                {
                    shape = movie ? CardShape.Portrait : CardShape.AutoVertical;
                    preferThumb = movie ? CardToggle.Unset : CardToggle.Auto;
                    defaultShape = movie ? CardShape.Portrait : CardShape.Backdrop;
                }

                break;
        }

        var lines = settings.ShowTitle ? 2 : 0;
        var options = new CardOptions
        {
            Shape = shape,
            ShowTitle = settings.ShowTitle,
            ShowYear = settings.ShowTitle,
            CenterText = true,
            CoverImage = true,
            PreferThumb = preferThumb,
            PreferDisc = preferDisc,
            PreferLogo = preferLogo,
            OverlayPlayButton = false,
            OverlayMoreButton = true,
            OverlayText = !settings.ShowTitle,
            DefaultShape = defaultShape,
            Action = parameters.Type == "Audio" ? ItemAction.PlayAllFromHere : null
        };

        switch (parameters.Type)
        {
            case "Person":
                options = options with { ShowYear = false, ShowParentTitle = false };
                lines = 1;
                break;
            case "MusicArtist":
                options = options with { ShowYear = false };
                lines = 1;
                break;
            case "Programs":
                lines = settings.ShowTitle ? 1 : 0;
                var showParentTitle = settings.ShowTitle && !movie;
                if (showParentTitle)
                {
                    lines++;
                }

                if (settings.ShowTitle)
                {
                    lines++;
                }

                options = options with
                {
                    InheritThumb = false,
                    Context = "livetv",
                    ShowParentTitle = showParentTitle,
                    ShowAirTime = settings.ShowTitle,
                    ShowAirDateTime = settings.ShowTitle,
                    ShowYear = false
                };
                break;
            default:
                options = options with { ShowParentTitle = settings.ShowTitle };
                break;
        }

        options = options with { Lines = lines };
        return item?.CollectionTypeName() == "folders" ? options with { Context = "folders" } : options;
    }

    private static void AddFlag(List<(string Key, object? Value)> query, string key, string? value)
    {
        if (value == "true")
        {
            query.Add((key, true));
        }
        else if (value == "false")
        {
            query.Add((key, false));
        }
    }
}

/// <summary>
/// Where the list page keeps its settings: the user settings (synchronised with the server) under
/// <c>{key}-sortby</c>, <c>{key}-sortorder</c>, <c>{key}-filter-{name}</c>, <c>{key}-showTitle</c>,
/// <c>{key}-showYear</c>, <c>{key}-imageType</c> and <c>{key}-viewType</c>, as the original does.
/// </summary>
[AppService]
public sealed class ListPageSettings(UserSettings settings)
{
    public ListSort GetSort(ListParameters parameters)
    {
        var key = ListPageModel.SettingsKey(parameters);
        var sortBy = settings.Get(key + "-sortby");
        return new ListSort(
            string.IsNullOrEmpty(sortBy) ? ListPageModel.DefaultSortBy(parameters) : sortBy,
            settings.Get(key + "-sortorder") == "Descending" ? "Descending" : "Ascending");
    }

    public void SetSort(ListParameters parameters, ListSort sort)
    {
        var key = ListPageModel.SettingsKey(parameters);
        settings.Set(key + "-sortby", sort.SortBy);
        settings.Set(key + "-sortorder", sort.SortOrder);
    }

    public ListFilters GetFilters(ListParameters parameters)
    {
        var key = ListPageModel.SettingsKey(parameters) + "-filter-";
        var filters = new ListFilters
        {
            VideoTypes = Text(settings.Get(key + "VideoTypes")),
            SeriesStatus = Text(settings.Get(key + "SeriesStatus")),
            GenreIds = Text(settings.Get(key + "GenreIds"))
        };
        foreach (var flag in ListFilters.Flags)
        {
            if (settings.Get(key + flag) == "true")
            {
                filters = filters.With(flag, true);
            }
        }

        return filters;
    }

    /// <summary>Saves the filters the way the filter menu does: a flag as "true" or removed, the lists as text.</summary>
    public void SetFilters(ListParameters parameters, ListFilters filters)
    {
        var key = ListPageModel.SettingsKey(parameters) + "-filter-";
        foreach (var flag in ListFilters.Flags)
        {
            settings.Set(key + flag, filters.Get(flag) ? "true" : null);
        }

        settings.Set(key + "VideoTypes", filters.VideoTypes ?? string.Empty);
        settings.Set(key + "SeriesStatus", filters.SeriesStatus ?? string.Empty);
        settings.Set(key + "GenreIds", filters.GenreIds ?? string.Empty);
    }

    public ListViewSettings GetView(ListParameters parameters, BaseItemDto? item)
    {
        var key = ListPageModel.SettingsKey(parameters);
        var showTitle = settings.Get(key + "-showTitle") switch
        {
            "true" => true,
            "false" => false,
            _ => ListPageModel.DefaultShowTitle(parameters, item)
        };
        var imageType = settings.Get(key + "-imageType");
        if (string.IsNullOrEmpty(imageType) && parameters.Type == "nextup")
        {
            imageType = settings.UseEpisodeImagesInNextUpAndResume ? "primary" : "thumb";
        }

        var viewType = settings.Get(key + "-viewType");
        return new ListViewSettings(
            showTitle,
            settings.Get(key + "-showYear") != "false",
            string.IsNullOrEmpty(imageType) ? "primary" : imageType,
            string.IsNullOrEmpty(viewType) ? "images" : viewType);
    }

    public void SetView(ListParameters parameters, bool showTitle, string imageType)
    {
        var key = ListPageModel.SettingsKey(parameters);
        settings.Set(key + "-showTitle", showTitle);
        settings.Set(key + "-imageType", imageType);
    }

    private static string? Text(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
