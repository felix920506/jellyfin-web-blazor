using Jellyfin.Web.Pages.User;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.User;

/// <summary>The option lists and value rules of the home screen settings.</summary>
public class HomeOptionsTests : AppTestContext
{
    private Globalize Globalize => Services.GetRequiredService<Globalize>();

    [Fact]
    public void The_default_sections_are_those_of_the_original()
    {
        Assert.Equal(10, HomeOptions.ConfigurableSections);
        Assert.Equal(
            ["smalllibrarytiles", "resume", "resumeaudio", "resumebook", "livetv", "nextup", "latestmedia", "none", "none", "none"],
            Enumerable.Range(0, HomeOptions.ConfigurableSections).Select(HomeOptions.GetDefaultSection));
        Assert.Equal(string.Empty, HomeOptions.GetDefaultSection(-1));
        Assert.Equal(string.Empty, HomeOptions.GetDefaultSection(10));
    }

    [Fact]
    public void A_section_select_offers_its_default_with_an_empty_value()
    {
        var first = HomeOptions.SectionOptions(0, Globalize);
        var second = HomeOptions.SectionOptions(1, Globalize);
        var eighth = HomeOptions.SectionOptions(7, Globalize);

        Assert.Equal(
            ["", "librarybuttons", "activerecordings", "resume", "resumeaudio", "resumebook", "latestmedia", "nextup", "livetv", "none"],
            first.Select(option => option.Value));
        Assert.Equal(
            ["smalllibrarytiles", "librarybuttons", "activerecordings", "", "resumeaudio", "resumebook", "latestmedia", "nextup", "livetv", "none"],
            second.Select(option => option.Value));
        Assert.Equal(string.Empty, eighth[^1].Value);
        Assert.Equal(Globalize["None"], eighth[^1].Name);
        Assert.Equal(
            [
                Globalize["HeaderMyMedia"], Globalize["HeaderMyMediaSmall"], Globalize["HeaderActiveRecordings"], Globalize["HeaderContinueWatching"],
                Globalize["HeaderContinueListening"], Globalize["HeaderContinueReading"], Globalize["HeaderLatestMedia"], Globalize["NextUp"],
                Globalize["LiveTV"], Globalize["None"]
            ],
            first.Select(option => option.Name));
    }

    [Theory]
    [InlineData(0, null, "")]
    [InlineData(0, "", "")]
    [InlineData(0, "smalllibrarytiles", "")]
    [InlineData(0, "librarybuttons", "librarybuttons")]
    [InlineData(1, "smalllibrarytiles", "smalllibrarytiles")]
    [InlineData(1, "resume", "")]
    [InlineData(7, "none", "")]
    [InlineData(6, "none", "none")]
    [InlineData(2, "folders", "")]
    [InlineData(2, "latestmovies", "")]
    public void A_stored_section_selects_its_option(int index, string? stored, string expected) =>
        Assert.Equal(expected, HomeOptions.SectionValue(index, stored));

    [Theory]
    [InlineData("movies", "movies", 7)]
    [InlineData("tvshows", "series", 8)]
    [InlineData("music", "albums", 8)]
    [InlineData("livetv", "programs", 6)]
    [InlineData("homevideos", "folders", 4)]
    [InlineData("musicvideos", "folders", 4)]
    [InlineData("mixed", "folders", 5)]
    [InlineData("books", "folders", 7)]
    public void Libraries_with_landing_screens_have_one_default(string collectionType, string defaultScreen, int count)
    {
        var options = HomeOptions.LandingOptions(collectionType);

        Assert.Equal(count, options.Count);
        Assert.Equal(defaultScreen, Assert.Single(options, option => option.IsDefault).Value);
        Assert.True(options[0].IsDefault);
        Assert.Equal(count, options.Select(option => option.Value).Distinct().Count());
        Assert.Contains(collectionType, HomeOptions.LandingScreenTypes);

        var select = HomeOptions.LandingSelectOptions(collectionType, Globalize);
        Assert.Equal(string.Empty, select[0].Value);
        Assert.Equal(options.Skip(1).Select(option => option.Value), select.Skip(1).Select(option => option.Value));
        Assert.Equal(options.Select(option => Globalize[option.NameKey]), select.Select(option => option.Name));
    }

    [Theory]
    [InlineData("boxsets")]
    [InlineData("playlists")]
    [InlineData("channels")]
    [InlineData("trailers")]
    [InlineData("")]
    [InlineData(null)]
    public void Other_libraries_have_no_landing_screens(string? collectionType)
    {
        Assert.Empty(HomeOptions.LandingOptions(collectionType));
        Assert.Empty(HomeOptions.LandingSelectOptions(collectionType, Globalize));
        Assert.Equal(string.Empty, HomeOptions.LandingValue(collectionType, "suggestions"));
    }

    [Theory]
    [InlineData("movies", null, "")]
    [InlineData("movies", "", "")]
    [InlineData("movies", "movies", "")]
    [InlineData("movies", "favorites", "favorites")]
    [InlineData("movies", "episodes", "")]
    [InlineData("tvshows", "episodes", "episodes")]
    [InlineData("tvshows", "series", "")]
    [InlineData("livetv", "guide", "guide")]
    [InlineData("music", "albumartists", "albumartists")]
    public void A_stored_landing_screen_selects_its_option(string collectionType, string? stored, string expected) =>
        Assert.Equal(expected, HomeOptions.LandingValue(collectionType, stored));

    [Fact]
    public void Latest_media_leaves_out_the_libraries_the_original_leaves_out()
    {
        Assert.Equal(["playlists", "livetv", "boxsets", "channels"], HomeOptions.ExcludedFromLatest);
        Assert.Equal(
            ["movies", "tvshows", "music", "livetv", "homevideos", "musicvideos", "mixed", "books"],
            HomeOptions.LandingScreenTypes);
    }
}
