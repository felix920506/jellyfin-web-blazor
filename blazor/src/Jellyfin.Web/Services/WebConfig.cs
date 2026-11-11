using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Web.Services;

public sealed record ThemeOption
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("color")]
    public string? Color { get; init; }

    [JsonPropertyName("default")]
    public bool Default { get; init; }
}

public sealed record MenuLink
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; init; } = string.Empty;

    [JsonPropertyName("icon")]
    public string? Icon { get; init; }
}

/// <summary>The shape of <c>config.json</c>, which server administrators may edit.</summary>
public sealed record WebConfigData
{
    [JsonPropertyName("includeCorsCredentials")]
    public bool IncludeCorsCredentials { get; init; }

    [JsonPropertyName("multiserver")]
    public bool Multiserver { get; init; }

    [JsonPropertyName("themes")]
    public List<ThemeOption> Themes { get; init; } = [];

    [JsonPropertyName("menuLinks")]
    public List<MenuLink> MenuLinks { get; init; } = [];

    [JsonPropertyName("servers")]
    public List<string> Servers { get; init; } = [];

    [JsonPropertyName("plugins")]
    public List<string> Plugins { get; init; } = [];
}

/// <summary>Loads <c>config.json</c> once. Port of scripts/settings/webSettings.js.</summary>
public sealed class WebConfig(AppAssets assets)
{
    public static readonly ThemeOption FallbackTheme = new() { Name = "Dark", Id = "dark", Color = "#202020", Default = true };

    public WebConfigData Data { get; private set; } = new();

    public bool Multiserver => Data.Multiserver;

    public IReadOnlyList<ThemeOption> Themes => Data.Themes.Count > 0 ? Data.Themes : [FallbackTheme];

    public IReadOnlyList<MenuLink> MenuLinks => Data.MenuLinks;

    public ThemeOption DefaultTheme => Themes.FirstOrDefault(t => t.Default) ?? FallbackTheme;

    public async Task LoadAsync()
    {
        try
        {
            Data = await assets.Http.GetFromJsonAsync<WebConfigData>("config.json") ?? new WebConfigData();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            AppLog.Warn($"[WebConfig] failed to load config.json: {ex.Message}");
            Data = new WebConfigData { Themes = [FallbackTheme] };
        }
    }

    /// <summary>For tests.</summary>
    internal void Set(WebConfigData data) => Data = data;
}

/// <summary>HTTP access to the app's own static files (config.json, generated/strings/*.json).</summary>
public sealed class AppAssets(HttpClient http)
{
    public HttpClient Http { get; } = http;
}
