using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Result of <see cref="PlayQueue.RemoveFromPlaylist"/>.</summary>
public sealed record QueueRemoveResult(string Result, bool IsCurrentIndex = false);

/// <summary>Result of <see cref="PlayQueue.MovePlaylistItem"/>.</summary>
public sealed record QueueMoveResult(string Result, string? PlaylistItemId = null, int NewIndex = 0);

/// <summary>An item of the queue together with its index.</summary>
public sealed record QueueItemInfo(BaseItemDto Item, int Index);

/// <summary>
/// The play queue. Port of components/playback/playqueuemanager.js: same ordering rules, repeat
/// and shuffle modes and the same "playlistItemN" ids (kept in <see cref="BaseItemDto.PlaylistItemId"/>).
/// </summary>
public sealed class PlayQueue
{
    public const string RepeatNone = "RepeatNone";
    public const string RepeatAll = "RepeatAll";
    public const string RepeatOne = "RepeatOne";
    public const string Sorted = "Sorted";
    public const string Shuffled = "Shuffle";

    private static int _currentId;

    private readonly Func<int, int, int> _randomInt;
    private List<BaseItemDto> _sortedPlaylist = [];
    private List<BaseItemDto> _playlist = [];
    private string? _currentPlaylistItemId;

    /// <param name="randomInt">Random integer in an inclusive range; replaced in tests.</param>
    public PlayQueue(Func<int, int, int>? randomInt = null)
    {
        _randomInt = randomInt ?? ((min, max) => Random.Shared.Next(min, max + 1));
    }

    public string RepeatMode { get; private set; } = RepeatNone;

    public string ShuffleMode { get; private set; } = Sorted;

    public string? CurrentPlaylistItemId => _currentPlaylistItemId;

    public IReadOnlyList<BaseItemDto> GetPlaylist() => _playlist.ToList();

    public void SetPlaylist(IEnumerable<BaseItemDto> items)
    {
        var list = items.ToList();
        AddUniquePlaylistItemId(list);
        _currentPlaylistItemId = null;
        _playlist = list;
        RepeatMode = RepeatNone;
    }

    public void Queue(IEnumerable<BaseItemDto> items)
    {
        var list = items.ToList();
        AddUniquePlaylistItemId(list);
        _playlist.AddRange(list);
    }

    public void ShufflePlaylist()
    {
        _sortedPlaylist = [.. _playlist];
        var current = _playlist.FirstOrDefault(i => i.PlaylistItemId == _currentPlaylistItemId);
        var currentIndex = GetCurrentPlaylistIndex();
        if (currentIndex >= 0)
        {
            _playlist.RemoveAt(currentIndex);
        }

        for (var i = _playlist.Count - 1; i > 0; i--)
        {
            var j = _randomInt(0, i - 1);
            (_playlist[i], _playlist[j]) = (_playlist[j], _playlist[i]);
        }

        if (current is not null)
        {
            _playlist.Insert(0, current);
        }

        ShuffleMode = Shuffled;
    }

    public void SortShuffledPlaylist()
    {
        _playlist = [.. _sortedPlaylist];
        _sortedPlaylist = [];
        ShuffleMode = Sorted;
    }

    public void ClearPlaylist(bool clearCurrentItem = false)
    {
        var current = _playlist.FirstOrDefault(i => i.PlaylistItemId == _currentPlaylistItemId);
        _playlist = [];
        if (!clearCurrentItem && current is not null)
        {
            _playlist.Add(current);
        }
    }

    public void QueueNext(IEnumerable<BaseItemDto> items)
    {
        var list = items.ToList();
        AddUniquePlaylistItemId(list);
        var currentIndex = GetCurrentPlaylistIndex();
        if (currentIndex == -1)
        {
            _playlist.AddRange(list);
        }
        else
        {
            _playlist.InsertRange(currentIndex + 1, list);
        }
    }

    public int GetCurrentPlaylistIndex() => FindPlaylistIndex(_currentPlaylistItemId, _playlist);

    public BaseItemDto? GetCurrentItem()
    {
        var index = GetCurrentPlaylistIndex();
        return index == -1 ? null : _playlist[index];
    }

    public void SetPlaylistState(string? playlistItemId) => _currentPlaylistItemId = playlistItemId;

    public void SetPlaylistIndex(int playlistIndex)
    {
        if (playlistIndex < 0)
        {
            SetPlaylistState(null);
        }
        else
        {
            SetPlaylistState(playlistIndex < _playlist.Count ? _playlist[playlistIndex].PlaylistItemId : null);
        }
    }

    public QueueRemoveResult RemoveFromPlaylist(IReadOnlyCollection<string> playlistItemIds)
    {
        if (_playlist.Count <= playlistItemIds.Count)
        {
            return new QueueRemoveResult("empty");
        }

        var currentPlaylistItemId = _currentPlaylistItemId;
        var isCurrentIndex = playlistItemIds.Contains(currentPlaylistItemId ?? string.Empty);
        _sortedPlaylist = _sortedPlaylist.Where(item => !playlistItemIds.Contains(item.PlaylistItemId ?? string.Empty)).ToList();
        _playlist = _playlist.Where(item => !playlistItemIds.Contains(item.PlaylistItemId ?? string.Empty)).ToList();
        return new QueueRemoveResult("removed", isCurrentIndex);
    }

    public QueueMoveResult MovePlaylistItem(string playlistItemId, int newIndex)
    {
        var oldIndex = _playlist.FindIndex(item => item.PlaylistItemId == playlistItemId);
        if (oldIndex == newIndex || oldIndex == -1)
        {
            return new QueueMoveResult("noop");
        }

        if (newIndex >= _playlist.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(newIndex), "newIndex out of bounds");
        }

        var item = _playlist[oldIndex];
        _playlist.RemoveAt(oldIndex);
        _playlist.Insert(newIndex, item);
        return new QueueMoveResult("moved", playlistItemId, newIndex);
    }

    public void Reset()
    {
        _sortedPlaylist = [];
        _playlist = [];
        _currentPlaylistItemId = null;
        RepeatMode = RepeatNone;
        ShuffleMode = Sorted;
    }

    public void SetRepeatMode(string value)
    {
        if (value is not (RepeatOne or RepeatAll or RepeatNone))
        {
            throw new ArgumentException("invalid value provided for setRepeatMode", nameof(value));
        }

        RepeatMode = value;
    }

    public void SetShuffleMode(string value)
    {
        switch (value)
        {
            case Shuffled:
                ShufflePlaylist();
                break;
            case Sorted:
                SortShuffledPlaylist();
                break;
            default:
                throw new ArgumentException("invalid value provided to setShuffleMode", nameof(value));
        }
    }

    public void ToggleShuffleMode()
    {
        if (ShuffleMode == Sorted)
        {
            ShufflePlaylist();
        }
        else
        {
            SortShuffledPlaylist();
        }
    }

    public QueueItemInfo? GetNextItemInfo()
    {
        int newIndex;
        var playlistLength = _playlist.Count;
        switch (RepeatMode)
        {
            case RepeatOne:
                newIndex = GetCurrentPlaylistIndex();
                break;
            case RepeatAll:
                newIndex = GetCurrentPlaylistIndex() + 1;
                if (newIndex >= playlistLength)
                {
                    newIndex = 0;
                }

                break;
            default:
                newIndex = GetCurrentPlaylistIndex() + 1;
                break;
        }

        if (newIndex < 0 || newIndex >= playlistLength)
        {
            return null;
        }

        return new QueueItemInfo(_playlist[newIndex], newIndex);
    }

    private static void AddUniquePlaylistItemId(List<BaseItemDto> items)
    {
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.PlaylistItemId))
            {
                item.PlaylistItemId = "playlistItem" + _currentId;
                _currentId++;
            }
        }
    }

    private static int FindPlaylistIndex(string? playlistItemId, List<BaseItemDto> list) =>
        list.FindIndex(item => item.PlaylistItemId == playlistItemId);
}
