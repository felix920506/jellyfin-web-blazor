using System.Globalization;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services;

/// <summary>
/// The server requests of the Live TV administration pages (tuners, guide providers, channel
/// mapping, DVR settings). Like the original's legacy pages they read and write the server's JSON
/// as it is, so that what is posted back has the members the server sent.
/// </summary>
[AppService]
public sealed class LiveTvAdminApi(ItemDialogApi api)
{
    private const string Configuration = "System/Configuration/livetv";

    /// <summary>Whether a failed request or an unreadable answer caused the exception.</summary>
    public static bool IsFailure(Exception exception) => ItemDialogApi.IsFailure(exception);

    /// <summary>The Live TV options (<c>getNamedConfiguration('livetv')</c>).</summary>
    public async Task<JsonObject> GetConfigurationAsync() => await api.GetObjectAsync(Configuration) ?? new JsonObject();

    /// <summary>Saves the Live TV options (<c>updateNamedConfiguration('livetv')</c>).</summary>
    public Task SaveConfigurationAsync(JsonObject configuration) => api.PostAsync(api.GetUrl(Configuration), configuration);

    /// <summary>The tuner types the server knows (<c>Id</c>, <c>Name</c>).</summary>
    public Task<JsonArray> GetTunerTypesAsync() => api.GetArrayAsync("LiveTv/TunerHosts/Types");

    /// <summary>Tuners found on the network that are not set up yet.</summary>
    public Task<JsonArray> DiscoverTunersAsync() => api.GetArrayAsync("LiveTv/Tuners/Discover", ("NewDevicesOnly", "true"));

    /// <summary>Adds or updates a tuner.</summary>
    public Task SaveTunerAsync(JsonObject info) => api.PostAsync(api.GetUrl("LiveTv/TunerHosts"), info);

    /// <summary>Removes a tuner.</summary>
    public Task DeleteTunerAsync(string id) => api.DeleteAsync(api.GetUrl("LiveTv/TunerHosts", ("id", id)));

    /// <summary>The values of a new guide provider.</summary>
    public async Task<JsonObject> GetDefaultProviderAsync() => await api.GetObjectAsync("LiveTv/ListingProviders/Default") ?? new JsonObject();

    /// <summary>Adds or updates a guide provider, checking its sign-in or its listings; returns the saved provider.</summary>
    public async Task<JsonObject?> SaveProviderAsync(JsonObject info, bool validateLogin = false, bool validateListings = false) =>
        await api.PostAsync(
            api.GetUrl(
                "LiveTv/ListingProviders",
                ("ValidateLogin", validateLogin ? "true" : null),
                ("ValidateListings", validateListings ? "true" : null)),
            info) as JsonObject;

    /// <summary>Removes a guide provider.</summary>
    public Task DeleteProviderAsync(string id) => api.DeleteAsync(api.GetUrl("LiveTv/ListingProviders", ("id", id)));

    /// <summary>The countries of Schedules Direct by region (<c>fullName</c>, <c>shortName</c>).</summary>
    public async Task<JsonObject> GetCountriesAsync() =>
        await api.GetObjectAsync("LiveTv/ListingProviders/SchedulesDirect/Countries") ?? new JsonObject();

    /// <summary>The lineups of a provider at a location (<c>Id</c>, <c>Name</c>).</summary>
    public Task<JsonArray> GetLineupsAsync(string? providerId, string location, string? country) =>
        api.GetArrayAsync("LiveTv/ListingProviders/Lineups", ("Id", providerId), ("Location", location), ("Country", country));

    /// <summary>The tuner channels and the provider channels they can be mapped to.</summary>
    public async Task<JsonObject> GetChannelMappingOptionsAsync(string? providerId) =>
        await api.GetObjectAsync("LiveTv/ChannelMappingOptions", ("providerId", providerId)) ?? new JsonObject();

    /// <summary>Maps a tuner channel to a channel of the provider; returns the mapping.</summary>
    public async Task<JsonObject?> SetChannelMappingAsync(string? providerId, string? tunerChannelId, string? providerChannelId) =>
        await api.PostAsync(
            api.GetUrl("LiveTv/ChannelMappings"),
            new JsonObject
            {
                ["providerId"] = providerId,
                ["tunerChannelId"] = tunerChannelId,
                ["providerChannelId"] = providerChannelId,
            }) as JsonObject;
}

/// <summary>
/// Names and links of tuners and guide providers. The original has one name table per place
/// (they differ in spelling); each is kept.
/// </summary>
public static class LiveTvAdmin
{
    /// <summary>The route of the Live TV status page.</summary>
    public const string IndexRoute = "/dashboard/livetv";

    /// <summary>A tuner type on the status page. Port of features/livetv/utils/getTunerName.ts.</summary>
    public static string TunerName(string? type) => Lower(type) switch
    {
        "m3u" => "M3U",
        "hdhomerun" => "HDHomeRun",
        "hauppauge" => "Hauppauge",
        "satip" => "DVB",
        _ => "Unknown"
    };

    /// <summary>A tuner type in the tuner picker (components/tunerPicker.js).</summary>
    public static string PickerTunerName(string? type) => Lower(type) switch
    {
        "m3u" => "M3U",
        "hdhomerun" => "HDHomerun",
        "hauppauge" => "Hauppauge",
        "satip" => "DVB",
        _ => "Unknown"
    };

    /// <summary>A tuner type in the tuner list of a guide provider form (components/tvproviders).</summary>
    public static string ProviderFormTunerName(string? type) => Lower(type) switch
    {
        "m3u" => "M3U Playlist",
        "hdhomerun" => "HDHomerun",
        "satip" => "DVB",
        _ => "Unknown"
    };

    /// <summary>A guide provider type. Port of features/livetv/utils/getProviderName.ts.</summary>
    public static string ProviderName(string? type) => Lower(type) switch
    {
        "schedulesdirect" => "Schedules Direct",
        "xmltv" => "XMLTV",
        _ => "Unknown"
    };

    /// <summary>
    /// The route of the form of a provider type, without the provider's id; null for a type without
    /// a form. Port of features/livetv/utils/getProviderConfigurationUrl.ts.
    /// </summary>
    public static string? ProviderRoute(string? type) => Lower(type) switch
    {
        "xmltv" => "/dashboard/livetv/guide?type=xmltv",
        "schedulesdirect" => "/dashboard/livetv/guide?type=schedulesdirect",
        _ => null
    };

    /// <summary>The route of the form of an existing provider.</summary>
    public static string? ProviderRoute(string? type, string? id) =>
        ProviderRoute(type) is { } route ? route + "&id=" + Uri.EscapeDataString(id ?? string.Empty) : null;

    /// <summary>The route of the form of a tuner; of a new tuner without an id.</summary>
    public static string TunerRoute(string? id = null) =>
        string.IsNullOrEmpty(id) ? "/dashboard/livetv/tuner" : "/dashboard/livetv/tuner?id=" + Uri.EscapeDataString(id);

    /// <summary>The provider with the id in the Live TV options; null when there is none.</summary>
    public static JsonObject? FindProvider(JsonNode? configuration, string? id) => Find(configuration, "ListingProviders", id);

    /// <summary>The tuner with the id in the Live TV options; null when there is none.</summary>
    public static JsonObject? FindTuner(JsonNode? configuration, string? id) => Find(configuration, "TunerHosts", id);

    /// <summary>The text of a padding field of the DVR settings: the seconds as minutes, empty for none.</summary>
    public static string PaddingMinutes(JsonNode? configuration, string name) =>
        configuration.Number(name) is { } seconds && seconds != 0 && !double.IsNaN(seconds)
            ? (seconds / 60).ToString("R", CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// The seconds a padding field of the DVR settings stands for: <c>parseInt(text, 10) * 60</c>,
    /// with null (as JSON has it) where that is not a number.
    /// </summary>
    public static JsonNode? PaddingSeconds(string? minutes)
    {
        var text = (minutes ?? string.Empty).TrimStart();
        var length = 0;
        if (length < text.Length && text[length] is '+' or '-')
        {
            length++;
        }

        var digits = length;
        while (length < text.Length && char.IsAsciiDigit(text[length]))
        {
            length++;
        }

        return length > digits && long.TryParse(text.AsSpan(0, length), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            ? JsonValue.Create(value * 60)
            : null;
    }

    private static JsonObject? Find(JsonNode? configuration, string list, string? id) =>
        string.IsNullOrEmpty(id)
            ? null
            : configuration.List(list).OfType<JsonObject>().FirstOrDefault(entry => entry.Text("Id") == id);

    private static string Lower(string? type) => (type ?? string.Empty).ToLowerInvariant();
}

/// <summary>
/// The state of the tuner form: which fields a tuner type has, how a tuner fills the form and what
/// the form sends. Port of the logic of apps/dashboard/controllers/livetvtuner.js.
/// </summary>
public sealed class LiveTvTunerForm
{
    /// <summary>The value of the "Other" entry of the type list, which cannot be saved.</summary>
    public const string OtherType = "other";

    /// <summary>The chosen type; empty until one is chosen.</summary>
    public string Type { get; set; } = string.Empty;

    public string? FriendlyName { get; set; }

    public string? Url { get; set; }

    public string? UserAgent { get; set; }

    public string? DeviceId { get; set; }

    public string? TunerCount { get; set; } = "0";

    /// <summary>The fallback bitrate in Mbps, as typed.</summary>
    public string? FallbackMaxStreamingBitrate { get; set; } = "30";

    public bool ImportFavoritesOnly { get; set; }

    public bool AllowHWTranscoding { get; set; }

    public bool AllowFmp4TranscodingContainer { get; set; }

    public bool AllowStreamSharing { get; set; } = true;

    public bool EnableStreamLooping { get; set; }

    public bool IgnoreDts { get; set; } = true;

    public bool ReadAtNativeFramerate { get; set; } = true;

    private bool IsHdHomeRun => Type == "hdhomerun";

    private bool IsM3u => Type == "m3u";

    /// <summary>The path field is shown: the tuner's address (HDHomeRun) or a file or URL (M3U).</summary>
    public bool ShowsPath => IsHdHomeRun || IsM3u;

    /// <summary>The translation key of the label of the path field.</summary>
    public string PathLabel => IsHdHomeRun ? "LabelTunerIpAddress" : "LabelFileOrUrl";

    /// <summary>The path can be picked on the server, and is required.</summary>
    public bool SupportsSelectablePath => IsM3u;

    public bool SupportsUserAgent => IsM3u;

    public bool SupportsFavorites => IsHdHomeRun;

    public bool SupportsTranscoding => IsHdHomeRun;

    public bool SupportsFmp4Container => IsM3u;

    public bool SupportsStreamSharing => IsM3u;

    public bool SupportsFallbackBitrate => IsM3u || IsHdHomeRun;

    public bool SupportsStreamLooping => IsM3u;

    public bool SupportsIgnoreDts => IsM3u;

    public bool SupportsReadInputAtNativeFramerate => IsM3u;

    /// <summary>The tuner count is shown, and required.</summary>
    public bool SupportsTunerCount => IsM3u;

    public bool MayIncludeUnsupportedDrmChannels => IsHdHomeRun;

    public bool SupportsSubmit => Type != OtherType;

    /// <summary>Types that are M3U tuners of a named source.</summary>
    public static bool IsM3uVariant(string? type) => type == "nextpvr";

    /// <summary>Shows a saved or detected tuner in the form (<c>fillTunerHostInfo</c>).</summary>
    public void Fill(JsonNode? info)
    {
        if (info is not JsonObject)
        {
            return;
        }

        var type = info.Text("Type") ?? string.Empty;
        if (info.Text("Source") is { Length: > 0 } source && IsM3uVariant(source))
        {
            type = source;
        }

        Type = type;
        Url = info.Text("Url") ?? string.Empty;
        FriendlyName = info.Text("FriendlyName") ?? string.Empty;
        UserAgent = info.Text("UserAgent") ?? string.Empty;
        DeviceId = info.Text("DeviceId") ?? string.Empty;
        ImportFavoritesOnly = info.Flag("ImportFavoritesOnly");
        AllowHWTranscoding = info.Flag("AllowHWTranscoding");
        EnableStreamLooping = info.Flag("EnableStreamLooping");
        AllowFmp4TranscodingContainer = info.Flag("AllowFmp4TranscodingContainer");
        AllowStreamSharing = info.Flag("AllowStreamSharing");
        IgnoreDts = info.Flag("IgnoreDts");
        ReadAtNativeFramerate = info.Flag("ReadAtNativeFramerate");

        // info.FallbackMaxStreamingBitrate / 1e6 || '30'
        var bitrate = (info.Number("FallbackMaxStreamingBitrate") ?? double.NaN) / 1e6;
        FallbackMaxStreamingBitrate = double.IsNaN(bitrate) || bitrate == 0 ? "30" : bitrate.ToString(CultureInfo.InvariantCulture);

        // info.TunerCount || '0'
        TunerCount = info.Number("TunerCount") is { } count && count != 0 ? count.ToString(CultureInfo.InvariantCulture) : "0";
    }

    /// <summary>What the form posts (<c>submitForm</c>); <paramref name="id"/> is the edited tuner's id.</summary>
    public JsonObject ToInfo(string? id)
    {
        var info = new JsonObject
        {
            ["Type"] = Type,
            ["Url"] = NullIfEmpty(Url),
            ["UserAgent"] = NullIfEmpty(UserAgent),
            ["FriendlyName"] = NullIfEmpty(FriendlyName),
            ["DeviceId"] = NullIfEmpty(DeviceId),
            // The original sends the text of the field; the server reads numbers from strings.
            ["TunerCount"] = string.IsNullOrEmpty(TunerCount) ? JsonValue.Create(0) : JsonValue.Create(TunerCount),
            ["FallbackMaxStreamingBitrate"] = BitrateOf(FallbackMaxStreamingBitrate),
            ["ImportFavoritesOnly"] = ImportFavoritesOnly,
            ["AllowHWTranscoding"] = AllowHWTranscoding,
            ["AllowFmp4TranscodingContainer"] = AllowFmp4TranscodingContainer,
            ["AllowStreamSharing"] = AllowStreamSharing,
            ["EnableStreamLooping"] = EnableStreamLooping,
            ["IgnoreDts"] = IgnoreDts,
            ["ReadAtNativeFramerate"] = ReadAtNativeFramerate,
        };

        if (IsM3uVariant(Type))
        {
            info["Source"] = Type;
            info["Type"] = "m3u";
        }

        if (!string.IsNullOrEmpty(id))
        {
            info["Id"] = id;
        }

        return info;
    }

    // parseInt(1e6 * parseFloat(value || '30'), 10); null stands for NaN, as in JSON.
    private static JsonNode? BitrateOf(string? text)
    {
        var value = string.IsNullOrEmpty(text) ? "30" : text.Trim();
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var megabits)
               && double.IsFinite(megabits)
               && Math.Abs(megabits) < 9e12
            ? JsonValue.Create((long)Math.Truncate(megabits * 1e6))
            : null;
    }

    private static JsonNode? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);
}
