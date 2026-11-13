using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

public sealed class PlaybackItemCommandTests
{
    [Fact]
    public async Task Clear_queue_command_clears_the_items_after_the_current_item()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var manager = harness.Manager;
        var items = PlaybackServer.Songs();
        manager.Queue.SetPlaylist(items);
        manager.Queue.SetPlaylistIndex(0);
        var handler = new PlaybackItemCommandHandler(
            manager,
            harness.Get<AppDialogs>(),
            harness.Get<Globalize>());

        await handler.ExecuteAsync(ItemCommand.ClearQueue, items[0], new ItemActionContext());

        Assert.Single(manager.GetPlaylist());
        Assert.Same(items[0], manager.GetPlaylist()[0]);
    }

    [Fact]
    public async Task Set_playlist_index_command_starts_the_selected_queue_item()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var items = PlaybackServer.Songs();
        await harness.Manager.PlayAsync(new PlayOptions { Items = items });
        var selected = harness.Manager.GetPlaylist()[1];
        var handler = new PlaybackItemCommandHandler(
            harness.Manager,
            harness.Get<AppDialogs>(),
            harness.Get<Globalize>());

        await handler.ExecuteAsync(ItemCommand.SetPlaylistIndex, selected, new ItemActionContext());

        await PlaybackHarness.Eventually(() => harness.Manager.CurrentItem?.Id == selected.Id, "the selected queue item");
        Assert.Equal(selected.PlaylistItemId, harness.Manager.CurrentPlaylistItemId);
    }

    [Fact]
    public async Task Play_menu_resumes_at_the_saved_position_when_selected()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var dialogs = harness.Get<AppDialogs>();
        var handler = new PlaybackItemCommandHandler(
            harness.Manager,
            dialogs,
            harness.Get<Globalize>());
        var item = PlaybackServer.Movie();
        item.UserData = new UserItemDataDto { PlaybackPositionTicks = 120_000_000 };
        PlaybackServer.ScriptItems(harness.Server, item);

        var pending = handler.ExecuteAsync(ItemCommand.PlayMenu, item, new ItemActionContext());
        Assert.Single(dialogs.Dialogs);
        dialogs.Dialogs[0].Close("resume");
        await pending;

        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 1, "the resumed media load");
        Assert.Equal(120_000_000, (long?)PlaybackServer.PlaybackInfoRequests(harness.Server).Single()["StartTimeTicks"]);
        Assert.Equal(12, harness.Player.Loads.Single().Request.StartSeconds);
    }
}
