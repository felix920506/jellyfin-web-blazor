using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jellyfin.Sdk;
using Jellyfin.Sdk.Generated.Models;
using Microsoft.Kiota.Abstractions;

namespace Jellyfin.Web.Services;

/// <summary>Subset of <c>GET /System/Info/Public</c> used while connecting.</summary>
public sealed record PublicServerInfo
{
    public string? Id { get; init; }
    public string? ServerName { get; init; }
    public string? Version { get; init; }
    public string? LocalAddress { get; init; }
    public string? ProductName { get; init; }
    public bool? StartupWizardCompleted { get; init; }
}

/// <summary>
/// The connection to the current Jellyfin server: address, identity of the signed-in user and the
/// generated SDK client. This port talks to one server at a time; <see cref="ServerConnections"/>
/// decides which one and owns sign-in and sign-out.
/// </summary>
/// <remarks>
/// Use <see cref="Client"/> for API calls (do not cache it: it is replaced when the server
/// changes). <see cref="GetUrl"/> builds URLs for images, streams and downloads.
/// </remarks>
public sealed class JellyfinApi
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly AppHost _appHost;
    private readonly JellyfinSdkSettings _settings = new();

    public JellyfinApi(HttpClient http, AppHost appHost)
    {
        Http = http;
        _appHost = appHost;
        _settings.Initialize(AppHost.AppName, appHost.AppVersion, appHost.DeviceName, appHost.DeviceId);
        Client = CreateClient();
    }

    /// <summary>Raised when the server, the token or the current user changes.</summary>
    public event Action? Changed;

    /// <summary>Plain HTTP client for requests the SDK does not cover. No base address.</summary>
    public HttpClient Http { get; }

    /// <summary>The generated SDK client, bound to <see cref="ServerAddress"/> and the current token.</summary>
    public JellyfinApiClient Client { get; private set; }

    /// <summary>Server base URL without a trailing slash, or null before a server is chosen.</summary>
    public string? ServerAddress { get; private set; }

    public string? ServerId { get; private set; }

    public string? ServerName { get; private set; }

    public string? AccessToken { get; private set; }

    public Guid? UserId { get; private set; }

    /// <summary>The signed-in user, once loaded.</summary>
    public UserDto? User { get; private set; }

    public bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken) && UserId is not null;

    public bool IsAdmin => User?.Policy?.IsAdministrator == true;

    /// <summary>The user id as the server formats it (32 hex digits), or null.</summary>
    public string? UserIdText => UserId?.ToString("N");

    public string DeviceId => _appHost.DeviceId;

    /// <summary>Value of the Authorization header for the current (or a given) token.</summary>
    public string AuthorizationHeader(string? token = null)
    {
        token ??= AccessToken;
        var builder = new StringBuilder("MediaBrowser ")
            .Append("Client=\"").Append(Uri.EscapeDataString(AppHost.AppName)).Append("\", ")
            .Append("Device=\"").Append(Uri.EscapeDataString(_appHost.DeviceName)).Append("\", ")
            .Append("DeviceId=\"").Append(Uri.EscapeDataString(_appHost.DeviceId)).Append("\", ")
            .Append("Version=\"").Append(Uri.EscapeDataString(_appHost.AppVersion)).Append('"');
        if (!string.IsNullOrEmpty(token))
        {
            builder.Append(", Token=\"").Append(Uri.EscapeDataString(token)).Append('"');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Builds an absolute server URL. <paramref name="path"/> is relative to the server root
    /// ("Items/{id}/Images/Primary"); null and empty query values are skipped.
    /// </summary>
    public string GetUrl(string path, params (string Key, object? Value)[] query)
    {
        var builder = new StringBuilder(ServerAddress ?? string.Empty);
        if (!path.StartsWith('/'))
        {
            builder.Append('/');
        }

        builder.Append(path);
        var separator = path.Contains('?') ? '&' : '?';
        foreach (var (key, value) in query)
        {
            var text = FormatQueryValue(value);
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            builder.Append(separator).Append(Uri.EscapeDataString(key)).Append('=').Append(Uri.EscapeDataString(text));
            separator = '&';
        }

        return builder.ToString();
    }

    /// <summary>Sends a request to the current server with the Authorization header set.</summary>
    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? jsonBody = null, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? path : GetUrl(path));
        request.Headers.TryAddWithoutValidation("Authorization", AuthorizationHeader());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
        {
            request.Content = JsonContent.Create(jsonBody, jsonBody.GetType(), options: Json);
        }

        return Http.SendAsync(request, cancellationToken);
    }

    /// <summary>GETs JSON from the current server for endpoints or shapes the SDK does not model.</summary>
    public async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    /// <summary>GETs <c>/System/Info/Public</c> from any address; null when unreachable or not a Jellyfin server.</summary>
    public async Task<PublicServerInfo?> GetPublicInfoAsync(string? address = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        address ??= ServerAddress;
        if (string.IsNullOrEmpty(address))
        {
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(20));
        try
        {
            using var response = await Http.GetAsync(address.TrimEnd('/') + "/System/Info/Public", cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var info = await response.Content.ReadFromJsonAsync<PublicServerInfo>(Json, cts.Token);
            return string.IsNullOrEmpty(info?.Id) ? null : info;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Loads the signed-in user from the server and stores it in <see cref="User"/>.</summary>
    public async Task<UserDto?> RefreshUserAsync(CancellationToken cancellationToken = default)
    {
        if (!IsLoggedIn)
        {
            User = null;
            return null;
        }

        User = await Client.Users.Me.GetAsync(cancellationToken: cancellationToken);
        Changed?.Invoke();
        return User;
    }

    internal void SetServer(string? address, string? serverId, string? serverName)
    {
        address = address?.TrimEnd('/');
        var addressChanged = !string.Equals(address, ServerAddress, StringComparison.Ordinal);
        ServerAddress = address;
        ServerId = serverId;
        ServerName = serverName;
        if (addressChanged)
        {
            _settings.SetServerUrl(address ?? string.Empty);
            Client = CreateClient();
        }

        Changed?.Invoke();
    }

    internal void SetAuthentication(string? accessToken, string? userId, UserDto? user = null)
    {
        AccessToken = string.IsNullOrEmpty(accessToken) ? null : accessToken;
        UserId = Guid.TryParse(userId, out var id) && AccessToken is not null ? id : null;
        User = IsLoggedIn ? user : null;
        _settings.SetAccessToken(AccessToken ?? string.Empty);
        Changed?.Invoke();
    }

    private JellyfinApiClient CreateClient()
    {
        var adapter = new JellyfinRequestAdapter(new JellyfinAuthenticationProvider(_settings), _settings, Http);
        if (!string.IsNullOrEmpty(ServerAddress))
        {
            adapter.BaseUrl = ServerAddress;
        }

        return new JellyfinApiClient(adapter);
    }

    private static string? FormatQueryValue(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        Guid guid => guid.ToString("N"),
        Enum enumValue => enumValue.ToApiString(),
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        System.Collections.IEnumerable items => string.Join(',', items.Cast<object?>().Select(FormatQueryValue).Where(v => !string.IsNullOrEmpty(v))),
        _ => value.ToString()
    };
}

/// <summary>Helpers for failures of SDK and raw HTTP calls.</summary>
public static class ApiErrors
{
    /// <summary>The HTTP status behind an exception thrown by the SDK or HttpClient, if any.</summary>
    public static HttpStatusCode? StatusOf(Exception exception) => exception switch
    {
        ApiException api when api.ResponseStatusCode > 0 => (HttpStatusCode)api.ResponseStatusCode,
        HttpRequestException http => http.StatusCode,
        _ => null
    };
}
