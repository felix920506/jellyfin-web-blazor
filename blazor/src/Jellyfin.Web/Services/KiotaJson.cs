using System.Text;
using System.Text.Json;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;

namespace Jellyfin.Web.Services;

/// <summary>
/// JSON conversion for SDK models outside of SDK calls (websocket payloads, raw HTTP responses,
/// values kept in storage). SDK models are not System.Text.Json friendly; use this instead.
/// </summary>
public static class KiotaJson
{
    /// <summary>Parses a model: <c>KiotaJson.Parse(json, BaseItemDto.CreateFromDiscriminatorValue)</c>.</summary>
    public static T? Parse<T>(string json, ParsableFactory<T> factory)
        where T : IParsable
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, factory);
    }

    public static T? Parse<T>(JsonElement element, ParsableFactory<T> factory)
        where T : IParsable =>
        element.ValueKind == JsonValueKind.Object ? new JsonParseNode(element.Clone()).GetObjectValue(factory) : default;

    public static List<T> ParseList<T>(JsonElement element, ParsableFactory<T> factory)
        where T : IParsable =>
        element.ValueKind == JsonValueKind.Array
            ? new JsonParseNode(element.Clone()).GetCollectionOfObjectValues(factory).ToList()
            : [];

    public static string Serialize<T>(T value)
        where T : IParsable
    {
        using var writer = new JsonSerializationWriter();
        writer.WriteObjectValue(null, value);
        using var stream = writer.GetSerializedContent();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>A deep copy of a model.</summary>
    public static T Clone<T>(T value, ParsableFactory<T> factory)
        where T : IParsable => Parse(Serialize(value), factory)!;
}
