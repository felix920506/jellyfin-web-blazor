using Jellyfin.Web.Routing;

namespace Jellyfin.Web.Tests.Foundation;

public class AppLocationTests
{
    [Theory]
    [InlineData("", "/", "/")]
    [InlineData("#/home", "/home", "/home")]
    [InlineData("/home?tab=1", "/home", "/home?tab=1")]
    [InlineData("home", "/home", "/home")]
    public void Parse_normalizes_the_fragment(string hash, string path, string url)
    {
        var location = AppLocation.Parse(hash);

        Assert.Equal(path, location.Path);
        Assert.Equal(url, location.Url);
        Assert.Equal("#" + url, location.Href);
    }

    [Fact]
    public void Query_values_are_decoded_and_case_insensitive()
    {
        var location = AppLocation.Parse("/details?id=abc&serverId=s%201");

        Assert.Equal("abc", location["id"]);
        Assert.Equal("s 1", location["serverid"]);
        Assert.Null(location["missing"]);
    }

    [Fact]
    public void Tab_defaults_to_zero()
    {
        Assert.Equal(0, AppLocation.Parse("/home").Tab);
        Assert.Equal(2, AppLocation.Parse("/home?tab=2").Tab);
    }

    [Fact]
    public void With_adds_replaces_and_removes_query_values()
    {
        var location = AppLocation.Parse("/movies?topParentId=1&tab=2");

        var changed = location.With(("tab", "3"), ("topParentId", null), ("q", "a b"));

        Assert.Equal("/movies", changed.Path);
        Assert.Equal("3", changed["tab"]);
        Assert.Null(changed["topParentId"]);
        Assert.Equal("a b", changed["q"]);
    }
}
