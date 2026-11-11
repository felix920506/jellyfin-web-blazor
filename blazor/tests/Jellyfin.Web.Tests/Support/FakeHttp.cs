using System.Net;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Web.Tests.Support;

/// <summary>A request seen by <see cref="FakeHttp"/>.</summary>
public sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body, IReadOnlyDictionary<string, string> Headers)
{
    public string Path => Uri.AbsolutePath;

    public string? Query(string name) => System.Web.HttpUtility.ParseQueryString(Uri.Query)[name];

    public T? Json<T>() => Body is null ? default : JsonSerializer.Deserialize<T>(Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// A scripted HTTP server. Register responses with <c>On*</c>; the most recently registered
/// matching rule wins. Unmatched requests get 404 and are still recorded.
/// </summary>
public sealed class FakeHttp : HttpMessageHandler
{
    private readonly List<(Func<RecordedRequest, bool> Match, Func<RecordedRequest, HttpResponseMessage> Respond)> _rules = [];

    public List<RecordedRequest> Requests { get; } = [];

    public HttpClient CreateClient(string? baseAddress = null) =>
        new(this, disposeHandler: false) { BaseAddress = baseAddress is null ? null : new Uri(baseAddress) };

    /// <summary>Responds to requests whose path ends with <paramref name="pathSuffix"/> (case-insensitive).</summary>
    public FakeHttp On(HttpMethod method, string pathSuffix, Func<RecordedRequest, HttpResponseMessage> respond)
    {
        _rules.Add((r => r.Method == method && r.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase), respond));
        return this;
    }

    public FakeHttp OnGet(string pathSuffix, object? json) => On(HttpMethod.Get, pathSuffix, _ => Json(json));

    public FakeHttp OnGet(string pathSuffix, Func<RecordedRequest, object?> json) => On(HttpMethod.Get, pathSuffix, r => Json(json(r)));

    public FakeHttp OnPost(string pathSuffix, object? json = null) =>
        On(HttpMethod.Post, pathSuffix, _ => json is null ? Status(HttpStatusCode.NoContent) : Json(json));

    public FakeHttp OnPost(string pathSuffix, Func<RecordedRequest, HttpResponseMessage> respond) => On(HttpMethod.Post, pathSuffix, respond);

    public FakeHttp OnDelete(string pathSuffix) => On(HttpMethod.Delete, pathSuffix, _ => Status(HttpStatusCode.NoContent));

    /// <summary>The recorded requests whose path ends with <paramref name="pathSuffix"/>.</summary>
    public IReadOnlyList<RecordedRequest> RequestsTo(string pathSuffix) =>
        Requests.Where(r => r.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>A JSON response. Strings are sent verbatim (already JSON); other values are serialized with web defaults.</summary>
    public static HttpResponseMessage Json(object? value, HttpStatusCode status = HttpStatusCode.OK)
    {
        var text = value as string ?? JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = null });
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body, headers);
        lock (Requests)
        {
            Requests.Add(recorded);
        }

        for (var i = _rules.Count - 1; i >= 0; i--)
        {
            if (_rules[i].Match(recorded))
            {
                var response = _rules[i].Respond(recorded);
                response.RequestMessage = request;
                return response;
            }
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound) { RequestMessage = request };
    }
}
