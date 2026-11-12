using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Library;

/// <summary>The selection of multi-select (components/multiSelect/multiSelect.js).</summary>
public class MultiSelectStateTests
{
    private readonly MultiSelectState _state = new();

    private static BaseItemDto Movie(int number) => TestItems.Item(TestItems.Movie(number, "Movie " + number));

    [Fact]
    public void Nothing_is_selected_at_first()
    {
        Assert.False(_state.IsActive);
        Assert.Equal(0, _state.Count);
        Assert.Empty(_state.Items);
        Assert.False(_state.IsSelected(Movie(1)));
    }

    [Fact]
    public void Starting_selects_the_item_and_notifies()
    {
        var changes = 0;
        _state.Changed += () => changes++;
        var movie = Movie(1);

        _state.Start(movie, null);
        _state.Start(movie, null);

        Assert.True(_state.IsActive);
        Assert.Same(movie, Assert.Single(_state.Items));
        Assert.True(_state.IsSelected(Movie(1)));
        Assert.False(_state.IsSelected(Movie(2)));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Toggling_does_nothing_before_selection_mode_starts()
    {
        _state.Toggle(Movie(1), null);

        Assert.False(_state.IsActive);
        Assert.Equal(0, _state.Count);
    }

    [Fact]
    public void Toggling_selects_and_deselects_in_order()
    {
        _state.Start(Movie(1), null);
        _state.Toggle(Movie(2), null);
        _state.Toggle(Movie(3), null);
        _state.Toggle(Movie(2), null);

        Assert.Equal(new[] { "Movie 1", "Movie 3" }, _state.Items.Select(item => item.Name));
        Assert.True(_state.IsActive);
    }

    [Fact]
    public void Deselecting_the_last_item_ends_selection_mode()
    {
        _state.Start(Movie(1), null);
        _state.Toggle(Movie(1), null);

        Assert.False(_state.IsActive);
        Assert.Equal(0, _state.Count);
    }

    [Fact]
    public void Stopping_clears_the_selection_and_notifies_once()
    {
        var changes = 0;
        _state.Start(Movie(1), null);
        _state.Changed += () => changes++;

        _state.Stop();
        _state.Stop();

        Assert.False(_state.IsActive);
        Assert.Empty(_state.Items);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void A_long_press_starts_from_the_card_with_that_id()
    {
        var context = new ItemActionContext();
        var movie = Movie(2);
        using var first = _state.Attach(() => Movie(1), () => null, () => { });
        using var second = _state.Attach(() => movie, () => context, () => { });

        Assert.False(_state.StartById(null));
        Assert.False(_state.StartById(string.Empty));
        Assert.False(_state.StartById(TestItems.Id(9)));
        Assert.False(_state.IsActive);

        // The data-id of a card may come with dashes and in upper case.
        Assert.True(_state.StartById(Guid.Parse(TestItems.Id(2)).ToString("D").ToUpperInvariant()));

        Assert.True(_state.IsActive);
        Assert.Same(movie, Assert.Single(_state.Items));
    }

    [Fact]
    public void Cards_redraw_on_every_change_until_they_leave_the_page()
    {
        var redraws = 0;
        var card = _state.Attach(() => Movie(1), () => null, () => redraws++);

        _state.Start(Movie(1), null);
        _state.Toggle(Movie(2), null);
        Assert.Equal(2, redraws);

        card.Dispose();
        _state.Stop();

        Assert.Equal(2, redraws);
        Assert.False(_state.StartById(TestItems.Id(1)));
    }

    [Fact]
    public void Select_all_adds_every_card_on_the_page()
    {
        using var first = _state.Attach(() => Movie(1), () => null, () => { });
        using var second = _state.Attach(() => Movie(2), () => null, () => { });
        using var empty = _state.Attach(() => null, () => null, () => { });

        _state.SelectAll();
        Assert.Equal(0, _state.Count);

        _state.Start(Movie(2), null);
        _state.SelectAll();

        Assert.Equal(new[] { "Movie 2", "Movie 1" }, _state.Items.Select(item => item.Name));
    }

    [Fact]
    public void Lists_of_the_selection_are_refreshed_once_each()
    {
        var first = 0;
        var second = 0;
        var firstList = new ItemActionContext { NotifyRefreshNeeded = () => first++ };
        var secondList = new ItemActionContext { NotifyRefreshNeeded = () => second++ };

        _state.Start(Movie(1), firstList);
        _state.Toggle(Movie(2), firstList);
        _state.Toggle(Movie(3), secondList);
        _state.Toggle(Movie(4), null);
        _state.RefreshLists();

        Assert.Equal(1, first);
        Assert.Equal(1, second);
    }

    [Fact]
    public async Task The_select_command_of_the_item_menu_starts_selection_mode()
    {
        var handler = new MultiSelectCommandHandler(_state);
        var movie = Movie(1);

        Assert.True(handler.CanHandle(ItemCommand.MultiSelect, movie));
        Assert.False(handler.CanHandle(ItemCommand.Download, movie));
        var result = await handler.ExecuteAsync(ItemCommand.MultiSelect, movie, new ItemActionContext());

        Assert.Equal(ItemCommandResult.None, result);
        Assert.True(_state.IsActive);
        Assert.Same(movie, Assert.Single(_state.Items));
    }
}

/// <summary>The menu of the selection toolbar (showMenuForSelectedItems).</summary>
public class MultiSelectCommandsTests : ItemsTestContext
{
    private readonly RecordingHandler _handler = new();

    private int _refreshes;

    private MultiSelectState State => Get<MultiSelectState>();

    private MultiSelectCommands Commands => Get<MultiSelectCommands>();

    protected override void ConfigureServices(IServiceCollection services) => services.AddSingleton<IItemCommandHandler>(_handler);

    private static BaseItemDto Movie(int number) => TestItems.Item(TestItems.Movie(number, "Movie " + number));

    private void Select(params int[] numbers)
    {
        var context = new ItemActionContext { NotifyRefreshNeeded = () => _refreshes++ };
        foreach (var number in numbers)
        {
            App.Server.OnGet("/Items/" + TestItems.Id(number), TestItems.Movie(number, "Movie " + number));
            if (State.IsActive)
            {
                State.Toggle(Movie(number), context);
            }
            else
            {
                State.Start(Movie(number), context);
            }
        }
    }

    private async Task<string[]> MenuIdsAsync() => (await Commands.MenuAsync()).Select(entry => entry.Id!).ToArray();

    [Fact]
    public async Task A_plain_user_gets_the_basic_commands()
    {
        SignIn();
        Select(1);

        var menu = await Commands.MenuAsync();

        Assert.Equal(new[] { "selectall", "playlist", "markplayed", "markunplayed" }, menu.Select(entry => entry.Id));
        Assert.Equal(new[] { "Select All", "Add to playlist", "Mark played", "Mark unplayed" }, menu.Select(entry => entry.Name));
        Assert.Equal(new[] { "select_all", "playlist_add", "check_box", "check_box_outline_blank" }, menu.Select(entry => entry.Icon));
    }

    [Fact]
    public async Task Policies_add_collection_and_delete_commands()
    {
        SignIn(configure: user =>
        {
            user.Policy!.EnableCollectionManagement = true;
            user.Policy.EnableContentDeletion = true;
        });
        Select(1);

        Assert.Equal(new[] { "selectall", "addtocollection", "playlist", "delete", "markplayed", "markunplayed" }, await MenuIdsAsync());
    }

    [Fact]
    public async Task An_administrator_can_group_versions_of_videos_and_refresh()
    {
        SignIn(admin: true);
        Select(1);

        Assert.Equal(
            new[] { "selectall", "addtocollection", "playlist", "groupvideos", "markplayed", "markunplayed", "refresh" },
            await MenuIdsAsync());
    }

    [Fact]
    public async Task Versions_are_grouped_for_videos_only()
    {
        SignIn(admin: true);
        var album = TestItems.Item(new { Id = TestItems.Id(5), Name = "Album", Type = "MusicAlbum", IsFolder = true });
        App.Server.OnGet("/Items/" + TestItems.Id(5), new { Id = TestItems.Id(5), Name = "Album", Type = "MusicAlbum", IsFolder = true });
        State.Start(album, null);

        Assert.DoesNotContain("groupvideos", await MenuIdsAsync());
    }

    [Fact]
    public async Task Download_is_offered_when_a_handler_downloads()
    {
        SignIn();
        Select(1, 2);
        _handler.Commands.Add(ItemCommand.Download);

        Assert.Equal(new[] { "selectall", "playlist", "download", "markplayed", "markunplayed" }, await MenuIdsAsync());

        await Commands.ExecuteAsync(MultiSelectCommands.Download);

        Assert.Equal(new[] { "Movie 1", "Movie 2" }, _handler.Calls.Select(call => call.Item.Name));
        Assert.All(_handler.Calls, call => Assert.Equal(ItemCommand.Download, call.Command));
        Assert.False(State.IsActive);
    }

    [Fact]
    public async Task The_menu_survives_a_failing_item_request()
    {
        SignIn();
        State.Start(Movie(7), null);

        Assert.Equal(new[] { "selectall", "playlist", "markplayed", "markunplayed" }, await MenuIdsAsync());
    }

    [Fact]
    public async Task Nothing_runs_without_a_selection()
    {
        SignIn();
        _handler.Commands.Add(ItemCommand.AddToPlaylist);

        await Commands.ExecuteAsync(MultiSelectCommands.AddToPlaylist);
        await Commands.ExecuteAsync(MultiSelectCommands.MarkPlayed);

        Assert.Empty(_handler.Calls);
        Assert.Empty(App.Server.Requests);
    }

    [Theory]
    [InlineData("addtocollection", "addtocollection")]
    [InlineData("playlist", "addtoplaylist")]
    [InlineData("refresh", "refresh")]
    public async Task Commands_of_other_workstreams_get_the_selection(string command, string forwarded)
    {
        SignIn();
        Select(1, 2);
        _handler.Commands.Add(forwarded);

        await Commands.ExecuteAsync(command);

        var call = Assert.Single(_handler.Calls);
        Assert.Equal(forwarded, call.Command);
        Assert.Equal("Movie 1", call.Item.Name);
        Assert.Equal(new[] { "Movie 1", "Movie 2" }, call.Context.SelectedItems!.Select(item => item.Name));
        Assert.False(State.IsActive);
        Assert.Equal(1, _refreshes);
    }

    [Fact]
    public async Task Select_all_keeps_selection_mode()
    {
        SignIn();
        using var first = State.Attach(() => Movie(1), () => null, () => { });
        using var second = State.Attach(() => Movie(2), () => null, () => { });
        Select(1);

        await Commands.ExecuteAsync(MultiSelectCommands.SelectAll);

        Assert.True(State.IsActive);
        Assert.Equal(2, State.Count);
        Assert.Equal(0, _refreshes);
    }

    [Fact]
    public async Task Delete_asks_first_and_deletes_every_item()
    {
        SignIn();
        Select(1, 2);
        App.Server.OnDelete("/Items/" + TestItems.Id(1));
        App.Server.OnDelete("/Items/" + TestItems.Id(2));

        var pending = Commands.ExecuteAsync(MultiSelectCommands.Delete);
        var confirmation = await NextDialogAsync();

        Assert.False(pending.IsCompleted);
        Assert.Empty(App.Server.Requests);
        confirmation.Close("ok");
        await pending;

        Assert.Equal(
            new[] { "DELETE /Items/" + TestItems.Id(1), "DELETE /Items/" + TestItems.Id(2) },
            App.Server.Requests.Select(request => $"{request.Method} {request.Path}"));
        Assert.False(State.IsActive);
        Assert.Equal(1, _refreshes);
    }

    [Fact]
    public async Task A_cancelled_delete_keeps_the_selection()
    {
        SignIn();
        Select(1);

        var pending = Commands.ExecuteAsync(MultiSelectCommands.Delete);
        (await NextDialogAsync()).Cancel();
        await pending;

        Assert.Empty(App.Server.Requests);
        Assert.True(State.IsActive);
        Assert.Equal(0, _refreshes);
    }

    [Fact]
    public async Task A_refused_delete_shows_an_alert_and_stops()
    {
        SignIn();
        Select(1, 2);

        var pending = Commands.ExecuteAsync(MultiSelectCommands.Delete);
        (await NextDialogAsync()).Close("ok");
        var alert = await NextDialogAsync();
        Assert.False(pending.IsCompleted);
        alert.Close("ok");
        await pending;

        Assert.Equal(HttpMethod.Delete, Assert.Single(App.Server.Requests).Method);
        Assert.False(State.IsActive);
    }

    [Fact]
    public async Task Grouping_versions_needs_two_items()
    {
        SignIn(admin: true);
        Select(1);

        var pending = Commands.ExecuteAsync(MultiSelectCommands.GroupVersions);
        (await NextDialogAsync()).Close("ok");
        await pending;

        Assert.Empty(App.Server.Requests);
        Assert.True(State.IsActive);
    }

    [Fact]
    public async Task Grouping_versions_merges_the_selected_videos()
    {
        SignIn(admin: true);
        Select(1, 2);
        App.Server.OnPost("/Videos/MergeVersions");

        await Commands.ExecuteAsync(MultiSelectCommands.GroupVersions);

        var request = Assert.Single(App.Server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(TestItems.Id(1) + "," + TestItems.Id(2), request.Query("Ids"));
        Assert.False(State.IsActive);
        Assert.Equal(1, _refreshes);
        Assert.False(Get<LoadingService>().IsVisible);
    }

    [Theory]
    [InlineData("markplayed", "POST")]
    [InlineData("markunplayed", "DELETE")]
    public async Task Marking_played_and_unplayed_changes_every_item(string command, string method)
    {
        SignIn();
        Select(1, 2);
        foreach (var number in new[] { 1, 2 })
        {
            App.Server.On(new HttpMethod(method), "/UserPlayedItems/" + TestItems.Id(number), _ => FakeHttp.Json(new { Played = method == "POST" }));
        }

        await Commands.ExecuteAsync(command);

        Assert.Equal(
            new[] { $"{method} /UserPlayedItems/{TestItems.Id(1)}", $"{method} /UserPlayedItems/{TestItems.Id(2)}" },
            App.Server.Requests.Select(request => $"{request.Method} {request.Path}"));
        Assert.False(State.IsActive);
        Assert.Equal(1, _refreshes);
    }

    private sealed class RecordingHandler : IItemCommandHandler
    {
        public HashSet<string> Commands { get; } = [];

        public List<(string Command, BaseItemDto Item, ItemActionContext Context)> Calls { get; } = [];

        public bool CanHandle(string command, BaseItemDto item) => Commands.Contains(command);

        public Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
        {
            Calls.Add((command, item, context));
            return Task.FromResult(ItemCommandResult.None);
        }
    }
}
