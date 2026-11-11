using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

public class PlayQueueTests
{
    private static BaseItemDto Item(string name) => new() { Name = name };

    private static PlayQueue QueueOf(params string[] names)
    {
        var queue = new PlayQueue((_, max) => max);
        queue.SetPlaylist(names.Select(Item));
        return queue;
    }

    private static string Names(PlayQueue queue) => string.Join(",", queue.GetPlaylist().Select(i => i.Name));

    [Fact]
    public void SetPlaylist_gives_every_item_a_unique_playlist_item_id()
    {
        var queue = QueueOf("a", "b", "c");

        var ids = queue.GetPlaylist().Select(i => i.PlaylistItemId).ToList();
        Assert.All(ids, id => Assert.StartsWith("playlistItem", id));
        Assert.Equal(3, ids.Distinct().Count());
        Assert.Null(queue.CurrentPlaylistItemId);
        Assert.Equal(-1, queue.GetCurrentPlaylistIndex());
        Assert.Null(queue.GetCurrentItem());
    }

    [Fact]
    public void Existing_playlist_item_ids_are_kept()
    {
        var queue = new PlayQueue();
        queue.SetPlaylist([new BaseItemDto { Name = "a", PlaylistItemId = "mine" }]);

        Assert.Equal("mine", queue.GetPlaylist()[0].PlaylistItemId);
    }

    [Fact]
    public void Queue_appends_and_QueueNext_inserts_after_the_current_item()
    {
        var queue = QueueOf("a", "b");
        queue.SetPlaylistIndex(0);

        queue.Queue([Item("z")]);
        queue.QueueNext([Item("n1"), Item("n2")]);

        Assert.Equal("a,n1,n2,b,z", Names(queue));
        Assert.Equal(5, queue.GetPlaylist().Select(i => i.PlaylistItemId).Distinct().Count());
    }

    [Fact]
    public void QueueNext_appends_when_nothing_is_current()
    {
        var queue = QueueOf("a", "b");

        queue.QueueNext([Item("n")]);

        Assert.Equal("a,b,n", Names(queue));
    }

    [Fact]
    public void SetPlaylistIndex_selects_the_current_item_and_out_of_range_clears_it()
    {
        var queue = QueueOf("a", "b", "c");

        queue.SetPlaylistIndex(1);
        Assert.Equal("b", queue.GetCurrentItem()?.Name);
        Assert.Equal(1, queue.GetCurrentPlaylistIndex());

        queue.SetPlaylistIndex(7);
        Assert.Null(queue.GetCurrentItem());

        queue.SetPlaylistIndex(2);
        queue.SetPlaylistIndex(-1);
        Assert.Null(queue.CurrentPlaylistItemId);
    }

    [Fact]
    public void Next_item_follows_the_repeat_mode()
    {
        var queue = QueueOf("a", "b");
        queue.SetPlaylistIndex(0);
        Assert.Equal(("b", 1), (queue.GetNextItemInfo()!.Item.Name, queue.GetNextItemInfo()!.Index));

        queue.SetPlaylistIndex(1);
        Assert.Null(queue.GetNextItemInfo());

        queue.SetRepeatMode(PlayQueue.RepeatAll);
        Assert.Equal(("a", 0), (queue.GetNextItemInfo()!.Item.Name, queue.GetNextItemInfo()!.Index));

        queue.SetRepeatMode(PlayQueue.RepeatOne);
        Assert.Equal(("b", 1), (queue.GetNextItemInfo()!.Item.Name, queue.GetNextItemInfo()!.Index));

        Assert.Throws<ArgumentException>(() => queue.SetRepeatMode("Sometimes"));
        Assert.Equal(PlayQueue.RepeatOne, queue.RepeatMode);
    }

    [Fact]
    public void RepeatOne_without_a_current_item_has_no_next_item()
    {
        var queue = QueueOf("a");
        queue.SetRepeatMode(PlayQueue.RepeatOne);

        Assert.Null(queue.GetNextItemInfo());
    }

    [Fact]
    public void SetPlaylist_resets_the_repeat_mode_but_not_the_shuffle_mode()
    {
        var queue = QueueOf("a", "b");
        queue.SetRepeatMode(PlayQueue.RepeatAll);
        queue.SetShuffleMode(PlayQueue.Shuffled);

        queue.SetPlaylist([Item("x")]);

        Assert.Equal(PlayQueue.RepeatNone, queue.RepeatMode);
        Assert.Equal(PlayQueue.Shuffled, queue.ShuffleMode);
    }

    [Fact]
    public void Shuffle_keeps_the_current_item_first_and_sorting_restores_the_order()
    {
        // The random source always answers "max": a deterministic Fisher-Yates.
        var queue = QueueOf("a", "b", "c", "d");
        queue.SetPlaylistIndex(2);

        queue.SetShuffleMode(PlayQueue.Shuffled);

        Assert.Equal(PlayQueue.Shuffled, queue.ShuffleMode);
        Assert.Equal("c", queue.GetPlaylist()[0].Name);
        Assert.Equal(0, queue.GetCurrentPlaylistIndex());
        Assert.Equal("a,b,c,d", string.Join(",", queue.GetPlaylist().Select(i => i.Name).Order()));
        Assert.NotEqual("a,b,c,d", Names(queue));

        queue.ToggleShuffleMode();

        Assert.Equal(PlayQueue.Sorted, queue.ShuffleMode);
        Assert.Equal("a,b,c,d", Names(queue));
        Assert.Equal(2, queue.GetCurrentPlaylistIndex());

        Assert.Throws<ArgumentException>(() => queue.SetShuffleMode("Random"));
    }

    [Fact]
    public void Shuffle_swaps_from_the_end_with_the_given_random_numbers()
    {
        var asked = new List<(int Min, int Max)>();
        var queue = new PlayQueue((min, max) =>
        {
            asked.Add((min, max));
            return min;
        });
        queue.SetPlaylist(new[] { "a", "b", "c", "d" }.Select(Item));

        queue.ShufflePlaylist();

        // i = 3: swap with 0 -> d,b,c,a; i = 2: swap with 0 -> c,b,d,a; i = 1: swap with 0 -> b,c,d,a.
        Assert.Equal(new List<(int Min, int Max)> { (0, 2), (0, 1), (0, 0) }, asked);
        Assert.Equal("b,c,d,a", Names(queue));
    }

    [Fact]
    public void Removing_items_reports_whether_the_current_one_went()
    {
        var queue = QueueOf("a", "b", "c");
        queue.SetPlaylistIndex(1);
        var ids = queue.GetPlaylist().Select(i => i.PlaylistItemId!).ToList();

        var other = queue.RemoveFromPlaylist([ids[2]]);
        Assert.Equal(new QueueRemoveResult("removed", false), other);
        Assert.Equal("a,b", Names(queue));

        var current = queue.RemoveFromPlaylist([ids[1]]);
        Assert.Equal(new QueueRemoveResult("removed", true), current);
        Assert.Equal("a", Names(queue));

        // Removing everything that is left is reported as "empty" and leaves the list alone.
        var last = queue.RemoveFromPlaylist([ids[0]]);
        Assert.Equal("empty", last.Result);
        Assert.Equal("a", Names(queue));
    }

    [Fact]
    public void Removing_from_a_shuffled_queue_also_removes_from_the_sorted_list()
    {
        var queue = QueueOf("a", "b", "c");
        queue.ShufflePlaylist();
        var id = queue.GetPlaylist().First(i => i.Name == "b").PlaylistItemId!;

        queue.RemoveFromPlaylist([id]);
        queue.SortShuffledPlaylist();

        Assert.Equal("a,c", Names(queue));
    }

    [Fact]
    public void Moving_an_item_changes_its_index()
    {
        var queue = QueueOf("a", "b", "c");
        var ids = queue.GetPlaylist().Select(i => i.PlaylistItemId!).ToList();

        Assert.Equal(new QueueMoveResult("moved", ids[0], 2), queue.MovePlaylistItem(ids[0], 2));
        Assert.Equal("b,c,a", Names(queue));

        Assert.Equal("noop", queue.MovePlaylistItem(ids[0], 2).Result);
        Assert.Equal("noop", queue.MovePlaylistItem("unknown", 0).Result);
        Assert.Throws<ArgumentOutOfRangeException>(() => queue.MovePlaylistItem(ids[0], 3));
    }

    [Fact]
    public void ClearPlaylist_keeps_the_current_item_unless_told_otherwise()
    {
        var queue = QueueOf("a", "b", "c");
        queue.SetPlaylistIndex(1);

        queue.ClearPlaylist();
        Assert.Equal("b", Names(queue));

        queue.ClearPlaylist(clearCurrentItem: true);
        Assert.Empty(queue.GetPlaylist());
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var queue = QueueOf("a", "b");
        queue.SetPlaylistIndex(1);
        queue.SetRepeatMode(PlayQueue.RepeatAll);
        queue.ShufflePlaylist();

        queue.Reset();

        Assert.Empty(queue.GetPlaylist());
        Assert.Null(queue.CurrentPlaylistItemId);
        Assert.Equal(PlayQueue.RepeatNone, queue.RepeatMode);
        Assert.Equal(PlayQueue.Sorted, queue.ShuffleMode);
    }
}
