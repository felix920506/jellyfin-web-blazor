using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Pages.Session;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Foundation;

public class RoutingTests
{
    [Fact]
    public void Route_table_finds_pages_and_their_access_level()
    {
        var routes = new TestApp().Get<RouteTable>();

        var login = routes.Match("/login");
        var home = routes.Match("/HOME");

        Assert.Equal(typeof(Login), login?.PageType);
        Assert.Equal(AccessLevel.Public, login?.Access);
        Assert.Equal(AccessLevel.User, home?.Access);
        Assert.Null(routes.Match("/no-such-page"));
    }

    [Fact]
    public void Page_parameters_bind_from_the_query_and_reset_when_absent()
    {
        var query = AppLocation.Parse("/login?serverid=abc&url=%2Fdetails%3Fid%3D1").Query;

        var bound = PageParameters.Bind(typeof(Login), new Dictionary<string, string>(), query);
        var empty = PageParameters.Bind(typeof(Login), new Dictionary<string, string>(), AppLocation.Parse("/login").Query);

        Assert.Equal("abc", bound["ServerId"]);
        Assert.Equal("/details?id=1", bound["Url"]);
        Assert.Null(empty["ServerId"]);
    }

    [Fact]
    public async Task Guard_sends_signed_out_visitors_to_login_and_back()
    {
        var app = new TestApp();
        var guard = app.Get<RouteGuard>();
        await app.Get<ServerConnections>().InitializeAsync();

        var result = await guard.CheckAsync(AccessLevel.User, AppLocation.Parse("/details?id=1"));

        var redirect = Assert.IsType<GuardResult.Redirect>(result);
        var target = AppLocation.Parse(redirect.Url);
        Assert.Equal("/login", target.Path);
        Assert.Equal(TestApp.ServerId, target["serverid"]);
        Assert.Equal("/details?id=1", target["url"]);
    }

    [Fact]
    public async Task Guard_keeps_non_administrators_out_of_admin_pages()
    {
        var app = new TestApp();
        app.SignIn(admin: false);
        app.Get<ServerConnections>().FirstConnectionDone = true;

        var result = await app.Get<RouteGuard>().CheckAsync(AccessLevel.Admin, AppLocation.Parse("/dashboard"));

        Assert.Equal("/home", Assert.IsType<GuardResult.Redirect>(result).Url);
    }

    [Fact]
    public void Item_urls_follow_the_original_rules()
    {
        var app = new TestApp();
        app.SignIn();
        var router = app.Get<AppRouter>();
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var movie = router.GetRouteUrl(new BaseItemDto { Id = id, Type = BaseItemDto_Type.Movie, ServerId = TestApp.ServerId });
        var movies = router.GetRouteUrl(new BaseItemDto { Id = id, Type = BaseItemDto_Type.CollectionFolder, CollectionType = BaseItemDto_CollectionType.Movies });

        Assert.Equal($"#/details?id={id:N}&serverId={TestApp.ServerId}", movie);
        Assert.StartsWith($"#/movies?topParentId={id:N}", movies, StringComparison.Ordinal);
    }
}
