using System.Globalization;
using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// The item requests of the shared components and the home screen, sent with the same paths and
/// query parameters as the original client. Lists are returned as plain item lists whether the
/// server answers with <c>{ Items: [...] }</c> or a bare array.
/// </summary>
[AppService]
public sealed class ItemQueries(JellyfinApi api)
{
    /// <summary>GETs a list of items from <paramref name="path"/> (relative to the server root).</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetItemsAsync(string path, params (string Key, object? Value)[] query)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl(path, query));
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseItems(json);
    }

    /// <summary>The items of a response body: <c>{ Items: [...] }</c> or <c>[...]</c>.</summary>
    public static IReadOnlyList<BaseItemDto> ParseItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Items", out var items))
        {
            root = items;
        }

        return KiotaJson.ParseList(root, BaseItemDto.CreateFromDiscriminatorValue);
    }

    /// <summary><c>GET /Items</c> for the current user (<c>apiClient.getItems(userId, options)</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetUserItemsAsync(params (string Key, object? Value)[] query) =>
        GetItemsAsync("Items", [("userId", api.UserIdText), .. query]);

    /// <summary>One item with the current user's data (<c>GET /Items/{id}?userId=</c>).</summary>
    public async Task<BaseItemDto?> GetItemAsync(string itemId)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"Items/{itemId}", ("userId", api.UserIdText)));
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), BaseItemDto.CreateFromDiscriminatorValue);
    }

    /// <summary>A Live TV timer as an item of type "Timer" (<c>GET /LiveTv/Timers/{id}</c>).</summary>
    public async Task<BaseItemDto?> GetTimerAsync(string timerId)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"LiveTv/Timers/{timerId}"));
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), TimerInfoDto.CreateFromDiscriminatorValue)?.ToItem();
    }

    /// <summary>A series timer as an item of type "SeriesTimer" (<c>GET /LiveTv/SeriesTimers/{id}</c>).</summary>
    public async Task<BaseItemDto?> GetSeriesTimerAsync(string seriesTimerId)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"LiveTv/SeriesTimers/{seriesTimerId}"));
        response.EnsureSuccessStatusCode();
        return KiotaJson.Parse(await response.Content.ReadAsStringAsync(), SeriesTimerInfoDto.CreateFromDiscriminatorValue)?.ToItem();
    }

    /// <summary>
    /// The full item behind a card or list row, as the context menu needs it: timers come from the
    /// Live TV endpoints, everything else from <c>/Items/{id}</c>.
    /// </summary>
    public Task<BaseItemDto?> GetFullItemAsync(BaseItemDto item) => item.TypeName() switch
    {
        "Timer" => GetTimerAsync(item.ActionId()),
        "SeriesTimer" => GetSeriesTimerAsync(item.ActionId()),
        _ => GetItemAsync(item.IdText())
    };

    /// <summary>Whether the current user may edit a playlist; false when the server refuses to say.</summary>
    public async Task<bool> CanEditPlaylistAsync(string playlistId)
    {
        try
        {
            using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl($"Playlists/{playlistId}/Users/{api.UserIdText}"));
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("CanEdit", out var canEdit)
                   && canEdit.ValueKind == JsonValueKind.True;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Items in progress of one media type (<c>GET /UserItems/Resume</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetResumeItemsAsync(string mediaType, int limit = 12) => GetItemsAsync(
        "UserItems/Resume",
        ("userId", api.UserIdText),
        ("limit", limit),
        ("fields", "PrimaryImageAspectRatio"),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Backdrop,Thumb"),
        ("enableTotalRecordCount", false),
        ("mediaTypes", mediaType));

    /// <summary>The next episode of each series being watched (<c>GET /Shows/NextUp</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetNextUpAsync(DateTimeOffset? dateCutoff, bool enableRewatching, int limit = 24) => GetItemsAsync(
        "Shows/NextUp",
        ("userId", api.UserIdText),
        ("limit", limit),
        ("fields", "PrimaryImageAspectRatio,DateCreated,Path,MediaSourceCount"),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Backdrop,Thumb"),
        ("enableTotalRecordCount", false),
        ("nextUpDateCutoff", dateCutoff?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)),
        ("enableResumable", false),
        ("enableRewatching", enableRewatching));

    /// <summary>The newest items of a library (<c>GET /Items/Latest</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetLatestItemsAsync(string parentId, int limit = 16) => GetItemsAsync(
        "Items/Latest",
        ("userId", api.UserIdText),
        ("limit", limit),
        ("fields", "PrimaryImageAspectRatio,Path"),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Backdrop,Thumb"),
        ("parentId", parentId));

    /// <summary>Programmes on air now (<c>GET /LiveTv/Programs/Recommended</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetOnNowProgramsAsync(int limit) => GetItemsAsync(
        "LiveTv/Programs/Recommended",
        ("userId", api.UserIdText),
        ("isAiring", true),
        ("limit", limit),
        ("imageTypeLimit", 1),
        ("enableImageTypes", "Primary,Thumb,Backdrop"),
        ("enableTotalRecordCount", false),
        ("fields", "ChannelInfo,PrimaryImageAspectRatio"));

    /// <summary>Recordings in progress (<c>GET /LiveTv/Recordings?isInProgress=true</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetActiveRecordingsAsync(int limit = 12) => GetItemsAsync(
        "LiveTv/Recordings",
        ("userId", api.UserIdText),
        ("limit", limit),
        ("fields", "PrimaryImageAspectRatio"),
        ("enableTotalRecordCount", false),
        ("isInProgress", true));
}
