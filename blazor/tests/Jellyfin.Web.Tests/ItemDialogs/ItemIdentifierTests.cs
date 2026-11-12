using System.Text.Json.Nodes;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class ItemIdentifierTests : ItemDialogsTestContext
{
    private const string First = """{"Name":"Third Film","ProductionYear":2008,"SearchProviderName":"TheMovieDb","ImageUrl":"http://images.test/third.jpg","ProviderIds":{"Tmdb":"42"}}""";
    private const string Second = """{"Name":"Third Film II","SearchProviderName":"TheMovieDb","ProviderIds":{"Tmdb":"43"}}""";
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string ItemId = TestItems.Id(1);

    private static JsonObject Item(string type = "Movie", string? path = "/media/movies/Third Film (2008)/Third Film.mkv")
    {
        var item = new JsonObject { ["Id"] = ItemId, ["Type"] = type, ["Name"] = "Third Film" };
        if (path is not null)
        {
            item["Path"] = path;
        }

        return item;
    }

    private void HaveIds() => Server.OnGet($"/Items/{ItemId}/ExternalIdInfos", new object[]
    {
        new { Name = "TheMovieDb", Key = "Tmdb" },
        new { Name = "IMDb", Key = "Imdb" },
        new { Name = "TheMovieDb", Key = "TmdbCollection", Type = "BoxSet" }
    });

    private void HaveResults(string type = "Movie") =>
        Server.OnPost($"/Items/RemoteSearch/{type}", _ => FakeHttp.Json($"[{First},{Second}]"));

    private Task<bool> OpenIdentifier(JsonObject? item = null)
    {
        var pending = Open(() => ItemIdentifier.ShowAsync(Dialogs, item ?? Item()));
        Eventually(() => Assert.Equal(3, FindAll(".txtLookupId").Count));
        return pending;
    }

    private static string Normalized(string? json) => JsonNode.Parse(json!)!.ToJsonString();

    [Fact]
    public async Task The_search_form_has_the_name_the_year_and_a_field_for_every_external_id()
    {
        SignIn(admin: true);
        HaveIds();

        var pending = OpenIdentifier();

        Assert.Equal(T("Identify"), DialogTitle);
        Assert.Equal($"/Items/{ItemId}/ExternalIdInfos", Assert.Single(Server.Requests).Uri.PathAndQuery);
        Assert.False(IsHidden("form.popupIdentifyForm"));
        Assert.True(IsHidden(".identificationSearchResults"));
        Assert.True(IsHidden("form.identifyOptionsForm"));
        Assert.False(IsHidden(".fldPath"));
        Assert.Equal("/media/movies/Third Film (2008)/Third Film.mkv", Find(".txtPath").TextContent);
        Assert.Equal("", Find("#txtLookupName").Value() ?? "");
        Assert.Equal("number", Find("#txtLookupYear").GetAttribute("type"));
        Assert.False(IsHidden(".fldLookupYear"));
        Assert.Equal(new[] { "txtLookupTmdb", "txtLookupImdb", "txtLookupTmdbCollection" }, FindAll(".txtLookupId").Select(input => input.Id));
        Assert.Equal(T("LabelDynamicExternalId", "TheMovieDb"), Find("label[for=txtLookupTmdb]").TextContent);
        Assert.Equal(T("LabelDynamicExternalId", "TheMovieDb " + T("BoxSet")), Find("label[for=txtLookupTmdbCollection]").TextContent);

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Theory]
    [InlineData("Movie", false)]
    [InlineData("Series", false)]
    [InlineData("BoxSet", true)]
    [InlineData("Person", true)]
    public void People_and_collections_have_no_year_and_an_item_without_path_shows_none(string type, bool yearHidden)
    {
        SignIn(admin: true);
        HaveIds();

        _ = OpenIdentifier(Item(type, path: null));

        Assert.Equal(yearHidden, IsHidden(".fldLookupYear"));
        Assert.True(IsHidden(".fldPath"));
    }

    [Fact]
    public async Task A_search_needs_a_name_or_an_id()
    {
        SignIn(admin: true);
        HaveIds();

        var pending = OpenIdentifier();
        Enter("#txtLookupYear", "2008");
        await Submit("form.popupIdentifyForm");

        Assert.Equal(new[] { T("PleaseEnterNameOrId") }, Toasts);
        Assert.Empty(Writes);
        Assert.False(IsHidden("form.popupIdentifyForm"));
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task The_search_sends_what_was_entered_and_a_result_is_applied_to_the_item()
    {
        SignIn(admin: true);
        HaveIds();
        HaveResults();
        Server.OnPost($"/Items/RemoteSearch/Apply/{ItemId}");

        var pending = OpenIdentifier();
        Enter("#txtLookupName", "Third Film");
        Enter("#txtLookupYear", "2008");
        Enter("#txtLookupImdb", "tt0000042");
        await Submit("form.popupIdentifyForm");

        Eventually(() => Assert.False(IsHidden(".identificationSearchResults")));
        var search = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, search.Method);
        Assert.Equal("/Items/RemoteSearch/Movie", search.Uri.PathAndQuery);
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"","Imdb":"tt0000042","TmdbCollection":""},"Name":"Third Film","Year":2008},"ItemId":"ITEM"}""".Replace("ITEM", ItemId),
            Normalized(search.Body));
        Assert.True(IsHidden("form.popupIdentifyForm"));
        Assert.True(IsHidden("form.identifyOptionsForm"));
        Assert.False(IsLoading);

        var cards = FindAll(".identificationSearchResultList .card");
        Assert.Equal(new[] { "0", "1" }, cards.Select(card => card.GetAttribute("data-index")));
        Assert.All(cards, card => Assert.True(card.HasClass("portraitCard")));
        Assert.Equal("background-image:url('http://images.test/third.jpg');", cards[0].QuerySelector(".cardImageContainer")!.GetAttribute("style"));
        Assert.Equal(new[] { "Third Film", "TheMovieDb", "2008" }, cards[0].QuerySelectorAll(".cardBox > .cardText").Select(line => line.TextContent.Trim()));
        Assert.Equal("Third Film II", cards[1].QuerySelector(".defaultCardBackground .cardCenteredText")!.TextContent);
        Assert.Equal(new[] { "Third Film II", "TheMovieDb", " " }, cards[1].QuerySelectorAll(".cardBox > .cardText").Select(line => line.TextContent.Trim(' ', '\n', '\r')));

        await Click(".card[data-index='0']");
        Assert.False(IsHidden("form.identifyOptionsForm"));
        Assert.True(IsHidden(".identificationSearchResults"));
        Assert.Equal("http://images.test/third.jpg", Find(".selectedSearchResult img").GetAttribute("src"));
        Assert.Equal("Third Film<br>2008", Find(".selectedSearchResult img + div").InnerHtml);
        Assert.True(Find("#chkIdentifyReplaceImages").IsChecked());

        await Submit("form.identifyOptionsForm");

        Assert.True(await pending);
        var apply = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, apply.Method);
        Assert.Equal($"/Items/RemoteSearch/Apply/{ItemId}?ReplaceAllImages=true", apply.Uri.PathAndQuery);
        Assert.Equal(Normalized(First), Normalized(apply.Body));
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task An_id_alone_is_enough_and_the_images_can_be_kept()
    {
        SignIn(admin: true);
        HaveIds();
        HaveResults();
        Server.OnPost($"/Items/RemoteSearch/Apply/{ItemId}");

        var pending = OpenIdentifier();
        Enter("#txtLookupTmdb", "43");
        await Submit("form.popupIdentifyForm");
        Eventually(() => Assert.False(IsHidden(".identificationSearchResults")));
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"43","Imdb":"","TmdbCollection":""}},"ItemId":"ITEM"}""".Replace("ITEM", ItemId),
            Normalized(Server.Requests.Last().Body));

        await Click(".card[data-index='1']");
        Assert.Empty(FindAll(".selectedSearchResult img"));
        Assert.Equal("Third Film II", Find(".selectedSearchResult").TextContent.Trim());
        Check("#chkIdentifyReplaceImages", false);
        await Submit("form.identifyOptionsForm");

        Assert.True(await pending);
        var apply = Server.Requests.Last();
        Assert.Equal($"/Items/RemoteSearch/Apply/{ItemId}?ReplaceAllImages=false", apply.Uri.PathAndQuery);
        Assert.Equal(Normalized(Second), Normalized(apply.Body));
    }

    [Theory]
    [InlineData("Episode", "backdropCard", 3)]
    [InlineData("MusicAlbum", "squareCard", 4)]
    [InlineData("MusicArtist", "squareCard", 3)]
    public async Task The_result_cards_have_the_shape_of_the_item_type(string type, string shape, int lines)
    {
        SignIn(admin: true);
        HaveIds();
        Server.OnPost($"/Items/RemoteSearch/{type}", new object[]
        {
            new { Name = "Result", SearchProviderName = "MusicBrainz", ProductionYear = 1999, AlbumArtist = new { Name = "The Band" } }
        });

        _ = OpenIdentifier(Item(type));
        Enter("#txtLookupName", "Result");
        await Submit("form.popupIdentifyForm");
        Eventually(() => Assert.Single(FindAll(".identificationSearchResultList .card")));

        var card = Find(".identificationSearchResultList .card");
        Assert.True(card.HasClass(shape));
        Assert.Equal(
            new[] { "Result", "MusicBrainz", "The Band", "1999" }.Take(lines),
            card.QuerySelectorAll(".cardBox > .cardText").Select(line => line.TextContent.Trim()));
    }

    [Fact]
    public async Task A_failed_search_stays_on_the_form_and_a_failed_apply_closes_without_a_change()
    {
        SignIn(admin: true);
        HaveIds();

        var pending = OpenIdentifier();
        Enter("#txtLookupName", "Third Film");
        await Submit("form.popupIdentifyForm");

        Assert.Equal(new[] { "POST /Items/RemoteSearch/Movie" }, Writes);
        Assert.False(IsHidden("form.popupIdentifyForm"));
        Assert.False(IsLoading);

        HaveResults();
        await Submit("form.popupIdentifyForm");
        Eventually(() => Assert.False(IsHidden(".identificationSearchResults")));
        await Click(".card[data-index='0']");
        await Submit("form.identifyOptionsForm");

        Assert.False(await pending);
        Assert.Equal($"POST /Items/RemoteSearch/Apply/{ItemId}", Writes.Last());
        Assert.False(IsLoading);
        Assert.Empty(Dialogs.Dialogs);
    }

    [Fact]
    public async Task An_item_with_locked_metadata_is_not_identified()
    {
        SignIn(admin: true);
        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new { Id = ItemId, Type = "Movie", LockData = true });

        var identified = await Open(() => Get<ItemDialogService>().IdentifyAsync(ItemId));

        Assert.False(identified);
        Assert.Equal(new[] { T("MessageItemMetadataLocked") }, Toasts);
        Assert.Empty(Dialogs.Dialogs);
        Assert.Single(Server.Requests);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task The_service_loads_the_item_and_opens_the_dialog_for_it()
    {
        SignIn(admin: true);
        HaveIds();

        // The item cannot be loaded yet.
        Assert.False(await Open(() => Get<ItemDialogService>().IdentifyAsync(ItemId)));
        Assert.Empty(Dialogs.Dialogs);
        Assert.Empty(Toasts);

        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new { Id = ItemId, Type = "Series", Name = "Show", Path = "/media/shows/Show" });
        var pending = Open(() => Get<ItemDialogService>().IdentifyAsync(ItemId));
        Eventually(() => Assert.Equal(3, FindAll(".txtLookupId").Count));

        Assert.Equal("/media/shows/Show", Find(".txtPath").TextContent);
        await Click(".btnCancel");
        Assert.False(await pending);
    }
}
