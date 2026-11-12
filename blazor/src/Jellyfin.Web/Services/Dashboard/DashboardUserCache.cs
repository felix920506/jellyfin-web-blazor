using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// The user the dashboard's user editor showed last. Each tab of the editor is a route of its
/// own, so switching tabs creates a new page; with this the user's name and the tabs are there at
/// once while the user is fetched again (the original keeps the user in its query cache).
/// </summary>
[AppService]
public sealed class DashboardUserCache
{
    private UserDto? _user;

    /// <summary>The remembered user when it is the one with this id.</summary>
    public UserDto? Get(Guid id) => _user?.Id == id ? _user : null;

    public void Set(UserDto user) => _user = user;

    /// <summary>Forgets the user, after a change that makes the remembered copy wrong.</summary>
    public void Clear() => _user = null;
}
