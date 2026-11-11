using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Foundation;

public class GlobalizeTests
{
    [Fact]
    public async Task Translates_keys_from_the_original_dictionaries()
    {
        var app = await new TestApp().LoadAssetsAsync();
        var globalize = app.Get<Globalize>();

        Assert.Equal("Home", globalize["Home"]);
        Assert.Equal("Delete Movie", globalize.Translate("DeleteName", "Movie"));
        Assert.Equal("NoSuchKey", globalize["NoSuchKey"]);
    }

    [Fact]
    public async Task User_language_switches_the_dictionary_and_direction()
    {
        var app = await new TestApp().LoadAssetsAsync();
        var globalize = app.Get<Globalize>();
        var raised = 0;
        globalize.LanguageChanged += () => raised++;

        await globalize.SetUserCultureAsync("ar", null);

        Assert.Equal(1, raised);
        Assert.True(globalize.IsRtl);
        Assert.Equal("rtl", app.Browser.Attributes["html:dir"]);
        Assert.NotEqual("Home", globalize["Home"]);
    }
}
