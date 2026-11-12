using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Web.Services;

/// <summary>
/// The server requests of the item dialogs. The editors read and write the server's JSON as it is
/// (<see cref="JsonNode"/>), so that what they send back has the members and the shape the original
/// client sends, whatever the SDK models know about.
/// </summary>
[AppService]
public sealed class ItemDialogApi(JellyfinApi api)
{
    /// <summary>The largest file an upload dialog reads; the browser's own picker has no limit.</summary>
    public const long MaxUploadSize = 256L * 1024 * 1024;

    /// <summary>The signed-in user's id, without dashes.</summary>
    public string? UserId => api.UserIdText;

    /// <summary>Whether a failed request or an unreadable answer caused the exception.</summary>
    public static bool IsFailure(Exception exception) => exception is HttpRequestException or JsonException;

    /// <summary>The bytes of a file the user picked for an upload (images, subtitles, lyrics).</summary>
    public static async Task<byte[]> ReadFileAsync(Microsoft.AspNetCore.Components.Forms.IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(MaxUploadSize);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    /// <summary>An absolute server URL (see <see cref="JellyfinApi.GetUrl"/>).</summary>
    public string GetUrl(string path, params (string Key, object? Value)[] query) => api.GetUrl(path, query);

    /// <summary>The item with the user's data, as <c>apiClient.getItem(userId, itemId)</c> requests it.</summary>
    public Task<JsonObject?> GetItemAsync(string itemId) => GetObjectAsync($"Users/{api.UserIdText}/Items/{itemId}");

    /// <summary>GETs JSON; null for an empty answer.</summary>
    public async Task<JsonNode?> GetAsync(string path, params (string Key, object? Value)[] query)
    {
        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl(path, query));
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    /// <summary>GETs a JSON object; null when the server answers with something else.</summary>
    public async Task<JsonObject?> GetObjectAsync(string path, params (string Key, object? Value)[] query) =>
        await GetAsync(path, query) as JsonObject;

    /// <summary>GETs a JSON array; empty when the server answers with something else.</summary>
    public async Task<JsonArray> GetArrayAsync(string path, params (string Key, object? Value)[] query) =>
        await GetAsync(path, query) as JsonArray ?? [];

    /// <summary>POSTs to a URL built by <see cref="GetUrl"/>, optionally with a JSON body; returns the answer's JSON, if any.</summary>
    public async Task<JsonNode?> PostAsync(string url, JsonNode? body = null)
    {
        using var response = await api.SendAsync(HttpMethod.Post, url, body);
        response.EnsureSuccessStatusCode();
        return await ReadAsync(response);
    }

    /// <summary>POSTs a body that is not JSON (an image as base64, lyrics as text).</summary>
    public async Task PostContentAsync(string url, HttpContent content)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.TryAddWithoutValidation("Authorization", api.AuthorizationHeader());
        using var response = await api.Http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>Sends a DELETE to a URL built by <see cref="GetUrl"/>.</summary>
    public async Task DeleteAsync(string url)
    {
        using var response = await api.SendAsync(HttpMethod.Delete, url);
        response.EnsureSuccessStatusCode();
    }

    private static async Task<JsonNode?> ReadAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
    }
}

/// <summary>Reading members of the server's JSON the way the original's JavaScript does.</summary>
public static class ItemJson
{
    /// <summary>A member as text: a string as it is, a number or boolean as JavaScript prints it; null when absent or null.</summary>
    public static string? Text(this JsonNode? node, string name) => AsText(Member(node, name));

    /// <summary>A member that is <c>true</c>.</summary>
    public static bool Flag(this JsonNode? node, string name) => FlagOrNull(node, name) == true;

    /// <summary>A boolean member; null when absent or not a boolean.</summary>
    public static bool? FlagOrNull(this JsonNode? node, string name) => Member(node, name)?.GetValueKind() switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };

    /// <summary>A numeric member; null when absent or not a number.</summary>
    public static double? Number(this JsonNode? node, string name) =>
        Member(node, name) is JsonValue value
        && value.GetValueKind() == JsonValueKind.Number
        && double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    /// <summary>Whether the object has the member at all (<c>'name' in object</c>).</summary>
    public static bool Has(this JsonNode? node, string name) => node is JsonObject item && item.ContainsKey(name);

    /// <summary>An object member; null when absent.</summary>
    public static JsonObject? Child(this JsonNode? node, string name) => Member(node, name) as JsonObject;

    /// <summary>The elements of an array member; none when absent.</summary>
    public static IReadOnlyList<JsonNode?> List(this JsonNode? node, string name) =>
        Member(node, name) is JsonArray array ? [.. array] : [];

    /// <summary>The texts of an array member; none when absent.</summary>
    public static List<string> Strings(this JsonNode? node, string name) =>
        [.. List(node, name).Select(AsText).OfType<string>()];

    /// <summary>A value as text (see <see cref="Text"/>).</summary>
    public static string? AsText(JsonNode? value) => value switch
    {
        JsonValue text when text.GetValueKind() == JsonValueKind.String => text.GetValue<string>(),
        JsonValue other when other.GetValueKind() != JsonValueKind.Null => other.ToJsonString(),
        _ => null
    };

    private static JsonNode? Member(JsonNode? node, string name) =>
        node is JsonObject item && item.TryGetPropertyValue(name, out var value) ? value : null;
}
