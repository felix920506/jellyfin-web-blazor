using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Items;

/// <summary>Which commands the item menu offers (itemContextMenu.js getCommands).</summary>
public class ItemContextMenuCommandTests : ItemsTestContext
{
    private static BaseItemDto Movie(object? extra = null) => TestItems.Item(TestItems.Movie(1, "Movie"));

    private async Task<string[]> Ids(ItemMenuOptions options) =>
        (await Get<ItemContextMenu>().GetCommandsAsync(options)).Select(command => command.Divider ? "-" : command.Id).ToArray();

    [Fact]
    public async Task Movie_for_an_administrator()
    {
        var user = SignIn(admin: true);

        var commands = await Get<ItemContextMenu>().GetCommandsAsync(new ItemMenuOptions { Item = Movie(), User = user });

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "-", "addtocollection", "addtoplaylist", "-", "edit", "editimages", "editsubtitles", "identify", "refresh" },
            commands.Select(command => command.Divider ? "-" : command.Id));
        Assert.Equal(
            new[] { "Play", "Add to play queue", "Play next", "Add to collection", "Add to playlist", "Edit metadata", "Edit images", "Edit subtitles", "Identify", "Refresh metadata" },
            commands.Where(command => !command.Divider).Select(command => command.Name));
        Assert.Equal("play_arrow", commands[0].Icon);
        Assert.Equal("playlist_add", commands[1].Icon);
    }

    [Fact]
    public async Task Movie_for_an_ordinary_user()
    {
        var user = SignIn();
        var item = Movie();

        Assert.Equal(new[] { "resume", "queue", "queuenext", "-", "addtoplaylist" }, await Ids(new ItemMenuOptions { Item = item, User = user }));
        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "-", "multiSelect", "addtoplaylist" },
            await Ids(new ItemMenuOptions { Item = item, User = user, FromCard = true }));
    }

    [Fact]
    public async Task Collection_managers_may_add_to_collections()
    {
        var user = SignIn(configure: u => u.Policy!.EnableCollectionManagement = true);

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "-", "addtocollection", "addtoplaylist" },
            await Ids(new ItemMenuOptions { Item = Movie(), User = user }));
    }

    [Fact]
    public async Task Without_a_user_only_the_playback_commands_remain()
    {
        Assert.Equal(new[] { "resume", "queue", "queuenext" }, await Ids(new ItemMenuOptions { Item = Movie() }));
    }

    [Fact]
    public async Task Nothing_to_offer_gives_no_menu()
    {
        Playback.Playable = false;
        Playback.Queueable = false;
        var menu = Get<ItemContextMenu>();

        Assert.Empty(await menu.GetCommandsAsync(new ItemMenuOptions { Item = Movie() }));
        Assert.Null(await menu.ShowAsync(new ItemMenuOptions { Item = Movie() }));
        Assert.Empty(Get<AppDialogs>().Dialogs);
    }

    [Fact]
    public async Task Option_switches_remove_their_commands()
    {
        var user = SignIn(admin: true);

        var ids = await Ids(new ItemMenuOptions
        {
            Item = Movie(),
            User = user,
            Play = false,
            Queue = false,
            Playlist = false,
            Edit = false,
            EditImages = false,
            EditSubtitles = false,
            Identify = false
        });

        Assert.Equal(new[] { "addtocollection", "-", "refresh" }, ids);
    }

    [Fact]
    public async Task Play_all_from_here_follows_play()
    {
        Assert.Equal(
            new[] { "resume", "playallfromhere", "queue", "queuenext" },
            await Ids(new ItemMenuOptions { Item = Movie(), PlayAllFromHere = true }));
    }

    [Fact]
    public async Task Stop_and_clear_queue_appear_while_playing()
    {
        var item = Movie();
        var options = new ItemMenuOptions { Item = item, StopPlayback = true, ClearQueue = true };

        Assert.Equal(new[] { "resume", "queue", "queuenext" }, await Ids(options));

        Playback.Start(item);

        Assert.Equal(new[] { "resume", "stopPlayback", "clearQueue", "queue", "queuenext" }, await Ids(options));
    }

    [Fact]
    public async Task Photos_cannot_be_played_from_the_menu()
    {
        var photo = TestItems.Item(new { Id = TestItems.Id(1), Type = "Photo", MediaType = "Photo", AlbumId = TestItems.Id(2) });

        Assert.Equal(new[] { "queue", "queuenext" }, await Ids(new ItemMenuOptions { Item = photo }));
    }

    [Fact]
    public async Task Albums_offer_shuffle_instant_mix_and_download_all()
    {
        var album = TestItems.Item(new { Id = TestItems.Id(1), Name = "Album", Type = "MusicAlbum", IsFolder = true });
        var user = SignIn();

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "shuffle", "instantmix", "-", "addtoplaylist" },
            await Ids(new ItemMenuOptions { Item = album, User = user }));

        user.Policy!.EnableContentDownloading = true;

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "shuffle", "instantmix", "-", "addtoplaylist", "downloadall" },
            await Ids(new ItemMenuOptions { Item = album, User = user }));
        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "-", "addtoplaylist", "downloadall" },
            await Ids(new ItemMenuOptions { Item = album, User = user, Shuffle = false, InstantMix = false }));
    }

    [Fact]
    public async Task Songs_link_to_album_artist_and_lyrics()
    {
        var song = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Name = "Song",
            Type = "Audio",
            MediaType = "Audio",
            AlbumId = TestItems.Id(2),
            AlbumArtists = new[] { new { Name = "Band", Id = TestItems.Id(3) } },
            HasLyrics = true
        });

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "instantmix", "-", "album", "artist", "lyrics" },
            await Ids(new ItemMenuOptions { Item = song }));
        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "instantmix", "-", "lyrics" },
            await Ids(new ItemMenuOptions { Item = song, OpenAlbum = false, OpenArtist = false }));

        var admin = SignIn(admin: true);

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "instantmix", "-", "addtoplaylist", "-", "edit", "editimages", "editlyrics", "refresh", "album", "artist", "lyrics" },
            await Ids(new ItemMenuOptions { Item = song, User = admin }));
    }

    [Fact]
    public async Task Live_tv_library_has_no_shuffle()
    {
        var liveTv = TestItems.Item(new { Id = TestItems.Id(1), Type = "CollectionFolder", CollectionType = "livetv", IsFolder = true });
        var movies = TestItems.Item(new { Id = TestItems.Id(2), Type = "CollectionFolder", CollectionType = "movies", IsFolder = true });

        Assert.DoesNotContain("shuffle", await Ids(new ItemMenuOptions { Item = liveTv }));
        Assert.Contains("shuffle", await Ids(new ItemMenuOptions { Item = movies }));
    }

    [Fact]
    public async Task Download_copy_and_delete_depend_on_the_item()
    {
        var menu = Get<ItemContextMenu>();
        var movie = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", MediaType = "Video", CanDownload = true, CanDelete = true });

        var commands = await menu.GetCommandsAsync(new ItemMenuOptions { Item = movie });

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "-", "download", "copy-stream", "delete" },
            commands.Select(command => command.Divider ? "-" : command.Id));
        Assert.Equal("Delete media", commands[^1].Name);
        Assert.DoesNotContain("delete", await Ids(new ItemMenuOptions { Item = movie, DeleteItem = false }));

        var book = TestItems.Item(new { Id = TestItems.Id(2), Type = "Book", MediaType = "Book", CanDownload = true });
        Assert.DoesNotContain("download", await Ids(new ItemMenuOptions { Item = book }));
    }

    [Theory]
    [InlineData("Series", "Delete Series")]
    [InlineData("Episode", "Delete Episode")]
    [InlineData("BoxSet", "Delete")]
    [InlineData("Playlist", "Delete")]
    [InlineData("Audio", "Delete media")]
    public async Task Delete_is_named_after_the_item_type(string type, string expected)
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = type, CanDelete = true });

        var commands = await Get<ItemContextMenu>().GetCommandsAsync(new ItemMenuOptions { Item = item });

        Assert.Equal(expected, Assert.Single(commands, command => command.Id == "delete").Name);
    }

    [Theory]
    [InlineData(0, new[] { "removefromplaylist", "movetobottom" })]
    [InlineData(1, new[] { "removefromplaylist", "movetotop", "movetobottom" })]
    [InlineData(2, new[] { "removefromplaylist", "movetotop" })]
    public async Task Playlist_entries_can_be_removed_and_moved(int index, string[] expected)
    {
        Playback.Playable = false;
        Playback.Queueable = false;
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", PlaylistItemId = "entry" });
        var extras = item.EnsureExtras();
        extras.PlaylistIndex = index;
        extras.PlaylistItemCount = 3;

        Assert.Equal(expected, await Ids(new ItemMenuOptions { Item = item, PlaylistId = "pl1", CanEditPlaylist = true, InstantMix = false }));
        Assert.Empty(await Ids(new ItemMenuOptions { Item = item, PlaylistId = "pl1", InstantMix = false }));
        Assert.Empty(await Ids(new ItemMenuOptions { Item = item, CanEditPlaylist = true, InstantMix = false }));
    }

    [Fact]
    public async Task Collection_members_can_be_removed()
    {
        Playback.Playable = false;
        Playback.Queueable = false;

        Assert.Equal(new[] { "removefromcollection" }, await Ids(new ItemMenuOptions { Item = Movie(), CollectionId = "c1" }));
    }

    [Fact]
    public async Task Playlists_can_be_edited_when_the_server_allows_it()
    {
        var user = SignIn();
        var playlist = TestItems.Item(new { Id = TestItems.Id(4), Type = "Playlist", IsFolder = true });
        var path = $"/Playlists/{TestItems.Id(4)}/Users/{TestApp.UserId:N}";

        Assert.DoesNotContain("editplaylist", await Ids(new ItemMenuOptions { Item = playlist, User = user }));

        App.Server.OnGet(path, new { CanEdit = true });

        Assert.Equal(
            new[] { "resume", "queue", "queuenext", "shuffle", "-", "addtoplaylist", "-", "editplaylist" },
            await Ids(new ItemMenuOptions { Item = playlist, User = user }));
        Assert.Equal(2, App.Server.RequestsTo(path).Count);
    }

    [Fact]
    public async Task Programmes_offer_recording()
    {
        var user = SignIn(admin: true);
        var program = TestItems.Item(new { Id = TestItems.Id(1), Type = "Program", ChannelId = TestItems.Id(9) });
        var menu = Get<ItemContextMenu>();

        var commands = await menu.GetCommandsAsync(new ItemMenuOptions { Item = program, User = user, PlayAllFromHere = true });

        Assert.Equal(new[] { "resume", "queue", "queuenext", "-", "record" }, commands.Select(command => command.Divider ? "-" : command.Id));
        Assert.Equal("Record", commands[^1].Name);

        program.TimerId = "timer1";
        commands = await menu.GetCommandsAsync(new ItemMenuOptions { Item = program, User = user });

        Assert.Equal("Manage recording", commands[^1].Name);
        Assert.DoesNotContain("record", await Ids(new ItemMenuOptions { Item = program, User = user, Record = false }));
    }

    [Fact]
    public async Task Timers_can_be_cancelled_by_live_tv_managers()
    {
        Playback.Playable = false;
        Playback.Queueable = false;
        var user = SignIn();
        var timer = new TimerInfoDto { Id = "t1", Name = "News" }.ToItem();
        var seriesTimer = new SeriesTimerInfoDto { Id = "s1", Name = "News" }.ToItem();
        var recording = TestItems.Item(new { Id = TestItems.Id(1), Type = "Recording", Status = "InProgress" });

        Assert.Empty(await Ids(new ItemMenuOptions { Item = timer, User = user }));

        user.Policy!.EnableLiveTvManagement = true;

        Assert.Equal(new[] { "canceltimer" }, await Ids(new ItemMenuOptions { Item = timer, User = user }));
        Assert.Equal(new[] { "cancelseriestimer" }, await Ids(new ItemMenuOptions { Item = seriesTimer, User = user }));
        Assert.Contains("canceltimer", await Ids(new ItemMenuOptions { Item = recording, User = user }));
        Assert.Empty(await Ids(new ItemMenuOptions { Item = timer, User = user, CancelTimer = false }));
    }

    [Fact]
    public async Task Media_info_needs_media_sources()
    {
        Playback.Playable = false;
        Playback.Queueable = false;
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", MediaSources = new[] { new { Id = "source" } } });

        Assert.Equal(new[] { "moremediainfo" }, await Ids(new ItemMenuOptions { Item = item }));
        Assert.Empty(await Ids(new ItemMenuOptions { Item = item, MoreMediaInfo = false }));
        Assert.Empty(await Ids(new ItemMenuOptions { Item = Movie() }));
    }

    [Fact]
    public async Task Share_needs_a_handler()
    {
        Playback.Playable = false;
        Playback.Queueable = false;
        var user = SignIn(configure: u => u.Policy!.EnablePublicSharing = true);

        Assert.DoesNotContain("share", await Ids(new ItemMenuOptions { Item = Movie(), User = user, Share = true }));
    }
}

/// <summary>What the commands of the item menu do (itemContextMenu.js executeCommand).</summary>
public class ItemContextMenuExecutionTests : ItemsTestContext
{
    private static readonly string MovieId = TestItems.Id(1);

    private ItemContextMenu Menu => Get<ItemContextMenu>();

    private static BaseItemDto Movie(long position = 0) => TestItems.Item(TestItems.Movie(1, "Movie", new { PlaybackPositionTicks = position }));

    [Fact]
    public async Task Resume_plays_from_the_saved_position()
    {
        SignIn();
        var item = Movie(500);

        var result = await Menu.ExecuteAsync("resume", new ItemMenuOptions { Item = item });

        Assert.Equal(new ItemMenuResult("resume"), result);
        Assert.Equal(new[] { "Play" }, Playback.Calls);
        var request = Assert.Single(Playback.PlayRequests);
        Assert.Same(item, Assert.Single(request.Items!));
        Assert.Equal(500, request.StartPositionTicks);
        Assert.Equal("Ascending", request.QueryOptions!["SortOrder"]);
        Assert.False(request.QueryOptions.ContainsKey("SortBy"));
    }

    [Fact]
    public async Task Play_starts_from_the_beginning_in_the_folders_saved_order()
    {
        SignIn();
        var item = TestItems.Item(new { Id = MovieId, Type = "Movie", ParentId = TestItems.Id(8), UserData = new { PlaybackPositionTicks = 500 } });
        var settings = Get<UserSettings>();
        settings.Set($"items-{TestItems.Id(8)}-Folder-sortby", "DateCreated");
        settings.Set($"items-{TestItems.Id(8)}-Folder-sortorder", "Descending");

        await Menu.ExecuteAsync("play", new ItemMenuOptions { Item = item });

        var request = Assert.Single(Playback.PlayRequests);
        Assert.Equal(0, request.StartPositionTicks);
        Assert.Equal("DateCreated", request.QueryOptions!["SortBy"]);
        Assert.Equal("Descending", request.QueryOptions["SortOrder"]);
    }

    [Theory]
    [InlineData("queue", "Queue")]
    [InlineData("queuenext", "QueueNext")]
    [InlineData("shuffle", "Shuffle")]
    [InlineData("instantmix", "InstantMix")]
    [InlineData("stopPlayback", "Stop")]
    public async Task Playback_commands_reach_the_playback_manager(string command, string call)
    {
        SignIn();

        await Menu.ExecuteAsync(command, new ItemMenuOptions { Item = Movie() });

        Assert.Equal(new[] { call }, Playback.Calls);
    }

    [Fact]
    public async Task A_programme_plays_its_channel()
    {
        SignIn();
        var program = TestItems.Item(new { Id = MovieId, Type = "Program", ChannelId = TestItems.Id(9) });

        await Menu.ExecuteAsync("resume", new ItemMenuOptions { Item = program });

        var request = Assert.Single(Playback.PlayRequests);
        Assert.Equal(new[] { TestItems.Id(9) }, request.Ids);
        Assert.Null(request.Items);
    }

    [Fact]
    public async Task Album_artist_and_lyrics_open_the_details_page()
    {
        SignIn();
        var song = TestItems.Item(new
        {
            Id = MovieId,
            ServerId = TestApp.ServerId,
            Type = "Audio",
            AlbumId = TestItems.Id(2),
            AlbumArtists = new[] { new { Name = "Band", Id = TestItems.Id(3) } }
        });
        var options = new ItemMenuOptions { Item = song };

        await Menu.ExecuteAsync("album", options);
        Assert.Equal($"/details?id={TestItems.Id(2)}&serverId={TestApp.ServerId}", App.Browser.Hash);

        await Menu.ExecuteAsync("artist", options);
        Assert.Equal($"/details?id={TestItems.Id(3)}&serverId={TestApp.ServerId}", App.Browser.Hash);

        await Menu.ExecuteAsync("lyrics", options);
        Assert.Equal($"/details?id={MovieId}&serverId={TestApp.ServerId}", App.Browser.Hash);
    }

    [Fact]
    public async Task Playlist_commands_call_the_server()
    {
        SignIn();
        App.Server.OnDelete("/Playlists/pl1/Items");
        App.Server.OnPost("/Move/0");
        App.Server.OnPost("/Move/2");
        var item = TestItems.Item(new { Id = MovieId, Type = "Audio", PlaylistItemId = "entry2" });
        item.EnsureExtras().PlaylistItemCount = 3;
        var options = new ItemMenuOptions { Item = item, PlaylistId = "pl1", CanEditPlaylist = true };

        Assert.Equal(new ItemMenuResult("removefromplaylist", Updated: true), await Menu.ExecuteAsync("removefromplaylist", options));
        Assert.Equal(new ItemMenuResult("movetotop", Updated: true), await Menu.ExecuteAsync("movetotop", options));
        Assert.Equal(new ItemMenuResult("movetobottom", Updated: true), await Menu.ExecuteAsync("movetobottom", options));

        var requests = App.Server.Requests;
        Assert.Equal(
            new[]
            {
                "DELETE /Playlists/pl1/Items?EntryIds=entry2",
                "POST /Playlists/pl1/Items/entry2/Move/0",
                "POST /Playlists/pl1/Items/entry2/Move/2"
            },
            requests.Select(request => $"{request.Method} {request.Uri.PathAndQuery}"));
    }

    [Fact]
    public async Task Remove_from_collection_calls_the_server()
    {
        SignIn();
        App.Server.OnDelete("/Collections/c1/Items");

        var result = await Menu.ExecuteAsync("removefromcollection", new ItemMenuOptions { Item = Movie(), CollectionId = "c1" });

        Assert.Equal(new ItemMenuResult("removefromcollection", Updated: true), result);
        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal($"/Collections/c1/Items?Ids={MovieId}", request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task Delete_asks_first_and_reports_the_deleted_item()
    {
        SignIn();
        App.Server.OnDelete("/Items/" + MovieId);

        var pending = Menu.ExecuteAsync("delete", new ItemMenuOptions { Item = Movie() });

        var confirmation = await NextDialogAsync();
        Assert.False(pending.IsCompleted);
        Assert.Empty(App.Server.Requests);
        confirmation.Close("ok");
        var result = await pending;

        Assert.Equal(new ItemMenuResult("delete", Updated: true, Deleted: true, ItemId: MovieId), result);
        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal("/Items/" + MovieId, request.Uri.PathAndQuery);
    }

    [Fact]
    public async Task A_cancelled_delete_does_nothing()
    {
        SignIn();

        var pending = Menu.ExecuteAsync("delete", new ItemMenuOptions { Item = Movie() });
        (await NextDialogAsync()).Cancel();

        Assert.Equal(new ItemMenuResult("delete"), await pending);
        Assert.Empty(App.Server.Requests);
    }

    [Fact]
    public async Task A_refused_delete_shows_an_alert()
    {
        SignIn();

        var pending = Menu.ExecuteAsync("delete", new ItemMenuOptions { Item = Movie() });
        (await NextDialogAsync()).Close("ok");
        var alert = await NextDialogAsync();
        Assert.False(pending.IsCompleted);
        alert.Close("ok");

        Assert.Equal(new ItemMenuResult("delete"), await pending);
        Assert.Equal(HttpMethod.Delete, Assert.Single(App.Server.Requests).Method);
    }

    [Fact]
    public async Task Copy_stream_url_puts_the_download_url_on_the_clipboard()
    {
        SignIn();

        await Menu.ExecuteAsync("copy-stream", new ItemMenuOptions { Item = Movie() });

        var invocation = Assert.Single(JSInterop.Invocations["navigator.clipboard.writeText"]);
        Assert.Equal($"{TestApp.ServerUrl}/Items/{MovieId}/Download?ApiKey=test-token", invocation.Arguments[0]);
        Assert.Equal(new[] { "URL copied successfully." }, Toasts);
    }

    [Fact]
    public async Task Commands_of_other_workstreams_say_that_they_are_not_available()
    {
        SignIn(admin: true);

        var result = await Menu.ExecuteAsync("edit", new ItemMenuOptions { Item = Movie() });

        Assert.Equal(new ItemMenuResult("edit"), result);
        Assert.Equal(new[] { ItemCommandHandlers.NotAvailableText }, Toasts);
    }

    [Fact]
    public async Task The_menu_is_an_action_sheet_and_runs_the_chosen_command()
    {
        var user = SignIn();
        var options = new ItemMenuOptions { Item = Movie(), User = user };

        var pending = Menu.ShowAsync(options);
        var dialog = await NextDialogAsync();
        Assert.Contains("actionSheet", dialog.Options.Class, StringComparison.Ordinal);
        dialog.Close("queue");

        Assert.Equal(new ItemMenuResult("queue"), await pending);
        Assert.Equal(new[] { "Queue" }, Playback.Calls);

        pending = Menu.ShowAsync(options);
        (await NextDialogAsync()).Cancel();

        Assert.Null(await pending);
        Assert.Equal(new[] { "Queue" }, Playback.Calls);
    }
}

/// <summary>The extension point for commands owned by other workstreams.</summary>
public class ItemCommandHandlerTests : ItemsTestContext
{
    private readonly RecordingHandler _shadowed = new("edit", ItemCommandResult.None);
    private readonly RecordingHandler _first = new("edit", new ItemCommandResult(Updated: true));
    private readonly RecordingHandler _second = new("share", new ItemCommandResult(Deleted: true));

    protected override void ConfigureServices(IServiceCollection services)
    {
        // The handler registered last wins.
        services.AddSingleton<IItemCommandHandler>(_shadowed);
        services.AddSingleton<IItemCommandHandler>(_first);
        services.AddSingleton<IItemCommandHandler>(_second);
    }

    [Fact]
    public async Task A_handler_runs_its_command_with_the_list_context()
    {
        SignIn(admin: true);
        var item = TestItems.Item(TestItems.Movie(1, "Movie"));

        var result = await Get<ItemContextMenu>().ExecuteAsync("edit", new ItemMenuOptions { Item = item, CollectionId = "c1", PlaylistId = "pl1" });

        Assert.Equal(new ItemMenuResult("edit", Updated: true), result);
        var call = Assert.Single(_first.Calls);
        Assert.Same(item, call.Item);
        Assert.Equal("c1", call.Context.CollectionId);
        Assert.Equal("pl1", call.Context.PlaylistId);
        Assert.Empty(_second.Calls);
        Assert.Empty(_shadowed.Calls);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task A_deleting_handler_reports_the_item()
    {
        var user = SignIn(configure: u => u.Policy!.EnablePublicSharing = true);
        var item = TestItems.Item(TestItems.Movie(1, "Movie"));
        var options = new ItemMenuOptions { Item = item, User = user, Share = true };
        var menu = Get<ItemContextMenu>();

        var commands = await menu.GetCommandsAsync(options);
        var result = await menu.ExecuteAsync("share", options);

        Assert.Contains(commands, command => command.Id == "share");
        Assert.Equal(new ItemMenuResult("share", Deleted: true, ItemId: TestItems.Id(1)), result);
    }

    [Fact]
    public async Task Handlers_answer_for_their_own_commands_only()
    {
        var handlers = Get<ItemCommandHandlers>();
        var item = TestItems.Item(TestItems.Movie(1, "Movie"));

        Assert.True(handlers.CanHandle("edit", item));
        Assert.True(handlers.CanHandle("share", item));
        Assert.False(handlers.CanHandle("identify", item));
        Assert.Equal(ItemCommandResult.None, await handlers.ExecuteAsync("identify", item));
        Assert.Equal(new[] { ItemCommandHandlers.NotAvailableText }, Toasts);
    }

    private sealed class RecordingHandler(string command, ItemCommandResult result) : IItemCommandHandler
    {
        public List<(string Command, BaseItemDto Item, ItemActionContext Context)> Calls { get; } = [];

        public bool CanHandle(string command1, BaseItemDto item) => command1 == command;

        public Task<ItemCommandResult> ExecuteAsync(string command1, BaseItemDto item, ItemActionContext context)
        {
            Calls.Add((command1, item, context));
            return Task.FromResult(result);
        }
    }
}
