using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Jellyfin.Web.Services;

/// <summary>
/// Startup work that must finish before the first render, and the wiring between the
/// session (sign-in / sign-out) and the per-user services. Port of the bootstrap in index.jsx.
/// </summary>
public sealed class AppStartup(
    WebConfig config,
    AppHost appHost,
    ServerConnections connections,
    Globalize globalize,
    UserSettings userSettings,
    ThemeManager theme,
    AppRouter router,
    ServerEvents events,
    UserViews userViews,
    JellyfinApi api,
    IEnumerable<IStartupTask> startupTasks)
{
    private bool _initialized;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        await config.LoadAsync();
        appHost.Initialize();

        connections.LocalUserSignedIn += OnSignedInAsync;
        connections.LocalUserSignedOut += OnSignedOutAsync;
        events.Subscribe("LibraryChanged", _ => userViews.Invalidate());

        await connections.InitializeAsync();
        await globalize.InitializeAsync();
        theme.SetTheme(null);
        foreach (var task in startupTasks)
        {
            await task.StartAsync();
        }

        router.Start();
    }

    private async Task OnSignedInAsync(UserDto user)
    {
        await userSettings.SetUserAsync(user.Id ?? api.UserId);
        await globalize.SetUserCultureAsync(userSettings.Language, userSettings.DateTimeLocale);
        theme.ApplyUserTheme();
        userViews.Invalidate();
        events.Start();
    }

    private async Task OnSignedOutAsync()
    {
        events.Stop();
        await userSettings.SetUserAsync(null);
        await globalize.SetUserCultureAsync(null, null);
        theme.SetTheme(null);
        userViews.Invalidate();
    }
}

public static class ServiceCollectionExtensions
{
    /// <summary>Registers every service of the app. <paramref name="baseAddress"/> is where the app's own static files live.</summary>
    public static IServiceCollection AddJellyfinWeb(this IServiceCollection services, string baseAddress)
    {
        services.AddSingleton<Interop.IBrowser, Interop.JsBrowser>();
        services.AddSingleton(new AppAssets(new HttpClient { BaseAddress = new Uri(baseAddress) }));
        services.AddJellyfinWebCore();
        services.AddMudServices();
        return services;
    }

    /// <summary>Everything except the browser and static-file access (tests substitute those).</summary>
    public static IServiceCollection AddJellyfinWebCore(this IServiceCollection services)
    {
        services.AddSingleton<AppHost>();
        services.AddSingleton<AppSettings>();
        services.AddSingleton<WebConfig>();
        services.AddSingleton<CredentialStore>();
        services.AddSingleton(sp => new JellyfinApi(sp.GetService<ApiHttpClient>()?.Http ?? new HttpClient(), sp.GetRequiredService<AppHost>()));
        services.AddSingleton<ServerConnections>();
        services.AddSingleton<Globalize>();
        services.AddSingleton<UserSettings>();
        services.AddSingleton<ThemeManager>();
        services.AddSingleton<LoadingService>();
        services.AddSingleton<ToastService>();
        services.AddSingleton<AppDialogs>();
        services.AddSingleton<AppRouter>();
        services.AddSingleton(new Routing.RouteTable(typeof(ServiceCollectionExtensions).Assembly));
        services.AddSingleton<Routing.RouteGuard>();
        services.AddSingleton<ServerEvents>();
        services.AddSingleton<UserViews>();
        services.AddSingleton<ShellState>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPlaybackManager, NullPlaybackManager>();
        services.AddSingleton<AppStartup>();
        Composition.AddAttributedServices(services, typeof(ServiceCollectionExtensions).Assembly);
        return services;
    }
}

/// <summary>Optional override of the HttpClient used for server requests (tests register one).</summary>
public sealed record ApiHttpClient(HttpClient Http);
