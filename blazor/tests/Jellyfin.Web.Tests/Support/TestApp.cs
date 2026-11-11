using System.Net;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Jellyfin.Web.Tests.Support;

/// <summary>
/// The app's services over a <see cref="FakeBrowser"/> and a scripted server (<see cref="FakeHttp"/>).
/// Static files (config.json, translations) are served from the repository, so real strings render.
/// <code>
/// var app = new TestApp();                 // plain services
/// app.Server.OnGet("/Items", new { Items = new[] { ... }, TotalRecordCount = 1 });
/// app.SignIn();                            // a signed-in user without any HTTP traffic
/// var views = app.Get&lt;UserViews&gt;();
/// </code>
/// For component tests call <see cref="TestAppExtensions.UseTestApp"/> on a BunitContext.
/// </summary>
public sealed class TestApp
{
    public const string ServerUrl = "http://jellyfin.test";
    public const string ServerId = "a29fbb466c0749999a3b9f1af5dc11e6";
    public static readonly Guid UserId = Guid.Parse("0f0e0d0c-0b0a-0908-0706-050403020100");

    private ServiceProvider? _provider;

    public TestApp()
    {
        Assets.On(HttpMethod.Get, string.Empty, ServeStaticFile);
        Server.OnGet("/System/Info/Public", new
        {
            Id = ServerId,
            ServerName = "Test Server",
            Version = "10.11.0",
            ProductName = "Jellyfin Server",
            LocalAddress = ServerUrl,
            StartupWizardCompleted = true
        });
    }

    public FakeBrowser Browser { get; } = new();

    /// <summary>The Jellyfin server. Everything not scripted answers 404.</summary>
    public FakeHttp Server { get; } = new();

    /// <summary>The app's own static files; defaults to the repository's files.</summary>
    public FakeHttp Assets { get; } = new();

    public IServiceProvider Services => _provider ??= Configure(new ServiceCollection()).BuildServiceProvider();

    public T Get<T>()
        where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Registers the app's services in <paramref name="services"/> (used by bUnit contexts).</summary>
    public IServiceCollection Configure(IServiceCollection services)
    {
        services.AddSingleton<IBrowser>(Browser);
        services.AddSingleton(new AppAssets(Assets.CreateClient("http://localhost/web/")));
        services.AddSingleton(new ApiHttpClient(Server.CreateClient()));
        services.AddJellyfinWebCore();
        services.AddMudServices();
        return services;
    }

    /// <summary>Loads config.json and the English strings, as startup does.</summary>
    public async Task<TestApp> LoadAssetsAsync(IServiceProvider? services = null)
    {
        services ??= Services;
        await services.GetRequiredService<WebConfig>().LoadAsync();
        await services.GetRequiredService<Globalize>().InitializeAsync();
        return this;
    }

    /// <summary>Makes the API a signed-in session against <see cref="ServerUrl"/> without any HTTP traffic.</summary>
    public UserDto SignIn(bool admin = false, IServiceProvider? services = null, Action<UserDto>? configure = null)
    {
        services ??= Services;
        var user = new UserDto
        {
            Id = UserId,
            Name = "tester",
            ServerId = ServerId,
            Policy = new UserPolicy { IsAdministrator = admin },
            Configuration = new UserConfiguration()
        };
        configure?.Invoke(user);

        var api = services.GetRequiredService<JellyfinApi>();
        api.SetServer(ServerUrl, ServerId, "Test Server");
        api.SetAuthentication("test-token", UserId.ToString("N"), user);
        return user;
    }

    private static HttpResponseMessage ServeStaticFile(RecordedRequest request)
    {
        var path = request.Path;
        var file = path.EndsWith("/config.json", StringComparison.Ordinal)
            ? Path.Combine(RepositoryRoot, "blazor/src/Jellyfin.Web/wwwroot/config.json")
            : path.Contains("/generated/strings/", StringComparison.Ordinal)
                ? Path.Combine(RepositoryRoot, "src/strings", Path.GetFileName(path))
                : null;
        return file is not null && File.Exists(file)
            ? FakeHttp.Json(File.ReadAllText(file))
            : FakeHttp.Status(HttpStatusCode.NotFound);
    }

    /// <summary>The checkout this test assembly was built from.</summary>
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src/strings/en-us.json")) && Directory.Exists(Path.Combine(dir.FullName, "blazor")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }
}

public static class TestAppExtensions
{
    /// <summary>
    /// Wires a bUnit context to a <see cref="TestApp"/>: app services, loose JS interop, loaded
    /// English strings. Returns the app so the test can script the server and sign in.
    /// <code>
    /// await using var ctx = new BunitContext();
    /// var app = await ctx.UseTestAppAsync();
    /// app.SignIn(services: ctx.Services);
    /// var cut = ctx.Render&lt;MyPage&gt;();
    /// </code>
    /// </summary>
    public static async Task<TestApp> UseTestAppAsync(this BunitContext context, TestApp? app = null)
    {
        app ??= new TestApp();
        app.Configure(context.Services);
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        await app.LoadAssetsAsync(context.Services);
        return app;
    }
}
