using Jellyfin.Web.Pages.User;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.User;

/// <summary>The option lists of the display settings.</summary>
public class DisplayOptionsTests : AppTestContext
{
    private Globalize Globalize => Services.GetRequiredService<Globalize>();

    [Fact]
    public void The_display_languages_are_those_of_the_original_template()
    {
        var languages = DisplayOptions.Languages;

        Assert.Equal(71, languages.Count);
        Assert.Equal(languages.Count, languages.Select(language => language.Value).Distinct().Count());
        Assert.Equal(new SelectOption("af", "Afrikaans"), languages[0]);
        Assert.Equal(new SelectOption("zh-HK", "廣東話 (香港)"), languages[^1]);
        Assert.Contains(new SelectOption("de", "Deutsch"), languages);
        Assert.Contains(new SelectOption("en-US", "English"), languages);
        Assert.Contains(new SelectOption("pr", "Pirate"), languages);
        // The original writes these with an underscore.
        Assert.Contains(languages, language => language.Value == "es_419");
        Assert.Contains(languages, language => language.Value == "ur_PK");
        Assert.DoesNotContain(languages, language => string.IsNullOrWhiteSpace(language.Name));
    }

    [Fact]
    public void Date_and_time_locales_are_the_languages_without_basque()
    {
        Assert.Equal(70, DisplayOptions.DateTimeLocales.Count);
        Assert.DoesNotContain(DisplayOptions.DateTimeLocales, locale => locale.Value == "eu");
        Assert.Equal(
            DisplayOptions.Languages.Where(language => language.Value != "eu"),
            DisplayOptions.DateTimeLocales);
    }

    [Fact]
    public void Automatic_comes_first()
    {
        var options = DisplayOptions.WithAuto(DisplayOptions.Languages, Globalize);

        Assert.Equal(72, options.Count);
        Assert.Equal(new SelectOption(string.Empty, Globalize["Auto"]), options[0]);
        Assert.Equal(DisplayOptions.Languages[0], options[1]);
    }

    [Fact]
    public void The_port_offers_its_two_layouts()
    {
        var layouts = DisplayOptions.Layouts(Globalize);

        Assert.Equal(["", "desktop", "mobile"], layouts.Select(layout => layout.Value));
        Assert.Equal([Globalize["Auto"], Globalize["Desktop"], Globalize["Mobile"]], layouts.Select(layout => layout.Name));
    }

    [Theory]
    [InlineData("desktop", "desktop")]
    [InlineData("mobile", "mobile")]
    [InlineData("auto", "")]
    [InlineData("tv", "")]
    [InlineData("experimental", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Only_the_layouts_of_the_port_are_selectable(string? setting, string expected) =>
        Assert.Equal(expected, DisplayOptions.LayoutValue(setting));

    [Fact]
    public void Without_a_plugin_list_every_screensaver_is_offered()
    {
        foreach (var plugins in new IReadOnlyCollection<string>?[] { null, [] })
        {
            var options = DisplayOptions.ScreensaverOptions(Globalize, plugins);

            Assert.Equal(["none", "backdropscreensaver", "logoscreensaver"], options.Select(option => option.Value));
            Assert.Equal(
                [Globalize["None"], Globalize["BackdropScreensaver"], Globalize["LogoScreensaver"]],
                options.Select(option => option.Name));
        }
    }

    [Fact]
    public void Screensavers_are_offered_when_their_plugin_is_configured()
    {
        Assert.Equal(
            ["none", "logoscreensaver"],
            DisplayOptions.ScreensaverOptions(Globalize, ["logoScreensaver/plugin", "htmlVideoPlayer/plugin"]).Select(option => option.Value));
        Assert.Equal(
            ["none"],
            DisplayOptions.ScreensaverOptions(Globalize, ["htmlVideoPlayer/plugin"]).Select(option => option.Value));
        Assert.Equal(
            ["none", "backdropscreensaver", "logoscreensaver"],
            DisplayOptions.ScreensaverOptions(Globalize, ["logoScreensaver/plugin", "backdropScreensaver/plugin"]).Select(option => option.Value));
    }
}
