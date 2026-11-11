using System.Text.Json.Nodes;
using Jellyfin.Web.Pages.User;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.User;

/// <summary>The stored subtitle appearance and the styles derived from it.</summary>
public class SubtitleAppearanceTests : AppTestContext
{
    private static string? Style(IEnumerable<(string Name, string Value)> styles, string name) =>
        styles.Where(style => style.Name == name).Select(style => style.Value).FirstOrDefault();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    public void Missing_or_unreadable_settings_give_the_defaults(string? json)
    {
        var appearance = SubtitleAppearance.Parse(json);

        Assert.Equal("-3", appearance.VerticalPosition);
        Assert.Equal(-3, appearance.VerticalPositionValue);
        Assert.Equal("contain", appearance.AspectMode);
        Assert.Null(appearance.TextSize);
        Assert.Null(appearance.SubtitleStyling);
    }

    [Fact]
    public void Stored_values_replace_the_defaults_and_unknown_ones_are_kept()
    {
        var appearance = SubtitleAppearance.Parse("""{"textSize":"large","verticalPosition":"4","aspectMode":"cover","future":{"a":1},"nothing":null}""");

        Assert.Equal("large", appearance.TextSize);
        Assert.Equal("4", appearance.VerticalPosition);
        Assert.Equal(4, appearance.VerticalPositionValue);
        Assert.Equal("cover", appearance.AspectMode);

        appearance.TextColor = "#ff0000";
        var json = JsonNode.Parse(appearance.ToJson())!.AsObject();

        Assert.Equal("large", (string?)json["textSize"]);
        Assert.Equal("#ff0000", (string?)json["textColor"]);
        Assert.Equal(1, (int?)json["future"]!["a"]);
        Assert.True(json.ContainsKey("nothing"));
        Assert.Null(json["nothing"]);
    }

    [Theory]
    [InlineData("2", 2)]
    [InlineData("-16", -16)]
    [InlineData("2.9", 2)]
    [InlineData("-2.9", -2)]
    [InlineData("", null)]
    [InlineData("top", null)]
    public void The_position_is_read_as_a_whole_number_of_lines(string position, int? expected)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.VerticalPosition = position;

        Assert.Equal(expected, appearance.VerticalPositionValue);
    }

    [Fact]
    public async Task The_appearance_is_stored_on_this_device_for_the_user()
    {
        SignIn();
        this.ScriptPreferences();
        var settings = Services.GetRequiredService<UserSettings>();
        await settings.SetUserAsync(TestApp.UserId);

        var appearance = SubtitleAppearance.Load(settings);
        Assert.Equal(-3, appearance.VerticalPositionValue);
        appearance.Font = "casual";
        appearance.Save(settings);

        var stored = this.UserSetting("localplayersubtitleappearance3");
        Assert.NotNull(stored);
        Assert.Contains("\"font\":\"casual\"", stored, StringComparison.Ordinal);
        Assert.Equal("casual", SubtitleAppearance.Load(settings).Font);
        // Nothing of it goes to the server.
        Assert.Null(this.PostedPreferences());
    }

    [Theory]
    [InlineData(null, "1.36em")]
    [InlineData("", "1.36em")]
    [InlineData("smaller", ".8em")]
    [InlineData("small", "inherit")]
    [InlineData("large", "1.72em")]
    [InlineData("larger", "2em")]
    [InlineData("extralarge", "2.2em")]
    public void The_text_size_sets_the_font_size(string? size, string expected)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.TextSize = size;

        Assert.Equal(expected, Style(appearance.TextStyles(true), "font-size"));
    }

    [Theory]
    [InlineData(null, "#000000 0px 0px 7px")]
    [InlineData("", "#000000 0px 0px 7px")]
    [InlineData("none", "none")]
    public void The_drop_shadow_sets_the_text_shadow(string? shadow, string expected)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.DropShadow = shadow;

        Assert.Equal(expected, Style(appearance.TextStyles(true), "text-shadow"));
    }

    [Theory]
    [InlineData("raised", "-0.04em -0.04em #fff,", 6)]
    [InlineData("depressed", "0.04em 0.04em #fff,", 6)]
    [InlineData("uniform", "#000 0px 0.03em,", 20)]
    public void The_outlined_shadows_are_built_from_several_shadows(string shadow, string start, int count)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.DropShadow = shadow;

        var style = Style(appearance.TextStyles(true), "text-shadow");

        Assert.NotNull(style);
        Assert.StartsWith(start, style, StringComparison.Ordinal);
        Assert.Equal(count, style.Split(',').Length);
    }

    [Theory]
    [InlineData(null, "-apple-system,", "none")]
    [InlineData("typewriter", "\"Courier New\",monospace", "none")]
    [InlineData("print", "Georgia,", "none")]
    [InlineData("console", "Consolas,", "none")]
    [InlineData("cursive", "Lucida Handwriting,", "none")]
    [InlineData("casual", "Gabriola,", "none")]
    [InlineData("smallcaps", "Copperplate Gothic,", "small-caps")]
    public void The_font_sets_the_family_and_variant(string? font, string family, string variant)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.Font = font;

        var styles = appearance.TextStyles(true);

        Assert.StartsWith(family, Style(styles, "font-family"), StringComparison.Ordinal);
        Assert.Equal(variant, Style(styles, "font-variant"));
    }

    [Fact]
    public void Weight_and_colours_have_defaults()
    {
        var appearance = SubtitleAppearance.Parse(null);
        var defaults = appearance.TextStyles(true);

        Assert.Equal("normal", Style(defaults, "font-weight"));
        Assert.Equal("transparent", Style(defaults, "background-color"));
        Assert.Equal("#ffffff", Style(defaults, "color"));

        appearance.TextWeight = "bold";
        appearance.TextBackground = "#000000";
        appearance.TextColor = "#ffff00";
        var styles = appearance.TextStyles(true);

        Assert.Equal("bold", Style(styles, "font-weight"));
        Assert.Equal("#000000", Style(styles, "background-color"));
        Assert.Equal("#ffff00", Style(styles, "color"));
    }

    [Theory]
    [InlineData("-1", "0em", "", "", "0")]
    [InlineData("-3", "2.7em", "", "", "0")]
    [InlineData("-16", "20.25em", "", "", "0")]
    [InlineData("0", "", "0em", "0", "")]
    [InlineData("2", "", "2.7em", "0", "")]
    [InlineData("16", "", "21.6em", "0", "")]
    public void The_position_counts_lines_from_the_top_or_the_bottom(string position, string marginBottom, string marginTop, string top, string bottom)
    {
        var appearance = SubtitleAppearance.Parse(null);
        appearance.VerticalPosition = position;

        var text = appearance.TextStyles(false);
        var window = appearance.WindowStyles(false);

        Assert.Equal(marginBottom, Style(text, "margin-bottom"));
        Assert.Equal(marginTop, Style(text, "margin-top"));
        Assert.Equal(top, Style(window, "top"));
        Assert.Equal(bottom, Style(window, "bottom"));
    }

    [Fact]
    public void The_small_preview_ignores_the_position()
    {
        var appearance = SubtitleAppearance.Parse(null);

        Assert.DoesNotContain(appearance.TextStyles(true), style => style.Name.StartsWith("margin", StringComparison.Ordinal));
        Assert.Empty(appearance.WindowStyles(true));
    }

    [Fact]
    public void Styles_without_a_value_are_left_out_of_the_attribute()
    {
        Assert.Equal("top:0;color:#fff;", SubtitleAppearance.ToCss([("top", "0"), ("bottom", string.Empty), ("color", "#fff")]));
        Assert.Equal(string.Empty, SubtitleAppearance.ToCss([]));
    }
}
