using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Something playback can be sent to: this browser or another session. Port of the target objects of
/// getTargets() (plugins/sessionPlayer) and createTarget() (playbackmanager.js).
/// </summary>
public sealed record PlayTarget
{
    /// <summary>The device name for a session, the player's name for a local player.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The session id, or the id of the local player.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Name of the <see cref="IMediaPlayer"/> that drives the target.</summary>
    public string PlayerName { get; init; } = string.Empty;

    public string? DeviceName { get; init; }

    /// <summary>"smartphone", "tablet", "desktop", "cast", "tv"; anything else gets the generic icon.</summary>
    public string? DeviceType { get; init; }

    public string? AppName { get; init; }

    public bool IsLocalPlayer { get; init; }

    public IReadOnlyList<string> PlayableMediaTypes { get; init; } = [];

    public IReadOnlyList<string> SupportedCommands { get; init; } = [];

    /// <summary>The user signed in on the target, if any.</summary>
    public string? UserName { get; init; }

    public string? UserId { get; init; }

    public string? UserPrimaryImageTag { get; init; }
}

/// <summary>Port of the object getPlayerInfo() returns: the active player and its target.</summary>
public sealed record PlayerInfo
{
    public string Name { get; init; } = string.Empty;

    public bool IsLocalPlayer { get; init; }

    public string Id { get; init; } = string.Empty;

    public string? DeviceName { get; init; }

    public IReadOnlyList<string> PlayableMediaTypes { get; init; } = [];

    public IReadOnlyList<string> SupportedCommands { get; init; } = [];
}

/// <summary>
/// A player that is not this browser: it keeps its own queue and state, and the manager hands the
/// requests to it instead of handling them itself (the original's branches on
/// <c>!enableLocalPlaylistManagement(player)</c>). The session player implements it; a cast player
/// of a later workstream can do the same.
/// </summary>
/// <remarks>
/// The <see cref="IMediaPlayer"/> members that make no sense remotely (device profile, stream
/// playback, picture in picture) are never called by the manager for these players.
/// </remarks>
public interface IRemotePlayer : IMediaPlayer
{
    /// <summary>Called once when the player is registered; the player asks the manager for the active target.</summary>
    void Attach(PlaybackManager manager);

    /// <summary>The targets this player can drive.</summary>
    Task<IReadOnlyList<PlayTarget>> GetTargetsAsync();

    /// <summary>Connects to the target; throws when that is not possible.</summary>
    Task TryPairAsync(PlayTarget target);

    /// <summary>Starts following the state of the active target (reference counted).</summary>
    void BeginPlayerUpdates();

    void EndPlayerUpdates();

    /// <summary>The state of the target in the shape of the local players' state.</summary>
    PlayerState GetPlayerState();

    /// <summary>What the target plays, or null.</summary>
    BaseItemDto? NowPlayingItem { get; }

    /// <summary>Port of isPlaying(mediaType): something plays, of this media type when one is given.</summary>
    bool IsPlaying(string? mediaType = null);

    IReadOnlyList<BaseItemDto> Playlist { get; }

    string? CurrentPlaylistItemId { get; }

    string RepeatMode { get; }

    /// <summary>"Shuffle" or "Sorted".</summary>
    string ShuffleMode { get; }

    int? AudioStreamIndex { get; }

    int? SubtitleStreamIndex { get; }

    IReadOnlyList<MediaStream> AudioTracks { get; }

    IReadOnlyList<MediaStream> SubtitleTracks { get; }

    Task PlayAsync(PlayOptions options);

    Task QueueAsync(IReadOnlyList<BaseItemDto> items);

    Task QueueNextAsync(IReadOnlyList<BaseItemDto> items);

    Task ShuffleAsync(BaseItemDto item);

    Task InstantMixAsync(BaseItemDto item);

    Task PlayTrailersAsync(BaseItemDto item);

    Task PlayPauseAsync();

    Task NextTrackAsync();

    Task PreviousTrackAsync();

    Task SeekAsync(long positionTicks);

    void ToggleMute();

    void VolumeUp();

    void VolumeDown();

    void SetRepeatMode(string value);

    void SetQueueShuffleMode(string value);

    Task SetCurrentPlaylistItemAsync(string playlistItemId);

    Task MovePlaylistItemAsync(string playlistItemId, int newIndex);

    /// <summary>Sends a general command ("MoveUp", "DisplayMessage", ...) with its arguments.</summary>
    Task SendCommandAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null);

    /// <summary>Ends the session on the target, for players that support "EndSession".</summary>
    Task EndSessionAsync() => Task.CompletedTask;
}

/// <summary>Arguments of a general command, in the order they are written.</summary>
public static class CommandArguments
{
    public static IReadOnlyDictionary<string, object?> Of(params (string Name, object? Value)[] arguments)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in arguments)
        {
            result[name] = value;
        }

        return result;
    }
}
