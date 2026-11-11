using System.Globalization;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Jellyfin.Web.Services.Playback;

/// <content>What to play: turning a play request into the list of playable items.</content>
public sealed partial class PlaybackManager
{
    /// <summary>The original's UNLIMITED_ITEMS.</summary>
    private const int UnlimitedItems = -1;
    private const int ItemsPerRequestLimit = 40;
    private const string PlaybackFields = "Chapters,MediaSources,Trickplay";

    /// <summary>Every parameter of the Items request (lib/jellyfin-apiclient/utils/legacyItemsQuery.ts).</summary>
    private static readonly Dictionary<string, string> ItemsParams = new[]
    {
        "userId", "maxOfficialRating", "hasThemeSong", "hasThemeVideo", "hasSubtitles", "hasSpecialFeature",
        "hasTrailer", "adjacentTo", "indexNumber", "parentIndexNumber", "hasParentalRating", "isHd", "is4K",
        "locationTypes", "excludeLocationTypes", "isMissing", "isUnaired", "minCommunityRating", "minCriticRating",
        "minPremiereDate", "minDateLastSaved", "minDateLastSavedForUser", "maxPremiereDate", "hasOverview",
        "hasImdbId", "hasTmdbId", "hasTvdbId", "isMovie", "isSeries", "isNews", "isKids", "isSports",
        "excludeItemIds", "startIndex", "limit", "recursive", "searchTerm", "sortOrder", "parentId", "fields",
        "excludeItemTypes", "includeItemTypes", "filters", "isFavorite", "mediaTypes", "imageTypes", "sortBy",
        "isPlayed", "genres", "officialRatings", "tags", "years", "enableUserData", "imageTypeLimit",
        "enableImageTypes", "person", "personIds", "personTypes", "studios", "artists", "excludeArtistIds",
        "artistIds", "albumArtistIds", "contributingArtistIds", "albums", "albumIds", "ids", "videoTypes",
        "minOfficialRating", "isLocked", "isPlaceHolder", "hasOfficialRating", "collapseBoxSetItems", "minWidth",
        "minHeight", "maxWidth", "maxHeight", "is3D", "seriesStatus", "nameStartsWithOrGreater", "nameStartsWith",
        "nameLessThan", "studioIds", "genreIds", "audioLanguages", "subtitleLanguages", "enableTotalRecordCount",
        "enableImages",
    }.ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task PlayAsync(PlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var request = new PlayRequest(options);

        // PORT-TODO(playback): when the current player is not a local one (remote session, cast), the
        // original hands the whole request to it (player.play(options)). Players of later workstreams
        // plug in here.
        if (request.Fullscreen)
        {
            _loading.Show();
        }

        try
        {
            var items = request.Items is not null
                ? request.Items.ToList()
                : await GetItemsForPlaybackAsync(new ItemsQuery { ["ids"] = request.Ids });
            items = await TranslateItemsForPlaybackAsync(items, request);
            items = await AddAdditionalPartsAsync(items, request);
            await PlayWithIntrosAsync(items, request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Microsoft.Kiota.Abstractions.ApiException)
        {
            // The original leaves the rejection unhandled here; the port reports it like any other
            // failure to start.
            OnPlaybackRejection(ex, request.Fullscreen);
        }
    }

    /// <inheritdoc />
    public Task QueueAsync(PlayOptions options) => QueueAsync(options, next: false);

    /// <inheritdoc />
    public Task QueueNextAsync(PlayOptions options) => QueueAsync(options, next: true);

    /// <inheritdoc />
    public Task ShuffleAsync(BaseItemDto item) => PlayAsync(new PlayOptions { Items = [item], Shuffle = true });

    /// <inheritdoc />
    public async Task InstantMixAsync(BaseItemDto item)
    {
        var result = await GetAsync(
            _api.GetUrl($"Items/{IdOf(item)}/InstantMix", ("UserId", _api.UserIdText), ("Limit", 200)),
            BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
        await PlayAsync(new PlayOptions { Items = result?.Items ?? [] });
    }

    /// <inheritdoc />
    public async Task PlayTrailersAsync(BaseItemDto item)
    {
        if (item.LocalTrailerCount is > 0)
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"Users/{_api.UserIdText}/Items/{IdOf(item)}/LocalTrailers");
            response.EnsureSuccessStatusCode();
            using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            await PlayAsync(new PlayOptions { Items = KiotaJson.ParseList(document.RootElement, BaseItemDto.CreateFromDiscriminatorValue) });
            return;
        }

        // PORT-TODO(playback): remote trailers are web URLs (mostly YouTube) played by the YouTube player
        // of the original; that player belongs to a later workstream.
        AppLog.Warn("[playback] remote trailers are not supported yet");
    }

    /// <summary>
    /// Port of translateItemsForPlayback(): expands folders, series, albums, artists, playlists and
    /// so on to the items to play. May change <see cref="PlayRequest.StartIndex"/> and
    /// <see cref="PlayRequest.MediaSourceId"/> as the original changes its options object.
    /// </summary>
    internal async Task<List<BaseItemDto>> TranslateItemsForPlaybackAsync(List<BaseItemDto> items, PlayRequest options)
    {
        if (items.Count == 0)
        {
            return [];
        }

        if (items.Count > 1 && options.Ids is { } ids)
        {
            // Use the original request id array for sorting the result in the proper order.
            var order = ids.Select(NormalizeId).ToList();
            items = items.OrderBy(i => order.IndexOf(IdOf(i) ?? string.Empty)).ToList();
        }

        var result = await GetPlaybackItemsAsync(items[0], options, items);
        return result ?? items;
    }

    /// <summary>Port of getItemsForPlayback().</summary>
    internal async Task<List<BaseItemDto>> GetItemsForPlaybackAsync(ItemsQuery query)
    {
        var ids = query.List("ids");
        if (ids is { Count: 1 })
        {
            var item = await GetItemAsync(ids[0]);
            return item is null ? [] : [item];
        }

        var request = new ItemsQuery(query)
        {
            ["userId"] = _api.UserIdText,
            ["fields"] = PlaybackFields,
            ["excludeLocationTypes"] = "Virtual",
            ["enableTotalRecordCount"] = false,
            ["collapseBoxSetItems"] = false,
        };

        var limit = query.Int("limit");
        if (limit == UnlimitedItems)
        {
            request.Remove("limit");
        }
        else
        {
            limit = limit is null or 0 ? 300 : limit;
            request["limit"] = limit;
        }

        // lib/jellyfin-apiclient/utils/getItems.ts: long id lists are split over several requests.
        if (ids is not null && limit is > 0 && ids.Count > limit)
        {
            ids = ids.Take(limit.Value).ToList();
        }

        if (ids is null || ids.Count <= ItemsPerRequestLimit)
        {
            return await FetchItemsAsync(request);
        }

        var all = new List<BaseItemDto>();
        foreach (var chunk in ids.Chunk(ItemsPerRequestLimit))
        {
            all.AddRange(await FetchItemsAsync(new ItemsQuery(request) { ["ids"] = chunk }));
        }

        return all;
    }

    /// <summary>
    /// Port of toItemsRequest(): maps a legacy (any casing) Items query to the request parameters;
    /// unknown keys are dropped.
    /// </summary>
    internal static ItemsQuery ToItemsRequest(IReadOnlyDictionary<string, string>? query)
    {
        var result = new ItemsQuery();
        foreach (var (key, value) in query ?? new Dictionary<string, string>())
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!ItemsParams.TryGetValue(key, out var name))
            {
                AppLog.Warn($"[toItemsRequest] Could not map unknown parameter: {key}");
                continue;
            }

            result[name] = value;
        }

        return result;
    }

    private static string? IdOf(BaseItemDto? item) => item?.Id?.ToString("N");

    private static string? IdOf(Guid? id) => id?.ToString("N");

    private static string NormalizeId(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("N") : id;

    private static bool IsServerItem(BaseItemDto item) => item.Id is not null;

    private static bool EnableIntros(BaseItemDto item)
    {
        if (item.MediaType != BaseItemDto_MediaType.Video || item.Type == BaseItemDto_Type.TvChannel)
        {
            return false;
        }

        // Disabled for in-progress recordings.
        return item.Status != "InProgress" && IsServerItem(item);
    }

    private async Task QueueAsync(PlayOptions options, bool next)
    {
        ArgumentNullException.ThrowIfNull(options);
        var player = CurrentPlayer;
        if (player is null)
        {
            await PlayAsync(options);
            return;
        }

        var request = new PlayRequest(options);
        var items = request.Items is not null
            ? request.Items.ToList()
            : await GetItemsForPlaybackAsync(new ItemsQuery { ["ids"] = request.Ids });
        items = await TranslateItemsForPlaybackAsync(items, request);
        if (items.Count == 0)
        {
            return;
        }

        if (next)
        {
            _playQueue.QueueNext(items);
        }
        else
        {
            _playQueue.Queue(items);
        }

        OnPlayerEvent(player, new PlayerEvent(PlaybackEvents.PlaylistItemAdd));
    }

    private async Task<T?> GetAsync<T>(string url, ParsableFactory<T> factory)
        where T : IParsable
    {
        using var response = await _api.SendAsync(HttpMethod.Get, url);
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), factory);
    }

    private Task<BaseItemDto?> GetItemAsync(string? id) =>
        GetAsync(_api.GetUrl($"Users/{_api.UserIdText}/Items/{NormalizeId(id ?? string.Empty)}"), BaseItemDto.CreateFromDiscriminatorValue);

    private async Task<List<BaseItemDto>> FetchItemsAsync(ItemsQuery query)
    {
        var result = await GetAsync(_api.GetUrl("Items", query.ToArray()), BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
        return result?.Items ?? [];
    }

    /// <summary>Port of getPlaybackPromise(): null means "play the items as they are".</summary>
    private async Task<List<BaseItemDto>?> GetPlaybackItemsAsync(BaseItemDto firstItem, PlayRequest options, List<BaseItemDto> items)
    {
        var sortBy = options.Shuffle ? "Random" : "SortName";
        const string musicSort = "Album,ParentIndexNumber,IndexNumber,SortName";

        // The query options take precedence over the defaults. Playback queues never contain
        // folders, whatever filters the options replace.
        var queryOptions = ToItemsRequest(options.QueryOptions);
        var filters = queryOptions.List("filters") ?? [];
        if (!filters.Contains("IsNotFolder"))
        {
            queryOptions["filters"] = filters.Append("IsNotFolder").ToList();
        }

        var id = IdOf(firstItem);
        switch (firstItem.Type)
        {
            case BaseItemDto_Type.Program:
                return await GetItemsForPlaybackAsync(new ItemsQuery
                {
                    ["ids"] = firstItem.ChannelId is { } channelId ? new List<string> { IdOf(channelId)! } : null,
                });
            case BaseItemDto_Type.Playlist:
                return await GetItemsForPlaybackAsync(new ItemsQuery
                {
                    ["parentId"] = id,
                    ["sortBy"] = options.Shuffle ? sortBy : null,
                });
            case BaseItemDto_Type.MusicArtist:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["artistIds"] = id,
                    ["recursive"] = true,
                    ["sortBy"] = options.Shuffle ? sortBy : musicSort,
                    ["mediaTypes"] = "Audio",
                }));
            case BaseItemDto_Type.PhotoAlbum:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["parentId"] = id,
                    ["recursive"] = false,
                    ["sortBy"] = sortBy,

                    // Only include Photos because we do not handle mixed queues currently.
                    ["mediaTypes"] = "Photo",
                    ["limit"] = UnlimitedItems,
                }));
            case BaseItemDto_Type.MusicGenre:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["genreIds"] = id,
                    ["recursive"] = true,
                    ["sortBy"] = sortBy,
                    ["mediaTypes"] = "Audio",
                }));
            case BaseItemDto_Type.Genre:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["genreIds"] = id,
                    ["parentId"] = IdOf(firstItem.ParentId),
                    ["recursive"] = true,
                    ["sortBy"] = sortBy,
                    ["mediaTypes"] = "Video",
                }));
            case BaseItemDto_Type.Studio:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["studioIds"] = id,
                    ["recursive"] = true,
                    ["sortBy"] = sortBy,
                    ["mediaTypes"] = "Video",
                }));
            case BaseItemDto_Type.Person:
                return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
                {
                    ["personIds"] = id,
                    ["parentId"] = IdOf(firstItem.ParentId),
                    ["recursive"] = true,
                    ["sortBy"] = sortBy,
                    ["mediaTypes"] = "Video",
                }));
            case BaseItemDto_Type.Series:
            case BaseItemDto_Type.Season:
                return await GetSeriesOrSeasonItemsAsync(firstItem, options, items);
            case BaseItemDto_Type.Episode:
                return await GetEpisodeItemsAsync(firstItem, options, items);
        }

        if (firstItem.MediaType == BaseItemDto_MediaType.Photo)
        {
            var photos = await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
            {
                ["parentId"] = IdOf(firstItem.ParentId),
                ["filters"] = "IsNotFolder",

                // Setting this to true may cause some incorrect sorting.
                ["recursive"] = false,
                ["sortBy"] = sortBy,
                ["mediaTypes"] = "Photo,Video",
                ["limit"] = UnlimitedItems,
            }));
            var index = photos.FindIndex(i => i.Id == firstItem.Id);
            options.StartIndex = index == -1 ? 0 : index;
            return photos;
        }

        if (firstItem.IsFolder == true && firstItem.CollectionType == BaseItemDto_CollectionType.Homevideos)
        {
            return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
            {
                ["parentId"] = id,
                ["filters"] = "IsNotFolder",
                ["recursive"] = true,
                ["sortBy"] = sortBy,

                // Only include Photos because we do not handle mixed queues currently.
                ["mediaTypes"] = "Photo",
                ["limit"] = UnlimitedItems,
            }));
        }

        if (firstItem.IsFolder == true && firstItem.CollectionType == BaseItemDto_CollectionType.Musicvideos)
        {
            return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
            {
                ["parentId"] = id,
                ["filters"] = "IsNotFolder",
                ["recursive"] = true,
                ["sortBy"] = sortBy,
                ["mediaTypes"] = "Video",
                ["limit"] = UnlimitedItems,
            }));
        }

        if (firstItem.IsFolder == true)
        {
            string? folderSort;
            if (options.Shuffle)
            {
                folderSort = sortBy;
            }
            else if (firstItem.Type != BaseItemDto_Type.BoxSet)
            {
                folderSort = firstItem.CollectionType == BaseItemDto_CollectionType.Music || firstItem.MediaType == BaseItemDto_MediaType.Audio
                    ? musicSort
                    : "SortName";
            }
            else
            {
                folderSort = null;
            }

            return await GetItemsForPlaybackAsync(new ItemsQuery(queryOptions, new ItemsQuery
            {
                ["parentId"] = id,
                ["filters"] = "IsNotFolder",
                ["recursive"] = true,

                // These are pre-sorted.
                ["sortBy"] = folderSort,
                ["mediaTypes"] = "Audio,Video",
            }));
        }

        return null;
    }

    /// <summary>Port of getSeriesOrSeasonPlaybackPromise().</summary>
    private async Task<List<BaseItemDto>?> GetSeriesOrSeasonItemsAsync(BaseItemDto firstItem, PlayRequest options, List<BaseItemDto> items)
    {
        var startIndex = options.StartIndex ?? 0;
        var startSeasonId = firstItem.Type == BaseItemDto_Type.Season && startIndex < items.Count ? items[startIndex].Id : null;
        var seasonId = startSeasonId is not null && items.Count == 1 ? startSeasonId : null;
        var seriesId = IdOf(firstItem.SeriesId) ?? IdOf(firstItem);

        string? startItemId = null;
        if (!options.Shuffle && seasonId is null)
        {
            var nextUp = await GetAsync(
                _api.GetUrl("Shows/NextUp", ("SeriesId", seriesId), ("UserId", _api.UserIdText)),
                BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
            startItemId = IdOf(nextUp?.Items?.FirstOrDefault());
        }

        var episodes = await GetAsync(
            _api.GetUrl(
                $"Shows/{seriesId}/Episodes",
                ("IsVirtualUnaired", false),
                ("IsMissing", false),
                ("SeasonId", IdOf(seasonId)),

                // Limit to loading 100 episodes to avoid loading too large payload.
                ("limit", seasonId is null ? 100 : null),
                ("SortBy", options.Shuffle ? "Random" : null),
                ("UserId", _api.UserIdText),
                ("Fields", PlaybackFields),
                ("startItemId", startItemId)),
            BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
        var result = episodes?.Items ?? [];

        var resultStartIndex = 0;
        if (!options.Shuffle)
        {
            int? firstUnplayed = null;
            int? seasonStartIndex = null;
            for (var index = 0; index < result.Count; index++)
            {
                var episode = result[index];
                if (startSeasonId is not null && items.Count != 1)
                {
                    if (episode.SeasonId == startSeasonId)
                    {
                        seasonStartIndex ??= index;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (episode.UserData?.Played != true)
                {
                    firstUnplayed = index;
                    break;
                }
            }

            resultStartIndex = firstUnplayed is > 0 ? firstUnplayed.Value : seasonStartIndex ?? 0;
        }

        options.StartIndex = resultStartIndex;
        return result;
    }

    /// <summary>Port of getEpisodePlaybackPromise(): "play all from here" for a single episode.</summary>
    private async Task<List<BaseItemDto>?> GetEpisodeItemsAsync(BaseItemDto firstItem, PlayRequest options, List<BaseItemDto> items)
    {
        if (items.Count != 1 || firstItem.SeriesId is not { } seriesId)
        {
            return null;
        }

        var episodes = await GetAsync(
            _api.GetUrl(
                $"Shows/{IdOf(seriesId)}/Episodes",
                ("IsVirtualUnaired", false),
                ("IsMissing", false),
                ("UserId", _api.UserIdText),
                ("Fields", PlaybackFields),

                // Limit to loading 100 episodes to avoid loading too large payload.
                ("limit", 100),
                ("startItemId", IdOf(firstItem))),
            BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
        var result = episodes?.Items ?? [];

        var index = result.FindIndex(e => e.Id == firstItem.Id);
        if (index >= 0)
        {
            options.StartIndex = index;
        }
        else if (result.Count > 0)
        {
            // An alternate version is not part of the episode listing, so the result starts at its
            // primary episode instead. Keep playing the version the user picked by selecting it as
            // the media source of that primary (unless a source was explicitly chosen).
            options.StartIndex = 0;
            options.MediaSourceId ??= IdOf(firstItem);
        }
        else
        {
            options.StartIndex = null;
        }

        return result;
    }

    /// <summary>
    /// Port of getAdditionalParts() and the flattening in play(): multi-part movies and episodes
    /// are followed by their other parts.
    /// </summary>
    private async Task<List<BaseItemDto>> AddAdditionalPartsAsync(List<BaseItemDto> items, PlayRequest options)
    {
        var startIndex = options.StartIndex ?? 0;
        var groups = new List<List<BaseItemDto>>(items.Count);
        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var group = new List<BaseItemDto> { item };
            if (item.PartCount is > 1 && item.Type is BaseItemDto_Type.Movie or BaseItemDto_Type.Episode)
            {
                // The parts belong to the version being played: for the start item that is the
                // selected media source, when it is another version than the item itself.
                var itemId = IdOf(item);
                var idForParts = index == startIndex && options.MediaSourceId is { Length: > 0 } sourceId && NormalizeId(sourceId) != itemId
                    ? NormalizeId(sourceId)
                    : itemId;
                var parts = await GetAsync(
                    _api.GetUrl($"Videos/{idForParts}/AdditionalParts", ("userId", _api.UserIdText)),
                    BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
                group.AddRange(parts?.Items ?? []);
            }

            groups.Add(group);
        }

        if (options.StartIndex is > 0)
        {
            // Adjust the start index for additional parts added to the queue.
            options.StartIndex = groups.Take(options.StartIndex.Value).Sum(g => g.Count);
        }

        return groups.SelectMany(g => g).ToList();
    }

    /// <summary>Port of playWithIntros().</summary>
    private async Task PlayWithIntrosAsync(List<BaseItemDto> items, PlayRequest options)
    {
        var playStartIndex = options.StartIndex ?? 0;
        if (playStartIndex < 0 || playStartIndex >= items.Count)
        {
            // If the index was bad, reset it.
            playStartIndex = 0;
        }

        if (items.Count == 0)
        {
            // Nothing to play.
            _loading.Hide();
            ShowPlaybackInfoErrorMessage("PlaybackError." + MediaErrors.NoMediaError);
            return;
        }

        var firstItem = items[playStartIndex];

        // PORT-TODO(playback): photos and books go to their own players in the original (playOther());
        // those players belong to later workstreams, so such items end in "ErrorPlayerNotFound".
        var intros = await GetIntrosAsync(firstItem, options);
        var firstItemOptions = options.ToItemOptions();
        _itemOptions.AddOrUpdate(firstItem, firstItemOptions);
        var introOptions = intros.Count > 0 ? new ItemPlayOptions { Fullscreen = firstItemOptions.Fullscreen } : firstItemOptions;

        items = [.. intros, .. items];
        var startItem = items[playStartIndex];
        await PlayInternalAsync(
            startItem,
            introOptions,
            () =>
            {
                _playQueue.SetPlaylist(items);
                SetPlaylistState(startItem.PlaylistItemId, playStartIndex);
                _loading.Hide();
            },
            prevSource: null);
    }

    private async Task<List<BaseItemDto>> GetIntrosAsync(BaseItemDto firstItem, PlayRequest options)
    {
        if (options.StartPositionTicks is > 0
            || options.StartIndex is > 0
            || !options.Fullscreen
            || !EnableIntros(firstItem)
            || !_userSettings.EnableCinemaMode)
        {
            return [];
        }

        try
        {
            var result = await GetAsync(
                _api.GetUrl($"Users/{_api.UserIdText}/Items/{IdOf(firstItem)}/Intros"),
                BaseItemDtoQueryResult.CreateFromDiscriminatorValue);
            return result?.Items ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>The mutable form of <see cref="PlayOptions"/> the translation steps work on.</summary>
    internal sealed class PlayRequest
    {
        public PlayRequest(PlayOptions options)
        {
            Items = options.Items;
            Ids = options.Ids;
            StartPositionTicks = options.StartPositionTicks;
            StartIndex = options.StartIndex;
            MediaSourceId = options.MediaSourceId;
            AudioStreamIndex = options.AudioStreamIndex;
            SubtitleStreamIndex = options.SubtitleStreamIndex;
            Shuffle = options.Shuffle;
            Fullscreen = options.Fullscreen;
            QueryOptions = options.QueryOptions;
        }

        public IReadOnlyList<BaseItemDto>? Items { get; }

        public IReadOnlyList<string>? Ids { get; }

        public long? StartPositionTicks { get; }

        public int? StartIndex { get; set; }

        public string? MediaSourceId { get; set; }

        public int? AudioStreamIndex { get; }

        public int? SubtitleStreamIndex { get; }

        public bool Shuffle { get; }

        public bool Fullscreen { get; }

        public IReadOnlyDictionary<string, string>? QueryOptions { get; }

        /// <summary>Port of truncatePlayOptions().</summary>
        public ItemPlayOptions ToItemOptions() => new()
        {
            Fullscreen = Fullscreen,
            MediaSourceId = MediaSourceId,
            AudioStreamIndex = AudioStreamIndex,
            SubtitleStreamIndex = SubtitleStreamIndex,
            StartPositionTicks = StartPositionTicks,
        };
    }

    /// <summary>
    /// Parameters of an Items request in insertion order. Lists are comma-separated strings or
    /// string lists; both end up comma-joined in the URL.
    /// </summary>
    internal sealed class ItemsQuery : IEnumerable<KeyValuePair<string, object?>>
    {
        private readonly List<string> _keys = [];
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public ItemsQuery()
        {
        }

        /// <summary>Copies <paramref name="defaults"/> and lets <paramref name="overrides"/> replace them.</summary>
        public ItemsQuery(ItemsQuery overrides, ItemsQuery? defaults = null)
        {
            foreach (var (key, value) in defaults ?? [])
            {
                this[key] = value;
            }

            foreach (var (key, value) in overrides)
            {
                this[key] = value;
            }
        }

        public object? this[string key]
        {
            get => _values.GetValueOrDefault(key);
            set
            {
                if (!_values.ContainsKey(key))
                {
                    _keys.Add(key);
                }

                _values[key] = value;
            }
        }

        public void Add(string key, object? value) => this[key] = value;

        public void Remove(string key)
        {
            if (_values.Remove(key))
            {
                _keys.Remove(key);
            }
        }

        public IReadOnlyList<string>? List(string key) => this[key] switch
        {
            null => null,
            string text => text.Length == 0 ? [] : text.Split(','),
            IEnumerable<string> list => list.ToList(),
            _ => null,
        };

        public int? Int(string key) => this[key] switch
        {
            int number => number,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };

        public (string Key, object? Value)[] ToArray() => _keys.Select(key => (key, _values[key])).ToArray();

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
            _keys.Select(key => new KeyValuePair<string, object?>(key, _values[key])).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
