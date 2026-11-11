namespace Jellyfin.Web.Tests.Support;

/// <summary>
/// Base class for component tests: a bUnit context wired to a <see cref="TestApp"/> with English
/// strings loaded. Inherit from it (in a .razor test: <c>@inherits AppTestContext</c>), script
/// <c>App.Server</c>, optionally <c>App.SignIn(services: Services)</c>, then <c>Render</c>.
/// It also disposes the context asynchronously, which MudBlazor's services require.
/// </summary>
public abstract class AppTestContext : BunitContext, IAsyncLifetime
{
    public TestApp App { get; private set; } = default!;

    public virtual async Task InitializeAsync() => App = await this.UseTestAppAsync();

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    /// <summary>Signs in the test user in this context's services.</summary>
    protected Jellyfin.Sdk.Generated.Models.UserDto SignIn(bool admin = false) => App.SignIn(admin, Services);
}
