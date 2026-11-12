using System.Globalization;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services;

/// <summary>Which fields the metadata editor shows for an item and how it labels them.</summary>
public sealed class MetadataEditorLayout
{
    private readonly HashSet<string> _visible = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    /// <summary>The options of <c>#selectDisplayOrder</c>: value and text (already translated).</summary>
    public IReadOnlyList<(string Value, string Text)> DisplayOrderOptions { get; internal set; } = [];

    /// <summary>Whether the container with this id (or class, for the ones without id) is shown.</summary>
    public bool Shows(string container) => _visible.Contains(container);

    // What the original's template says before an item is loaded; the index numbers have no label there.
    private static readonly Dictionary<string, string> TemplateLabels = new(StringComparer.Ordinal)
    {
        ["txtName"] = "LabelTitle",
        ["txtOriginalName"] = "LabelOriginalTitle",
        ["txtSortName"] = "LabelSortTitle",
        ["txtPremiereDate"] = "LabelReleaseDate",
        ["txtProductionYear"] = "LabelYear",
        ["txtEndDate"] = "LabelEndDate"
    };

    /// <summary>
    /// The translation key of the label of an input, by the input's id. Before an item is loaded these
    /// are the labels of the original's template; null for an input that has none then.
    /// </summary>
    public string? LabelKey(string input) =>
        _labels.TryGetValue(input, out var key) ? key : TemplateLabels.GetValueOrDefault(input);

    internal void Show(string container, bool visible)
    {
        if (visible)
        {
            _visible.Add(container);
        }
    }

    internal void Label(string input, string key) => _labels[input] = key;
}

/// <summary>
/// The rules of the metadata editor that do not depend on its markup. Port of the corresponding
/// functions of components/metadataEditor/metadataEditor.js.
/// </summary>
public static class MetadataEditorLogic
{
    /// <summary>The week days of the air day check boxes; also their translation keys.</summary>
    public static readonly IReadOnlyList<string> AirDays = ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    /// <summary>The ids of the fields that "Reset" empties.</summary>
    public static readonly IReadOnlyList<string> ResetFields =
    [
        "txtName", "txtOriginalName", "selectOriginalLanguage", "txtSortName", "txtCommunityRating", "txtCriticRating", "txtIndexNumber",
        "txtAirsBeforeSeason", "txtAirsAfterSeason", "txtAirsBeforeEpisode", "txtParentIndexNumber", "txtAlbum", "txtAlbumArtist", "txtArtist",
        "txtOverview", "selectStatus", "txtAirTime", "txtPremiereDate", "txtDateAdded", "txtEndDate", "txtProductionYear", "selectHeight",
        "txtOriginalAspectRatio", "select3dFormat", "selectOfficialRating", "selectCustomRating", "txtSeriesRuntime", "txtTagline",
        "selectDisplayOrder", "selectLanguage", "selectCountry"
    ];

    /// <summary>Port of <c>setFieldVisibilities()</c> and of the tagline rule of <c>reload()</c>.</summary>
    public static MetadataEditorLayout Layout(JsonNode? item, Globalize globalize)
    {
        var type = item.Text("Type");
        var isVideo = item.Text("MediaType") == "Video";
        var isChannel = type == "TvChannel";
        var isPerson = type == "Person";
        var isSeries = type == "Series";
        var layout = new MetadataEditorLayout();

        layout.Show("fldPath", !string.IsNullOrEmpty(item.Text("Path")) && item.FlagOrNull("EnableMediaSourceDisplay") != false);
        layout.Show("fldOriginalName", type is "Series" or "Season" or "Episode" or "Movie" or "Trailer" or "Person");
        layout.Show("fldOriginalLanguage", isSeries || isVideo);
        layout.Show("fldSeriesRuntime", isSeries);
        layout.Show("fldEndDate", isSeries || isPerson);
        layout.Show("fldCriticRating", type is "Movie" or "Trailer");
        layout.Show("seriesName", type == "Book");
        layout.Show("fldStatus", isSeries);
        layout.Show("fldAirDays", isSeries);
        layout.Show("fldAirTime", isSeries);
        layout.Show("fld3dFormat", isVideo && !isChannel);
        layout.Show("fldArtist", type is "Audio" or "MusicAlbum" or "MusicVideo");
        layout.Show("fldAlbumArtist", type is "Audio" or "MusicAlbum" or "MusicVideo");
        layout.Show("fldAlbum", type is "Audio" or "MusicVideo");
        layout.Show("collapsibleSpecialEpisodeInfo", type == "Episode" && item.Number("ParentIndexNumber") == 0);

        var isEntity = type is "Person" or "Genre" or "Studio" or "MusicGenre" or "TvChannel";
        layout.Show("peopleCollapsible", !isEntity);
        layout.Show("fldCommunityRating", !isEntity);
        layout.Show("genresCollapsible", !isEntity);
        layout.Show("studiosCollapsible", !isEntity);
        layout.Show("fldOfficialRating", !isEntity || isChannel);
        layout.Show("fldCustomRating", !isEntity);
        layout.Show("tagsCollapsible", true);

        layout.Show("metadataSettingsCollapsible", !isChannel);
        layout.Show("fldPremiereDate", !isChannel);
        layout.Show("fldDateAdded", !isChannel);
        layout.Show("fldYear", !isChannel);
        layout.Show("overviewContainer", !isChannel);

        layout.Label("txtName", isPerson ? "LabelName" : "LabelTitle");
        layout.Label("txtSortName", isPerson ? "LabelSortName" : "LabelSortTitle");
        layout.Label("txtOriginalName", isPerson ? "LabelOriginalName" : "LabelOriginalTitle");
        layout.Label("txtProductionYear", isPerson ? "LabelBirthYear" : "LabelYear");
        layout.Label("txtPremiereDate", isPerson ? "LabelBirthDate" : "LabelReleaseDate");
        layout.Label("txtEndDate", isPerson ? "LabelDeathDate" : "LabelEndDate");
        layout.Show("fldPlaceOfBirth", isPerson);

        layout.Show("fldHeight", isVideo && isChannel);
        layout.Show("fldOriginalAspectRatio", isVideo && !isChannel);

        layout.Show("fldIndexNumber", type is "Audio" or "Episode" or "Season");
        layout.Label("txtIndexNumber", type switch
        {
            "Episode" => "LabelEpisodeNumber",
            "Season" => "LabelSeasonNumber",
            "Audio" => "LabelTrackNumber",
            _ => "LabelNumber"
        });

        layout.Show("fldParentIndexNumber", type is "Audio" or "Episode");
        layout.Label("txtParentIndexNumber", type switch
        {
            "Episode" => "LabelSeasonNumber",
            "Audio" => "LabelDiscNumber",
            _ => "LabelParentNumber"
        });

        layout.Show("fldDisplayOrder", type is "BoxSet" or "Series");
        layout.Show("seriesDisplayOrderDescription", isSeries);
        layout.DisplayOrderOptions = type switch
        {
            "BoxSet" =>
            [
                ("Default", globalize.Translate("DateModified")),
                ("SortName", globalize.Translate("SortName")),
                ("PremiereDate", globalize.Translate("ReleaseDate"))
            ],
            "Series" =>
            [
                (string.Empty, globalize.Translate("Aired")),
                ("originalAirDate", globalize.Translate("OriginalAirDate")),
                ("absolute", globalize.Translate("Absolute")),
                ("dvd", "DVD"),
                ("digital", globalize.Translate("Digital")),
                ("storyArc", globalize.Translate("StoryArc")),
                ("production", globalize.Translate("Production")),
                ("tv", "TV"),
                ("alternate", globalize.Translate("Alternate")),
                ("regional", globalize.Translate("Regional")),
                ("altdvd", globalize.Translate("AlternateDVD"))
            ],
            _ => []
        };

        layout.Show("fldTagline", isVideo && type is not ("Episode" or "TvChannel"));
        return layout;
    }

    /// <summary>
    /// The fields that can be locked, as (LockedFields value, translation key). Port of
    /// <c>fillMetadataSettings()</c>.
    /// </summary>
    public static IReadOnlyList<(string Value, string Key)> LockableFields(string? itemType)
    {
        var fields = new List<(string Value, string Key)>
        {
            ("Name", "Name"),
            ("Overview", "Overview"),
            ("Genres", "Genres"),
            ("OfficialRating", "ParentalRating"),
            ("Cast", "People")
        };

        fields.Add(("ProductionLocations", itemType == "Person" ? "BirthLocation" : "ProductionLocations"));
        if (itemType == "Series")
        {
            fields.Add(("Runtime", "Runtime"));
        }

        fields.Add(("Studios", "Studios"));
        fields.Add(("Tags", "Tags"));
        return fields;
    }

    /// <summary>
    /// The value of a date input for a date of the server: the UTC day as <c>yyyy-MM-dd</c>, empty when
    /// there is none or it cannot be read (<c>parseISO8601Date(value, true).toISOString().slice(0, 10)</c>).
    /// </summary>
    public static string DateInputValue(string? isoDate) =>
        ParseDate(isoDate) is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

    /// <summary>
    /// The date to save for a date input. Port of <c>getDateValue()</c>: nothing when the input is
    /// empty; the time of day of the item's date is kept when the day did not change.
    /// </summary>
    public static string? DateToSave(string? inputValue, string? currentIsoDate)
    {
        if (string.IsNullOrEmpty(inputValue))
        {
            return null;
        }

        if (ParseDate(currentIsoDate) is { } date)
        {
            // toISOString(): always UTC with milliseconds.
            var iso = date.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            var parts = iso.Split('T');
            if (parts[0].StartsWith(inputValue, StringComparison.Ordinal))
            {
                return inputValue + "T" + parts[1];
            }
        }

        return inputValue;
    }

    /// <summary>The names of a ";" separated list, as objects with a Name (<c>getAlbumArtists()</c>, <c>getArtists()</c>).</summary>
    public static JsonArray SplitNames(string? value)
    {
        var names = new JsonArray();
        foreach (var name in (value ?? string.Empty).Trim().Split(';'))
        {
            if (name.Length > 0)
            {
                names.Add(new JsonObject { ["Name"] = name });
            }
        }

        return names;
    }

    /// <summary>
    /// The order of the genre, tag and studio lists: by lower-cased name. The original compares with the
    /// browser's <c>localeCompare</c>; without cultures in the port the comparison is ordinal.
    /// </summary>
    public static void Sort(List<string> values) =>
        values.Sort((a, b) => string.Compare(a.ToLowerInvariant(), b.ToLowerInvariant(), StringComparison.Ordinal));

    /// <summary>The series runtime in minutes for a number of ticks; empty when there is none.</summary>
    public static string RuntimeMinutes(double? runTimeTicks) =>
        runTimeTicks is > 0 or < 0
            ? Math.Round(runTimeTicks.Value / 600000000, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)
            : string.Empty;

    /// <summary>
    /// The values of the editor's inputs and selects, by element id, for an item. Port of the part of
    /// <c>fillItemInfo()</c> that fills plain fields.
    /// </summary>
    public static Dictionary<string, string?> FieldValues(JsonNode? item)
    {
        // "x || ''": nothing for null, 0 and an empty text.
        string Or(string name) => item.Text(name) is { Length: > 0 } text && text != "0" ? text : string.Empty;

        // "x == null ? '' : x" and "'x' in item ? item.x : ''".
        string OrNull(string name) => item.Text(name) ?? string.Empty;

        string Names(string name) => string.Join(';', item.List(name).Select(entry => entry.Text("Name") ?? string.Empty));

        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["selectOfficialRating"] = Or("OfficialRating"),
            ["selectCustomRating"] = Or("CustomRating"),
            ["selectStatus"] = Or("Status"),
            ["select3dFormat"] = Or("Video3DFormat"),
            ["txtPath"] = Or("Path"),
            ["txtName"] = Or("Name"),
            ["txtOriginalName"] = Or("OriginalTitle"),
            ["selectOriginalLanguage"] = Or("OriginalLanguage"),
            ["txtOverview"] = Or("Overview"),
            ["txtTagline"] = item.Strings("Taglines").FirstOrDefault() ?? string.Empty,
            ["txtSortName"] = Or("ForcedSortName"),
            ["txtCommunityRating"] = Or("CommunityRating"),
            ["txtCriticRating"] = Or("CriticRating"),
            ["txtIndexNumber"] = OrNull("IndexNumber"),
            ["txtParentIndexNumber"] = OrNull("ParentIndexNumber"),
            ["txtAirsBeforeSeason"] = OrNull("AirsBeforeSeasonNumber"),
            ["txtAirsAfterSeason"] = OrNull("AirsAfterSeasonNumber"),
            ["txtAirsBeforeEpisode"] = OrNull("AirsBeforeEpisodeNumber"),
            ["txtAlbum"] = Or("Album"),
            ["txtAlbumArtist"] = Names("AlbumArtists"),
            ["selectDisplayOrder"] = Or("DisplayOrder"),
            ["txtseriesName"] = item.Text("Type") == "Book" ? Or("SeriesName") : string.Empty,
            ["txtArtist"] = Names("ArtistItems"),
            ["txtDateAdded"] = DateInputValue(item.Text("DateCreated")),
            ["txtPremiereDate"] = DateInputValue(item.Text("PremiereDate")),
            ["txtEndDate"] = DateInputValue(item.Text("EndDate")),
            ["txtProductionYear"] = Or("ProductionYear"),
            ["txtAirTime"] = Or("AirTime"),
            ["txtPlaceOfBirth"] = item.Strings("ProductionLocations").FirstOrDefault() ?? string.Empty,
            ["selectHeight"] = Or("Height"),
            ["txtOriginalAspectRatio"] = Or("AspectRatio"),
            ["selectLanguage"] = Or("PreferredMetadataLanguage"),
            ["selectCountry"] = Or("PreferredMetadataCountryCode"),
            ["txtSeriesRuntime"] = RuntimeMinutes(item.Number("RunTimeTicks"))
        };
    }

    /// <summary>The full name of an external id for its label: "Name" or "Name Type" (<c>getExternalIdFullName()</c>).</summary>
    public static string ExternalIdName(JsonNode? idInfo, Globalize globalize)
    {
        var name = idInfo.Text("Name") ?? string.Empty;
        return idInfo.Text("Type") is { Length: > 0 } type ? name + " " + globalize.Translate(type) : name;
    }

    private static DateTime? ParseDate(string? isoDate) =>
        !string.IsNullOrEmpty(isoDate)
        && DateTimeOffset.TryParse(isoDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? date.UtcDateTime
            : null;
}
