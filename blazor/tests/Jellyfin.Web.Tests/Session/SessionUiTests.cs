using Jellyfin.Web.Pages.Session;

namespace Jellyfin.Web.Tests.Session;

public class SessionUiTests
{
    [Theory]
    // The digits of the middle character's code are summed; the sum's last digit picks the colour.
    [InlineData("a", 2)] // 97 -> 16 -> 6
    [InlineData("0", 3)] // 48 -> 12 -> 2
    [InlineData("Movies", 2)] // 'i' = 105 -> 6
    [InlineData("ab", 3)] // 'b' = 98 -> 17 -> 7
    [InlineData("z", 1)] // 122 -> 5 -> 5 % 5 = 0
    public void The_default_colour_follows_the_middle_character(string text, int expected) =>
        Assert.Equal(expected, SessionUi.DefaultColorIndex(text));

    [Fact]
    public void The_background_class_carries_the_colour()
    {
        Assert.Equal("defaultCardBackground defaultCardBackground2", SessionUi.DefaultBackgroundClass("a"));
    }

    [Fact]
    public void Without_text_the_colour_is_random_but_valid()
    {
        for (var i = 0; i < 50; i++)
        {
            Assert.InRange(SessionUi.DefaultColorIndex(), 1, 5);
            Assert.InRange(SessionUi.DefaultColorIndex(string.Empty), 1, 5);
        }
    }

    [Theory]
    [InlineData("https://sso.example/reset", true)]
    [InlineData("http://sso.example", true)]
    [InlineData("/config/passwordreset.txt", false)]
    [InlineData("C:\\ProgramData\\Jellyfin\\passwordreset.txt", false)]
    [InlineData("ftp://example.org/file", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_absolute_http_urls_are_valid(string? value, bool expected) => Assert.Equal(expected, SessionUi.IsValidUrl(value));

    [Fact]
    public void Html_is_encoded()
    {
        Assert.Equal("&lt;b&gt;&amp;&quot;", SessionUi.HtmlEncode("<b>&\""));
        Assert.Equal(string.Empty, SessionUi.HtmlEncode(null));
    }
}
