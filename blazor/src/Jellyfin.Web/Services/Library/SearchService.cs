using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>One request of the search page and the result row(s) it fills.</summary>
/// <param name="Title">The translation key of the row title; null for the item search, which is split per item type.</param>
/// <param name="Request">The server request.</param>
/// <param name="Cards">The card options of the row on top of <see cref="SearchQueries.RowCards"/>.</param>
/// <param name="ItemTypes">The item types the result is split into, in row order (item search only).</param>
public sealed record SearchRequest(
    string? Title,
    LibraryRequest Request,
    Func<CardOptions, CardOptions>? Cards = null,
    IReadOnlyList<string>? ItemTypes = null);

/// <summary>A row of search results.</summary>
public sealed record SearchSection(string Title, IReadOnlyList<BaseItemDto> Items, CardOptions Cards);

/// <summary>
/// The requests and rows of the search page (<c>apps/legacy/features/search</c>: <c>useSearchItems</c>,
/// <c>useArtistsSearch</c>, <c>usePeopleSearch</c>, <c>useStudiosSearch</c>, <c>useVideoSearch</c>,
/// <c>useProgramsSearch</c>, <c>useLiveTvSearch</c>, <c>useSearchSuggestions</c> and <c>utils/search.ts</c>).
/// </summary>
public static class SearchQueries
{
    /// <summary>The order of the result rows (<c>SEARCH_SECTIONS_SORT_ORDER</c>); other titles come first.</summary>
    public static readonly IReadOnlyList<string> SectionOrder =
    [
        "Movies", "Shows", "Episodes", "People", "Studios", "Playlists", "Artists", "Albums", "Songs", "HeaderVideos",
        "Programs", "Channels", "HeaderPhotoAlbums", "Photos", "HeaderAudioBooks", "Books", "Collections"
    ];

    /// <summary>The cards every row starts from (<c>SearchResultsRow</c>); the row's own options override them.</summary>
    /// <remarks>PORT-TODO(library): <c>scalable</c> and <c>allowBottomPadding: false</c> have no counterpart in <see cref="CardOptions"/>.</remarks>
    public static readonly CardOptions RowCards = new()
    {
        Shape = CardShape.AutoOverflow,
        ShowTitle = true,
        OverlayText = false,
        CenterText = true
    };

    /// <summary><c>LIVETV_CARD_OPTIONS</c>.</summary>
    public static CardOptions LiveTvCards(CardOptions options) => options with
    {
        PreferThumb = true,
        InheritThumb = false,
        ShowParentTitleOrTitle = true,
        ShowTitle = false,
        CoverImage = true,
        OverlayMoreButton = true,
        ShowAirTime = true,
        ShowAirDateTime = true,
        ShowChannelName = true
    };

    /// <summary>The item types the item search asks for (<c>getItemTypesFromCollectionType</c>).</summary>
    public static IReadOnlyList<string> ItemTypes(string? collectionType) => collectionType switch
    {
        "movies" => ["Movie"],
        "tvshows" => ["Series", "Episode"],
        "music" => ["Playlist", "MusicAlbum", "Audio"],
        _ => ["Movie", "Series", "Episode", "Playlist", "MusicAlbum", "Audio", "TvChannel", "PhotoAlbum", "Photo", "AudioBook", "Book", "BoxSet"]
    };

    /// <summary>The translation key of the row of an item type (<c>getTitleFromType</c>).</summary>
    public static string TitleOf(string itemType) => itemType switch
    {
        "Movie" => "Movies",
        "Series" => "Shows",
        "Episode" => "Episodes",
        "Playlist" => "Playlists",
        "MusicAlbum" => "Albums",
        "Audio" => "Songs",
        "LiveTvProgram" => "Programs",
        "TvChannel" => "Channels",
        "PhotoAlbum" => "HeaderPhotoAlbums",
        "Photo" => "Photos",
        "AudioBook" => "HeaderAudioBooks",
        "Book" => "Books",
        "BoxSet" => "Collections",
        _ => string.Empty
    };

    /// <summary>The card options of the row of an item type (<c>getCardOptionsFromType</c>).</summary>
    public static CardOptions CardsOf(string itemType, CardOptions options) => itemType switch
    {
        "Movie" or "Series" or "MusicAlbum" => options with { ShowYear = true },
        "Episode" => options with { CoverImage = true, ShowParentTitle = true },
        "MusicArtist" => options with { CoverImage = true },
        "Audio" => options with { ShowParentTitle = true, Shape = CardShape.OverflowSquare },
        "LiveTvProgram" => LiveTvCards(options),
        _ => options
    };

    /// <summary>The request of the suggestions shown while the search field is empty (<c>useSearchSuggestions</c>).</summary>
    public static LibraryRequest Suggestions(string? parentId, string? userId) => new("Items",
    [
        ("userId", userId),
        ("sortBy", "IsFavoriteOrLiked,Random"),
        ("includeItemTypes", "Movie,Series,MusicArtist"),
        ("limit", 20),
        ("recursive", true),
        ("imageTypeLimit", 0),
        ("enableImages", false),
        ("parentId", parentId),
        ("enableTotalRecordCount", false)
    ]);

    /// <summary>
    /// The requests of a search in the order the original adds their rows; <see cref="Sort"/> then orders the
    /// rows. A Live TV search (<c>collectionType=livetv</c>) asks seven program requests instead.
    /// </summary>
    public static IReadOnlyList<SearchRequest> Requests(
        string? parentId, string? collectionType, string query, string? userId, bool displayMissingEpisodes)
    {
        var searchTerm = query.Trim();
        var type = string.IsNullOrEmpty(collectionType) ? null : collectionType;
        parentId = string.IsNullOrEmpty(parentId) ? null : parentId;

        LibraryRequest Items(params (string Key, object? Value)[] extra) =>
            new("Items", [.. Common(userId), ("recursive", true), .. extra, ("searchTerm", searchTerm)]);

        if (type == "livetv")
        {
            return
            [
                new SearchRequest(
                    "Movies",
                    Items(("includeItemTypes", "LiveTvProgram"), ("isMovie", true)),
                    options => LiveTvCards(options) with { Shape = CardShape.OverflowPortrait }),
                new SearchRequest(
                    "Episodes",
                    Items(("includeItemTypes", "LiveTvProgram"), ("isMovie", false), ("isSeries", true), ("isSports", false), ("isKids", false), ("isNews", false)),
                    LiveTvCards),
                new SearchRequest("Sports", Items(("includeItemTypes", "LiveTvProgram"), ("isSports", true)), LiveTvCards),
                new SearchRequest("Kids", Items(("includeItemTypes", "LiveTvProgram"), ("isKids", true)), LiveTvCards),
                new SearchRequest("News", Items(("includeItemTypes", "LiveTvProgram"), ("isNews", true)), LiveTvCards),
                new SearchRequest(
                    "Programs",
                    Items(("includeItemTypes", "LiveTvProgram"), ("isMovie", false), ("isSeries", false), ("isSports", false), ("isKids", false), ("isNews", false)),
                    LiveTvCards),
                new SearchRequest("Channels", Items(("includeItemTypes", "TvChannel")))
            ];
        }

        var requests = new List<SearchRequest>();
        var peopleEnabled = type is null or "movies" or "tvshows";
        if (peopleEnabled)
        {
            requests.Add(new SearchRequest(
                "Artists",
                new LibraryRequest("Persons", [.. Common(userId), ("searchTerm", searchTerm), ("personTypes", "Artist,AlbumArtist")]),
                options => options with { CoverImage = true }));
        }

        if (type is null)
        {
            requests.Add(new SearchRequest("Programs", Items(("includeItemTypes", "LiveTvProgram"), ("parentId", parentId)), LiveTvCards));
        }

        if (peopleEnabled)
        {
            requests.Add(new SearchRequest(
                "People",
                new LibraryRequest("Persons", [.. Common(userId), ("searchTerm", searchTerm), ("excludePersonTypes", "Artist,AlbumArtist")]),
                options => options with { CoverImage = true }));
            requests.Add(new SearchRequest(
                "Studios",
                new LibraryRequest("Studios", [.. Common(userId), ("parentId", parentId), ("searchTerm", searchTerm)]),
                options => options with { Shape = CardShape.OverflowSquare }));
        }

        if (type is null)
        {
            requests.Add(new SearchRequest(
                "HeaderVideos",
                Items(("mediaTypes", "Video"), ("excludeItemTypes", "Movie,Episode,TvChannel"), ("parentId", parentId)),
                options => options with { ShowParentTitle = true }));
        }

        var itemTypes = ItemTypes(type);
        requests.Add(new SearchRequest(
            null,
            new LibraryRequest("Items",
            [
                .. Common(userId, 800),
                ("recursive", true),
                ("includeItemTypes", string.Join(',', itemTypes)),
                ("parentId", parentId),
                ("searchTerm", searchTerm),
                ("isMissing", itemTypes.Contains("Episode") && !displayMissingEpisodes ? false : (object?)null)
            ]),
            ItemTypes: itemTypes));
        return requests;
    }

    /// <summary>The rows of a request's result: one row, or one per item type for the item search; empty rows are left out.</summary>
    public static IEnumerable<SearchSection> Sections(SearchRequest request, IReadOnlyList<BaseItemDto> items)
    {
        if (request.ItemTypes is null)
        {
            if (items.Count > 0)
            {
                yield return new SearchSection(request.Title ?? string.Empty, items, request.Cards?.Invoke(RowCards) ?? RowCards);
            }

            yield break;
        }

        foreach (var itemType in request.ItemTypes)
        {
            var ofType = items.Where(item => item.TypeName() == itemType).ToList();
            if (ofType.Count > 0)
            {
                yield return new SearchSection(TitleOf(itemType), ofType, CardsOf(itemType, RowCards));
            }
        }
    }

    /// <summary><c>sortSections</c>: a stable sort by the position of the title in <see cref="SectionOrder"/>.</summary>
    public static IReadOnlyList<SearchSection> Sort(IEnumerable<SearchSection> sections)
    {
        var order = SectionOrder.ToList();
        return sections.OrderBy(section => order.IndexOf(section.Title)).ToList();
    }

    // QUERY_OPTIONS; the item search replaces its limit.
    private static (string Key, object? Value)[] Common(string? userId, int limit = 100) =>
    [
        ("limit", limit),
        ("fields", "PrimaryImageAspectRatio,CanDelete,MediaSourceCount"),
        ("enableTotalRecordCount", false),
        ("imageTypeLimit", 1),
        ("userId", userId)
    ];
}

/// <summary>Runs the requests of the search page.</summary>
[AppService]
public sealed class SearchService(JellyfinApi api, ItemQueries queries)
{
    /// <summary>The delay between the last key stroke and the search (<c>useDebounceValue(query, 500)</c>).</summary>
    public static TimeSpan Debounce { get; set; } = TimeSpan.FromMilliseconds(500);

    public Task<IReadOnlyList<BaseItemDto>> GetSuggestionsAsync(string? parentId)
    {
        var request = SearchQueries.Suggestions(string.IsNullOrEmpty(parentId) ? null : parentId, api.UserIdText);
        return queries.GetItemsAsync(request.Path, [.. request.Query]);
    }

    /// <summary>The result rows of a search, in display order. A failed request leaves its row out.</summary>
    public async Task<IReadOnlyList<SearchSection>> SearchAsync(string? parentId, string? collectionType, string query)
    {
        var displayMissing = api.User?.Configuration?.DisplayMissingEpisodes == true;
        var requests = SearchQueries.Requests(parentId, collectionType, query, api.UserIdText, displayMissing);
        var results = await Task.WhenAll(requests.Select(RunAsync));
        return SearchQueries.Sort(requests.Zip(results).SelectMany(pair => SearchQueries.Sections(pair.First, pair.Second)));
    }

    private async Task<IReadOnlyList<BaseItemDto>> RunAsync(SearchRequest request)
    {
        try
        {
            return await queries.GetItemsAsync(request.Request.Path, [.. request.Request.Query]);
        }
        catch (HttpRequestException exception)
        {
            AppLog.Warn("Search request failed: " + exception.Message);
            return [];
        }
    }
}
