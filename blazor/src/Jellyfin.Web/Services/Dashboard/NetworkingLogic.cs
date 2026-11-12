using System.Globalization;

namespace Jellyfin.Web.Services;

/// <summary>The addresses the server announces: one for everyone, or one each for the local network and the internet.</summary>
public sealed record PublishedServerUris(string? Internal = null, string? External = null, string? All = null);

/// <summary>
/// The logic of the networking page that does not need the page.
/// Port of apps/dashboard/features/networking/utils.ts and of the form handling in routes/networking/index.tsx.
/// </summary>
public static class NetworkingLogic
{
    /// <summary>Port of getPublishedServerUris: reads the server's "type=uri" entries.</summary>
    public static PublishedServerUris GetPublishedServerUris(IReadOnlyList<string> uris)
    {
        if (uris.Count == 1 && !uris[0].Contains('=', StringComparison.Ordinal))
        {
            return new PublishedServerUris(All: uris[0]);
        }

        var result = new PublishedServerUris();
        foreach (var part in uris)
        {
            // String.split('=') of the original: what follows a second "=" is dropped.
            var pieces = part.Split('=');
            var uri = pieces.Length > 1 ? pieces[1] : null;
            result = pieces[0] switch
            {
                "internal" => result with { Internal = uri },
                "external" => result with { External = uri },
                "all" => result with { All = uri },
                _ => result,
            };
        }

        return result;
    }

    /// <summary>Port of encodePublishedServerUris: the entries the server stores.</summary>
    public static List<string> EncodePublishedServerUris(PublishedServerUris uris)
    {
        if (!string.IsNullOrEmpty(uris.All))
        {
            return ["all=" + uris.All];
        }

        var encoded = new List<string>();
        if (!string.IsNullOrEmpty(uris.Internal))
        {
            encoded.Add("internal=" + uris.Internal);
        }

        if (!string.IsNullOrEmpty(uris.External))
        {
            encoded.Add("external=" + uris.External);
        }

        return encoded;
    }

    /// <summary>Port of splitString: the trimmed, non-empty parts of a comma separated list.</summary>
    public static List<string> SplitString(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : text.Split(',').Select(part => part.Trim()).Where(part => part.Length > 0).ToList();

    /// <summary>What the page shows for a list: the entries joined with ", ".</summary>
    public static string JoinList(IEnumerable<string>? values) => string.Join(", ", values ?? []);

    /// <summary>
    /// JavaScript's <c>parseInt(text, 10)</c>: the whole number a text starts with, after blanks and
    /// a sign. Null (NaN, which the original sends as null) when there is none.
    /// </summary>
    public static int? ParseInt(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var trimmed = text.TrimStart();
        var index = 0;
        if (index < trimmed.Length && trimmed[index] is '+' or '-')
        {
            index++;
        }

        var start = index;
        while (index < trimmed.Length && char.IsAsciiDigit(trimmed[index]))
        {
            index++;
        }

        if (index == start)
        {
            return null;
        }

        return int.TryParse(trimmed.AsSpan(0, index), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }
}
