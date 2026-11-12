using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Items;

/// <summary>The actions behind <c>data-action</c> (shortcuts.js executeAction).</summary>
public class ItemActionsTests : ItemsTestContext
{
    private ItemActions Actions => Get<ItemActions>();

    private static BaseItemDto Movie(int number = 1, long position = 0) =>
        TestItems.Item(TestItems.Movie(number, "Movie " + number, new { PlaybackPositionTicks = position }));

    [Fact]
    public async Task Link_opens_the_items_page()
    {
        SignIn();
        var item = Movie();

        await Actions.ExecuteAsync(item, ItemAction.Link, new ItemActionContext { Context = "movies", ParentId = "p1" });

        var expected = Get<AppRouter>().GetRouteUrl(item, new RouteOptions { Context = "movies", ParentId = "p1" });
        Assert.Equal(expected.TrimStart('#'), App.Browser.Hash);
        Assert.StartsWith("/details?id=" + TestItems.Id(1), App.Browser.Hash, StringComparison.Ordinal);
        Assert.Empty(Playback.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    public async Task No_action_does_nothing(string? action)
    {
        SignIn();
        App.Browser.Navigate("/home");

        await Actions.ExecuteAsync(Movie(), action);

        Assert.Equal("/home", App.Browser.Hash);
        Assert.Empty(Playback.Calls);
        Assert.Empty(App.Server.Requests);
    }

    [Fact]
    public async Task A_photo_link_starts_the_slideshow()
    {
        SignIn();
        App.Browser.Navigate("/home");
        var photo = TestItems.Item(new { Id = TestItems.Id(5), Type = "Photo", MediaType = "Photo" });

        await Actions.ExecuteAsync(photo, ItemAction.Link);

        Assert.Equal("/home", App.Browser.Hash);
        Assert.Equal(new[] { TestItems.Id(5) }, Assert.Single(Playback.PlayRequests).Ids);
    }

    [Theory]
    [InlineData("play")]
    [InlineData("resume")]
    public async Task Play_resumes_in_the_lists_sort_order(string action)
    {
        SignIn();
        var settings = Get<UserSettings>();
        settings.Set("items-p1-Folder-sortby", "DateCreated");
        settings.Set("items-p1-Folder-sortorder", "Descending");

        await Actions.ExecuteAsync(Movie(position: 700), action, new ItemActionContext { ParentId = "p1" });

        Assert.Equal(new[] { "Play" }, Playback.Calls);
        var request = Assert.Single(Playback.PlayRequests);
        Assert.Equal(new[] { TestItems.Id(1) }, request.Ids);
        Assert.Null(request.Items);
        Assert.Equal(700, request.StartPositionTicks);
        Assert.Equal("DateCreated", request.QueryOptions!["SortBy"]);
        Assert.Equal("Descending", request.QueryOptions["SortOrder"]);
    }

    [Fact]
    public async Task Play_of_a_folder_uses_the_folders_own_order_and_a_programme_plays_its_channel()
    {
        SignIn();
        var folder = TestItems.Item(new { Id = TestItems.Id(2), Type = "Series", IsFolder = true });
        var program = TestItems.Item(new { Id = TestItems.Id(3), Type = "Program", ChannelId = TestItems.Id(9) });
        Get<UserSettings>().Set($"items-{TestItems.Id(2)}-Folder-sortby", "Random");

        await Actions.ExecuteAsync(folder, ItemAction.Play, new ItemActionContext { ParentId = "p1" });
        await Actions.ExecuteAsync(program, ItemAction.Play);

        Assert.Equal("Random", Playback.PlayRequests[0].QueryOptions!["SortBy"]);
        Assert.Equal("Ascending", Playback.PlayRequests[0].QueryOptions!["SortOrder"]);
        Assert.Equal(new[] { TestItems.Id(9) }, Playback.PlayRequests[1].Ids);
        Assert.Equal("SortName", Playback.PlayRequests[1].QueryOptions!["SortBy"]);
    }

    [Fact]
    public async Task Play_is_ignored_for_items_that_cannot_be_played()
    {
        SignIn();
        Playback.Playable = false;

        await Actions.ExecuteAsync(Movie(), ItemAction.Play);

        Assert.Empty(Playback.Calls);
    }

    [Fact]
    public async Task Queue_confirms_with_a_toast_while_something_plays()
    {
        SignIn();
        var item = Movie();

        await Actions.ExecuteAsync(item, ItemAction.Queue);

        Assert.Equal(new[] { "Queue" }, Playback.Calls);
        Assert.Equal(new[] { TestItems.Id(1) }, Playback.PlayRequests[0].Ids);
        Assert.Empty(Toasts);

        Playback.Start(Movie(2));
        await Actions.ExecuteAsync(item, ItemAction.Queue);

        Assert.Equal(new[] { "Queue", "Queue" }, Playback.Calls);
        Assert.Equal(new[] { Get<Globalize>().Translate("MediaQueued") }, Toasts);
    }

    [Fact]
    public async Task Play_and_queue_all_from_here_use_the_shown_items()
    {
        SignIn();
        var items = new[] { Movie(1), Movie(2), Movie(3) };
        var context = new ItemActionContext { Items = () => items };

        await Actions.ExecuteAsync(items[1], ItemAction.PlayAllFromHere, context);
        await Actions.ExecuteAsync(items[1], ItemAction.QueueAllFromHere, context);
        await Actions.ExecuteAsync(Movie(7), ItemAction.PlayAllFromHere, context);
        await Actions.ExecuteAsync(items[1], ItemAction.PlayAllFromHere);

        Assert.Equal(new[] { "Play", "Queue" }, Playback.Calls);
        Assert.Equal(new[] { TestItems.Id(1), TestItems.Id(2), TestItems.Id(3) }, Playback.PlayRequests[0].Ids);
        Assert.Equal(1, Playback.PlayRequests[0].StartIndex);
        Assert.Equal(new[] { TestItems.Id(2), TestItems.Id(3) }, Playback.PlayRequests[1].Ids);
    }

    [Fact]
    public async Task Play_and_queue_all_from_here_ask_the_list_for_fresh_items()
    {
        SignIn();
        var items = new[] { Movie(1), Movie(2), Movie(3) };
        IReadOnlyList<BaseItemDto> fresh = [Movie(1), Movie(2), Movie(3), Movie(4)];
        var context = new ItemActionContext { Items = () => items, FetchItems = () => Task.FromResult(fresh) };

        await Actions.ExecuteAsync(items[2], ItemAction.PlayAllFromHere, context);
        await Actions.ExecuteAsync(items[2], ItemAction.QueueAllFromHere, context);

        Assert.Same(fresh, Playback.PlayRequests[0].Items);
        Assert.Equal(2, Playback.PlayRequests[0].StartIndex);
        Assert.Equal(new[] { fresh[2], fresh[3] }, Playback.PlayRequests[1].Items);
    }

    [Fact]
    public async Task Instant_mix_and_trailers_reach_the_playback_manager()
    {
        SignIn();

        await Actions.ExecuteAsync(Movie(), ItemAction.InstantMix);
        await Actions.ExecuteAsync(Movie(), ItemAction.PlayTrailer);

        Assert.Equal(new[] { "InstantMix", "PlayTrailers" }, Playback.Calls);
    }

    [Fact]
    public async Task A_custom_action_is_handed_to_the_list()
    {
        SignIn();
        var item = Movie();
        var received = new List<(string Action, BaseItemDto Item)>();
        var context = new ItemActionContext
        {
            OnCustomAction = (action, target) =>
            {
                received.Add((action, target));
                return Task.CompletedTask;
            }
        };

        await Actions.ExecuteAsync(item, ItemAction.Custom, context, customAction: "removefromlist");
        await Actions.ExecuteAsync(item, ItemAction.Custom, context);

        var call = Assert.Single(received);
        Assert.Equal("removefromlist", call.Action);
        Assert.Same(item, call.Item);
    }

    [Theory]
    [InlineData("programdialog")]
    [InlineData("setplaylistindex")]
    [InlineData("playmenu")]
    public async Task Actions_of_other_workstreams_say_that_they_are_not_available(string action)
    {
        SignIn();

        await Actions.ExecuteAsync(Movie(), action);

        Assert.Equal(new[] { ItemCommandHandlers.NotAvailableText }, Toasts);
    }

    [Fact]
    public async Task Record_applies_to_programmes_and_timers_only()
    {
        SignIn();
        var program = TestItems.Item(new { Id = TestItems.Id(3), Type = "Program", ChannelId = TestItems.Id(9) });

        await Actions.ExecuteAsync(Movie(), ItemAction.Record);
        Assert.Empty(Toasts);

        await Actions.ExecuteAsync(program, ItemAction.Record);
        Assert.Empty(Toasts);
        Assert.DoesNotContain(App.Server.Requests, request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Edit_loads_the_full_item_first()
    {
        SignIn(admin: true);
        App.Server.OnGet("/Items/" + TestItems.Id(1), TestItems.Movie(1, "Movie"));

        var editing = Actions.ExecuteAsync(Movie(), ItemAction.Edit);
        var editor = await NextDialogAsync();

        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(TestApp.UserId.ToString("N"), request.Query("userId"));

        // The metadata editor of the item dialogs opens for the loaded item.
        editor.Cancel();
        await editing;
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task Menu_loads_the_item_and_runs_the_chosen_command()
    {
        var user = SignIn();
        App.Server.OnGet("/Items/" + TestItems.Id(1), TestItems.Movie(1, "Movie"));
        ItemMenuOptions? shown = null;
        var refreshes = 0;
        var context = new ItemActionContext
        {
            NotifyRefreshNeeded = () => refreshes++,
            ConfigureMenu = options =>
            {
                shown = options;
                return options;
            }
        };

        var pending = Actions.ExecuteAsync(Movie(), ItemAction.Menu, context);
        var dialog = await NextDialogAsync();

        Assert.NotNull(shown);
        Assert.Same(user, shown.User);
        Assert.Same(context, shown.Context);
        Assert.True(shown.FromCard);
        Assert.True(shown.PlayAllFromHere);
        Assert.True(shown.QueueAllFromHere);
        Assert.True(shown.Play);
        Assert.Equal("Movie", shown.Item.Name);
        Assert.Equal(TestApp.UserId.ToString("N"), Assert.Single(App.Server.RequestsTo("/Items/" + TestItems.Id(1))).Query("userId"));

        dialog.Close("queue");
        await pending;

        Assert.Equal(new[] { "Queue" }, Playback.Calls);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task Menu_without_play_options_and_for_folders()
    {
        SignIn();
        App.Server.OnGet("/Items/" + TestItems.Id(1), TestItems.Movie(1, "Movie"));
        App.Server.OnGet("/Items/" + TestItems.Id(2), new { Id = TestItems.Id(2), Name = "Series", Type = "Series", IsFolder = true });
        ItemMenuOptions? shown = null;
        var context = new ItemActionContext
        {
            ConfigureMenu = options =>
            {
                shown = options;
                return options;
            }
        };

        var pending = Actions.ExecuteAsync(Movie(), ItemAction.Menu, context, playOptions: false);
        (await NextDialogAsync()).Cancel();
        await pending;

        Assert.NotNull(shown);
        Assert.False(shown.Play);
        Assert.False(shown.Queue);
        Assert.False(shown.Shuffle);
        Assert.False(shown.InstantMix);
        Assert.False(shown.PlayAllFromHere);
        Assert.False(shown.QueueAllFromHere);
        Assert.Empty(Playback.Calls);

        pending = Actions.ShowContextMenuAsync(TestItems.Item(new { Id = TestItems.Id(2), Type = "Series", IsFolder = true }), context);
        (await NextDialogAsync()).Cancel();
        await pending;

        Assert.False(shown.PlayAllFromHere);
        Assert.False(shown.QueueAllFromHere);
        Assert.True(shown.Play);
    }

    [Fact]
    public async Task A_deleting_menu_command_asks_the_list_to_refresh()
    {
        SignIn();
        App.Server.OnGet("/Items/" + TestItems.Id(1), new { Id = TestItems.Id(1), Name = "Movie", Type = "Movie", MediaType = "Video", CanDelete = true });
        App.Server.OnDelete("/Items/" + TestItems.Id(1));
        var refreshes = 0;
        var context = new ItemActionContext { NotifyRefreshNeeded = () => refreshes++ };

        var pending = Actions.ExecuteAsync(Movie(), ItemAction.Menu, context);
        (await NextDialogAsync()).Close("delete");
        (await NextDialogAsync()).Close("ok");
        await pending;

        Assert.Equal(1, refreshes);
        Assert.Equal(HttpMethod.Delete, App.Server.Requests[^1].Method);
    }

    [Fact]
    public async Task Play_all_from_here_chosen_in_the_menu_plays_the_list()
    {
        SignIn();
        App.Server.OnGet("/Items/" + TestItems.Id(2), TestItems.Movie(2, "Movie 2"));
        var items = new[] { Movie(1), Movie(2), Movie(3) };
        var context = new ItemActionContext { Items = () => items };

        var pending = Actions.ExecuteAsync(items[1], ItemAction.Menu, context);
        (await NextDialogAsync()).Close("playallfromhere");
        await pending;

        var request = Assert.Single(Playback.PlayRequests);
        Assert.Equal(3, request.Ids!.Count);
        Assert.Equal(1, request.StartIndex);
    }

    [Fact]
    public async Task Menu_of_a_playlist_entry_knows_its_position()
    {
        SignIn();
        var entries = new[]
        {
            TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", MediaType = "Audio", PlaylistItemId = "e1" }),
            TestItems.Item(new { Id = TestItems.Id(2), Type = "Audio", MediaType = "Audio", PlaylistItemId = "e2" }),
            TestItems.Item(new { Id = TestItems.Id(2), Type = "Audio", MediaType = "Audio", PlaylistItemId = "e3" })
        };
        App.Server.OnGet("/Items/" + TestItems.Id(2), new { Id = TestItems.Id(2), Name = "Song", Type = "Audio", MediaType = "Audio" });
        App.Server.OnGet("/Playlists/pl1/Users/" + TestApp.UserId.ToString("N"), new { CanEdit = true });
        App.Server.OnPost("/Move/2");
        ItemMenuOptions? shown = null;
        var refreshes = 0;
        var context = new ItemActionContext
        {
            PlaylistId = "pl1",
            Items = () => entries,
            NotifyRefreshNeeded = () => refreshes++,
            ConfigureMenu = options =>
            {
                shown = options;
                return options;
            }
        };

        var pending = Actions.ExecuteAsync(entries[1], ItemAction.Menu, context);
        (await NextDialogAsync()).Close("movetobottom");
        await pending;

        Assert.NotNull(shown);
        Assert.Equal("pl1", shown.PlaylistId);
        Assert.True(shown.CanEditPlaylist);
        Assert.Equal("e2", shown.Item.PlaylistItemId);
        Assert.Equal(1, shown.Item.Extras()!.PlaylistIndex);
        Assert.Equal(3, shown.Item.Extras()!.PlaylistItemCount);
        Assert.Equal("/Playlists/pl1/Items/e2/Move/2", App.Server.Requests[^1].Path);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public void Sort_query_defaults_to_ascending()
    {
        SignIn();
        var settings = Get<UserSettings>();

        var plain = Actions.GetSortQuery("f1", null);
        var withDefault = Actions.GetSortQuery("f1", "SortName");
        settings.Set("items-f1-Folder-sortby", "PremiereDate");
        settings.Set("items-f1-Folder-sortorder", "Descending");
        var saved = Actions.GetSortQuery("f1", "SortName");

        Assert.Equal("Ascending", Assert.Single(plain).Value);
        Assert.Equal("SortName", withDefault["SortBy"]);
        Assert.Equal("PremiereDate", saved["SortBy"]);
        Assert.Equal("Descending", saved["SortOrder"]);
    }

    [Fact]
    public void Shortcut_attributes_describe_the_item()
    {
        var movie = Movie();
        var channelItem = TestItems.Item(new { Id = TestItems.Id(3), Type = "Program", ChannelId = TestItems.Id(9) });
        var library = TestItems.Item(new { Id = TestItems.Id(4), Type = "CollectionFolder", CollectionType = "movies", IsFolder = true });
        var timer = new TimerInfoDto { Id = "t1", Name = "News" }.ToItem();

        Assert.Equal(
            new[]
            {
                ("data-id", TestItems.Id(1)),
                ("data-serverid", TestApp.ServerId),
                ("data-type", "Movie"),
                ("data-mediatype", "Video"),
                ("data-isfolder", "false")
            },
            ItemActions.GetShortcutAttributes(movie));
        Assert.Equal(TestItems.Id(9), ItemActions.GetShortcutAttributeMap(channelItem)["data-channelid"]);
        Assert.Equal("movies", ItemActions.GetShortcutAttributeMap(library)["data-collectiontype"]);
        Assert.Equal("true", ItemActions.GetShortcutAttributeMap(library)["data-isfolder"]);
        Assert.Equal("t1", ItemActions.GetShortcutAttributeMap(timer)["data-id"]);
        Assert.Equal("Timer", ItemActions.GetShortcutAttributeMap(timer)["data-type"]);
    }
}

/// <summary>Played and favourite state (emby-playstatebutton, emby-ratingbutton, UserDataChanged).</summary>
public class ItemUserDataTests : ItemsTestContext
{
    private static readonly string MovieId = TestItems.Id(1);

    private static readonly string UserIdText = TestApp.UserId.ToString("N");

    private static BaseItemDto Movie() => TestItems.Item(TestItems.Movie(1, "Movie"));

    [Fact]
    public async Task Marking_played_posts_with_the_date_and_publishes_the_new_state()
    {
        SignIn();
        App.Server.OnPost("/UserPlayedItems/" + MovieId, new { Played = true, PlayCount = 1, ItemId = MovieId });
        var userData = Get<ItemUserData>();
        var changes = new List<UserItemDataDto>();
        userData.Changed += changes.Add;

        var result = await userData.SetPlayedAsync(Movie(), true);

        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/UserPlayedItems/" + MovieId, request.Path);
        Assert.Equal(UserIdText, request.Query("userId"));
        Assert.Matches("^[0-9]{14}$", request.Query("DatePlayed"));
        Assert.True(result!.Played);
        Assert.Same(result, Assert.Single(changes));
        Assert.Equal(Guid.Parse(MovieId), result.ItemId);
    }

    [Fact]
    public async Task Marking_unplayed_deletes()
    {
        SignIn();
        App.Server.On(HttpMethod.Delete, "/UserPlayedItems/" + MovieId, _ => FakeHttp.Json(new { Played = false }));
        var userData = Get<ItemUserData>();

        var result = await userData.SetPlayedAsync(Movie(), false);

        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(UserIdText, request.Query("userId"));
        Assert.Null(request.Query("DatePlayed"));
        Assert.False(result!.Played);
        Assert.Equal(Guid.Parse(MovieId), result.ItemId);
    }

    [Theory]
    [InlineData(true, "POST")]
    [InlineData(false, "DELETE")]
    public async Task Favourites_are_posted_and_deleted(bool favorite, string method)
    {
        SignIn();
        App.Server.On(new HttpMethod(method), "/UserFavoriteItems/" + MovieId, _ => FakeHttp.Json(new { IsFavorite = favorite, ItemId = MovieId }));
        var userData = Get<ItemUserData>();
        var changes = new List<UserItemDataDto>();
        userData.Changed += changes.Add;

        var result = await userData.SetFavoriteAsync(Movie(), favorite);

        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(method, request.Method.Method);
        Assert.Equal($"/UserFavoriteItems/{MovieId}?userId={UserIdText}", request.Uri.PathAndQuery);
        Assert.Equal(favorite, result!.IsFavorite);
        Assert.Single(changes);
    }

    [Fact]
    public void Server_messages_publish_changes_of_the_current_user_only()
    {
        SignIn();
        var userData = Get<ItemUserData>();
        var changes = new List<UserItemDataDto>();
        userData.Changed += changes.Add;

        ServerMessage("UserDataChanged", new
        {
            UserId = UserIdText,
            UserDataList = new object[]
            {
                new { ItemId = MovieId, Played = true, IsFavorite = false },
                new { ItemId = TestItems.Id(2), Played = false, IsFavorite = true }
            }
        });
        ServerMessage("UserDataChanged", new
        {
            UserId = TestItems.Id(77),
            UserDataList = new object[] { new { ItemId = MovieId, Played = false } }
        });

        Assert.Equal(2, changes.Count);
        Assert.Equal(Guid.Parse(MovieId), changes[0].ItemId);
        Assert.True(changes[0].Played);
        Assert.True(changes[1].IsFavorite);
    }

    [Fact]
    public void Changes_apply_to_their_own_item()
    {
        var item = Movie();
        var mine = new UserItemDataDto { ItemId = item.Id, Played = true };
        var other = new UserItemDataDto { ItemId = Guid.Parse(TestItems.Id(2)), Played = true };

        Assert.False(ItemUserData.Apply(item, other));
        Assert.NotEqual(true, item.UserData!.Played);
        Assert.True(ItemUserData.Apply(item, mine));
        Assert.Same(mine, item.UserData);
    }
}
