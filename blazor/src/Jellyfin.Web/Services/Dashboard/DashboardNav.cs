namespace Jellyfin.Web.Services;

/// <summary>
/// One entry of the dashboard drawer: a link, or (with <see cref="Children"/>) a collapsible group.
/// </summary>
/// <param name="TextKey">Translation key of the label.</param>
/// <param name="Icon">Material icon name.</param>
/// <param name="Path">App path such as "/dashboard/users"; null for a group.</param>
public sealed record DashboardLink(string TextKey, string Icon, string? Path = null)
{
    /// <summary>Other paths on which the link is highlighted (the original's <c>includePaths</c>).</summary>
    public IReadOnlyList<string> AlsoSelectedOn { get; init; } = [];

    /// <summary>The links of a collapsible group.</summary>
    public IReadOnlyList<DashboardLink> Children { get; init; } = [];

    public string Href => "#" + Path;
}

/// <summary>A titled block of drawer entries.</summary>
public sealed record DashboardSection(string TitleKey, IReadOnlyList<DashboardLink> Links);

/// <summary>
/// The dashboard's navigation: the drawer sections (port of
/// apps/dashboard/components/drawer/sections/*.tsx) and the help link of each page (port of
/// components/toolbar/HelpButton.tsx).
/// <para>
/// To add a page to the drawer, add a <see cref="DashboardLink"/> here; nothing else needs to change.
/// </para>
/// </summary>
public static class DashboardNav
{
    private const string Docs = "https://jellyfin.org/docs/general/";

    public static readonly IReadOnlyList<DashboardSection> Sections =
    [
        new("TabServer",
        [
            new("TabDashboard", "dashboard", "/dashboard"),
            new("General", "settings", "/dashboard/settings"),
            new("HeaderBranding", "palette", "/dashboard/branding"),
            new("HeaderUsers", "people", "/dashboard/users"),
            new("HeaderLibraries", "library_add")
            {
                Children =
                [
                    new("HeaderLibraries", string.Empty, "/dashboard/libraries"),
                    new("Display", string.Empty, "/dashboard/libraries/display"),
                    new("LabelMetadata", string.Empty, "/dashboard/libraries/metadata"),
                    new("TabNfoSettings", string.Empty, "/dashboard/libraries/nfo"),
                ],
            },
            new("TitlePlayback", "play_circle")
            {
                Children =
                [
                    new("Transcoding", string.Empty, "/dashboard/playback/transcoding"),
                    new("ButtonResume", string.Empty, "/dashboard/playback/resume"),
                    new("TabStreaming", string.Empty, "/dashboard/playback/streaming"),
                    new("Trickplay", string.Empty, "/dashboard/playback/trickplay"),
                ],
            },
        ]),
        new("HeaderDevices",
        [
            new("HeaderDevices", "devices", "/dashboard/devices"),
            new("HeaderActivity", "analytics", "/dashboard/activity"),
        ]),
        new("LiveTV",
        [
            new("LiveTV", "live_tv", "/dashboard/livetv"),
            new("HeaderDVR", "dvr", "/dashboard/livetv/recordings"),
        ]),
        new(PluginsSection,
        [
            new("TabPlugins", "extension", "/dashboard/plugins")
            {
                AlsoSelectedOn = ["/configurationpage", "/dashboard/plugins/repositories"],
            },
        ]),
        new("TabAdvanced",
        [
            new("TabNetworking", "lan", "/dashboard/networking"),
            new("HeaderApiKeys", "vpn_key", "/dashboard/keys"),
            new("HeaderBackups", "backup", "/dashboard/backups"),
            new("TabLogs", "article", "/dashboard/logs"),
            new("TabScheduledTasks", "schedule", "/dashboard/tasks"),
        ]),
    ];

    /// <summary>Title key of the section that also lists the plugins' own menu pages.</summary>
    public const string PluginsSection = "TabPlugins";

    /// <summary>Whether a link is highlighted on <paramref name="path"/>: the exact page or one of its extra paths.</summary>
    public static bool IsSelected(DashboardLink link, string path) =>
        link.Path is not null
        && (SamePath(link.Path, path) || link.AlsoSelectedOn.Any(other => SamePath(other, path)));

    /// <summary>Whether a group starts out expanded: the current page is one of its links.</summary>
    public static bool ContainsPath(DashboardLink group, string path) =>
        group.Children.Any(child => IsSelected(child, path));

    /// <summary>The documentation page behind the help button, or null when the page has none.</summary>
    public static string? HelpUrl(string path)
    {
        path = path.TrimEnd('/').ToLowerInvariant();
        var known = path switch
        {
            "/dashboard/devices" => Docs + "server/devices",
            "/dashboard/libraries" => Docs + "server/libraries",
            "/dashboard/livetv" or "/dashboard/livetv/tuner" or "/dashboard/livetv/recordings" => Docs + "server/live-tv/",
            "/dashboard/livetv/guide" => Docs + "server/live-tv/setup-guide#adding-guide-data",
            "/dashboard/networking" => Docs + "networking/",
            "/dashboard/playback/transcoding" => Docs + "server/transcoding",
            "/dashboard/plugins" => Docs + "server/plugins/",
            "/dashboard/plugins/repositories" => Docs + "server/plugins/#repositories",
            "/dashboard/branding" or "/dashboard/settings" => Docs + "server/settings",
            "/dashboard/tasks" => Docs + "server/tasks",
            "/dashboard/users" => Docs + "server/users/adding-managing-users",
            "/dashboard/backups" => Docs + "administration/backup-and-restore/",
            _ => null,
        };

        if (known is not null)
        {
            return known;
        }

        // "/dashboard/users/:userId/:tab"
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments is ["dashboard", "users", _, _] ? Docs + "server/users/" : null;
    }

    /// <summary>The route of a plugin's configuration page ("configurationpage?name=..."), as Dashboard.getPluginUrl().</summary>
    public static string PluginPageUrl(string? name) => "configurationpage?name=" + Uri.EscapeDataString(name ?? string.Empty);

    /// <summary>
    /// The server version as shown to people: "10.11.0" for "10.11.0.0". Stands in for the SDK's
    /// getDisplayVersion().
    /// </summary>
    public static string? DisplayVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return null;
        }

        var parts = version.Split('.');
        return parts.Length == 4 && parts[3] == "0" ? string.Join('.', parts[..3]) : version;
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
