using System.Text;

namespace Jellyfin.Web.Routing;

/// <summary>
/// The app location taken from the URL fragment: <c>#/details?id=1</c> has path <c>/details</c> and
/// query <c>id=1</c>. Query keys are case-insensitive.
/// </summary>
public sealed class AppLocation : IEquatable<AppLocation>
{
    public static readonly AppLocation Empty = Parse(string.Empty);

    private AppLocation(string path, IReadOnlyDictionary<string, string> query, string url)
    {
        Path = path;
        Query = query;
        Url = url;
    }

    /// <summary>Always starts with '/'. Never ends with one unless it is the root.</summary>
    public string Path { get; }

    public IReadOnlyDictionary<string, string> Query { get; }

    /// <summary>Path plus query string, e.g. <c>/details?id=1</c>.</summary>
    public string Url { get; }

    /// <summary>The value to put in an <c>href</c>.</summary>
    public string Href => "#" + Url;

    public string? this[string key] => Query.TryGetValue(key, out var value) ? value : null;

    public int Tab => int.TryParse(this["tab"], out var tab) ? tab : 0;

    /// <summary>Parses "#/x?y=1", "/x?y=1", "x?y=1" or "#!/x".</summary>
    public static AppLocation Parse(string? hash)
    {
        var text = (hash ?? string.Empty).Trim();
        if (text.StartsWith('#'))
        {
            text = text[1..];
        }

        if (text.StartsWith('!'))
        {
            text = text[1..];
        }

        var queryIndex = text.IndexOf('?');
        var path = queryIndex >= 0 ? text[..queryIndex] : text;
        var queryText = queryIndex >= 0 ? text[(queryIndex + 1)..] : string.Empty;

        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in queryText.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Decode(eq >= 0 ? pair[..eq] : pair);
            var value = eq >= 0 ? Decode(pair[(eq + 1)..]) : string.Empty;
            if (key.Length > 0)
            {
                query[key] = value;
            }
        }

        return new AppLocation(path, query, queryText.Length > 0 ? path + "?" + queryText : path);
    }

    /// <summary>Returns this location with query values replaced (null removes a key).</summary>
    public AppLocation With(params (string Key, string? Value)[] changes)
    {
        var query = new Dictionary<string, string>(Query, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                query.Remove(key);
            }
            else
            {
                query[key] = value;
            }
        }

        return Parse(BuildUrl(Path, query));
    }

    public static string BuildUrl(string path, IEnumerable<KeyValuePair<string, string>> query)
    {
        var builder = new StringBuilder(path);
        var separator = '?';
        foreach (var (key, value) in query)
        {
            builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    public bool Equals(AppLocation? other) => other is not null && string.Equals(Url, other.Url, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as AppLocation);

    public override int GetHashCode() => Url.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => Url;

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }
}
