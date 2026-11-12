using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Details;

/// <summary>The header texts of the details page: title lines, links and the lines of a person.</summary>
public class DetailsTextTests : DetailsTestContext
{
    private const string ItemLinkStart = "<a style=\"color:inherit;\" class=\"button-link itemAction emby-button\" href=\"";

    private DetailsText Text => Get<DetailsText>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    private static string ItemLink(string id, string type, string text) =>
        $"{ItemLinkStart}#/details?id={id}&amp;serverId={TestApp.ServerId}\" data-action=\"link\" data-id=\"{id}\" data-serverid=\"{TestApp.ServerId}\""
        + $" data-type=\"{type}\" data-isfolder=\"true\">{text}</a>";

    private static string ArtistLink(string id, string name) =>
        $"<a style=\"color:inherit;\" class=\"button-link emby-button\" href=\"#/details?id={id}&amp;serverId={TestApp.ServerId}\">{name}</a>";

    // --- Name ---------------------------------------------------------------------------------------

    [Fact]
    public void A_movie_shows_its_name()
    {
        var movie = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", Name = "Tom & Jerry", ServerId = TestApp.ServerId });

        Assert.Equal("<h1 class=\"itemName infoText parentNameLast\"><bdi>Tom &amp; Jerry</bdi></h1>", Text.GetNameHtml(movie, null, mobile: false));
    }

    [Fact]
    public void A_different_original_title_follows_the_name()
    {
        var movie = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", Name = "Spirited Away", OriginalTitle = "千と千尋の神隠し" });
        var same = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", Name = "Alien", OriginalTitle = "Alien" });

        Assert.Equal(
            "<h1 class=\"itemName infoText parentNameLast withOriginalTitle\"><bdi>Spirited Away</bdi></h1>"
            + "<h4 class=\"itemName infoText originalTitle\">" + System.Net.WebUtility.HtmlEncode("千と千尋の神隠し") + "</h4>",
            Text.GetNameHtml(movie, null, mobile: false));
        Assert.Equal("<h1 class=\"itemName infoText parentNameLast\"><bdi>Alien</bdi></h1>", Text.GetNameHtml(same, null, mobile: false));
    }

    [Fact]
    public void An_episode_links_to_its_series_and_season()
    {
        var episode = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Episode",
            Name = "Pilot",
            ServerId = TestApp.ServerId,
            SeriesName = "The Show",
            SeriesId = TestItems.Id(10),
            SeasonName = "Season 1",
            SeasonId = TestItems.Id(11),
            ParentIndexNumber = 1,
            IndexNumber = 1
        });

        Assert.Equal(
            "<h1 class=\"parentName focuscontainer-x\"><bdi>" + ItemLink(TestItems.Id(10), "Series", "The Show") + "</bdi></h1>"
            + "<h3 class=\"itemName infoText subtitle focuscontainer-x\"><bdi>" + ItemLink(TestItems.Id(11), "Season", "Season 1") + " - 1. Pilot</bdi></h3>",
            Text.GetNameHtml(episode, null, mobile: false));
    }

    [Fact]
    public void A_season_links_to_its_series()
    {
        var season = TestItems.Item(new
        {
            Id = TestItems.Id(11),
            Type = "Season",
            Name = "Season 1",
            ServerId = TestApp.ServerId,
            SeriesName = "The Show",
            SeriesId = TestItems.Id(10),
            IndexNumber = 1
        });

        Assert.Equal(
            "<h1 class=\"parentName focuscontainer-x\"><bdi>" + ItemLink(TestItems.Id(10), "Series", "The Show") + "</bdi></h1>"
            + "<h3 class=\"itemName infoText subtitle\"><bdi>Season 1</bdi></h3>",
            Text.GetNameHtml(season, null, mobile: false));
    }

    [Fact]
    public void A_song_links_to_its_artists_and_album()
    {
        var song = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Audio",
            MediaType = "Audio",
            Name = "First Song",
            ServerId = TestApp.ServerId,
            Album = "The Album",
            AlbumId = TestItems.Id(20),
            AlbumArtists = new[] { new { Id = TestItems.Id(30), Name = "Artist A" }, new { Id = TestItems.Id(31), Name = "Artist B" } }
        });

        var desktop = Text.GetNameHtml(song, null, mobile: false);
        var mobile = Text.GetNameHtml(song, null, mobile: true);

        var artists = ArtistLink(TestItems.Id(30), "Artist A") + " / " + ArtistLink(TestItems.Id(31), "Artist B");
        var album = ItemLink(TestItems.Id(20), "MusicAlbum", "The Album");
        Assert.Equal(
            "<h1 class=\"itemName infoText parentNameLast\"><bdi>First Song</bdi></h1>"
            + "<h3 class=\"parentName musicParentName focuscontainer-x\">" + artists + " - " + album + "</h3>",
            desktop);
        Assert.Equal(
            "<h1 class=\"itemName infoText parentNameLast\"><bdi>First Song</bdi></h1>"
            + "<h3 class=\"parentName musicParentName\">" + artists + "</br>" + album + "</h3>",
            mobile);
    }

    [Fact]
    public void An_album_links_to_its_artists()
    {
        var album = TestItems.Item(new
        {
            Id = TestItems.Id(20),
            Type = "MusicAlbum",
            Name = "The Album",
            ServerId = TestApp.ServerId,
            AlbumArtists = new[] { new { Id = TestItems.Id(30), Name = "Artist A" } }
        });

        Assert.Equal(
            "<h1 class=\"itemName infoText parentNameLast\"><bdi>The Album</bdi></h1>"
            + "<h3 class=\"parentName musicParentName focuscontainer-x\">" + ArtistLink(TestItems.Id(30), "Artist A") + "</h3>",
            Text.GetNameHtml(album, null, mobile: false));
    }

    [Fact]
    public void More_than_ten_artists_are_summed_up()
    {
        var artists = Enumerable.Range(1, 12).Select(number => new NameGuidPair { Id = Guid.Parse(TestItems.Id(number)), Name = "Artist " + number }).ToList();

        var html = Text.GetArtistLinksHtml(artists, TestApp.ServerId, null);

        Assert.Contains(">Artist 10</a> and 2 other artists.", html);
        Assert.DoesNotContain("Artist 11", html);
    }

    // --- Links and tags -----------------------------------------------------------------------------

    [Fact]
    public void External_links_open_web_addresses_in_a_new_tab()
    {
        var movie = TestItems.Item(new
        {
            Type = "Movie",
            ExternalUrls = new[]
            {
                new { Name = "IMDb", Url = "https://www.imdb.com/title/tt1?a=1&b=2" },
                new { Name = "Bad", Url = "javascript:alert(1)" },
                new { Name = "TMDb", Url = "http://tmdb.test/movie/1" }
            }
        });

        Assert.Equal(
            "<a class=\"button-link emby-button\" href=\"https://www.imdb.com/title/tt1?a=1&amp;b=2\" target=\"_blank\" rel=\"noopener noreferrer\">IMDb</a>, "
            + "<a class=\"button-link emby-button\" href=\"http://tmdb.test/movie/1\" target=\"_blank\" rel=\"noopener noreferrer\">TMDb</a>",
            Text.GetExternalLinksHtml(movie));
        Assert.Equal(string.Empty, Text.GetExternalLinksHtml(TestItems.Item(new { Type = "Movie" })));
    }

    [Fact]
    public void Tags_link_to_the_items_sharing_them()
    {
        var movie = TestItems.Item(new { Type = "Movie", Tags = new[] { "feel good", "4K" } });

        Assert.Equal(
            $"Tags: <a href=\"#/list?type=tag&amp;tag=feel%20good&amp;serverId={TestApp.ServerId}\" class=\"button-link emby-button\">feel good</a>, "
            + $"<a href=\"#/list?type=tag&amp;tag=4K&amp;serverId={TestApp.ServerId}\" class=\"button-link emby-button\">4K</a>",
            Text.GetTagsHtml(movie));
        Assert.Equal(string.Empty, Text.GetTagsHtml(TestItems.Item(new { Type = "Movie" })));
        Assert.Equal(string.Empty, Text.GetTagsHtml(TestItems.Item(new { Type = "Program", Tags = new[] { "news" } })));
    }

    // --- Person -------------------------------------------------------------------------------------

    [Fact]
    public void A_living_person_shows_birthday_and_age()
    {
        var person = TestItems.Item(new { Type = "Person", PremiereDate = new DateTimeOffset(1980, 5, 18, 0, 0, 0, TimeSpan.Zero) });

        var text = Text.GetBirthdayText(person, Now);

        Assert.StartsWith("Born: ", text);
        Assert.EndsWith(" (43 years old)", text);
        Assert.Null(Text.GetDeathDateText(person));
    }

    [Fact]
    public void A_dead_person_shows_the_age_reached_with_the_date_of_death()
    {
        var person = TestItems.Item(new
        {
            Type = "Person",
            PremiereDate = new DateTimeOffset(1900, 1, 1, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(1980, 6, 1, 0, 0, 0, TimeSpan.Zero)
        });

        var born = Text.GetBirthdayText(person, Now);
        var died = Text.GetDeathDateText(person);

        Assert.StartsWith("Born: ", born);
        Assert.DoesNotContain("years old", born);
        Assert.StartsWith("Died: ", died);
        Assert.EndsWith(" (80 years old)", died);
    }

    [Fact]
    public void The_birth_place_links_to_a_map()
    {
        var person = TestItems.Item(new { Type = "Person", ProductionLocations = new[] { "Springfield, USA" } });

        Assert.Equal(
            "Birth place: <a class=\"button-link textlink emby-button\" target=\"_blank\" rel=\"noopener noreferrer\""
            + " href=\"https://www.openstreetmap.org/search?query=Springfield%2C%20USA\">Springfield, USA</a>",
            Text.GetBirthPlaceHtml(person));
    }

    [Fact]
    public void Only_people_have_person_lines()
    {
        var movie = TestItems.Item(new
        {
            Type = "Movie",
            PremiereDate = new DateTimeOffset(1980, 5, 18, 0, 0, 0, TimeSpan.Zero),
            EndDate = new DateTimeOffset(1981, 5, 18, 0, 0, 0, TimeSpan.Zero),
            ProductionLocations = new[] { "Springfield" }
        });

        Assert.Null(Text.GetBirthdayText(movie, Now));
        Assert.Null(Text.GetDeathDateText(movie));
        Assert.Null(Text.GetBirthPlaceHtml(movie));
    }
}
