using Jellyfin.Web.Components;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class ImageEditorTests : ItemDialogsTestContext
{
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string ItemId = TestItems.Id(1);

    private void HaveItem(string type = "Movie", bool providers = true, bool backdrops = true, string? parentId = null)
    {
        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new
        {
            Id = ItemId,
            TestApp.ServerId,
            Type = type,
            ParentId = parentId,
            ImageTags = new { Primary = "ptag", Logo = "ltag" },
            BackdropImageTags = backdrops ? new[] { "b0", "b1" } : []
        });
        Server.OnGet($"/Items/{ItemId}/RemoteImages/Providers", providers ? new object[] { new { Name = "TheMovieDb", SupportedImages = new[] { "Primary", "Backdrop" } } } : []);
        var images = new List<object>
        {
            new { ImageType = "Primary", Width = 1000, Height = 1500 },
            new { ImageType = "Logo", Width = 0, Height = 0 },
            new { ImageType = "Chapter", ImageIndex = 0, Width = 320, Height = 180 }
        };
        if (backdrops)
        {
            // Out of order, as the server may send them.
            images.Add(new { ImageType = "Backdrop", ImageIndex = 1, Width = 1280, Height = 720 });
            images.Add(new { ImageType = "Backdrop", ImageIndex = 0, Width = 1920, Height = 1080 });
        }

        Server.OnGet($"/Items/{ItemId}/Images", images);
    }

    private Task<bool> OpenEditor()
    {
        var pending = Open(() => ImageEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(2, FindAll("#images .imageEditorCard").Count));
        return pending;
    }

    private int Loads => Server.RequestsTo($"/Items/{ItemId}/Images").Count(request => request.Method == HttpMethod.Get);

    [Fact]
    public async Task The_images_and_backdrops_of_the_item_are_listed()
    {
        SignIn(admin: true);
        HaveItem();

        var pending = OpenEditor();

        Assert.Equal(T("HeaderEditImages"), DialogTitle);
        Assert.Equal(
            new[] { $"/Users/{UserId}/Items/{ItemId}", $"/Items/{ItemId}/RemoteImages/Providers", $"/Items/{ItemId}/Images" },
            Server.Requests.Select(request => request.Uri.PathAndQuery));

        var cards = FindAll("#images .imageEditorCard");
        Assert.Equal(new[] { "Primary", "Logo" }, cards.Select(card => card.GetAttribute("data-imagetype")));
        Assert.All(cards, card => Assert.Equal("2", card.GetAttribute("data-numimages")));
        Assert.All(cards, card => Assert.Equal("1", card.GetAttribute("data-providers")));
        Assert.All(cards, card => Assert.Equal(ItemId, card.GetAttribute("data-id")));
        Assert.Equal(T("Primary"), cards[0].QuerySelector("h3.cardText")!.TextContent);
        Assert.Equal("1000 X 1500", cards[0].QuerySelector(".cardText-secondary")!.TextContent.Trim());
        Assert.Equal(" ", cards[1].QuerySelector(".cardText-secondary")!.TextContent.Trim(' ', '\n', '\r'));
        var primary = cards[0].QuerySelector(".cardImageContainer")!.GetAttribute("style")!;
        Assert.Contains($"url('{TestApp.ServerUrl}/Items/{ItemId}/Images/Primary?maxWidth=", primary);
        Assert.Contains("&tag=ptag&quality=90'", primary);

        Assert.False(IsHidden("#backdropsContainer"));
        var backdrops = FindAll("#backdrops .imageEditorCard");
        Assert.Equal(new[] { "0", "1" }, backdrops.Select(card => card.GetAttribute("data-index")));
        var second = backdrops[1].QuerySelector(".cardImageContainer")!.GetAttribute("style")!;
        Assert.Contains($"/Items/{ItemId}/Images/Backdrop/1?maxWidth=", second);
        Assert.Contains("&tag=b1&quality=80'", second);
        Assert.Equal("1280 X 720", backdrops[1].QuerySelector(".cardText-secondary")!.TextContent.Trim());

        // Only the moves that are possible are offered; images that are not backdrops can be searched instead.
        Assert.Equal(new[] { "0>1", "1>0" }, FindAll(".btnMoveImage").Select(button => button.GetAttribute("data-index") + ">" + button.GetAttribute("data-newindex")));
        Assert.Equal(new[] { "Primary", "Logo" }, FindAll(".btnSearchImages").Select(button => button.GetAttribute("data-imagetype")));
        Assert.Equal(new[] { "null", "null", "0", "1" }, FindAll(".btnDeleteImage").Select(button => button.GetAttribute("data-index")));
        Assert.All(FindAll(".btnBrowseAllImages"), button => Assert.False(button.HasClass("hide")));
        Assert.False(IsLoading);

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public void Without_providers_nothing_can_be_searched_and_without_backdrops_their_section_is_hidden()
    {
        SignIn(admin: true);
        HaveItem(providers: false, backdrops: false);

        _ = OpenEditor();

        Assert.All(FindAll(".btnBrowseAllImages"), button => Assert.True(button.HasClass("hide")));
        Assert.Empty(FindAll(".btnSearchImages"));
        Assert.True(IsHidden("#backdropsContainer"));
        Assert.Equal(2, FindAll(".btnOpenUploadMenu").Count);
    }

    [Fact]
    public async Task Deleting_an_image_asks_first_and_reloads()
    {
        SignIn(admin: true);
        HaveItem();
        Server.OnDelete($"/Items/{ItemId}/Images/Logo");
        Server.OnDelete($"/Items/{ItemId}/Images/Backdrop/1");

        var pending = OpenEditor();
        await Click("#images .btnDeleteImage[data-imagetype=Logo]");

        Assert.Equal(2, Dialogs.Dialogs.Count);
        Assert.Equal(T("ConfirmDeleteImage"), Find(".dialog .text").TextContent.Trim());
        Assert.Equal(T("Delete"), Find("button.btnOption[data-id=ok]").TextContent.Trim());
        Assert.Empty(Writes);

        await Answer("ok");
        Eventually(() => Assert.Equal(2, Loads));
        Assert.Equal(new[] { $"DELETE /Items/{ItemId}/Images/Logo" }, Writes);

        await Click("#backdrops .btnDeleteImage[data-index='1']");
        await Answer("ok");
        Eventually(() => Assert.Equal(3, Loads));
        Assert.Equal(new[] { $"DELETE /Items/{ItemId}/Images/Logo", $"DELETE /Items/{ItemId}/Images/Backdrop/1" }, Writes);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task A_delete_that_is_not_confirmed_or_fails_changes_nothing()
    {
        SignIn(admin: true);
        HaveItem();

        var pending = OpenEditor();
        await Click("#images .btnDeleteImage[data-imagetype=Primary]");
        await Answer("cancel");
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Empty(Writes);

        // The server answers the DELETE with 404.
        await Click("#images .btnDeleteImage[data-imagetype=Primary]");
        await Answer("ok");
        Eventually(() => Assert.Equal(new[] { $"DELETE /Items/{ItemId}/Images/Primary" }, Writes));
        Assert.Equal(1, Loads);

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public async Task Backdrops_are_moved_with_their_new_index()
    {
        SignIn(admin: true);
        HaveItem();
        Server.OnPost($"/Items/{ItemId}/Images/Backdrop/0/Index");

        var pending = OpenEditor();
        await Click("#backdrops .btnMoveImage[data-newindex='1']");

        Eventually(() => Assert.Equal(2, Loads));
        var move = Assert.Single(Server.Requests, request => request.Method == HttpMethod.Post);
        Assert.Equal($"/Items/{ItemId}/Images/Backdrop/0/Index?newIndex=1", move.Uri.PathAndQuery);

        // Moving the other one back is refused by the server.
        await Click("#backdrops .btnMoveImage[data-newindex='0']");
        Assert.Equal(2, Dialogs.Dialogs.Count);
        Assert.Equal(T("ErrorDefault"), Find(".dialog .text").TextContent.Trim());
        await Answer("ok");
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(2, Loads);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task Searching_opens_the_downloader_for_the_image_type_and_reloads_after_a_download()
    {
        SignIn(admin: true);
        HaveItem();
        Server.OnGet($"/Items/{ItemId}/RemoteImages", new
        {
            Images = new[] { new { ProviderName = "TheMovieDb", Url = "http://images.test/logo.png", Type = "Logo" } },
            TotalRecordCount = 1,
            Providers = new[] { "TheMovieDb" }
        });
        Server.OnPost($"/Items/{ItemId}/RemoteImages/Download");

        var pending = OpenEditor();
        await Click(".btnSearchImages[data-imagetype=Logo]");

        Eventually(() => Assert.Single(FindAll(".btnDownloadRemoteImage")));
        Assert.Equal(2, Dialogs.Dialogs.Count);
        Assert.Equal("Logo", Find("#selectBrowsableImageType").Value());
        Assert.True(IsHidden("#lblShowParentImages"));

        await Click(".btnDownloadRemoteImage");
        Eventually(() => Assert.Equal(2, Loads));
        Assert.Single(Dialogs.Dialogs);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task The_search_buttons_of_the_sections_browse_primary_images_and_backdrops()
    {
        SignIn(admin: true);
        HaveItem("Season", parentId: "series1");
        Server.OnGet("/RemoteImages", new { Images = Array.Empty<object>(), TotalRecordCount = 0, Providers = Array.Empty<string>() });

        var pending = OpenEditor();
        await Click("#imagesContainer .btnBrowseAllImages");
        Eventually(() => Assert.Equal("Primary", Find("#selectBrowsableImageType").Value()));

        // A season can take the images of its series.
        Assert.False(IsHidden("#lblShowParentImages"));
        Dialogs.Dialogs[^1].Cancel();

        await Click("#backdropsContainer .btnBrowseAllImages");
        Eventually(() => Assert.Equal("Backdrop", Find("#selectBrowsableImageType").Value()));
        Dialogs.Dialogs[^1].Cancel();

        // Closing the downloader without a download changes nothing.
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(1, Loads);
        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public async Task The_add_buttons_open_the_uploader_and_reload_after_an_upload()
    {
        SignIn(admin: true);
        HaveItem();
        Server.OnPost($"/Items/{ItemId}/Images/Backdrop");

        var pending = OpenEditor();
        await Click("#imagesContainer .btnOpenUploadMenu");
        Eventually(() => Assert.Equal(T("HeaderUploadImage"), DialogTitle));
        Assert.Equal("Primary", Find("#selectImageType").Value());
        Dialogs.Dialogs[^1].Cancel();

        await Click("#backdropsContainer .btnOpenUploadMenu");
        Eventually(() => Assert.Equal("Backdrop", Find("#selectImageType").Value()));
        View.FindComponent<InputFile>().UploadFiles(BinaryFile("backdrop.jpg", [1, 2, 3], "image/jpeg"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadItemImageForm");

        Eventually(() => Assert.Equal(2, Loads));
        Assert.Equal(new[] { $"POST /Items/{ItemId}/Images/Backdrop" }, Writes);
        await Click(".btnCancel");
        Assert.True(await pending);
    }
}

public class ImageDownloaderTests : ItemDialogsTestContext
{
    private static readonly string ItemId = TestItems.Id(1);

    private static object Image(int number, string provider = "TheMovieDb", string type = "Primary") => new
    {
        ProviderName = provider,
        Url = $"http://images.test/{number}.jpg",
        Type = type,
        Width = 1000,
        Height = 1500,
        Language = "en",
        CommunityRating = 7.5,
        VoteCount = 12,
        RatingType = "Score"
    };

    /// <summary>45 images at the server, handed out in pages.</summary>
    private void HaveImages(string itemId, int total = 45) => Server.OnGet($"/Items/{itemId}/RemoteImages", request =>
    {
        var start = int.Parse(request.Query("startIndex")!, System.Globalization.CultureInfo.InvariantCulture);
        var limit = int.Parse(request.Query("limit")!, System.Globalization.CultureInfo.InvariantCulture);
        return new
        {
            Images = Enumerable.Range(start, Math.Max(0, Math.Min(limit, total - start))).Select(number => Image(number, type: request.Query("type")!)).ToArray(),
            TotalRecordCount = total,
            Providers = new[] { "TheMovieDb", "Fanart" }
        };
    });

    private string LastQuery => Server.Requests.Last().Uri.PathAndQuery;

    [Fact]
    public async Task The_remote_images_are_listed_in_pages()
    {
        SignIn(admin: true);
        HaveImages(ItemId);

        var pending = Open(() => ImageDownloader.ShowAsync(Dialogs, ItemId, "Movie"));
        Eventually(() => Assert.Equal(30, FindAll(".availableImagesList .card").Count));

        Assert.Equal(T("Search"), DialogTitle);
        Assert.Equal($"/Items/{ItemId}/RemoteImages?type=Primary&startIndex=0&limit=30&IncludeAllLanguages=false", LastQuery);
        Assert.Equal(new[] { "", "TheMovieDb", "Fanart" }, Options("#selectImageProvider"));
        Assert.Equal(new[] { "Primary", "Art", "Backdrop", "Banner", "Box", "BoxRear", "Disc", "Logo", "Menu", "Thumb" }, Options("#selectBrowsableImageType"));
        Assert.Equal("Primary", Find("#selectBrowsableImageType").Value());
        Assert.True(IsHidden("#lblShowParentImages"));
        Assert.Equal(T("ListPaging", 1, 30, 45), Find(".listPaging span").TextContent);
        Assert.True(Find(".btnPreviousPage").HasAttribute("disabled"));
        Assert.False(Find(".btnNextPage").HasAttribute("disabled"));

        var card = Find(".availableImagesList .card");
        Assert.True(card.HasClass("portraitCard"));
        Assert.Equal("TheMovieDb", card.GetAttribute("data-imageprovider"));
        Assert.Equal("http://images.test/0.jpg", card.GetAttribute("data-imageurl"));
        Assert.Equal("Primary", card.GetAttribute("data-imagetype"));
        Assert.Equal("http://images.test/0.jpg", card.QuerySelector("a.cardImageContainer")!.GetAttribute("href"));
        Assert.Equal(
            new[] { "TheMovieDb", "1000 x 1500 • en", "7.5 • 12 votes" },
            card.QuerySelectorAll(".cardFooter .cardText").Take(3).Select(text => text.TextContent));

        await Click(".btnNextPage");
        Eventually(() => Assert.Equal(15, FindAll(".availableImagesList .card").Count));
        Assert.Equal($"/Items/{ItemId}/RemoteImages?type=Primary&startIndex=30&limit=30&IncludeAllLanguages=false", LastQuery);
        Assert.Equal(T("ListPaging", 31, 45, 45), Find(".listPaging span").TextContent);
        Assert.True(Find(".btnNextPage").HasAttribute("disabled"));

        await Click(".btnPreviousPage");
        Eventually(() => Assert.Equal(30, FindAll(".availableImagesList .card").Count));
        Assert.Equal("0", Server.Requests.Last().Query("startIndex"));

        await Click(".btnCancel");
        Assert.False(await pending);
        Assert.False(IsLoading);
    }

    [Fact]
    public void The_filters_start_again_at_the_first_page()
    {
        SignIn(admin: true);
        HaveImages(ItemId);
        HaveImages("series1");

        _ = Open(() => ImageDownloader.ShowAsync(Dialogs, ItemId, "Season", "Backdrop", "series1"));
        Eventually(() => Assert.Equal(30, FindAll(".availableImagesList .card").Count));
        Assert.Equal($"/Items/{ItemId}/RemoteImages?type=Backdrop&startIndex=0&limit=30&IncludeAllLanguages=false", LastQuery);
        Assert.True(Find(".availableImagesList .card").HasClass("backdropCard"));
        Assert.False(IsHidden("#lblShowParentImages"));

        Pick("#selectImageProvider", "Fanart");
        Eventually(() => Assert.Equal($"/Items/{ItemId}/RemoteImages?type=Backdrop&startIndex=0&limit=30&IncludeAllLanguages=false&ProviderName=Fanart", LastQuery));

        Check("#chkAllLanguages", true);
        Eventually(() => Assert.Equal($"/Items/{ItemId}/RemoteImages?type=Backdrop&startIndex=0&limit=30&IncludeAllLanguages=true&ProviderName=Fanart", LastQuery));

        Check("#chkShowParentImages", true);
        Eventually(() => Assert.Equal("/Items/series1/RemoteImages?type=Backdrop&startIndex=0&limit=30&IncludeAllLanguages=true&ProviderName=Fanart", LastQuery));

        // Another type starts with every provider again.
        Pick("#selectBrowsableImageType", "Banner");
        Eventually(() => Assert.Equal("/Items/series1/RemoteImages?type=Banner&startIndex=0&limit=30&IncludeAllLanguages=true", LastQuery));
        Eventually(() => Assert.True(Find(".availableImagesList .card").HasClass("bannerCard")));
    }

    [Fact]
    public async Task Few_images_have_no_paging_buttons_and_none_show_an_empty_range()
    {
        SignIn(admin: true);
        HaveImages(ItemId, total: 0);

        var pending = Open(() => ImageDownloader.ShowAsync(Dialogs, ItemId, "Movie"));
        Eventually(() => Assert.Equal(T("ListPaging", 0, 0, 0), Find(".listPaging span").TextContent));

        Assert.Empty(FindAll(".btnNextPage"));
        Assert.Empty(FindAll(".availableImagesList .card"));
        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public async Task Downloading_an_image_sends_it_to_the_item_and_closes()
    {
        SignIn(admin: true);
        HaveImages(ItemId);
        HaveImages("series1");
        Server.OnPost($"/Items/{ItemId}/RemoteImages/Download");

        var pending = Open(() => ImageDownloader.ShowAsync(Dialogs, ItemId, "Season", null, "series1"));
        Eventually(() => Assert.Equal(30, FindAll(".availableImagesList .card").Count));
        Check("#chkShowParentImages", true);
        Eventually(() => Assert.StartsWith("/Items/series1/RemoteImages?", LastQuery));

        await Settle(() => FindAll(".btnDownloadRemoteImage")[2].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        Assert.True(await pending);
        var download = Server.Requests.Last();
        Assert.Equal(HttpMethod.Post, download.Method);
        Assert.Equal($"/Items/{ItemId}/RemoteImages/Download", download.Path);
        Assert.Equal("Primary", download.Query("Type"));
        Assert.Equal("http://images.test/2.jpg", download.Query("ImageUrl"));
        Assert.Equal("TheMovieDb", download.Query("ProviderName"));
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task A_failed_download_or_search_leaves_the_dialog_open()
    {
        SignIn(admin: true);
        HaveImages(ItemId);

        var pending = Open(() => ImageDownloader.ShowAsync(Dialogs, ItemId, "Movie"));
        Eventually(() => Assert.Equal(30, FindAll(".availableImagesList .card").Count));

        await Click(".btnDownloadRemoteImage");
        Assert.Equal(new[] { $"POST /Items/{ItemId}/RemoteImages/Download" }, Writes);
        Assert.False(pending.IsCompleted);
        Assert.False(IsLoading);

        // A search that fails keeps what was listed.
        Fail(HttpMethod.Get, $"/Items/{ItemId}/RemoteImages");
        Check("#chkAllLanguages", true);
        Eventually(() => Assert.Equal("true", Server.Requests.Last().Query("IncludeAllLanguages")));
        Eventually(() => Assert.False(IsLoading));
        Assert.Equal(30, FindAll(".availableImagesList .card").Count);
    }
}

public class ImageUploaderTests : ItemDialogsTestContext
{
    private static readonly string ItemId = TestItems.Id(1);
    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 1, 2, 3];

    private void PickFile(InputFileContent file) => View.FindComponent<InputFile>().UploadFiles(file);

    [Fact]
    public async Task An_image_is_previewed_and_uploaded_as_base64()
    {
        SignIn(admin: true);
        Server.OnPost($"/Items/{ItemId}/Images/Logo");

        var pending = Open(() => ImageUploader.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(T("HeaderUploadImage"), DialogTitle));

        Assert.True(IsHidden("#fldUpload"));
        Assert.False(IsHidden("#dropImageText"));
        Assert.Equal("image/*", Find("#uploadImage").GetAttribute("accept"));
        await Click(".btnBrowse");
        Assert.Equal(1, Browser.Clicks);

        PickFile(BinaryFile("poster.png", Png, "image/png"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));

        Assert.True(IsHidden("#dropImageText"));
        var preview = Find("#imageOutput img");
        Assert.Equal("data:image/png;base64," + Convert.ToBase64String(Png), preview.GetAttribute("src"));
        Assert.Equal("poster.png", preview.GetAttribute("title"));
        Assert.Equal("Primary", Find("#selectImageType").Value());
        Assert.Equal(
            new[] { "None", "Primary", "Art", "Backdrop", "Banner", "Box", "BoxRear", "Disc", "Logo", "Menu", "Thumb" },
            Options("#selectImageType"));

        Pick("#selectImageType", "Logo");
        await Submit("form.uploadItemImageForm");

        Assert.True(await pending);
        var upload = Assert.Single(Server.Requests);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal($"/Items/{ItemId}/Images/Logo", upload.Uri.PathAndQuery);
        Assert.Equal(Convert.ToBase64String(Png), upload.Body);
        Assert.Contains("Token=\"test-token\"", upload.Headers["Authorization"]);
        Assert.False(IsLoading);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task A_file_that_is_no_image_is_not_taken()
    {
        SignIn(admin: true);

        var pending = Open(() => ImageUploader.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(T("HeaderUploadImage"), DialogTitle));
        PickFile(BinaryFile("poster.png", Png, "image/png"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));

        PickFile(TextFile("notes.txt", "hello"));
        Eventually(() => Assert.True(IsHidden("#fldUpload")));
        Assert.Empty(FindAll("#imageOutput img"));
        Assert.False(IsHidden("#dropImageText"));

        await Submit("form.uploadItemImageForm");
        Assert.Empty(Server.Requests);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task An_image_type_has_to_be_chosen()
    {
        SignIn(admin: true);

        var pending = Open(() => ImageUploader.ShowAsync(Dialogs, ItemId, "Backdrop"));
        Eventually(() => Assert.Equal("Backdrop", Find("#selectImageType").Value()));
        PickFile(BinaryFile("poster.png", Png, "image/png"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));

        Pick("#selectImageType", "None");
        await Submit("form.uploadItemImageForm");

        Assert.Equal(new[] { T("MessageImageTypeNotSelected") }, Toasts);
        Assert.Empty(Server.Requests);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task A_failed_upload_is_reported_and_leaves_the_dialog_open()
    {
        SignIn(admin: true);

        var pending = Open(() => ImageUploader.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(T("HeaderUploadImage"), DialogTitle));
        PickFile(BinaryFile("poster.png", Png, "image/png"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadItemImageForm");

        Assert.Equal(new[] { $"POST /Items/{ItemId}/Images/Primary" }, Writes);
        Assert.Equal(new[] { T("ImageUploadFailed") }, Toasts);
        Assert.False(pending.IsCompleted);
        Assert.False(IsLoading);

        await Click(".btnCancel");
        Assert.False(await pending);
    }
}
