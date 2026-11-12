using System.Text.Json;

using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>A page of items with the number of items the query matches in all.</summary>
public sealed record DetailItems(IReadOnlyList<BaseItemDto> Items, int TotalRecordCount)
{
    public static readonly DetailItems Empty = new([], 0);
}

/// <summary>
/// The server requests of the details page, with the paths and query parameters of
/// <c>apps/legacy/controllers/itemDetails/index.js</c>, <c>scripts/itemsByName.js</c> and
/// <c>scripts/playlistViewer.js</c>.
/// </summary>
[AppService]
public sealed class DetailsQueries(JellyfinApi api, ItemQueries items)
{
    /// <summary>The fields the children of an item are asked for.</summary>
    public const string ChildFields = "ItemCounts,PrimaryImageAspectRatio,CanDelete,MediaSourceCount";

    /// <summary>
    /// The item the page is about (<c>getPromise</c>): by id, a series timer, or a genre, music
    /// genre or artist by name. Null when the address names none of them.
    /// </summary>
    public Task<BaseItemDto?> GetItemAsync(string? id, string? seriesTimerId = null, string? genre = null, string? musicGenre = null, string? musicArtist = null)
    {
        if (!string.IsNullOrEmpty(id))
        {
            return items.GetItemAsync(id);
        }

        if (!string.IsNullOrEmpty(seriesTimerId))
        {
            return items.GetSeriesTimerAsync(seriesTimerId);
        }

        if (!string.IsNullOrEmpty(genre))
        {
            return GetByNameAsync("Genres", genre);
        }

        if (!string.IsNullOrEmpty(musicGenre))
        {
            return GetByNameAsync("MusicGenres", musicGenre);
        }

        // The original asks /Persons for an artist given by name.
        return string.IsNullOrEmpty(musicArtist) ? Task.FromResult<BaseItemDto?>(null) : GetByNameAsync("Persons", musicArtist);
    }

    /// <summary>The seasons of a series.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSeasonsAsync(string seriesId) => items.GetItemsAsync(
        $"Shows/{seriesId}/Seasons",
        ("userId", api.UserIdText),
        ("Fields", ChildFields));

    /// <summary>The episodes of a season, with their overviews for the list.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSeasonEpisodesAsync(string seriesId, string seasonId) => items.GetItemsAsync(
        $"Shows/{seriesId}/Episodes",
        ("seasonId", seasonId),
        ("userId", api.UserIdText),
        ("Fields", ChildFields + ",Overview"));

    /// <summary>The episodes of the season an episode belongs to ("more from season").</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetMoreFromSeasonAsync(string seriesId, string seasonId) => items.GetItemsAsync(
        $"Shows/{seriesId}/Episodes",
        ("SeasonId", seasonId),
        ("UserId", api.UserIdText),
        ("Fields", ChildFields));

    /// <summary>
    /// The children of a folder (<c>renderChildren</c>): seasons, episodes, or the items below it
    /// in the order its type asks for.
    /// </summary>
    public Task<IReadOnlyList<BaseItemDto>> GetChildrenAsync(BaseItemDto item)
    {
        var type = item.TypeName();
        if (type == "Series")
        {
            return GetSeasonsAsync(item.IdText());
        }

        if (type == "Season" && item.SeriesId is not null)
        {
            return GetSeasonEpisodesAsync(item.SeriesId.IdText(), item.IdText());
        }

        return items.GetUserItemsAsync(
            ("ParentId", item.IdText()),
            ("Fields", ChildFields),
            ("SortBy", type switch
            {
                "MusicAlbum" => "ParentIndexNumber,IndexNumber,SortName",
                "BoxSet" => null,
                _ => "SortName"
            }));
    }

    /// <summary>The next episode to watch of a series.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetNextUpAsync(string seriesId) => items.GetItemsAsync(
        "Shows/NextUp",
        ("SeriesId", seriesId),
        ("UserId", api.UserIdText),
        ("Fields", "MediaSourceCount"));

    /// <summary>Items like this one ("more like this").</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSimilarAsync(BaseItemDto item) => items.GetItemsAsync(
        $"Items/{item.IdText()}/Similar",
        ("userId", api.UserIdText),
        ("limit", 12),
        ("fields", "PrimaryImageAspectRatio,CanDelete"),
        ("ExcludeArtistIds", item.TypeName() == "MusicAlbum" && item.AlbumArtists is { Count: > 0 } artists ? artists[0].Id.IdText() : null));

    /// <summary>The collections an item is part of; none when the server cannot say.</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetCollectionsAsync(string itemId)
    {
        try
        {
            return await items.GetItemsAsync(
                $"Items/{itemId}/Collections",
                ("userId", api.UserIdText),
                ("fields", "PrimaryImageAspectRatio"));
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            AppLog.Warn("Loading the collections of an item failed: " + exception.Message);
            return [];
        }
    }

    /// <summary>The special features of an item.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSpecialFeaturesAsync(string itemId) =>
        items.GetItemsAsync($"Items/{itemId}/SpecialFeatures", ("userId", api.UserIdText));

    /// <summary>The further parts of a video in several files.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetAdditionalPartsAsync(string itemId) =>
        items.GetItemsAsync($"Videos/{itemId}/AdditionalParts", ("userId", api.UserIdText));

    /// <summary>The music videos of an album.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetMusicVideosAsync(string albumId) => items.GetUserItemsAsync(
        ("SortBy", "SortName"),
        ("SortOrder", "Ascending"),
        ("IncludeItemTypes", "MusicVideo"),
        ("Recursive", true),
        ("Fields", "PrimaryImageAspectRatio,CanDelete,MediaSourceCount"),
        ("AlbumIds", albumId));

    /// <summary>
    /// Other albums of the artist of an album or song ("more from"), or the albums an artist
    /// contributes to ("appears on").
    /// </summary>
    public Task<IReadOnlyList<BaseItemDto>> GetMoreFromArtistAsync(BaseItemDto item)
    {
        var artist = item.TypeName() == "MusicArtist";
        return items.GetUserItemsAsync(
            ("IncludeItemTypes", "MusicAlbum"),
            ("Recursive", true),
            ("ExcludeItemIds", item.IdText()),
            ("SortBy", "PremiereDate,ProductionYear,SortName"),
            ("SortOrder", "Descending"),
            ("ContributingArtistIds", artist ? item.IdText() : null),
            ("AlbumArtistIds", artist ? null : string.Join(",", (item.AlbumArtists ?? []).Select(pair => pair.Id.IdText()))));
    }

    /// <summary>The lines of a song's lyrics; none when it has none.</summary>
    public async Task<IReadOnlyList<string>> GetLyricsAsync(string itemId)
    {
        try
        {
            using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"Audio/{itemId}/Lyrics"));
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("Lyrics", out var lyrics) || lyrics.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return lyrics.EnumerateArray()
                .Select(line => line.ValueKind == JsonValueKind.Object && line.TryGetProperty("Text", out var text) ? text.GetString() ?? string.Empty : string.Empty)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            AppLog.Warn("Loading lyrics failed: " + exception.Message);
            return [];
        }
    }

    /// <summary>The items of a playlist in its order.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetPlaylistItemsAsync(string playlistId) => items.GetItemsAsync(
        $"Playlists/{playlistId}/Items",
        ("Fields", "PrimaryImageAspectRatio,MediaSourceCount,Chapters,Trickplay"),
        ("EnableImageTypes", "Primary,Backdrop,Banner,Thumb"),
        ("UserId", api.UserIdText));

    /// <summary>Moves an entry of a playlist to another position (what dropping a dragged row does in the original).</summary>
    public async Task MovePlaylistItemAsync(string playlistId, string playlistItemId, int newIndex)
    {
        using var response = await api.SendAsync(
            HttpMethod.Post,
            api.GetUrl($"Playlists/{playlistId}/Items/{playlistItemId}/Move/{DetailsRules.Invariant(newIndex)}"));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Upcoming broadcasts of a series of the library.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSeriesScheduleAsync(string seriesId) => items.GetItemsAsync(
        "LiveTv/Programs",
        ("UserId", api.UserIdText),
        ("ImageTypeLimit", 1),
        ("HasAired", false),
        ("SortBy", "StartDate"),
        ("EnableTotalRecordCount", false),
        ("Limit", 50),
        ("EnableUserData", false),
        ("Fields", "ChannelInfo,ChannelImage"),
        ("LibrarySeriesId", seriesId));

    /// <summary>The channel a programme is on, which is what plays for a programme on air.</summary>
    public async Task<BaseItemDto?> GetChannelAsync(string channelId)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"LiveTv/Channels/{channelId}", ("userId", api.UserIdText)));
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), BaseItemDto.CreateFromDiscriminatorValue);
    }

    /// <summary>The folders above an item, nearest first.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetAncestorsAsync(string itemId) =>
        items.GetItemsAsync($"Items/{itemId}/Ancestors", ("userId", api.UserIdText));

    /// <summary>Splits the grouped versions of a video into separate items again.</summary>
    public async Task SplitVersionsAsync(string itemId)
    {
        using var response = await api.SendAsync(HttpMethod.Delete, api.GetUrl($"Videos/{itemId}/AlternateSources"));
        response.EnsureSuccessStatusCode();
    }

    /// <summary>The address of the file of an item, for the download button.</summary>
    public string GetDownloadUrl(string itemId) => api.GetUrl($"Items/{itemId}/Download", ("api_key", api.AccessToken));

    /// <summary>
    /// The items of one section of a person, genre, studio or artist page, with the total the
    /// query matches (<c>itemsByName.js</c>: <c>loadItems</c>).
    /// </summary>
    public async Task<DetailItems> GetItemsByNameAsync(BaseItemDto item, ItemsByNameSection section, bool displayMissingEpisodes)
    {
        var query = section.GetQuery(item, displayMissingEpisodes);
        var path = section.Type == "MusicArtist" ? "Persons" : "Items";
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl(path, [("userId", api.UserIdText), .. query]));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        var list = ItemQueries.ParseItems(json);
        var total = list.Count;
        if (!string.IsNullOrWhiteSpace(json))
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("TotalRecordCount", out var count) && count.TryGetInt32(out var value))
            {
                total = value;
            }
        }

        return new DetailItems(list, total);
    }

    private async Task<BaseItemDto?> GetByNameAsync(string path, string name)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"{path}/{Uri.EscapeDataString(name)}", ("userId", api.UserIdText)));
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), BaseItemDto.CreateFromDiscriminatorValue);
    }
}
