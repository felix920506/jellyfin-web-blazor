using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>What to play. Port of the options object of playbackManager.play().</summary>
public sealed record PlayOptions
{
    /// <summary>Items to play, already loaded. Either this or <see cref="Ids"/> is set.</summary>
    public IReadOnlyList<BaseItemDto>? Items { get; init; }

    /// <summary>Ids of the items to play; folders, series, albums etc. are expanded to their children.</summary>
    public IReadOnlyList<string>? Ids { get; init; }

    /// <summary>Resume position of the first item, in ticks (100 ns).</summary>
    public long? StartPositionTicks { get; init; }

    /// <summary>Index of the first item to play within the list.</summary>
    public int? StartIndex { get; init; }
    public string? MediaSourceId { get; init; }
    public int? AudioStreamIndex { get; init; }
    public int? SubtitleStreamIndex { get; init; }
    public bool Shuffle { get; init; }

    /// <summary>Play in full screen (video). Default true.</summary>
    public bool Fullscreen { get; init; } = true;

    /// <summary>Extra query for expanding folders, e.g. filters of the list being played.</summary>
    public IReadOnlyDictionary<string, string>? QueryOptions { get; init; }
}

/// <summary>
/// Entry point for starting and controlling playback from the UI (cards, details page, lists,
/// remote commands). Pages depend on this interface only; the implementation owns the players,
/// the queue and progress reporting.
/// </summary>
public interface IPlaybackManager
{
    /// <summary>Raised when anything observable changed: start, stop, pause, track change, queue change.</summary>
    event Action? StateChanged;

    /// <summary>True while something is playing or paused.</summary>
    bool IsPlaying { get; }

    bool IsPlayingVideo { get; }

    bool IsPlayingAudio { get; }

    bool IsPaused { get; }

    /// <summary>The item being played, or null.</summary>
    BaseItemDto? CurrentItem { get; }

    /// <summary>Whether this client can play the item at all (used to show/hide play buttons).</summary>
    bool CanPlay(BaseItemDto item);

    /// <summary>Whether the item can be added to the current queue.</summary>
    bool CanQueue(BaseItemDto item);

    Task PlayAsync(PlayOptions options);

    /// <summary>Appends to the end of the queue (starts playback when nothing is playing).</summary>
    Task QueueAsync(PlayOptions options);

    /// <summary>Inserts after the current item.</summary>
    Task QueueNextAsync(PlayOptions options);

    /// <summary>Plays the item's children in random order.</summary>
    Task ShuffleAsync(BaseItemDto item);

    /// <summary>Plays an instant mix based on the item.</summary>
    Task InstantMixAsync(BaseItemDto item);

    /// <summary>Plays the item's trailers.</summary>
    Task PlayTrailersAsync(BaseItemDto item);

    Task PauseAsync();

    Task UnpauseAsync();

    Task PlayPauseAsync();

    Task StopAsync();

    Task NextTrackAsync();

    Task PreviousTrackAsync();

    /// <summary>Seeks the current item to an absolute position in ticks.</summary>
    Task SeekAsync(long positionTicks);
}

/// <summary>Placeholder used until the playback feature is registered: nothing can be played.</summary>
public sealed class NullPlaybackManager : IPlaybackManager
{
    public event Action? StateChanged
    {
        add { }
        remove { }
    }

    public bool IsPlaying => false;
    public bool IsPlayingVideo => false;
    public bool IsPlayingAudio => false;
    public bool IsPaused => false;
    public BaseItemDto? CurrentItem => null;

    public bool CanPlay(BaseItemDto item) => false;

    public bool CanQueue(BaseItemDto item) => false;

    public Task PlayAsync(PlayOptions options) => Task.CompletedTask;

    public Task QueueAsync(PlayOptions options) => Task.CompletedTask;

    public Task QueueNextAsync(PlayOptions options) => Task.CompletedTask;

    public Task ShuffleAsync(BaseItemDto item) => Task.CompletedTask;

    public Task InstantMixAsync(BaseItemDto item) => Task.CompletedTask;

    public Task PlayTrailersAsync(BaseItemDto item) => Task.CompletedTask;

    public Task PauseAsync() => Task.CompletedTask;

    public Task UnpauseAsync() => Task.CompletedTask;

    public Task PlayPauseAsync() => Task.CompletedTask;

    public Task StopAsync() => Task.CompletedTask;

    public Task NextTrackAsync() => Task.CompletedTask;

    public Task PreviousTrackAsync() => Task.CompletedTask;

    public Task SeekAsync(long positionTicks) => Task.CompletedTask;
}
