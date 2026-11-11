using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.User;

/// <summary>Scripting and storage helpers shared by the user settings page tests.</summary>
public static class SettingsTestExtensions
{
    /// <summary>A user other than the signed-in one.</summary>
    public static readonly Guid OtherUserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    /// <summary>Scripts <c>GET Users/{id}</c> (whichever way the id is written into the URL).</summary>
    public static void ScriptUser(this AppTestContext context, Guid id, object user)
    {
        context.App.Server.OnGet("/Users/" + id.ToString("N"), user);
        context.App.Server.OnGet("/Users/" + id.ToString("D"), user);
    }

    /// <summary>The requests that read the user with the given id.</summary>
    public static IReadOnlyList<RecordedRequest> UserLoads(this AppTestContext context, Guid id) =>
        context.App.Server.Requests
            .Where(request => request.Method == HttpMethod.Get
                && (request.Path.EndsWith("/Users/" + id.ToString("N"), StringComparison.OrdinalIgnoreCase)
                    || request.Path.EndsWith("/Users/" + id.ToString("D"), StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// <summary>Scripts the display preferences the user settings are stored in, and accepts their update.</summary>
    public static void ScriptPreferences(this AppTestContext context, object? customPrefs = null)
    {
        context.App.Server.OnGet("/DisplayPreferences/usersettings", new { Id = "usersettings", CustomPrefs = customPrefs ?? new { } });
        context.App.Server.OnPost("/DisplayPreferences/usersettings");
    }

    /// <summary>The custom preferences last posted to the server (null when nothing was posted).</summary>
    public static System.Text.Json.JsonElement? PostedPreferences(this AppTestContext context)
    {
        var request = context.App.Server.Requests.LastOrDefault(r => r.Method == HttpMethod.Post
            && r.Path.EndsWith("/DisplayPreferences/usersettings", StringComparison.OrdinalIgnoreCase));
        if (request?.Body is null)
        {
            return null;
        }

        using var document = System.Text.Json.JsonDocument.Parse(request.Body);
        return document.RootElement.TryGetProperty("CustomPrefs", out var prefs) ? prefs.Clone() : null;
    }

    /// <summary>The user configuration last posted to <c>Users/Configuration</c> (null when nothing was posted).</summary>
    public static RecordedRequest? PostedConfiguration(this AppTestContext context) =>
        context.App.Server.Requests.LastOrDefault(r => r.Method == HttpMethod.Post
            && r.Path.EndsWith("/Users/Configuration", StringComparison.OrdinalIgnoreCase));

    /// <summary>The local storage key of a setting: the bare name (app-wide) or prefixed with the user id.</summary>
    public static string StorageKey(string name, Guid? userId) => userId is { } id ? id.ToString("N") + "-" + name : name;

    /// <summary>A setting of the signed-in (or the given) user in local storage.</summary>
    public static string? UserSetting(this AppTestContext context, string name, Guid? userId = null) =>
        context.App.Browser.LocalStorage.TryGetValue(StorageKey(name, userId ?? TestApp.UserId), out var value) ? value : null;

    /// <summary>An app-wide setting in local storage.</summary>
    public static string? AppSetting(this AppTestContext context, string name) =>
        context.App.Browser.LocalStorage.TryGetValue(name, out var value) ? value : null;

    public static void StoreUserSetting(this AppTestContext context, string name, string value, Guid? userId = null) =>
        context.App.Browser.LocalStorage[StorageKey(name, userId ?? TestApp.UserId)] = value;

    public static void StoreAppSetting(this AppTestContext context, string name, string value) =>
        context.App.Browser.LocalStorage[name] = value;

    /// <summary>Whether a query parameter holds the given id, however it is formatted.</summary>
    public static bool QueryIs(this RecordedRequest request, string name, Guid id) =>
        Guid.TryParse(request.Query(name), out var value) && value == id;
}
