using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services;

/// <summary>
/// The server's named configurations ("encoding", "network", "metadata", "xbmcmetadata"), which the
/// SDK has no models for. They are read and written as JSON objects with the server's property
/// names, so that a page sends back what it did not edit exactly as it came. Port of
/// hooks/useNamedConfiguration.ts and the <c>updateNamedConfiguration</c> calls of the routes.
/// </summary>
public static class NamedConfiguration
{
    /// <summary>GET System/Configuration/{key}.</summary>
    public static async Task<JsonObject> GetAsync(JellyfinApi api, string key, CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAsync(HttpMethod.Get, Path(key), null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException($"The configuration '{key}' is not an object.");
    }

    /// <summary>POST System/Configuration/{key}.</summary>
    public static async Task SaveAsync(JellyfinApi api, string key, JsonObject configuration, CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAsync(HttpMethod.Post, Path(key), configuration, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>A text property; null when it is missing or not a text.</summary>
    public static string? Text(this JsonObject configuration, string name) =>
        configuration[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    /// <summary>A text or number property as the text a field shows; null when missing.</summary>
    public static string? Shown(this JsonObject configuration, string name) =>
        configuration[name] is JsonValue value
            ? value.GetValueKind() switch
            {
                JsonValueKind.String => value.GetValue<string>(),
                JsonValueKind.Number => value.ToJsonString(),
                _ => null,
            }
            : null;

    /// <summary>A yes/no property; false when missing (as an unchecked box of the original).</summary>
    public static bool Flag(this JsonObject configuration, string name) =>
        configuration[name] is JsonValue value && value.GetValueKind() == JsonValueKind.True;

    /// <summary>A number property; also a number the server sent as text. Null when missing.</summary>
    public static double? Number(this JsonObject configuration, string name)
    {
        if (configuration[name] is not JsonValue value)
        {
            return null;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.Number => value.GetValue<double>(),
            JsonValueKind.String when double.TryParse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
            _ => null,
        };
    }

    /// <summary>A whole number property; null when missing or not whole.</summary>
    public static int? Whole(this JsonObject configuration, string name) =>
        configuration.Number(name) is { } number && Math.Abs(number - Math.Round(number)) < double.Epsilon && Math.Abs(number) <= int.MaxValue
            ? (int)number
            : null;

    /// <summary>A list of texts; empty when missing.</summary>
    public static List<string> Texts(this JsonObject configuration, string name) =>
        configuration[name] is JsonArray array
            ? array
                .OfType<JsonValue>()
                .Where(value => value.GetValueKind() == JsonValueKind.String)
                .Select(value => value.GetValue<string>())
                .ToList()
            : [];

    /// <summary>Whether the property holds a list (the original only edits a codec list that exists).</summary>
    public static bool HasList(this JsonObject configuration, string name) => configuration[name] is JsonArray;

    /// <summary>Stores a list of texts.</summary>
    public static void SetTexts(this JsonObject configuration, string name, IEnumerable<string> values) =>
        configuration[name] = new JsonArray(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static string Path(string key) => "System/Configuration/" + Uri.EscapeDataString(key);
}
