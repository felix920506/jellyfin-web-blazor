using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class RefreshDialogTests : ItemDialogsTestContext
{
    private static readonly string[] Items = [TestItems.Id(1), TestItems.Id(2)];

    [Fact]
    public async Task Scanning_is_the_default_and_queues_a_default_refresh_for_every_item()
    {
        SignIn();
        Server.OnPost("/Refresh");

        var pending = Open(() => RefreshDialog.ShowAsync(Dialogs, Items));
        Eventually(() => Assert.Equal(T("RefreshMetadata"), DialogTitle));

        Assert.Equal(new[] { "scan", "missing", "all" }, Options("#selectMetadataRefreshMode"));
        Assert.Equal("scan", Find("#selectMetadataRefreshMode").Value());
        Assert.True(IsHidden("label.fldReplaceExistingImages"));
        Assert.True(IsHidden("label.fldReplaceTrickplayImages"));
        Assert.Equal(string.Join(',', Items), Find(".fldSelectedItemIds").Value());

        await Submit("form");

        Assert.True(await pending);
        Eventually(() => Assert.Equal(2, Server.Requests.Count));
        Assert.Equal(
            Items.Select(id => $"/Items/{id}/Refresh?Recursive=true&ImageRefreshMode=Default&MetadataRefreshMode=Default"
                + "&ReplaceAllImages=false&RegenerateTrickplay=false&ReplaceAllMetadata=false"),
            Server.Requests.Select(request => request.Uri.PathAndQuery));
        Assert.All(Server.Requests, request => Assert.Equal(HttpMethod.Post, request.Method));
        Assert.Equal(new[] { T("RefreshQueued") }, Toasts);
        Assert.False(IsLoading);
    }

    [Theory]
    [InlineData("missing", false, false, false)]
    [InlineData("missing", true, true, false)]
    [InlineData("all", true, false, true)]
    [InlineData("all", false, true, true)]
    public async Task The_other_modes_refresh_fully_with_the_chosen_replacements(string mode, bool images, bool trickplay, bool replaceMetadata)
    {
        SignIn();
        Server.OnPost("/Refresh");

        var pending = Open(() => RefreshDialog.ShowAsync(Dialogs, [Items[0]]));
        Eventually(() => Assert.NotNull(Find("#selectMetadataRefreshMode")));
        Pick("#selectMetadataRefreshMode", mode);
        Assert.False(IsHidden("label.fldReplaceExistingImages"));
        Assert.False(IsHidden("label.fldReplaceTrickplayImages"));
        Check(".chkReplaceImages", images);
        Check(".chkReplaceTrickplayImages", trickplay);
        await Submit("form");

        Assert.True(await pending);
        Eventually(() => Assert.Single(Server.Requests));
        var request = Server.Requests[0];
        Assert.Equal("FullRefresh", request.Query("ImageRefreshMode"));
        Assert.Equal("FullRefresh", request.Query("MetadataRefreshMode"));
        Assert.Equal(images ? "true" : "false", request.Query("ReplaceAllImages"));
        Assert.Equal(trickplay ? "true" : "false", request.Query("RegenerateTrickplay"));
        Assert.Equal(replaceMetadata ? "true" : "false", request.Query("ReplaceAllMetadata"));
        Assert.Equal("true", request.Query("Recursive"));
    }

    [Fact]
    public async Task Replacements_ticked_before_going_back_to_scanning_are_not_sent()
    {
        SignIn();
        Server.OnPost("/Refresh");

        var pending = Open(() => RefreshDialog.ShowAsync(Dialogs, [Items[0]], "all"));
        Eventually(() => Assert.Equal("all", Find("#selectMetadataRefreshMode").Value()));
        Check(".chkReplaceImages", true);
        Check(".chkReplaceTrickplayImages", true);
        Pick("#selectMetadataRefreshMode", "scan");
        await Submit("form");

        Assert.True(await pending);
        Eventually(() => Assert.Single(Server.Requests));
        var request = Server.Requests[0];
        Assert.Equal("Default", request.Query("MetadataRefreshMode"));
        Assert.Equal("false", request.Query("ReplaceAllImages"));
        Assert.Equal("false", request.Query("RegenerateTrickplay"));
        Assert.Equal("false", request.Query("ReplaceAllMetadata"));
    }

    [Fact]
    public async Task The_refresh_is_reported_as_queued_even_when_the_server_refuses()
    {
        SignIn();

        var pending = Open(() => RefreshDialog.ShowAsync(Dialogs, [Items[0]]));
        Eventually(() => Assert.NotNull(Find("#selectMetadataRefreshMode")));
        await Submit("form");

        Assert.True(await pending);
        Assert.Equal(new[] { T("RefreshQueued") }, Toasts);
    }

    [Fact]
    public async Task Closing_the_dialog_refreshes_nothing()
    {
        SignIn();

        var pending = Open(() => RefreshDialog.ShowAsync(Dialogs, Items));
        Eventually(() => Assert.NotNull(Find("#selectMetadataRefreshMode")));
        await Click(".btnCancel");

        Assert.False(await pending);
        Assert.Empty(Server.Requests);
        Assert.Empty(Toasts);
    }
}

public class ItemMediaInfoTests : ItemDialogsTestContext
{
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string MovieId = TestItems.Id(1);

    private void HaveMovie() => Server.OnGet($"/Users/{UserId}/Items/{MovieId}", new
    {
        Id = MovieId,
        Name = "Movie",
        Type = "Movie",
        MediaSources = new object[]
        {
            new
            {
                Name = "1080p",
                Container = "mkv",
                Path = "/media/movie.mkv",
                Size = 1572864,
                MediaStreams = new object[]
                {
                    new { Type = "Subtitle", Index = 2, DisplayTitle = "English - SRT", Language = "eng", Codec = "srt", IsDefault = false, IsForced = false, IsExternal = true },
                    new { Type = "Audio", Index = 1, DisplayTitle = "AAC - Stereo", Language = "eng", Codec = "aac", Channels = 2, BitRate = 128000, SampleRate = 48000, IsDefault = true, IsForced = false, IsExternal = false },
                    new { Type = "Video", Index = 0, DisplayTitle = "1080p H264", Codec = "h264", IsDefault = true, IsAVC = true, Profile = "High", Level = 41, Width = 1920, Height = 1080, AspectRatio = "16:9", IsInterlaced = false, ReferenceFrameRate = 24, BitRate = 4000000, BitDepth = 8, PixelFormat = "yuv420p" }
                }
            }
        }
    });

    private string[] Lines(string scope) => FindAll(scope + " .mediaInfoLabel")
        .Zip(FindAll(scope + " .mediaInfoAttribute"), (label, value) => label.TextContent + ": " + value.TextContent)
        .ToArray();

    [Fact]
    public async Task The_sources_and_streams_of_the_item_are_listed_for_an_administrator()
    {
        SignIn(admin: true);
        HaveMovie();

        var pending = Open(() => Get<ItemDialogService>().ShowMediaInfoAsync(MovieId));
        Eventually(() => Assert.Equal(T("MoreMediaInfo"), DialogTitle));

        Assert.Equal($"/Users/{UserId}/Items/{MovieId}", Assert.Single(Server.Requests).Uri.PathAndQuery);
        Assert.Equal(
            new[] { "1080p", T("Video"), T("Audio"), T("Subtitle") },
            FindAll("h2.mediaInfoStreamType").Select(heading => heading.TextContent.Trim()));
        Assert.Equal(
            new[]
            {
                T("MediaInfoContainer") + ": mkv",
                T("MediaInfoPath") + ": /media/movie.mkv",
                T("MediaInfoSize") + ": " + PlaybackText.ReadableSize(1572864)
            },
            Lines(".mediaInfoSource > "));
        Assert.Equal("ltr", FindAll(".mediaInfoSource > .mediaInfoAttribute")[1].GetAttribute("dir"));

        var streams = FindAll(".mediaInfoStream");
        Assert.Equal(3, streams.Count);
        Assert.Equal(
            new[]
            {
                T("MediaInfoTitle") + ": 1080p H264",
                T("MediaInfoCodec") + ": H264",
                "AVC: Yes",
                T("MediaInfoProfile") + ": High",
                T("MediaInfoLevel") + ": 41",
                T("MediaInfoResolution") + ": 1920x1080",
                T("MediaInfoAspectRatio") + ": 16:9",
                T("MediaInfoInterlaced") + ": No",
                T("MediaInfoFramerate") + ": 24",
                T("MediaInfoBitrate") + ": 4000 kbps",
                T("MediaInfoBitDepth") + ": 8 bit",
                T("MediaInfoPixelFormat") + ": yuv420p"
            },
            streams[0].QuerySelectorAll(".mediaInfoLabel").Zip(streams[0].QuerySelectorAll(".mediaInfoAttribute"), (label, value) => label.TextContent + ": " + value.TextContent));
        Assert.Equal(
            new[]
            {
                T("MediaInfoTitle") + ": AAC - Stereo",
                T("MediaInfoLanguage") + ": eng",
                T("MediaInfoCodec") + ": AAC",
                T("MediaInfoChannels") + ": 2 ch",
                T("MediaInfoBitrate") + ": 128 kbps",
                T("MediaInfoSampleRate") + ": 48000 Hz",
                T("MediaInfoDefault") + ": Yes",
                T("MediaInfoForced") + ": No",
                T("MediaInfoExternal") + ": No"
            },
            streams[1].QuerySelectorAll(".mediaInfoLabel").Zip(streams[1].QuerySelectorAll(".mediaInfoAttribute"), (label, value) => label.TextContent + ": " + value.TextContent));

        await Click(".btnCancel");
        await pending;
        Assert.Empty(Dialogs.Dialogs);
    }

    [Fact]
    public void Other_users_do_not_see_the_path()
    {
        SignIn();
        HaveMovie();

        _ = Open(() => Get<ItemDialogService>().ShowMediaInfoAsync(MovieId));
        Eventually(() => Assert.Equal(T("MoreMediaInfo"), DialogTitle));

        Assert.DoesNotContain(FindAll(".mediaInfoLabel"), label => label.TextContent == T("MediaInfoPath"));
        Assert.DoesNotContain("/media/movie.mkv", View.Markup);
    }

    [Fact]
    public async Task The_copy_buttons_put_the_block_on_the_clipboard()
    {
        SignIn();
        HaveMovie();

        _ = Open(() => Get<ItemDialogService>().ShowMediaInfoAsync(MovieId));
        Eventually(() => Assert.Equal(4, FindAll("button.btnCopy").Count));

        await Settle(() => FindAll("button.btnCopy")[2].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        var audio = Assert.Single(Browser.Copies);
        Assert.StartsWith("\n" + T("Audio") + "\n" + T("MediaInfoTitle") + ": AAC - Stereo\n", audio);
        Assert.EndsWith(T("MediaInfoExternal") + ": No\n", audio);
        Assert.Equal(new[] { T("Copied") }, Toasts);

        Browser.CopyResult = false;
        await Settle(() => FindAll("button.btnCopy")[0].ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs()));

        Assert.StartsWith("1080p\n" + T("MediaInfoContainer") + ": mkv\n", Browser.Copies[1]);
        Assert.Contains("\n" + T("Subtitle") + "\n", Browser.Copies[1]);
        Assert.Equal(new[] { T("Copied"), T("CopyFailed") }, Toasts);
    }

    [Fact]
    public async Task An_item_that_cannot_be_loaded_opens_no_dialog()
    {
        SignIn();

        await Open(() => Get<ItemDialogService>().ShowMediaInfoAsync(MovieId));

        Assert.Empty(Dialogs.Dialogs);
        Assert.False(IsLoading);
    }
}
