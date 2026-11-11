using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.User;

/// <summary>An option of a settings select.</summary>
public sealed record SelectOption(string Value, string Name);

/// <summary>What the user settings pages share: which user is edited and with which settings object.</summary>
public static class UserPages
{
    /// <summary>The <c>userId</c> page parameter, or the signed-in user (<c>params.userId || getCurrentUserId()</c>).</summary>
    public static Guid? ResolveUserId(JellyfinApi api, string? userId) =>
        !string.IsNullOrEmpty(userId) && Guid.TryParse(userId, out var id) ? id : api.UserId;

    /// <summary>
    /// The settings object a page edits: the app's own for the signed-in user, a separate one for
    /// another user (so that editing someone else's settings never changes the running app).
    /// </summary>
    public static UserSettings SettingsFor(Guid userId, JellyfinApi api, UserSettings current, AppSettings appSettings) =>
        userId == api.UserId ? current : new UserSettings(appSettings, api);

    public static Task<UserDto?> GetUserAsync(JellyfinApi api, Guid userId) => api.Client.Users[userId].GetAsync();

    public static Task UpdateConfigurationAsync(JellyfinApi api, Guid userId, UserConfiguration configuration) =>
        api.Client.Users.Configuration.PostAsync(configuration, request => request.QueryParameters.UserId = userId);

    /// <summary>The id as the original client writes it into links and element attributes.</summary>
    public static string IdText(Guid? id) => id?.ToString("N") ?? string.Empty;
}
