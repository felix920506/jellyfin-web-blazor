using System.Net;
using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Foundation;

public sealed class CredentialStoreTests
{
    [Fact]
    public void Reads_legacy_shape_and_selects_the_last_connection_address()
    {
        var app = new TestApp();
        app.Browser.StorageSet(CredentialStore.StorageKey, "{\"Servers\":[{\"Id\":\"one\",\"ManualAddress\":\"http://manual\",\"LocalAddress\":\"http://local\",\"RemoteAddress\":\"http://remote\",\"LastConnectionMode\":0,\"unknown\":42}]}");

        var store = app.Get<CredentialStore>();
        Assert.Equal("http://local", store.Servers[0].Address);
        Assert.Equal(JsonValueKind.Number, store.Servers[0].Extra!["unknown"].ValueKind);
        store.Servers[0].LastConnectionMode = ConnectionMode.Manual;
        Assert.Equal("http://manual", store.Servers[0].Address);
    }

    [Fact]
    public void Merges_by_id_without_losing_existing_addresses_and_can_replace()
    {
        var app = new TestApp();
        var store = app.Get<CredentialStore>();
        store.Replace([new ServerInfo { Id = "A", Name = "old", LocalAddress = "http://local" }]);

        var merged = store.AddOrUpdate(new ServerInfo { Id = "a", Name = "new", RemoteAddress = "http://remote", AccessToken = "token", UserId = "user" });

        Assert.Single(store.Servers);
        Assert.Same(store.Servers[0], merged);
        Assert.Equal("new", merged.Name);
        Assert.Equal("http://local", merged.LocalAddress);
        Assert.Equal("http://remote", merged.RemoteAddress);
        Assert.Equal("token", merged.AccessToken);
        Assert.Equal("user", merged.UserId);
        Assert.Contains("\"Servers\"", app.Browser.StorageGet(CredentialStore.StorageKey));
    }
}

public sealed class AppSettingsTests
{
    [Fact]
    public void Uses_defaults_typed_keys_and_change_notifications()
    {
        var app = new TestApp();
        var settings = app.Get<AppSettings>();
        var changed = new List<string>();
        settings.Changed += changed.Add;

        Assert.True(settings.EnableAutoLogin);
        Assert.False(settings.EnableGamepad);
        Assert.Equal(320000, settings.MaxStaticMusicBitrate);
        settings.EnableGamepad = true;
        settings.Set("custom", 12, TestApp.UserId.ToString("N"));

        Assert.Equal("true", app.Browser.StorageGet("enableGamepad"));
        Assert.Equal("12", app.Browser.StorageGet(TestApp.UserId.ToString("N") + "-custom"));
        Assert.Equal(["enableGamepad", "custom"], changed);
        Assert.False(AppSettings.ToBoolean("TRUE", false));
    }
}

public sealed class ServerConnectionsTests
{
    private static ServerInfo Server(string address = TestApp.ServerUrl) => new()
    {
        Id = TestApp.ServerId,
        ManualAddress = address,
        ManualAddressOnly = true,
        LastConnectionMode = ConnectionMode.Manual,
        DateLastAccessed = 1
    };

    [Fact]
    public async Task Connect_without_saved_servers_returns_selection()
    {
        var result = await new TestApp().Get<ServerConnections>().ConnectAsync();
        Assert.Equal(ConnectionState.ServerSelection, result.State);
    }

    [Fact]
    public async Task Connect_reports_sign_in_when_server_is_reachable_without_auto_login()
    {
        var app = new TestApp();
        var server = Server();
        app.Get<CredentialStore>().Replace([server]);

        var result = await app.Get<ServerConnections>().ConnectToServerAsync(server, enableAutoLogin: false);

        Assert.Equal(ConnectionState.ServerSignIn, result.State);
        Assert.Null(server.AccessToken);
        Assert.Equal(TestApp.ServerId, result.SystemInfo!.Id);
    }

    [Fact]
    public async Task Connect_restores_a_valid_saved_token_and_raises_the_event()
    {
        var app = new TestApp();
        var server = Server();
        server.AccessToken = "saved-token";
        server.UserId = TestApp.UserId.ToString("N");
        app.Get<CredentialStore>().Replace([server]);
        app.Server.OnGet("/Users/Me", new { Id = TestApp.UserId, Name = "tester", ServerId = TestApp.ServerId, Policy = new { IsAdministrator = true } });
        UserDto? signedIn = null;
        app.Get<ServerConnections>().LocalUserSignedIn += user => { signedIn = user; return Task.CompletedTask; };

        var result = await app.Get<ServerConnections>().ConnectToServerAsync(server);

        Assert.Equal(ConnectionState.SignedIn, result.State);
        Assert.Equal("tester", signedIn!.Name);
        Assert.True(app.Get<JellyfinApi>().IsLoggedIn);
        Assert.Contains(app.Server.Requests, request => request.Path.EndsWith("/Users/Me", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Invalid_token_is_cleared_and_returns_server_sign_in()
    {
        var app = new TestApp();
        var server = Server();
        server.AccessToken = "expired";
        server.UserId = TestApp.UserId.ToString("N");
        app.Get<CredentialStore>().Replace([server]);
        app.Server.On(HttpMethod.Get, "/Users/Me", _ => FakeHttp.Status(HttpStatusCode.Unauthorized));

        var result = await app.Get<ServerConnections>().ConnectToServerAsync(server);

        Assert.Equal(ConnectionState.ServerSignIn, result.State);
        Assert.Null(server.AccessToken);
        Assert.Null(server.UserId);
    }

    [Fact]
    public async Task Reports_unavailable_mismatch_and_old_server_states()
    {
        var app = new TestApp();
        var connections = app.Get<ServerConnections>();
        app.Server.On(HttpMethod.Get, "/System/Info/Public", request =>
            request.Uri.Host.Equals("missing", StringComparison.OrdinalIgnoreCase)
                ? FakeHttp.Status(HttpStatusCode.NotFound)
                : FakeHttp.Json(new { Id = TestApp.ServerId, ServerName = "Test Server", Version = "10.11.0", StartupWizardCompleted = true }));
        var unavailable = await connections.ConnectToServerAsync(Server("http://missing"), false);
        Assert.Equal(ConnectionState.Unavailable, unavailable.State);

        var mismatch = Server();
        mismatch.Id = "another-server";
        var mismatchResult = await connections.ConnectToServerAsync(mismatch, false);
        Assert.Equal(ConnectionState.ServerMismatch, mismatchResult.State);

        app.Server.OnGet("/System/Info/Public", new { Id = TestApp.ServerId, ServerName = "Old", Version = "10.8.0", StartupWizardCompleted = true });
        var old = await connections.ConnectToServerAsync(Server(), false);
        Assert.Equal(ConnectionState.ServerUpdateNeeded, old.State);
    }

    [Fact]
    public async Task Address_connection_normalizes_scheme_and_tries_https_then_http()
    {
        var app = new TestApp();
        app.Server.On(HttpMethod.Get, "/System/Info/Public", request =>
            request.Uri.Host.Equals("example.test", StringComparison.OrdinalIgnoreCase)
                ? FakeHttp.Status(HttpStatusCode.NotFound)
                : FakeHttp.Json(new { Id = TestApp.ServerId, Version = "10.11.0", StartupWizardCompleted = true }));
        var result = await app.Get<ServerConnections>().ConnectToAddressAsync("  example.test///", false);

        Assert.Equal(ConnectionState.Unavailable, result.State);
        Assert.Equal("https://example.test/System/Info/Public", app.Server.Requests[0].Uri.ToString());
        Assert.Equal("http://example.test/System/Info/Public", app.Server.Requests[1].Uri.ToString());
    }

    [Fact]
    public async Task Logout_clears_non_guest_credentials_and_raises_signed_out()
    {
        var app = new TestApp();
        app.SignIn();
        app.Get<CredentialStore>().Replace([
            new ServerInfo { Id = "one", AccessToken = "a", UserId = "u" },
            new ServerInfo { Id = "guest", AccessToken = "g", UserId = "g", UserLinkType = "Guest" }
        ]);
        var signedOut = 0;
        app.Get<ServerConnections>().LocalUserSignedOut += () => { signedOut++; return Task.CompletedTask; };

        await app.Get<ServerConnections>().LogoutAsync();

        Assert.Equal(1, signedOut);
        Assert.Null(app.Get<JellyfinApi>().AccessToken);
        Assert.Null(app.Get<CredentialStore>().Servers[0].AccessToken);
        Assert.Equal("g", app.Get<CredentialStore>().Servers[1].AccessToken);
    }
}

public sealed class UserSettingsTests
{
    [Fact]
    public async Task Loads_server_preferences_and_debounces_display_preference_updates()
    {
        var app = new TestApp();
        app.SignIn();
        app.Server.OnGet("/DisplayPreferences/usersettings", new { CustomPrefs = new { dashboardTheme = "light", skipBackLength = "7000" } });
        app.Server.OnPost("/DisplayPreferences/usersettings", _ => FakeHttp.Status(HttpStatusCode.NoContent));
        var settings = app.Get<UserSettings>();

        await settings.SetUserAsync(TestApp.UserId);
        Assert.Equal("light", settings.DashboardTheme);
        Assert.Equal(7000, settings.SkipBackLength);
        settings.SkipForwardLength = 15000;
        await Task.Delay(500);

        var request = Assert.Single(app.Server.RequestsTo("/DisplayPreferences/usersettings"), r => r.Method == HttpMethod.Post);
        Assert.Contains("skipForwardLength", request.Body, StringComparison.Ordinal);
        Assert.Equal("15000", app.Browser.LocalStorage[$"{TestApp.UserId:N}-skipForwardLength"]);
    }
}

public sealed class ThemeAndEventTests
{
    [Fact]
    public void Theme_manager_applies_configured_theme_and_avoids_duplicate_events()
    {
        var app = new TestApp();
        app.Get<WebConfig>().Set(new WebConfigData { Themes = [new ThemeOption { Id = "light", Name = "Light", Color = "#fff", Default = true }] });
        var theme = app.Get<ThemeManager>();
        var changes = 0;
        theme.Changed += () => changes++;

        theme.SetTheme("missing");
        theme.SetTheme("light");
        theme.SetTheme("light");

        Assert.Equal("light", theme.CurrentId);
        Assert.False(theme.IsDark);
        Assert.Equal("light", app.Browser.Attributes["html:data-theme"]);
        Assert.Equal("#fff", app.Browser.ThemeColor);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Server_events_dispatch_and_dispose_subscriptions()
    {
        var app = new TestApp();
        var events = app.Get<ServerEvents>();
        var values = new List<string>();
        var subscription = events.Subscribe("UserDataChanged", data => values.Add(data.GetProperty("ItemId").GetString()!));
        events.Dispatch("UserDataChanged", JsonSerializer.SerializeToElement(new { ItemId = "one" }));
        subscription.Dispose();
        events.Dispatch("UserDataChanged", JsonSerializer.SerializeToElement(new { ItemId = "two" }));

        Assert.Equal(["one"], values);
    }
}
