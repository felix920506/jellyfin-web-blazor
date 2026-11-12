using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>The filter values of a library view (<c>Filters</c> of <c>types/library.ts</c>); null means "not set".</summary>
public sealed record LibraryFilters
{
    public IReadOnlyList<string>? Features { get; init; }
    public IReadOnlyList<string>? Genres { get; init; }
    public IReadOnlyList<string>? OfficialRatings { get; init; }
    public IReadOnlyList<string>? EpisodeFilter { get; init; }
    public IReadOnlyList<string>? Status { get; init; }
    public IReadOnlyList<string>? SeriesStatus { get; init; }
    public IReadOnlyList<string>? StudioIds { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
    public IReadOnlyList<string>? PersonKinds { get; init; }
    public IReadOnlyList<string>? VideoBasicFilter { get; init; }
    public IReadOnlyList<string>? VideoTypes { get; init; }
    public IReadOnlyList<int>? Years { get; init; }
    public IReadOnlyList<string>? AudioLanguages { get; init; }
    public IReadOnlyList<string>? SubtitleLanguages { get; init; }

    /// <summary>True when any group has a value (<c>hasFilterOptions</c>: an emptied group is stored as undefined).</summary>
    [JsonIgnore]
    public bool HasAny =>
        Features is not null || Genres is not null || OfficialRatings is not null || EpisodeFilter is not null || Status is not null
        || SeriesStatus is not null || StudioIds is not null || Tags is not null || PersonKinds is not null || VideoBasicFilter is not null
        || VideoTypes is not null || Years is not null || AudioLanguages is not null || SubtitleLanguages is not null;

    /// <summary>The values of a group by its property name.</summary>
    public IReadOnlyList<string> Get(string group) => group switch
    {
        nameof(Features) => Features,
        nameof(Genres) => Genres,
        nameof(OfficialRatings) => OfficialRatings,
        nameof(EpisodeFilter) => EpisodeFilter,
        nameof(Status) => Status,
        nameof(SeriesStatus) => SeriesStatus,
        nameof(StudioIds) => StudioIds,
        nameof(Tags) => Tags,
        nameof(PersonKinds) => PersonKinds,
        nameof(VideoBasicFilter) => VideoBasicFilter,
        nameof(VideoTypes) => VideoTypes,
        nameof(Years) => Years?.Select(year => year.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
        nameof(AudioLanguages) => AudioLanguages,
        nameof(SubtitleLanguages) => SubtitleLanguages,
        _ => null
    } ?? [];

    /// <summary>A copy with the values of a group replaced; an empty list clears the group, as the original does.</summary>
    public LibraryFilters With(string group, IReadOnlyList<string> values)
    {
        var list = values.Count == 0 ? null : values;
        return group switch
        {
            nameof(Features) => this with { Features = list },
            nameof(Genres) => this with { Genres = list },
            nameof(OfficialRatings) => this with { OfficialRatings = list },
            nameof(EpisodeFilter) => this with { EpisodeFilter = list },
            nameof(Status) => this with { Status = list },
            nameof(SeriesStatus) => this with { SeriesStatus = list },
            nameof(StudioIds) => this with { StudioIds = list },
            nameof(Tags) => this with { Tags = list },
            nameof(PersonKinds) => this with { PersonKinds = list },
            nameof(VideoBasicFilter) => this with { VideoBasicFilter = list },
            nameof(VideoTypes) => this with { VideoTypes = list },
            nameof(Years) => this with
            {
                Years = list?.Select(year => int.Parse(year, System.Globalization.CultureInfo.InvariantCulture)).ToArray()
            },
            nameof(AudioLanguages) => this with { AudioLanguages = list },
            nameof(SubtitleLanguages) => this with { SubtitleLanguages = list },
            _ => this
        };
    }
}

/// <summary>
/// The persisted settings of one view of one library (<c>LibraryViewSettings</c> of <c>types/library.ts</c>);
/// the property names are the original JSON names.
/// </summary>
public sealed record LibraryViewSettings
{
    public const string Grid = "grid";
    public const string List = "list";

    public IReadOnlyList<string> SortBy { get; init; } = ["SortName"];
    public string SortOrder { get; init; } = "Ascending";
    public int StartIndex { get; init; }
    public bool CardLayout { get; init; }
    public string ImageType { get; init; } = "Primary";
    public string ViewMode { get; init; } = Grid;
    public bool ShowTitle { get; init; } = true;
    public bool ShowYear { get; init; } = true;
    public LibraryFilters? Filters { get; init; }
    public string? Alphabet { get; init; }

    [JsonIgnore]
    public bool IsList => ViewMode == List;

    /// <summary><c>hasFilterOptions</c>: a filter group is set (the badge of the filter button).</summary>
    [JsonIgnore]
    public bool HasFilterOptions => Filters?.HasAny == true;

    /// <summary><c>hasFilters</c>: a filter or a letter narrows the list.</summary>
    [JsonIgnore]
    public bool HasFilters => !string.IsNullOrEmpty(Alphabet) || HasFilterOptions;

    /// <summary>The settings of a view nothing was stored for (<c>getDefaultLibraryViewSettings</c>).</summary>
    public static LibraryViewSettings Defaults(string viewType) => new()
    {
        ViewMode = viewType == LibraryTab.Songs ? List : Grid,
        ImageType = viewType == LibraryTab.Studios ? "Thumb" : "Primary",
        SortBy = [viewType == LibraryTab.Episodes ? "SeriesSortName" : "SortName"]
    };
}

/// <summary>
/// Where the library pages keep their view settings: as JSON in the browser's local storage under the
/// original's key "<c>{viewType} - {libraryId}</c>" (usehooks-ts <c>useLocalStorage</c> in <c>useLibrary.tsx</c>),
/// not prefixed with the user and not synchronised with the server.
/// </summary>
[AppService]
public sealed class LibraryViewSettingsStore(IBrowser browser)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>The storage key of a view: the library id, or "livetv" for the Live TV page.</summary>
    public static string Key(string viewType, string? settingsKey) => viewType + " - " + (settingsKey ?? "null");

    public LibraryViewSettings Load(string viewType, string? settingsKey)
    {
        var stored = browser.StorageGet(Key(viewType, settingsKey));
        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                if (JsonSerializer.Deserialize<LibraryViewSettings>(stored, Json) is { } settings)
                {
                    return settings with { SortBy = settings.SortBy is { Count: > 0 } ? settings.SortBy : ["SortName"] };
                }
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or FormatException)
            {
                AppLog.Warn("Ignoring unreadable library view settings: " + exception.Message);
            }
        }

        return LibraryViewSettings.Defaults(viewType);
    }

    public void Save(string viewType, string? settingsKey, LibraryViewSettings settings) =>
        browser.StorageSet(Key(viewType, settingsKey), Serialize(settings));

    public static string Serialize(LibraryViewSettings settings) => JsonSerializer.Serialize(settings, Json);
}
