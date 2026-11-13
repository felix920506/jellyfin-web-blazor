using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.ItemDialogs;

/// <summary>The commands of the item menu that this area answers, and the downloads and the sharing behind them.</summary>
public class ItemDialogCommandTests : ItemDialogsTestContext
{
    private const string Token = "test-token";

    private static readonly string MovieId = TestItems.Id(1);

    private static readonly string UserId = TestApp.UserId.ToString("N");

    private ItemCommandHandlers Handlers => Get<ItemCommandHandlers>();

    private ItemDialogService Service => Get<ItemDialogService>();

    private static BaseItemDto Movie() => TestItems.Item(new
    {
        Id = MovieId,
        Name = "Third Film",
        Overview = "The third one.",
        ServerId = TestApp.ServerId,
        Type = "Movie",
        MediaType = "Video",
        CanDownload = true
    });

    private static BaseItemDto Of(string type, int number = 1, object? seriesId = null) =>
        TestItems.Item(new { Id = TestItems.Id(number), Name = type, ServerId = TestApp.ServerId, Type = type, SeriesId = seriesId });

    private static string Download(int number) => $"{TestApp.ServerUrl}/Items/{TestItems.Id(number)}/Download?ApiKey={Token}";

    private static object Children(params (int Number, bool CanDownload)[] children) => new
    {
        Items = children.Select(child => new { Id = TestItems.Id(child.Number), CanDownload = child.CanDownload }).ToArray()
    };

    [Theory]
    [InlineData(ItemCommand.AddToCollection)]
    [InlineData(ItemCommand.AddToPlaylist)]
    [InlineData(ItemCommand.EditPlaylist)]
    [InlineData(ItemCommand.Edit)]
    [InlineData(ItemCommand.EditImages)]
    [InlineData(ItemCommand.EditSubtitles)]
    [InlineData(ItemCommand.EditLyrics)]
    [InlineData(ItemCommand.Identify)]
    [InlineData(ItemCommand.MoreMediaInfo)]
    [InlineData(ItemCommand.Refresh)]
    [InlineData(ItemCommand.Download)]
    [InlineData(ItemCommand.DownloadAll)]
    public void The_dialog_commands_of_the_item_menu_are_available(string command)
    {
        Assert.True(Handlers.CanHandle(command, Movie()));
    }

    [Theory]
    [InlineData(ItemCommand.Record)]
    [InlineData("play")]
    [InlineData("delete")]
    [InlineData("copy-stream")]
    public void Other_commands_are_left_to_their_own_areas(string command)
    {
        Assert.False(Handlers.CanHandle(command, Movie()));
    }

    [Theory]
    [InlineData("Timer")]
    [InlineData("SeriesTimer")]
    public void Editing_a_timer_is_left_to_the_recording_editors(string type)
    {
        // The SDK has no such item types: the Live TV lists mark their timers through the extras.
        var timer = TestItems.Item(new { Id = MovieId, Name = type, ServerId = TestApp.ServerId });
        timer.EnsureExtras().TypeName = type;

        Assert.True(Handlers.CanHandle(ItemCommand.Edit, timer));
        Assert.True(Handlers.CanHandle(ItemCommand.Refresh, timer));
    }

    [Fact]
    public void Sharing_is_available_where_the_browser_can_share()
    {
        Assert.False(Service.CanShare);
        Assert.False(Handlers.CanHandle(ItemCommand.Share, Movie()));

        Browser.CanShare = true;

        Assert.True(Service.CanShare);
        Assert.True(Handlers.CanHandle(ItemCommand.Share, Movie()));
    }

    [Fact]
    public async Task The_item_menu_offers_sharing_only_where_the_browser_can_share()
    {
        var user = SignIn(configure: u => u.Policy!.EnablePublicSharing = true);
        var options = new ItemMenuOptions { Item = Movie(), User = user, Share = true };

        Assert.DoesNotContain(await Get<ItemContextMenu>().GetCommandsAsync(options), command => command.Id == ItemCommand.Share);

        Browser.CanShare = true;

        Assert.Contains(await Get<ItemContextMenu>().GetCommandsAsync(options), command => command.Id == ItemCommand.Share);
    }

    [Fact]
    public async Task Sharing_hands_the_name_the_overview_and_the_link_of_the_item_to_the_browser()
    {
        SignIn();
        Browser.CanShare = true;

        var result = await Handlers.ExecuteAsync(ItemCommand.Share, Movie());

        Assert.Equal(ItemCommandResult.None, result);
        var share = Assert.Single(Browser.Shares);
        Assert.Equal("Third Film", share.Title);
        Assert.Equal("The third one.", share.Text);
        Assert.Equal($"{TestApp.ServerUrl}/web/#/details?id={MovieId}&serverId={TestApp.ServerId}", share.Url);
        Assert.Equal(share.Url, Service.ShareUrl(Movie()));
        Assert.Empty(Server.Requests);
    }

    [Fact]
    public async Task Download_hands_the_file_of_the_item_to_the_browser()
    {
        SignIn();

        var result = await Handlers.ExecuteAsync(ItemCommand.Download, Movie());

        Assert.Equal(ItemCommandResult.None, result);
        Assert.Equal(Download(1), Service.DownloadUrl(MovieId));
        Assert.Equal(new[] { Download(1) }, Assert.Single(Browser.Downloads));
        Assert.Empty(Server.Requests);
    }

    [Theory]
    [InlineData("BoxSet")]
    [InlineData("MusicAlbum")]
    public async Task Download_all_downloads_the_children_that_may_be_downloaded(string type)
    {
        SignIn();
        Server.OnGet($"/Users/{UserId}/Items", Children((2, true), (3, false), (4, true)));

        var result = await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of(type));

        Assert.Equal(ItemCommandResult.None, result);
        Assert.Equal(
            $"/Users/{UserId}/Items?ParentId={MovieId}&Fields=CanDownload%2CPath",
            Assert.Single(Server.Requests).Uri.PathAndQuery);
        Assert.Equal(new[] { Download(2), Download(4) }, Assert.Single(Browser.Downloads));
    }

    [Fact]
    public async Task Download_all_downloads_the_episodes_of_a_season()
    {
        SignIn();
        var seriesId = TestItems.Id(7);
        Server.OnGet($"/Shows/{seriesId}/Episodes", Children((2, true), (3, true)));

        await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of("Season", seriesId: seriesId));

        Assert.Equal(
            $"/Shows/{seriesId}/Episodes?seasonId={MovieId}&userId={UserId}&Fields=CanDownload%2CPath",
            Assert.Single(Server.Requests).Uri.PathAndQuery);
        Assert.Equal(new[] { Download(2), Download(3) }, Assert.Single(Browser.Downloads));
    }

    [Fact]
    public async Task Download_all_downloads_the_episodes_of_every_season_of_a_series_together()
    {
        SignIn();
        var seriesId = TestItems.Id(7);
        var first = TestItems.Id(8);
        var second = TestItems.Id(9);
        Server.OnGet($"/Shows/{seriesId}/Seasons", new
        {
            Items = new[] { new { Id = first, SeriesId = seriesId }, new { Id = second, SeriesId = seriesId } }
        });
        Server.OnGet(
            $"/Shows/{seriesId}/Episodes",
            request => request.Query("seasonId") == first ? Children((2, true), (3, false)) : Children((4, true)));

        await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of("Series", 7));

        Assert.Equal(
            new[]
            {
                $"/Shows/{seriesId}/Seasons?userId={UserId}&Fields=ItemCounts",
                $"/Shows/{seriesId}/Episodes?seasonId={first}&userId={UserId}&Fields=CanDownload%2CPath",
                $"/Shows/{seriesId}/Episodes?seasonId={second}&userId={UserId}&Fields=CanDownload%2CPath"
            },
            Server.Requests.Select(request => request.Uri.PathAndQuery));
        Assert.Equal(new[] { Download(2), Download(4) }, Assert.Single(Browser.Downloads));
    }

    [Fact]
    public async Task Download_all_downloads_nothing_when_nothing_may_be_downloaded()
    {
        SignIn();
        Server.OnGet($"/Users/{UserId}/Items", Children((2, false)));

        await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of("BoxSet"));

        Assert.Single(Server.Requests);
        Assert.Empty(Browser.Downloads);
    }

    [Fact]
    public async Task Download_all_downloads_nothing_when_the_children_cannot_be_loaded()
    {
        SignIn();

        var result = await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of("MusicAlbum"));

        Assert.Equal(ItemCommandResult.None, result);
        Assert.Empty(Browser.Downloads);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task Download_all_knows_collections_albums_seasons_and_series_only()
    {
        SignIn();

        await Handlers.ExecuteAsync(ItemCommand.DownloadAll, Of("Folder"));

        Assert.Empty(Server.Requests);
        Assert.Empty(Browser.Downloads);
    }

    [Fact]
    public async Task A_dialog_that_changed_something_asks_the_list_to_refresh()
    {
        SignIn();
        Server.OnGet($"/Users/{UserId}/Items", new { Items = new[] { new { Id = TestItems.Id(5), Name = "Favourites" } } });
        Server.OnPost($"/Collections/{TestItems.Id(5)}/Items");

        var pending = Open(() => Handlers.ExecuteAsync(ItemCommand.AddToCollection, Movie()));
        Eventually(() => Assert.Contains(TestItems.Id(5), Options("#selectCollectionToAddTo")));
        Pick("#selectCollectionToAddTo", TestItems.Id(5));
        await Submit("form.newCollectionForm");

        Assert.Equal(new ItemCommandResult(Updated: true), await pending);
        Assert.Equal(MovieId, Server.RequestsTo($"/Collections/{TestItems.Id(5)}/Items").Single().Query("Ids"));
    }

    [Fact]
    public async Task Adding_a_selection_to_a_collection_passes_every_selected_item()
    {
        SignIn();
        var secondId = TestItems.Id(2);
        var thirdId = TestItems.Id(3);
        Server.OnGet($"/Users/{UserId}/Items", new { Items = new[] { new { Id = TestItems.Id(5), Name = "Favourites" } } });
        Server.OnPost($"/Collections/{TestItems.Id(5)}/Items");

        var pending = Open(() => Handlers.ExecuteAsync(
            ItemCommand.AddToCollection,
            Movie(),
            new ItemActionContext
            {
                SelectedItems = [Movie(), Of("Movie", 2), Of("Movie", 3)]
            }));
        Eventually(() => Assert.Contains(TestItems.Id(5), Options("#selectCollectionToAddTo")));
        Pick("#selectCollectionToAddTo", TestItems.Id(5));
        await Submit("form.newCollectionForm");

        Assert.Equal(new ItemCommandResult(Updated: true), await pending);
        Assert.Equal(
            string.Join(',', MovieId, secondId, thirdId),
            Server.RequestsTo($"/Collections/{TestItems.Id(5)}/Items").Single().Query("Ids"));
    }

    [Fact]
    public async Task Adding_a_selection_to_a_playlist_passes_every_selected_item()
    {
        SignIn();
        var secondId = TestItems.Id(2);
        var thirdId = TestItems.Id(3);
        Server.OnGet("/Items", new { Items = new[] { new { Id = TestItems.Id(5), Name = "Favourites" } } });
        Server.OnGet($"/Playlists/{TestItems.Id(5)}/Users/{UserId}", new { CanEdit = true });
        Server.OnPost($"/Playlists/{TestItems.Id(5)}/Items");

        var pending = Open(() => Handlers.ExecuteAsync(
            ItemCommand.AddToPlaylist,
            Movie(),
            new ItemActionContext
            {
                SelectedItems = [Movie(), Of("Movie", 2), Of("Movie", 3)]
            }));
        Eventually(() => Assert.Contains(TestItems.Id(5), Options("#selectPlaylistToAddTo")));
        Pick("#selectPlaylistToAddTo", TestItems.Id(5));
        await Submit("form");

        Assert.Equal(new ItemCommandResult(Updated: true), await pending);
        Assert.Equal(
            string.Join(',', MovieId, secondId, thirdId),
            Server.RequestsTo($"/Playlists/{TestItems.Id(5)}/Items").Single().Query("Ids"));
    }

    [Fact]
    public async Task Refreshing_a_selection_queues_one_refresh_for_each_selected_item()
    {
        SignIn(admin: true);
        Server.OnPost("/Refresh");
        var selected = new[] { Movie(), Of("Movie", 2), Of("Movie", 3) };

        var pending = Open(() => Handlers.ExecuteAsync(
            ItemCommand.Refresh,
            selected[0],
            new ItemActionContext { SelectedItems = selected }));
        Eventually(() => Assert.NotNull(Find("#selectMetadataRefreshMode")));
        await Submit("form");

        Assert.Equal(ItemCommandResult.None, await pending);
        Eventually(() => Assert.Equal(3, Writes.Count(write => write.StartsWith("POST /Items/", StringComparison.Ordinal))));
        Assert.Equal(
            selected.Select(item => $"POST /Items/{item.IdText()}/Refresh").OrderBy(path => path),
            Writes.Where(write => write.StartsWith("POST /Items/", StringComparison.Ordinal)).OrderBy(path => path));
    }

    [Fact]
    public async Task A_dialog_that_was_closed_changes_nothing()
    {
        SignIn();
        Server.OnGet($"/Users/{UserId}/Items", new { Items = Array.Empty<object>() });

        var pending = Open(() => Handlers.ExecuteAsync(ItemCommand.AddToCollection, Movie()));
        Eventually(() => Assert.NotNull(Find("#selectCollectionToAddTo")));
        await Click(".btnCancel");

        Assert.Equal(ItemCommandResult.None, await pending);
        Assert.Empty(Writes);
    }

    [Fact]
    public async Task Refresh_queues_the_refresh_and_leaves_the_list_as_it_is()
    {
        SignIn(admin: true);
        Server.OnPost("/Refresh");

        var pending = Open(() => Handlers.ExecuteAsync(ItemCommand.Refresh, Of("CollectionFolder")));
        Eventually(() => Assert.Equal("scan", Find("#selectMetadataRefreshMode").GetAttribute("value")));
        await Submit("form");

        Assert.Equal(ItemCommandResult.None, await pending);
        Eventually(() => Assert.Equal(new[] { $"POST /Items/{MovieId}/Refresh" }, Writes));
    }

    [Fact]
    public async Task The_item_menu_opens_at_the_button_that_was_clicked_and_runs_the_chosen_command()
    {
        var user = SignIn();

        var pending = Open(() => Get<ItemContextMenu>().ShowAsync(new ItemMenuOptions { Item = Movie(), User = user }));
        Eventually(() => Assert.Single(Positioner.Calls));

        Assert.Same(ActionSheetAnchor.LastClicked, Positioner.Calls[0].PositionTo);
        Assert.Null(Positioner.Calls[0].PositionTo!.Element);
        Assert.Equal(new[] { ItemCommand.Share }, Positioner.Calls[0].ResolveOnClick);

        await Click($"button.actionSheetMenuItem[data-id={ItemCommand.Download}]");

        Assert.Equal(new ItemMenuResult(ItemCommand.Download), await pending);
        Assert.Equal(new[] { Download(1) }, Assert.Single(Browser.Downloads));
    }

    [Fact]
    public async Task A_menu_without_an_anchor_stays_in_the_middle_of_the_window()
    {
        var user = SignIn();

        var pending = Open(() => Get<ItemContextMenu>().ShowAsync(new ItemMenuOptions { Item = Movie(), User = user, PositionTo = null }));
        Eventually(() => Assert.NotEmpty(FindAll("button.actionSheetMenuItem")));

        Assert.Empty(Positioner.Calls);
        Dialogs.Dialogs[^1].Cancel();
        Assert.Null(await pending);
    }
}
