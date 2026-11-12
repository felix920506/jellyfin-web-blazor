using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using PluginService = Jellyfin.Web.Services.DashboardPlugins;

namespace Jellyfin.Web.Tests.PluginDashboard;

public sealed class DashboardPluginLogicTests
{
    [Fact]
    public void Duplicate_installed_entries_prefer_disabled_then_restart()
    {
        var id = Guid.NewGuid();
        PluginInfo Entry(PluginInfo_Status status) => new() { Id = id, Status = status };
        var active = Entry(PluginInfo_Status.Active);
        var restart = Entry(PluginInfo_Status.Restart);
        var disabled = Entry(PluginInfo_Status.Disabled);
        Assert.Same(disabled, PluginService.BestPlugin([active, restart, disabled], id));
        Assert.Same(restart, PluginService.BestPlugin([active, restart], id));
        Assert.Same(active, PluginService.BestPlugin([active], id));
        Assert.Null(PluginService.BestPlugin([active], Guid.NewGuid()));
    }

    [Fact]
    public void Configuration_pages_prefer_main_menu_and_fall_back_to_first()
    {
        var id = Guid.NewGuid();
        var first = new ConfigurationPageInfo { PluginId = id, Name = "first" };
        var menu = new ConfigurationPageInfo { PluginId = id, Name = "menu", EnableInMainMenu = true };
        Assert.Same(menu, PluginService.BestPage([first, menu], id));
        Assert.Same(first, PluginService.BestPage([first], id));
        Assert.Null(PluginService.BestPage([first], Guid.NewGuid()));
    }

    [Fact]
    public void Merge_keeps_bundled_and_catalog_only_plugins_and_selects_installed_revision()
    {
        var id = Guid.NewGuid();
        var bundledId = Guid.Parse("8c95c4d2e50c4fb0a4f36c06ff0f9a1a");
        var version = new VersionInfo { Version = "1.0", RepositoryName = "Official" };
        var packages = new[] { new PackageInfo { Guid = id, Name = "Zebra", Versions = [new() { Version = "2.0" }, version] } };
        var plugins = new[] { new PluginInfo { Id = id, Name = "Zebra", Version = "1.0", Status = PluginInfo_Status.Active }, new PluginInfo { Id = bundledId, Name = "MusicBrainz", Version = "1", Status = PluginInfo_Status.Active } };
        var merged = PluginService.Merge(plugins, packages, []);
        Assert.Equal(["MusicBrainz", "Zebra"], merged.Select(p => p.Name));
        Assert.Equal("Music", merged[0].Category);
        Assert.Same(version, merged[1].Version);
        Assert.Equal("1", merged[0].Version?.Version);
    }

    [Fact]
    public void Filtering_matches_original_status_category_other_and_case_insensitive_search()
    {
        var installed = new DashboardPlugin(Guid.NewGuid(), new() { Name = "Installed", Status = PluginInfo_Status.Disabled }, new() { Category = "Music" }, null);
        var available = new DashboardPlugin(Guid.NewGuid(), null, new() { Name = "Available", Category = "Custom" }, null);
        var uncategorized = new DashboardPlugin(Guid.NewGuid(), null, new() { Name = "No category" }, null);
        DashboardPlugin[] all = [installed, available, uncategorized];
        Assert.Equal([installed], PluginService.Filter(all, "Installed", null, "INSTALL"));
        Assert.Equal([available, uncategorized], PluginService.Filter(all, "Available", null, null));
        Assert.Equal([available], PluginService.Filter(all, "All", "other", null));
        Assert.Equal([installed], PluginService.Filter(all, "All", "music", null));
        Assert.Empty(PluginService.Filter(all, "All", null, "absent"));
    }

    [Theory]
    [InlineData("https://repo.jellyfin.org/releases/plugin/manifest.json", true)]
    [InlineData("https://repo.jellyfin.org.evil/manifest.json", false)]
    [InlineData("http://repo.jellyfin.org/manifest.json", false)]
    [InlineData("https://community.example/manifest.json", false)]
    [InlineData(null, false)]
    public void Only_the_original_official_repository_prefix_is_trusted(string? url, bool trusted) =>
        Assert.Equal(trusted, PluginService.IsTrusted(new VersionInfo { RepositoryUrl = url }));
}
