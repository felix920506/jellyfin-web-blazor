using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services;

/// <summary>
/// What a dialog reports to the code that opened it: the original's dialogs keep a <c>hasChanges</c>
/// flag and resolve with it however they are closed, by a button, the backdrop or Escape.
/// </summary>
public sealed class DialogChanges
{
    /// <summary>Whether the dialog changed something on the server.</summary>
    public bool HasChanges { get; set; }
}

/// <summary>The logic of the image editor and the image downloader that does not need the page.</summary>
public static class ImageEditorLogic
{
    /// <summary>The image types a remote search or an upload can be for, in the order of the original's selects.</summary>
    public static readonly IReadOnlyList<string> BrowsableTypes =
        ["Primary", "Art", "Backdrop", "Banner", "Box", "BoxRear", "Disc", "Logo", "Menu", "Thumb"];

    /// <summary>The width images are requested with: 300, or a quarter of a wide window (<c>renderImages()</c>).</summary>
    public static int ImageSize(int windowWidth) =>
        windowWidth >= 1280 ? (int)Math.Round(windowWidth / 4d, MidpointRounding.AwayFromZero) : 300;

    /// <summary>The cache tag of an image of the item (<c>getImageUrl()</c> of imageeditor.js).</summary>
    public static string? ImageTag(JsonNode? item, string type, int? index) => type switch
    {
        "Backdrop" => item.Strings("BackdropImageTags").ElementAtOrDefault(index ?? 0),
        "Primary" => item.Text("PrimaryImageTag") is { Length: > 0 } tag ? tag : item.Child("ImageTags").Text(type),
        _ => item.Child("ImageTags").Text(type)
    };

    /// <summary>
    /// The path and query of an image of the item, as <c>apiClient.getScaledImageUrl</c> builds it: the
    /// width multiplied by the device pixel ratio and the default quality.
    /// </summary>
    public static (string Path, (string Key, object? Value)[] Query) ImageRequest(JsonNode? item, string type, int? index, int maxWidth, double devicePixelRatio)
    {
        var path = $"Items/{item.Text("Id")}/Images/{type}" + (index is { } number ? "/" + number.ToString(CultureInfo.InvariantCulture) : null);
        return (path,
        [
            ("maxWidth", (int)Math.Round(maxWidth * devicePixelRatio, MidpointRounding.AwayFromZero)),
            ("tag", ImageTag(item, type, index)),
            ("quality", type == "Backdrop" ? 80 : 90)
        ]);
    }

    /// <summary>The images the editor lists first: every kind but backdrops and chapter images.</summary>
    public static IReadOnlyList<JsonNode?> StandardImages(JsonArray imageInfos) =>
        [.. imageInfos.Where(image => image.Text("ImageType") is not ("Backdrop" or "Chapter"))];

    /// <summary>The backdrops in their order.</summary>
    public static IReadOnlyList<JsonNode?> Backdrops(JsonArray imageInfos) =>
        [.. imageInfos.Where(image => image.Text("ImageType") == "Backdrop").OrderBy(image => image.Number("ImageIndex") ?? 0)];

    /// <summary>The card shape of a remote image (<c>getRemoteImageHtml()</c>).</summary>
    public static string RemoteImageShape(string? imageType, string? itemType) => imageType switch
    {
        "Backdrop" or "Art" or "Thumb" or "Logo" => "backdrop",
        "Banner" => "banner",
        "Disc" => "square",
        _ => itemType switch
        {
            "Episode" => "backdrop",
            "MusicAlbum" or "MusicArtist" => "square",
            _ => "portrait"
        }
    };

    /// <summary>"1920 x 1080 • en", the language alone, or null when the image has neither.</summary>
    public static string? RemoteImageSize(JsonNode? image)
    {
        var width = image.Number("Width") ?? 0;
        var height = image.Number("Height") ?? 0;
        var language = image.Text("Language");
        if (width != 0 && height != 0)
        {
            return image.Text("Width") + " x " + image.Text("Height") + (string.IsNullOrEmpty(language) ? null : " • " + language);
        }

        return string.IsNullOrEmpty(language) ? null : language;
    }

    /// <summary>The rating line of a remote image; null when the image has no rating. The words are not translated in the original.</summary>
    public static string? RemoteImageRating(JsonNode? image)
    {
        if (image.Number("CommunityRating") is not { } rating)
        {
            return null;
        }

        if (image.Text("RatingType") == "Likes")
        {
            return image.Text("CommunityRating") + (rating == 1 ? " like" : " likes");
        }

        if (rating == 0)
        {
            return "Unrated";
        }

        var text = rating.ToString("0.0", CultureInfo.InvariantCulture);
        var votes = image.Number("VoteCount") ?? 0;
        return votes == 0 ? text : text + " • " + image.Text("VoteCount") + (votes == 1 ? " vote" : " votes");
    }

    /// <summary>The first and last number of the page ("1-30 of 55" takes 1, 30, 55) as <c>getPagingHtml()</c> computes them.</summary>
    public static (int Start, int End) PageRange(int startIndex, int limit, int total) =>
        (total == 0 ? 0 : startIndex + 1, Math.Min(startIndex + limit, total));
}

/// <summary>The logic of the item identifier that does not need the page.</summary>
public static class ItemIdentifierLogic
{
    /// <summary>The shape of the search result cards for an item type (<c>getSearchResultHtml()</c>).</summary>
    public static string CardShape(string? itemType) => itemType switch
    {
        "Episode" => "backdrop",
        "MusicAlbum" or "MusicArtist" => "square",
        _ => "portrait"
    };

    /// <summary>
    /// The text lines under a search result: name, provider, album artist and year as far as the result
    /// has them, cut or filled to three lines (four for an album). Missing lines are empty.
    /// </summary>
    public static IReadOnlyList<string> ResultLines(JsonNode? result, string? itemType)
    {
        var lines = new List<string?> { result.Text("Name"), result.Text("SearchProviderName") };
        if (result.Child("AlbumArtist") is { } albumArtist)
        {
            lines.Add(albumArtist.Text("Name"));
        }

        if (result.Number("ProductionYear") is { } year and not 0)
        {
            lines.Add(result.Text("ProductionYear"));
        }

        var count = itemType == "MusicAlbum" ? 4 : 3;
        return [.. Enumerable.Range(0, count).Select(index => lines.ElementAtOrDefault(index) ?? string.Empty)];
    }

    /// <summary>
    /// The body of the remote search (<c>searchForIdentificationResults()</c>): every external id, also
    /// the empty ones, the name and the year when entered. Null when neither a name nor an id was entered.
    /// </summary>
    public static JsonObject? SearchBody(string? itemId, string? name, string? year, IReadOnlyList<(string Key, string Value)> providerIds)
    {
        if (string.IsNullOrEmpty(name) && providerIds.All(id => string.IsNullOrEmpty(id.Value)))
        {
            return null;
        }

        var ids = new JsonObject();
        foreach (var (key, value) in providerIds)
        {
            ids[key] = value;
        }

        var searchInfo = new JsonObject { ["ProviderIds"] = ids };
        if (!string.IsNullOrEmpty(name))
        {
            searchInfo["Name"] = name;
        }

        // parseInt(value, 10) of a number input.
        if (double.TryParse(year, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            searchInfo["Year"] = (int)Math.Truncate(number);
        }

        return new JsonObject { ["SearchInfo"] = searchInfo, ["ItemId"] = itemId };
    }
}

/// <summary>The logic of the subtitle and lyrics dialogs that does not need the page.</summary>
public static class SubtitleLyricsLogic
{
    private const long TicksPerMinute = 600000000;
    private const long TicksPerSecond = 10000000;

    /// <summary>The file extensions a subtitle upload accepts.</summary>
    public static readonly IReadOnlyList<string> SubtitleExtensions = [".sub", ".srt", ".vtt", ".ass", ".ssa", ".mks"];

    /// <summary>The file extensions a lyrics upload accepts.</summary>
    public static readonly IReadOnlyList<string> LyricsExtensions = [".lrc", ".txt"];

    /// <summary>The file name of a path with either kind of separator (the "File" line of the editors).</summary>
    public static string FileName(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var index = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        return index < 0 ? path : path[(index + 1)..];
    }

    /// <summary>Whether a file name ends with one of the extensions; the original compares case-sensitively.</summary>
    public static bool HasExtension(string? fileName, IReadOnlyList<string> extensions) =>
        fileName is not null && extensions.Any(extension => fileName.EndsWith(extension, StringComparison.Ordinal));

    /// <summary>The subtitle format sent with an upload: the text after the last dot, in lower case.</summary>
    public static string SubtitleFormat(string fileName)
    {
        var index = fileName.LastIndexOf('.');
        return (index < 0 ? fileName : fileName[(index + 1)..]).ToLowerInvariant();
    }

    /// <summary>The pills of a remote subtitle (translation keys), in the original's order.</summary>
    public static IReadOnlyList<string> SubtitleFeatures(JsonNode? result)
    {
        var features = new List<string>();
        void AddIf(string name, string key)
        {
            if (result.Flag(name))
            {
                features.Add(key);
            }
        }

        AddIf("IsHashMatch", "PerfectMatch");
        AddIf("AiTranslated", "AiTranslated");
        AddIf("MachineTranslated", "MachineTranslated");
        AddIf("Forced", "ForeignPartsOnly");
        AddIf("HearingImpaired", "HearingImpairedShort");
        return features;
    }

    /// <summary>
    /// Remote search results under the heading of their provider: like the original, a new group starts
    /// whenever the provider differs from the one of the result before.
    /// </summary>
    public static IReadOnlyList<(string Provider, IReadOnlyList<JsonNode?> Results)> GroupByProvider(IEnumerable<JsonNode?> results, Func<JsonNode?, string?> provider)
    {
        var groups = new List<(string Provider, List<JsonNode?> Results)>();
        foreach (var result in results)
        {
            var name = provider(result) ?? string.Empty;
            if (groups.Count == 0 || groups[^1].Provider != name)
            {
                groups.Add((name, []));
            }

            groups[^1].Results.Add(result);
        }

        return [.. groups.Select(group => (group.Provider, (IReadOnlyList<JsonNode?>)group.Results))];
    }

    /// <summary>"[mm:ss.hh] " for the start of a synced lyrics line (<c>formatLyricsTimestamp</c>); empty for a line without a time.</summary>
    public static string LyricTimestamp(JsonNode? line)
    {
        if (line.Number("Start") is not { } start)
        {
            return string.Empty;
        }

        var ticks = (long)start;
        var minutes = ticks / TicksPerMinute;
        var seconds = ticks % TicksPerMinute / TicksPerSecond;
        var hundredths = ticks % TicksPerSecond / 100000;
        return string.Create(CultureInfo.InvariantCulture, $"[{minutes:00}:{seconds:00}.{hundredths:00}] ");
    }

    /// <summary>The lines of lyrics as the editor shows them: each with its timestamp when it has one.</summary>
    public static IReadOnlyList<string> LyricLines(JsonNode? lyrics) =>
        [.. lyrics.List("Lyrics").Select(line => LyricTimestamp(line) + line.Text("Text"))];

    /// <summary>"Artist - Album - Title" of a remote lyrics result; the original joins the three whatever the provider sent.</summary>
    public static string LyricsTitle(JsonNode? metadata) =>
        metadata.Text("Artist") + " - " + metadata.Text("Album") + " - " + metadata.Text("Title");

    /// <summary>"m:ss" for a length in ticks (the duration line of a remote lyrics result).</summary>
    public static string LyricsDuration(double? ticks)
    {
        var total = (long)(ticks ?? 0);
        var minutes = total / TicksPerMinute;
        var seconds = total % TicksPerMinute / TicksPerSecond;
        return string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }

    /// <summary>The text of an upload as base64, the body of the subtitle and image uploads.</summary>
    public static string Base64(byte[] data) => Convert.ToBase64String(data);

    /// <summary>Text read from an uploaded lyrics file.</summary>
    public static string Utf8(byte[] data) => Encoding.UTF8.GetString(data);
}
