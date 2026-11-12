using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>The merged installed/catalog view, including the server's duplicate-version workaround.</summary>
public sealed record DashboardPlugin(Guid Id, PluginInfo? Installed, PackageInfo? Package, ConfigurationPageInfo? Configuration)
{
    public string Name => Installed?.Name ?? Package?.Name ?? string.Empty;
    public string? Description => Installed?.Description ?? Package?.Description ?? Package?.Overview;
    public PluginInfo_Status? Status => Installed?.Status;
    public bool CanUninstall => Installed?.CanUninstall == true;
    public VersionInfo? Version => Installed is { } plugin
        ? Versions.FirstOrDefault(v => v.Version == plugin.Version) ?? new VersionInfo { Version = plugin.Version }
        : Versions.FirstOrDefault();
    public IReadOnlyList<VersionInfo> Versions => Package?.Versions ?? [];
    public string? Category => Package?.Category ?? (Id.ToString("N") switch
    {
        "a629c0dafac54c7e931a7174223f14c8" or "8c95c4d2e50c4fb0a4f36c06ff0f9a1a" => "Music",
        "a628c0dafac54c7e9d1a7134223f14c8" or "b8715ed16c4745289ad3f72deb539cd4" => "MoviesAndShows",
        "872a78491171458da6fb3de3d442ad30" => "General",
        _ => null,
    });
    public string Href => $"#/dashboard/plugins/{Id:N}?name={Uri.EscapeDataString(Name)}";
    public string? ImageUrl(JellyfinApi api) => Installed?.HasImage == true
        ? api.GetUrl($"Plugins/{Id:N}/{Installed.Version}/Image", ("ApiKey", api.AccessToken)) : Package?.ImageUrl;
}

[AppService]
public sealed class DashboardPlugins(JellyfinApi api)
{
    public static readonly (string Category, string Key)[] Categories =
    [
        ("Administration", "HeaderAdmin"), ("General", "General"), ("Anime", "Anime"), ("Books", "Books"),
        ("LiveTV", "LiveTV"), ("MoviesAndShows", "MoviesAndShows"), ("Music", "TabMusic"), ("Subtitles", "Subtitles"), ("Other", "Other"),
    ];

    public static PluginInfo? BestPlugin(IEnumerable<PluginInfo> plugins, Guid id)
    {
        var matches = plugins.Where(p => p.Id == id).ToList();
        return matches.FirstOrDefault(p => p.Status == PluginInfo_Status.Disabled)
            ?? matches.FirstOrDefault(p => p.Status == PluginInfo_Status.Restart) ?? matches.FirstOrDefault();
    }

    public static ConfigurationPageInfo? BestPage(IEnumerable<ConfigurationPageInfo> pages, Guid id)
    {
        var matches = pages.Where(p => p.PluginId == id).ToList();
        return matches.FirstOrDefault(p => p.EnableInMainMenu == true) ?? matches.FirstOrDefault();
    }

    public static IReadOnlyList<DashboardPlugin> Merge(IEnumerable<PluginInfo> plugins, IEnumerable<PackageInfo> packages, IEnumerable<ConfigurationPageInfo> pages) =>
        packages.Select(p => p.Guid).Concat(plugins.Select(p => p.Id)).OfType<Guid>().Distinct()
            .Select(id => new DashboardPlugin(id, BestPlugin(plugins, id), packages.FirstOrDefault(p => p.Guid == id), BestPage(pages, id)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public static IEnumerable<DashboardPlugin> Filter(IEnumerable<DashboardPlugin> plugins, string? status, string? category, string? query) =>
        plugins.Where(p => status switch { "Installed" => p.Status is not null, "Available" => p.Status is null, _ => true })
            .Where(p => string.IsNullOrEmpty(category) || (category.Equals("other", StringComparison.OrdinalIgnoreCase)
                ? p.Category is not null && !Categories.Take(8).Any(c => c.Category.Equals(p.Category, StringComparison.OrdinalIgnoreCase))
                : category.Equals(p.Category, StringComparison.OrdinalIgnoreCase)))
            .Where(p => p.Name.Contains(query ?? string.Empty, StringComparison.OrdinalIgnoreCase));

    public static bool IsTrusted(VersionInfo version) => version.RepositoryUrl?.StartsWith("https://repo.jellyfin.org/", StringComparison.Ordinal) == true;

    public Task InstallAsync(DashboardPlugin plugin, VersionInfo version) => api.Client.Packages.Installed[plugin.Name].PostAsync(r =>
    {
        r.QueryParameters.AssemblyGuid = plugin.Id;
        r.QueryParameters.Version = version.Version;
        r.QueryParameters.RepositoryUrl = version.RepositoryUrl;
    });

    public Task EnableAsync(DashboardPlugin plugin, bool enabled) => enabled
        ? api.Client.Plugins[plugin.Id][plugin.Version!.Version!].Enable.PostAsync()
        : api.Client.Plugins[plugin.Id][plugin.Version!.Version!].Disable.PostAsync();

    public Task UninstallAsync(DashboardPlugin plugin) => api.Client.Plugins[plugin.Id][plugin.Version!.Version!].DeleteAsync();
}
