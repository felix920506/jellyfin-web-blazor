using Jellyfin.Web.Services;

namespace Jellyfin.Web.Routing;

/// <summary>What the router should do with a requested page.</summary>
public abstract record GuardResult
{
    public static readonly GuardResult Allow = new Allowed();

    public sealed record Allowed : GuardResult;

    /// <summary>Navigate (replacing the history entry) to <paramref name="Url"/> instead.</summary>
    public sealed record Redirect(string Url) : GuardResult;

    /// <summary>Show the connection error page.</summary>
    public sealed record ConnectionError(ConnectResult Result) : GuardResult;
}

/// <summary>
/// Decides whether a page may render. Port of components/ConnectionRequired.tsx: runs the first
/// automatic connection, then enforces the page's <see cref="AccessLevel"/>.
/// </summary>
public sealed class RouteGuard(ServerConnections connections, JellyfinApi api)
{
    public async Task<GuardResult> CheckAsync(AccessLevel access, AppLocation location)
    {
        ConnectResult? first = null;
        if (!connections.FirstConnectionDone)
        {
            first = await connections.ConnectAsync();
            connections.FirstConnectionDone = true;

            if (first.State is ConnectionState.ServerMismatch or ConnectionState.ServerUpdateNeeded or ConnectionState.Unavailable)
            {
                // Let the next navigation try again.
                connections.FirstConnectionDone = false;
                return new GuardResult.ConnectionError(first);
            }
        }

        if (access == AccessLevel.Wizard)
        {
            var info = await api.GetPublicInfoAsync();
            return info?.StartupWizardCompleted == false ? GuardResult.Allow : new GuardResult.Redirect("/home");
        }

        if (first is not null && first.State != ConnectionState.SignedIn && !api.IsLoggedIn)
        {
            if (first.State == ConnectionState.ServerSignIn && first.SystemInfo?.StartupWizardCompleted == false)
            {
                return new GuardResult.Redirect("/wizard/start");
            }

            return Bounce(first, location);
        }

        if (access is AccessLevel.User or AccessLevel.Admin && !api.IsLoggedIn)
        {
            return Bounce(await connections.ConnectAsync(), location);
        }

        if (access == AccessLevel.Admin && !api.IsAdmin)
        {
            return new GuardResult.Redirect("/home");
        }

        return GuardResult.Allow;
    }

    private static GuardResult Bounce(ConnectResult result, AppLocation location)
    {
        switch (result.State)
        {
            case ConnectionState.SignedIn:
                return Redirect(location, "/home");
            case ConnectionState.ServerSignIn:
                return location.Path.Equals("/login", StringComparison.OrdinalIgnoreCase)
                    ? GuardResult.Allow
                    : new GuardResult.Redirect($"/login?serverid={result.Server?.Id}&url={Uri.EscapeDataString(location.Url)}");
            case ConnectionState.ServerSelection:
                return Redirect(location, "/selectserver");
            default:
                return new GuardResult.ConnectionError(result);
        }
    }

    private static GuardResult Redirect(AppLocation location, string url) =>
        location.Path.Equals(url, StringComparison.OrdinalIgnoreCase) ? GuardResult.Allow : new GuardResult.Redirect(url);
}
