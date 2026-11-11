using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Wizard;

public class LibraryUiTests : AppTestContext
{
    [Fact]
    public void Content_types_are_offered_in_the_original_order()
    {
        var options = LibraryUi.GetCollectionTypeOptions(Services.GetRequiredService<Globalize>());

        Assert.Equal(["", "movies", "music", "tvshows", "books", "homevideos", "musicvideos", "mixed"], options.Select(option => option.Value));
        Assert.Equal(string.Empty, options[0].Name);
        Assert.Null(options[0].Message);
        Assert.Equal(
            "Review the <a class=\"button-link emby-button\" href=\"https://jellyfin.org/docs/general/server/media/movies\" target=\"_blank\" rel=\"noopener noreferrer\" data-autohide=\"true\">movie naming guide</a>.",
            options[1].Message);
        Assert.Null(options.Single(option => option.Value == "homevideos").Message);
        Assert.False(string.IsNullOrEmpty(options.Single(option => option.Value == "mixed").Message));
    }

    [Theory]
    [InlineData("movies", "movie")]
    [InlineData("music", "music_note")]
    [InlineData("homevideos", "photo")]
    [InlineData("photos", "photo")]
    [InlineData("livetv", "live_tv")]
    [InlineData("tvshows", "tv")]
    [InlineData("trailers", "theaters")]
    [InlineData("musicvideos", "music_video")]
    [InlineData("books", "book")]
    [InlineData("boxsets", "video_library")]
    [InlineData("playlists", "queue")]
    [InlineData("channels", "videocam")]
    [InlineData(null, "ondemand_video")]
    [InlineData("unknown", "folder")]
    public void Libraries_have_the_icon_of_their_type(string? collectionType, string icon) =>
        Assert.Equal(icon, LibraryUi.GetLibraryIcon(collectionType));

    [Fact]
    public void Plugins_follow_the_configured_order_with_new_ones_first()
    {
        string[] plugins = ["a", "b", "c", "d"];

        Assert.Equal(["a", "c", "d", "b"], LibraryUi.OrderPlugins(plugins, ["d", "b"], name => name));
        Assert.Equal(plugins, LibraryUi.OrderPlugins(plugins, null, name => name));
        Assert.Empty(LibraryUi.OrderPlugins<string>(null, ["a"], name => name));
    }
}
