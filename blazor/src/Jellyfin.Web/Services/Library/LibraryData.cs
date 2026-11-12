using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>One page of a library view.</summary>
public sealed record LibraryItemsResult(IReadOnlyList<BaseItemDto> Items, int TotalRecordCount)
{
    public static readonly LibraryItemsResult Empty = new([], 0);
}

/// <summary>A row of the suggestions view (<c>Section</c> of <c>types/sections.ts</c>).</summary>
public sealed record LibrarySection(
    string Type,
    string Name,
    string ApiMethod,
    string ItemTypes,
    IReadOnlyList<(string Key, object? Value)> Parameters,
    Func<UserSettings, CardOptions> Cards)
{
    public const string ResumeItems = "resumeItems";
    public const string LatestMedia = "latestMedia";
    public const string NextUp = "nextUp";
    public const string Items = "items";
}

/// <summary>A row of movie recommendations (<c>RecommendationDto</c>).</summary>
public sealed record LibraryRecommendation(string? RecommendationType, string? BaselineItemName, string? CategoryId, IReadOnlyList<BaseItemDto> Items)
{
    /// <summary>The translation key of the row title (<c>SuggestionsSectionView.getRecommendationTitle</c>).</summary>
    public string? TitleKey => RecommendationType switch
    {
        "SimilarToRecentlyPlayed" => "RecommendationBecauseYouWatched",
        "SimilarToLikedItem" => "RecommendationBecauseYouLike",
        "HasDirectorFromRecentlyPlayed" or "HasLikedDirector" => "RecommendationDirectedBy",
        "HasActorFromRecentlyPlayed" or "HasLikedActor" => "RecommendationStarring",
        _ => null
    };
}

/// <summary>The suggestion sections in the original's order (<c>getSuggestionSections</c> of <c>utils/sections.ts</c>).</summary>
public static class LibrarySections
{
    private static readonly (string Key, object? Value)[] PlayedCommon =
    [
        ("fields", "PrimaryImageAspectRatio"),
        ("filters", "IsPlayed"),
        ("IsPlayed", true),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Backdrop,Thumb")
    ];

    public static readonly IReadOnlyList<LibrarySection> All =
    [
        new(LibrarySectionType.ContinueWatchingMixed, "HeaderContinueWatching", LibrarySection.ResumeItems, "Movie,Series",
            [("includeItemTypes", "Movie,Series")],
            _ => new() { OverlayPlayButton = true, PreferThumb = true, Shape = CardShape.OverflowBackdrop, ShowYear = true }),
        new(LibrarySectionType.ContinueWatchingMovies, "HeaderContinueWatching", LibrarySection.ResumeItems, "Movie",
            [("includeItemTypes", "Movie")],
            _ => new() { OverlayPlayButton = true, PreferThumb = true, Shape = CardShape.OverflowBackdrop, ShowYear = true }),
        new(LibrarySectionType.ContinueReading, "HeaderContinueReading", LibrarySection.ResumeItems, "Book",
            [("includeItemTypes", "Book")],
            _ => new() { OverlayPlayButton = true, Shape = CardShape.OverflowPortrait }),
        new(LibrarySectionType.LatestBooks, "HeaderLatestBooks", LibrarySection.LatestMedia, "Book",
            [("includeItemTypes", "AudioBook,Book")],
            _ => new() { OverlayPlayButton = true, Shape = CardShape.OverflowPortrait }),
        new(LibrarySectionType.LatestMixed, "HeaderLatestMedia", LibrarySection.LatestMedia, "Movie,Series",
            [("includeItemTypes", "Movie,Series")],
            _ => new() { OverlayPlayButton = true, Shape = CardShape.OverflowPortrait, ShowYear = true }),
        new(LibrarySectionType.LatestMovies, "HeaderLatestMovies", LibrarySection.LatestMedia, "Movie",
            [("includeItemTypes", "Movie")],
            _ => new() { OverlayPlayButton = true, Shape = CardShape.OverflowPortrait, ShowYear = true }),
        new(LibrarySectionType.ContinueWatchingEpisode, "HeaderContinueWatching", LibrarySection.ResumeItems, "Episode",
            [("includeItemTypes", "Episode")],
            settings => new()
            {
                OverlayPlayButton = true,
                Shape = CardShape.OverflowBackdrop,
                PreferThumb = true,
                InheritThumb = !settings.UseEpisodeImagesInNextUpAndResume,
                ShowYear = true
            }),
        new(LibrarySectionType.LatestEpisode, "HeaderLatestEpisodes", LibrarySection.LatestMedia, "Episode",
            [("includeItemTypes", "Episode")],
            _ => new()
            {
                OverlayPlayButton = true,
                Shape = CardShape.OverflowBackdrop,
                PreferThumb = true,
                ShowSeriesYear = true,
                ShowParentTitle = true,
                ShowChildCountIndicator = true,
                Lines = 2
            }),
        new(LibrarySectionType.NextUp, "NextUp", LibrarySection.NextUp, "nextup", [],
            settings => new()
            {
                OverlayPlayButton = true,
                Shape = CardShape.OverflowBackdrop,
                PreferThumb = true,
                InheritThumb = !settings.UseEpisodeImagesInNextUpAndResume,
                ShowParentTitle = true
            }),
        new(LibrarySectionType.LatestMusic, "HeaderLatestMusic", LibrarySection.LatestMedia, "Audio",
            [("includeItemTypes", "Audio")],
            _ => new() { Shape = CardShape.OverflowSquare, ShowParentTitle = true, OverlayPlayButton = true, CoverImage = true }),
        new(LibrarySectionType.RecentlyPlayedMusic, "HeaderRecentlyPlayed", LibrarySection.Items, "Audio",
            [("sortBy", "DatePlayed"), ("sortOrder", "Descending"), ("includeItemTypes", "Audio"), .. PlayedCommon],
            _ => MusicPlayed(CardShape.OverflowSquare, ItemAction.InstantMix)),
        new(LibrarySectionType.FrequentlyPlayedMusic, "HeaderFrequentlyPlayed", LibrarySection.Items, "Audio",
            [("sortBy", "PlayCount"), ("sortOrder", "Descending"), ("includeItemTypes", "Audio"), .. PlayedCommon],
            _ => MusicPlayed(CardShape.OverflowSquare, ItemAction.InstantMix)),
        new(LibrarySectionType.LatestMusicVideos, "HeaderLatestMusicVideos", LibrarySection.LatestMedia, "Video",
            [("includeItemTypes", "MusicVideo")],
            _ => new() { Shape = CardShape.OverflowBackdrop, ShowParentTitle = true, OverlayPlayButton = true, CoverImage = true }),
        new(LibrarySectionType.RecentlyPlayedMusicVideos, "HeaderRecentlyPlayed", LibrarySection.Items, "Video",
            [("sortBy", "DatePlayed"), ("sortOrder", "Descending"), ("includeItemTypes", "MusicVideo"), .. PlayedCommon],
            _ => MusicPlayed(CardShape.OverflowBackdrop, null)),
        new(LibrarySectionType.FrequentlyPlayedMusicVideos, "HeaderFrequentlyPlayed", LibrarySection.Items, "Video",
            [("sortBy", "PlayCount"), ("sortOrder", "Descending"), ("includeItemTypes", "MusicVideo"), .. PlayedCommon],
            _ => MusicPlayed(CardShape.OverflowBackdrop, null))
    ];

    /// <summary>The sections of a suggestions view, in the order of <see cref="All"/> as the original loads them.</summary>
    public static IReadOnlyList<LibrarySection> For(IReadOnlyList<string>? types) =>
        types is null ? [] : All.Where(section => types.Contains(section.Type)).ToList();

    /// <summary>The card options of a section as the suggestions view draws it.</summary>
    public static CardOptions CardsOf(LibrarySection section, UserSettings settings) =>
        section.Cards(settings) with { ShowTitle = true, CenterText = true, CardLayout = false, OverlayText = false };

    private static CardOptions MusicPlayed(string shape, string? action) => new()
    {
        Shape = shape,
        ShowParentTitle = true,
        Action = action,
        OverlayMoreButton = true,
        CoverImage = true
    };
}

/// <summary>The server requests of the library pages.</summary>
[AppService]
public sealed class LibraryData(JellyfinApi api, ItemQueries queries)
{
    /// <summary>The number of genres a page of the genres view loads.</summary>
    public const int GenrePageSize = 10;

    /// <summary>The number of episodes a page of the upcoming view loads.</summary>
    public const int UpcomingPageSize = 25;

    /// <summary>Loads one page of a view: its items and the total number of matching items.</summary>
    public async Task<LibraryItemsResult> GetPageAsync(LibraryRequest request)
    {
        var json = await GetJsonAsync(request.Path, [.. request.Query]);
        var items = ItemQueries.ParseItems(json);
        var total = items.Count;
        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("TotalRecordCount", out var count) && count.TryGetInt32(out var value))
            {
                total = value;
            }
        }

        return new LibraryItemsResult(items, total);
    }

    /// <summary>The library item itself (<c>useItem(parentId)</c>), or null.</summary>
    public async Task<BaseItemDto?> GetLibraryItemAsync(string? parentId)
    {
        if (string.IsNullOrEmpty(parentId))
        {
            return null;
        }

        try
        {
            return await queries.GetItemAsync(parentId);
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn("Loading the library failed: " + exception.Message);
            return null;
        }
    }

    /// <summary>
    /// The filter values the server offers for a view: <c>Items/Filters</c> (genres, ratings, tags, years),
    /// <c>Items/Filters2</c> (languages) and <c>Studios</c>. Only asked for a library (<c>parentId</c>).
    /// </summary>
    public async Task<LibraryFilterValues> GetFilterValuesAsync(string viewType, string? parentId, IReadOnlyList<string>? itemTypes)
    {
        if (string.IsNullOrEmpty(parentId) || !LibraryQueryBuilder.UsesServerFilters(viewType))
        {
            return LibraryFilterValues.Empty;
        }

        (string Key, object? Value)[] query = [("userId", api.UserIdText), ("parentId", parentId), ("includeItemTypes", itemTypes)];
        var values = new LibraryFilterValues();
        try
        {
            using (var legacy = JsonDocument.Parse(await GetJsonAsync("Items/Filters", query)))
            {
                var root = legacy.RootElement;
                values = values with
                {
                    Genres = Strings(root, "Genres"),
                    OfficialRatings = Strings(root, "OfficialRatings"),
                    Tags = Strings(root, "Tags"),
                    Years = root.TryGetProperty("Years", out var years) && years.ValueKind == JsonValueKind.Array
                        ? years.EnumerateArray().Where(year => year.ValueKind == JsonValueKind.Number).Select(year => year.GetInt32()).ToList()
                        : []
                };
            }

            using (var filters = JsonDocument.Parse(await GetJsonAsync("Items/Filters2", query)))
            {
                values = values with
                {
                    AudioLanguages = Pairs(filters.RootElement, "AudioLanguages", "Value"),
                    SubtitleLanguages = Pairs(filters.RootElement, "SubtitleLanguages", "Value")
                };
            }

            using var studios = JsonDocument.Parse(await GetJsonAsync(
                "Studios",
                ("userId", api.UserIdText),
                ("includeItemTypes", itemTypes),
                ("fields", "DateCreated,PrimaryImageAspectRatio"),
                ("enableImageTypes", "Thumb"),
                ("parentId", parentId),
                ("enableTotalRecordCount", false)));
            values = values with { Studios = Pairs(studios.RootElement, "Items", "Id") };
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            AppLog.Warn("Loading the filter values failed: " + exception.Message);
        }

        return values;
    }

    /// <summary>A page of the genres of a library (<c>useGetGenres</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetGenresAsync(string parentId, IReadOnlyList<string>? itemTypes, string? alphabet, int page)
    {
        var query = new List<(string Key, object? Value)>
        {
            ("userId", api.UserIdText),
            ("includeItemTypes", itemTypes),
            ("parentId", parentId),
            ("sortBy", "SortName"),
            ("sortOrder", "Ascending")
        };
        LibraryQueryBuilder.AddAlphabet(query, alphabet);
        query.Add(("enableTotalRecordCount", false));
        query.Add(("startIndex", page * GenrePageSize));
        query.Add(("limit", GenrePageSize));
        return queries.GetItemsAsync("Genres", [.. query]);
    }

    /// <summary>The items of a genre row (<c>GenresSectionContainer</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetGenreItemsAsync(string? genreId, string? parentId, IReadOnlyList<string>? itemTypes) =>
        queries.GetItemsAsync(
            "Items",
            ("userId", api.UserIdText),
            ("sortBy", "Random"),
            ("sortOrder", "Ascending"),
            ("includeItemTypes", itemTypes),
            ("recursive", true),
            ("fields", "PrimaryImageAspectRatio,MediaSourceCount"),
            ("imageTypeLimit", 1),
            ("enableImageTypes", "Primary"),
            ("limit", 25),
            ("genreIds", genreId),
            ("enableTotalRecordCount", false),
            ("parentId", parentId));

    /// <summary>The items of a suggestion section (<c>useSections.ts</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSectionItemsAsync(LibrarySection section, string parentId)
    {
        (string Key, object? Value) user = ("userId", api.UserIdText);
        (string Key, object? Value) parent = ("parentId", parentId);
        return section.ApiMethod switch
        {
            LibrarySection.ResumeItems => queries.GetItemsAsync(
                "UserItems/Resume",
                [
                    user, parent, ("fields", "PrimaryImageAspectRatio,MediaSourceCount"), ("imageTypeLimit", 1),
                    ("enableImageTypes", "Primary,Thumb,Backdrop"), ("enableTotalRecordCount", false), .. section.Parameters
                ]),
            LibrarySection.LatestMedia => queries.GetItemsAsync(
                "Items/Latest",
                [
                    user, ("fields", "PrimaryImageAspectRatio,MediaSourceCount"), parent, ("imageTypeLimit", 1),
                    ("enableImageTypes", "Primary,Thumb"), .. section.Parameters
                ]),
            LibrarySection.NextUp => queries.GetItemsAsync(
                "Shows/NextUp",
                user, ("limit", 25), ("fields", "PrimaryImageAspectRatio,MediaSourceCount"), parent, ("imageTypeLimit", 1),
                ("enableImageTypes", "Primary,Thumb,Backdrop"), ("enableTotalRecordCount", false)),
            _ => queries.GetItemsAsync(
                "Items",
                [user, parent, ("recursive", true), ("limit", 25), ("enableTotalRecordCount", false), .. section.Parameters])
        };
    }

    /// <summary>The movie recommendation rows of a library (<c>useGetMovieRecommendations</c>).</summary>
    public async Task<IReadOnlyList<LibraryRecommendation>> GetMovieRecommendationsAsync(string parentId)
    {
        var json = await GetJsonAsync(
            "Movies/Recommendations",
            ("userId", api.UserIdText),
            ("parentId", parentId),
            ("fields", "PrimaryImageAspectRatio,MediaSourceCount"),
            ("categoryLimit", 6),
            ("itemLimit", 20));
        var result = new List<LibraryRecommendation>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var items = entry.TryGetProperty("Items", out var list) && list.ValueKind == JsonValueKind.Array
                ? KiotaJson.ParseList(list, BaseItemDto.CreateFromDiscriminatorValue)
                : [];
            result.Add(new LibraryRecommendation(String(entry, "RecommendationType"), String(entry, "BaselineItemName"), String(entry, "CategoryId"), items));
        }

        return result;
    }

    /// <summary>A page of upcoming episodes of a library (<c>useGetUpcomingEpisodes</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetUpcomingAsync(string parentId, int startIndex) => queries.GetItemsAsync(
        "Shows/Upcoming",
        ("userId", api.UserIdText),
        ("parentId", parentId),
        ("fields", "AirTime"),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Backdrop,Thumb"),
        ("startIndex", startIndex),
        ("limit", UpcomingPageSize));

    /// <summary>
    /// The genres the filter dialog of the list page offers (<c>filtermenu.js loadDynamicFilters</c>:
    /// <c>Items/Filters2</c> with the page's flags). Empty when the request fails.
    /// </summary>
    public async Task<IReadOnlyList<(string Id, string Name)>> GetListGenresAsync(ListParameters parameters, bool recursive)
    {
        static object? Flag(string? value) => value switch { "true" => true, "false" => false, _ => null };
        try
        {
            using var document = JsonDocument.Parse(await GetJsonAsync(
                "Items/Filters2",
                ("IsAiring", Flag(parameters.IsAiring)),
                ("IsMovie", Flag(parameters.IsMovie)),
                ("IsSports", Flag(parameters.IsSports)),
                ("IsKids", Flag(parameters.IsKids)),
                ("IsNews", Flag(parameters.IsNews)),
                ("IsSeries", Flag(parameters.IsSeries)),
                ("Recursive", recursive ? true : null),
                ("UserId", api.UserIdText),
                ("ParentId", parameters.ParentId),
                ("IncludeItemTypes", string.Join(',', ListPageModel.FilterItemTypes(parameters)))));
            return Pairs(document.RootElement, "Genres", "Id");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            AppLog.Warn("Loading the genres failed: " + exception.Message);
            return [];
        }
    }

    private async Task<string> GetJsonAsync(string path, params (string Key, object? Value)[] query)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl(path, query));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<string> Strings(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array
            ? values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).ToList()
            : [];

    private static List<(string, string)> Pairs(JsonElement element, string name, string valueName)
    {
        var result = new List<(string, string)>();
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var values) || values.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var entry in values.EnumerateArray())
        {
            if (String(entry, valueName) is { Length: > 0 } value)
            {
                result.Add((value, String(entry, "Name") ?? value));
            }
        }

        return result;
    }
}
