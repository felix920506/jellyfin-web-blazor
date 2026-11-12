using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Foundation;

public sealed class RouterAndUiTests
{
    [Fact]
    public void Router_builds_live_tv_list_and_back_navigation_urls()
    {
        var app = new TestApp();
        app.SignIn();
        var router = app.Get<AppRouter>();

        Assert.Equal("#/livetv?tab=1&serverId=" + TestApp.ServerId, router.GetLiveTvUrl("guide"));
        Assert.Equal("#/list?type=tag&tag=Science%20Fiction&serverId=" + TestApp.ServerId + "&parentId=p", router.GetTagUrl("Science Fiction", "p"));
        router.Start();
        router.Navigate("/movies");
        Assert.True(router.CanGoBack);
        router.Back();
        Assert.Equal(string.Empty, app.Browser.Hash);
    }

    [Fact]
    public async Task Dialogs_and_loading_service_have_the_expected_lifecycle()
    {
        var app = await new TestApp().LoadAssetsAsync();
        var dialogs = app.Get<AppDialogs>();
        var changed = 0;
        dialogs.Changed += () => changed++;
        var task = dialogs.ShowAsync(builder => builder.AddContent(0, "dialog"));
        var dialog = Assert.Single(dialogs.Dialogs);
        dialog.Close("done");

        Assert.Equal("done", await task);
        Assert.Empty(dialogs.Dialogs);
        _ = dialogs.ShowAsync(builder => builder.AddContent(0, "one"));
        dialogs.CloseAll();
        Assert.True(changed >= 4);

        var loading = app.Get<LoadingService>();
        loading.Show();
        loading.Show();
        Assert.True(loading.IsVisible);
        loading.Hide();
        Assert.False(loading.IsVisible);
    }
}

public sealed class ElementComponentTests : AppTestContext
{
    [Fact]
    public void Elements_preserve_original_classes_attributes_and_markup()
    {
        var button = Render<EmbyButton>(parameters => parameters
            .Add(p => p.Class, "button-submit")
            .Add(p => p.Icon, "home")
            .AddChildContent("Save"));
        Assert.Contains("emby-button button-submit", button.Markup);
        Assert.Contains("material-icons", button.Markup);
        Assert.Contains("home", button.Markup);
        Assert.Equal("Save", button.Find("button").TextContent.Trim());

        var input = Render<EmbyInput>(parameters => parameters.Add(p => p.Id, "txtName").Add(p => p.Label, "Name").Add(p => p.Description, "Required"));
        Assert.Equal("txtName", input.Find("input").Id);
        Assert.Contains("inputLabel", input.Find("label").ClassList);
        Assert.Contains("fieldDescription", input.Markup);

        var select = Render<EmbySelect>(parameters => parameters
            .Add(p => p.Id, "selectTheme")
            .AddChildContent("<option value=dark>Dark</option>"));
        Assert.Equal("selectTheme", select.Find("select").Id);
        Assert.Contains("selectArrow", select.Markup);

        var checkbox = Render<EmbyCheckbox>(parameters => parameters.Add(p => p.Id, "check").Add(p => p.Label, "Remember"));
        Assert.Contains("checkboxLabel", checkbox.Markup);
        Assert.Contains("Remember", checkbox.Markup);
    }

    [Fact]
    public void Collapse_toggles_and_page_renders_the_original_page_wrapper()
    {
        var collapse = Render<EmbyCollapse>(parameters => parameters.Add(p => p.Title, "More").AddChildContent("Details"));
        Assert.Contains("hide", collapse.Find(".collapseContent").ClassList);
        collapse.Find("button").Click();
        Assert.Contains("expanded", collapse.Find(".collapseContent").ClassList);

        var page = Render<Page>(parameters => parameters.Add(p => p.Id, "testPage").Add(p => p.Class, "mainAnimatedPage").AddChildContent("Body"));
        var root = page.Find(".page");
        Assert.Equal("testPage", root.Id);
        Assert.Contains("mainAnimatedPage", root.ClassList);
        Assert.Equal("Body", root.TextContent.Trim());
    }
}
