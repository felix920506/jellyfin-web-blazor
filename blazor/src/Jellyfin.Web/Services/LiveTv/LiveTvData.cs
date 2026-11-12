using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>The dates the server has guide data for (<c>GET /LiveTv/GuideInfo</c>).</summary>
public sealed record GuideInfo(DateTimeOffset StartDate, DateTimeOffset EndDate);

/// <summary>One page of guide channels and how many channels there are in all.</summary>
public sealed record GuideChannels(IReadOnlyList<BaseItemDto> Items, int TotalRecordCount);

/// <summary>
/// The Live TV requests of the user's pages, sent with the paths and parameters of the original
/// client (<c>apiClient.getLiveTv*</c>, <c>hooks/api/useSections.ts</c>, <c>useTimers.ts</c>).
/// Timers come back as items of type "Timer" / "SeriesTimer" (see <see cref="ItemExtensions.ToItem(TimerInfoDto)"/>);
/// the editors read and write the server's own JSON so nothing they do not show is lost.
/// </summary>
[AppService]
public sealed class LiveTvData(JellyfinApi api, IBrowser browser)
{
    /// <summary>
    /// The URL of an image no taller than <paramref name="maxHeight"/> CSS pixels
    /// (<c>apiClient.getScaledImageUrl</c> with <c>maxHeight</c>).
    /// </summary>
    public string GetImageUrl(string itemId, string imageType, string? tag, int maxHeight) => api.GetUrl(
        $"Items/{itemId}/Images/{imageType}",
        ("maxHeight", (int)Math.Round(maxHeight * browser.Environment.DevicePixelRatio, MidpointRounding.AwayFromZero)),
        ("tag", tag),
        ("quality", 90));

    /// <summary>Above this length the channel ids of a programme query travel in a POST body (<c>getLiveTvPrograms</c>).</summary>
    public const int MaxChannelIdsInQuery = 1800;

    /// <summary>The items of one section row.</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetSectionItemsAsync(LiveTvSection section)
    {
        (string Key, object? Value)[] query = section.Path switch
        {
            LiveTvSections.RecommendedPrograms =>
            [
                ("userId", api.UserIdText),
                ("limit", 12),
                ("imageTypeLimit", 1),
                ("enableImageTypes", "Primary,Thumb,Backdrop"),
                ("enableTotalRecordCount", false),
                ("fields", "ChannelInfo,PrimaryImageAspectRatio,MediaSourceCount"),
                .. section.Query
            ],
            LiveTvSections.Programs =>
            [
                ("userId", api.UserIdText),
                ("limit", 12),
                ("imageTypeLimit", 1),
                ("enableImageTypes", "Primary,Thumb,Backdrop"),
                ("enableTotalRecordCount", false),
                ("fields", "ChannelInfo,PrimaryImageAspectRatio"),
                .. section.Query
            ],
            LiveTvSections.Recordings =>
            [
                ("userId", api.UserIdText),
                ("enableImageTypes", "Primary,Thumb,Backdrop"),
                ("enableTotalRecordCount", false),
                ("fields", "CanDelete,PrimaryImageAspectRatio"),
                .. section.Query
            ],
            _ => [("userId", api.UserIdText), .. section.Query]
        };
        return GetItemsAsync(section.Path, query);
    }

    /// <summary>GETs a list of items (<c>{ Items: [...] }</c> or a bare array).</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetItemsAsync(string path, params (string Key, object? Value)[] query) =>
        ItemQueries.ParseItems(await GetTextAsync(path, query));

    /// <summary>Timers as items of type "Timer" (<c>GET /LiveTv/Timers</c>).</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetTimersAsync(params (string Key, object? Value)[] query) =>
        ParseTimers(await GetTextAsync("LiveTv/Timers", query));

    /// <summary>The recordings that are scheduled and not running (<c>useGetTimers</c>: <c>isActive: false, isScheduled: true</c>).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetUpcomingRecordingsAsync() =>
        GetTimersAsync(("isActive", false), ("isScheduled", true));

    /// <summary>The series timers by name, as items of type "SeriesTimer" (<c>getSeriesTimers</c>).</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetSeriesTimersAsync() =>
        ParseSeriesTimers(await GetTextAsync("LiveTv/SeriesTimers", ("sortBy", "SortName"), ("sortOrder", "Ascending")));

    /// <summary>The recordings a series timer has scheduled (the schedule section of its details page).</summary>
    public async Task<IReadOnlyList<BaseItemDto>> GetSeriesTimerScheduleAsync(string seriesTimerId)
    {
        var timers = await GetTimersAsync(
            ("UserId", api.UserIdText),
            ("ImageTypeLimit", 1),
            ("SortBy", "StartDate"),
            ("EnableTotalRecordCount", false),
            ("EnableUserData", false),
            ("SeriesTimerId", seriesTimerId),
            ("Fields", "ChannelInfo,ChannelImage"));

        // The server ignores the filter when the timer is unknown; the original then shows nothing.
        return timers.Count > 0 && timers[0].SeriesTimerId != seriesTimerId ? [] : timers;
    }

    /// <summary>What a channel will show from now on (the guide section of its details page).</summary>
    public Task<IReadOnlyList<BaseItemDto>> GetChannelProgramsAsync(string channelId) => GetItemsAsync(
        LiveTvSections.Programs,
        ("ChannelIds", channelId),
        ("UserId", api.UserIdText),
        ("HasAired", false),
        ("SortBy", "StartDate"),
        ("EnableTotalRecordCount", false),
        ("EnableImages", false),
        ("ImageTypeLimit", 0),
        ("EnableUserData", false));

    /// <summary>A programme with the user's data and its timers (<c>getLiveTvProgram</c>).</summary>
    public async Task<BaseItemDto?> GetProgramAsync(string programId) => KiotaJson.Parse(
        await GetTextAsync($"LiveTv/Programs/{programId}", ("userId", api.UserIdText)),
        BaseItemDto.CreateFromDiscriminatorValue);

    /// <summary>The span of the guide data (<c>getLiveTvGuideInfo</c>); null when the server has none.</summary>
    public async Task<GuideInfo?> GetGuideInfoAsync()
    {
        var json = await GetTextAsync("LiveTv/GuideInfo");
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        return GuideProgram.Date(document.RootElement, "StartDate") is { } start && GuideProgram.Date(document.RootElement, "EndDate") is { } end
            ? new GuideInfo(start, end)
            : null;
    }

    /// <summary>One page of the channels of the guide (<c>getLiveTvChannels</c> as <c>guide.js</c> asks).</summary>
    public async Task<GuideChannels> GetGuideChannelsAsync(GuideChannelQuery query)
    {
        var json = await GetTextAsync(
            "LiveTv/Channels",
            ("StartIndex", query.StartIndex),
            ("EnableFavoriteSorting", query.EnableFavoriteSorting),
            ("UserId", api.UserIdText),
            ("Limit", query.Limit),
            ("AddCurrentProgram", false),
            ("EnableUserData", false),
            ("EnableImageTypes", "Primary"),
            ("IsNews", query.IsNews),
            ("IsSports", query.IsSports),
            ("IsKids", query.IsKids),
            ("IsMovie", query.IsMovie),
            ("IsSeries", query.IsSeries),
            ("SortBy", query.SortBy),
            ("SortOrder", query.SortOrder));
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

        return new GuideChannels(items, total);
    }

    /// <summary>
    /// The programmes of the given channels that run between two moments, by start
    /// (<c>getLiveTvPrograms</c> as <c>guide.js</c> asks; a POST when the channel ids are too long for a URL).
    /// </summary>
    public async Task<IReadOnlyList<GuideProgram>> GetGuideProgramsAsync(
        IReadOnlyList<string> channelIds,
        DateTimeOffset minEndDate,
        DateTimeOffset maxStartDate,
        bool withHd)
    {
        var ids = string.Join(',', channelIds);
        var query = new List<(string Key, object? Value)>
        {
            ("UserId", api.UserIdText),
            ("MaxStartDate", ToIso(maxStartDate)),
            ("MinEndDate", ToIso(minEndDate)),
            ("channelIds", ids),
            ("ImageTypeLimit", 1),
            ("EnableImages", false),
            ("SortBy", "StartDate"),
            ("EnableTotalRecordCount", false),
            ("EnableUserData", false),
            ("Fields", withHd ? "IsHD" : null)
        };

        string json;
        if (ids.Length > MaxChannelIdsInQuery)
        {
            var body = new JsonObject();
            foreach (var (key, value) in query)
            {
                if (value is not null)
                {
                    body[key] = JsonSerializer.SerializeToNode(value, value.GetType());
                }
            }

            using var response = await api.SendAsync(HttpMethod.Post, api.GetUrl(LiveTvSections.Programs), body);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync();
        }
        else
        {
            json = await GetTextAsync(LiveTvSections.Programs, [.. query]);
        }

        return GuideProgram.ParseList(json);
    }

    /// <summary>The server's JSON of a timer (<c>getLiveTvTimer</c>); null when it is gone.</summary>
    public Task<JsonObject?> GetTimerAsync(string timerId) => GetObjectAsync($"LiveTv/Timers/{timerId}");

    /// <summary>Saves a timer (<c>updateLiveTvTimer</c>).</summary>
    public Task UpdateTimerAsync(JsonObject timer) => PostAsync($"LiveTv/Timers/{timer.Text("Id")}", timer);

    /// <summary>The server's JSON of a series timer (<c>getLiveTvSeriesTimer</c>); null when it is gone.</summary>
    public Task<JsonObject?> GetSeriesTimerAsync(string seriesTimerId) => GetObjectAsync($"LiveTv/SeriesTimers/{seriesTimerId}");

    /// <summary>Saves a series timer (<c>updateLiveTvSeriesTimer</c>).</summary>
    public Task UpdateSeriesTimerAsync(JsonObject timer) => PostAsync($"LiveTv/SeriesTimers/{timer.Text("Id")}", timer);

    /// <summary>The timer the server would create for a programme (<c>getNewLiveTvTimerDefaults</c>).</summary>
    public Task<JsonObject?> GetTimerDefaultsAsync(string programId) => GetObjectAsync("LiveTv/Timers/Defaults", ("programId", programId));

    /// <summary>Schedules a recording (<c>createLiveTvTimer</c>).</summary>
    public Task CreateTimerAsync(JsonObject timer) => PostAsync("LiveTv/Timers", timer);

    /// <summary>Schedules a series recording (<c>createLiveTvSeriesTimer</c>).</summary>
    public Task CreateSeriesTimerAsync(JsonObject timer) => PostAsync("LiveTv/SeriesTimers", timer);

    /// <summary>Cancels a recording (<c>cancelLiveTvTimer</c>).</summary>
    public Task CancelTimerAsync(string timerId) => DeleteAsync($"LiveTv/Timers/{timerId}");

    /// <summary>Cancels a series recording (<c>cancelLiveTvSeriesTimer</c>).</summary>
    public Task CancelSeriesTimerAsync(string seriesTimerId) => DeleteAsync($"LiveTv/SeriesTimers/{seriesTimerId}");

    /// <summary>The timers of a response body as items of type "Timer".</summary>
    public static IReadOnlyList<BaseItemDto> ParseTimers(string json) =>
        [.. ParseList(json, TimerInfoDto.CreateFromDiscriminatorValue).Select(timer => timer.ToItem())];

    /// <summary>The series timers of a response body as items of type "SeriesTimer".</summary>
    public static IReadOnlyList<BaseItemDto> ParseSeriesTimers(string json) =>
        [.. ParseList(json, SeriesTimerInfoDto.CreateFromDiscriminatorValue).Select(timer => timer.ToItem())];

    /// <summary>A moment the way JavaScript's <c>toISOString</c> writes it.</summary>
    public static string ToIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static List<T> ParseList<T>(string json, Microsoft.Kiota.Abstractions.Serialization.ParsableFactory<T> factory)
        where T : Microsoft.Kiota.Abstractions.Serialization.IParsable
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

        return root.ValueKind == JsonValueKind.Array ? KiotaJson.ParseList(root, factory) : [];
    }

    private async Task<string> GetTextAsync(string path, params (string Key, object? Value)[] query)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl(path, query));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<JsonObject?> GetObjectAsync(string path, params (string Key, object? Value)[] query)
    {
        var json = await GetTextAsync(path, query);
        return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json) as JsonObject;
    }

    private async Task PostAsync(string path, JsonObject body)
    {
        using var response = await api.SendAsync(HttpMethod.Post, api.GetUrl(path), body);
        response.EnsureSuccessStatusCode();
    }

    private async Task DeleteAsync(string path)
    {
        using var response = await api.SendAsync(HttpMethod.Delete, api.GetUrl(path));
        response.EnsureSuccessStatusCode();
    }
}
