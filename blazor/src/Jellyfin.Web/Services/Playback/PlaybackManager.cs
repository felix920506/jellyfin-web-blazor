using System.Runtime.CompilerServices;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Play options that travel with a queue item (the original's item.playOptions).</summary>
public sealed class ItemPlayOptions
{
    public string? AspectRatio { get; set; }
    public bool Fullscreen { get; set; } = true;
    public string? MediaSourceId { get; set; }
    public int? AudioStreamIndex { get; set; }
    public int? SubtitleStreamIndex { get; set; }
    public int? SecondarySubtitleStreamIndex { get; set; }
    public long? StartPositionTicks { get; set; }
    public int StartIndex { get; set; }
    public bool IsFirstItem { get; set; }

    public ItemPlayOptions Clone() => (ItemPlayOptions)MemberwiseClone();
}

/// <summary>What the manager knows about a player's current stream (the original's playerData).</summary>
public sealed class PlayerData
{
    public StreamInfo? StreamInfo { get; set; }
    public int? AudioStreamIndex { get; set; }
    public int? SubtitleStreamIndex { get; set; }
    public int? SecondarySubtitleStreamIndex { get; set; }
    public int? MaxStreamingBitrate { get; set; }
    public bool IsChangingStream { get; set; }
}

/// <summary>Details of a "playbackstop" event.</summary>
public sealed record PlaybackStoppedInfo(IMediaPlayer Player, PlayerState State, BaseItemDto? NextItem, string? NextMediaType);

/// <summary>
/// The playback manager. Port of components/playback/playbackmanager.js: item translation, the
/// play queue, PlaybackInfo negotiation, stream changes and progress reporting for the local
/// players; requests for a remote target (<see cref="IRemotePlayer"/>) are handed to its player.
/// </summary>
/// <remarks>
/// Split over partial classes: this file (state, players, events), <c>.Items</c> (what to play),
/// <c>.Streams</c> (how to play it), <c>.Reporting</c> (telling the server), <c>.Controls</c>
/// (transport, tracks, queue) and <c>.Remote</c> (play targets). See
/// docs/blazor-port/06-playback-port.md and 15-playback-extras.md.
/// </remarks>
[AppService(typeof(IPlaybackManager))]
public sealed partial class PlaybackManager : IPlaybackManager, IStartupTask
{
    private readonly JellyfinApi _api;
    private readonly AppSettings _appSettings;
    private readonly UserSettings _userSettings;
    private readonly Globalize _globalize;
    private readonly AppDialogs _dialogs;
    private readonly LoadingService _loading;
    private readonly BitrateDetector _bitrateDetector;
    private readonly IPlaybackTimers _timers;
    private readonly Interop.IBrowser _browser;
    private readonly PlaybackInterceptors _interceptors;
    private readonly List<IMediaPlayer> _players = [];
    private readonly Dictionary<IMediaPlayer, PlayerData> _playerData = [];
    private readonly ConditionalWeakTable<BaseItemDto, ItemPlayOptions> _itemOptions = [];
    private readonly PlayQueue _playQueue = new();
    private bool _playNextAfterEnded = true;
    private IDisposable? _progressTimer;
    private long _startingPlaySession;
    private IReadOnlyList<BaseItemDto>? _pendingOtherItems;

    public PlaybackManager(
        JellyfinApi api,
        AppSettings appSettings,
        UserSettings userSettings,
        Globalize globalize,
        AppDialogs dialogs,
        LoadingService loading,
        ServerConnections connections,
        BitrateDetector bitrateDetector,
        IPlaybackTimers timers,
        Interop.IBrowser browser,
        IEnumerable<IMediaPlayer> players,
        PlaybackInterceptors interceptors)
    {
        _api = api;
        _appSettings = appSettings;
        _userSettings = userSettings;
        _globalize = globalize;
        _dialogs = dialogs;
        _loading = loading;
        _bitrateDetector = bitrateDetector;
        _timers = timers;
        _browser = browser;
        _interceptors = interceptors;
        _startingPlaySession = timers.NowMs;
        foreach (var player in players)
        {
            RegisterPlayer(player);
        }

        // Stopping at the start of the sign-out lets the stop report reach the server; the second
        // subscription covers sign-outs that do not go through LogoutAsync.
        connections.LocalUserSigningOut += OnLocalUserSignedOutAsync;
        connections.LocalUserSignedOut += OnLocalUserSignedOutAsync;
    }

    /// <inheritdoc />
    public event Action? StateChanged;

    /// <summary>Raised with one of <see cref="PlaybackEvents"/> for everything that happens.</summary>
    public event Action<string>? Event;

    /// <summary>Raised with the details of a "playbackstop".</summary>
    public event Action<PlaybackStoppedInfo>? PlaybackStopped;

    /// <summary>The registered players; local players first, in registration order.</summary>
    public IReadOnlyList<IMediaPlayer> Players => _players;

    /// <summary>The player that is playing or was chosen as target, if any.</summary>
    public IMediaPlayer? CurrentPlayer { get; private set; }

    public PlayQueue Queue => _playQueue;

    public StreamInfo? CurrentStreamInfo => CurrentPlayer is { } player ? Data(player).StreamInfo : null;

    public BaseItemDto? CurrentItem => Remote is { } remote ? remote.NowPlayingItem : CurrentStreamInfo?.Item;

    public MediaSourceInfo? CurrentMediaSource => CurrentStreamInfo?.MediaSource;

    public string? PlayMethod => CurrentStreamInfo?.PlayMethod;

    public string? PlaySessionId => CurrentStreamInfo?.PlaySessionId;

    public bool IsPlaying => Remote is { } remote ? remote.IsPlaying() : CurrentPlayer?.CurrentSrc is not null;

    public bool IsPlayingVideo => IsPlayingMediaType("Video");

    public bool IsPlayingAudio => IsPlayingMediaType("Audio");

    public bool IsPaused => CurrentPlayer?.Paused == true;

    /// <summary>Video is playing behind another page (the OSD is not shown).</summary>
    public bool IsPlayingLocally => CurrentPlayer is { IsLocalPlayer: true } && IsPlaying;

    /// <summary>
    /// Adds a player. Later workstreams register players for other targets here (remote sessions,
    /// cast); <see cref="SetCurrentPlayer"/> makes one the target of the transport commands.
    /// </summary>
    public void RegisterPlayer(IMediaPlayer player)
    {
        if (_players.Contains(player))
        {
            return;
        }

        _players.Add(player);
        _playerData[player] = new PlayerData();
        player.Event += OnPlayerEvent;
        (player as IRemotePlayer)?.Attach(this);
    }

    /// <summary>
    /// Makes a local player the current one (or none): raises "playerchange" when the player
    /// changes. Remote targets are chosen with <see cref="TrySetActivePlayerAsync"/>.
    /// </summary>
    public void SetCurrentPlayer(IMediaPlayer? player) => SetCurrentPlayerInternal(player, null);

    public bool IsPlayingMediaType(string mediaType) =>
        Remote is { } remote ? remote.IsPlaying(mediaType) : IsPlaying && CurrentStreamInfo?.MediaType == mediaType;

    /// <summary>The play options stored with a queue item, or the defaults.</summary>
    public ItemPlayOptions GetItemPlayOptions(BaseItemDto item) =>
        _itemOptions.TryGetValue(item, out var options) ? options : new ItemPlayOptions();

    /// <inheritdoc />
    public bool CanPlay(BaseItemDto item)
    {
        var type = item.Type.ToApiString();
        if (type is "Book" or "PhotoAlbum" or "MusicGenre" or "Season" or "Series" or "BoxSet" or "MusicAlbum" or "MusicArtist" or "Playlist")
        {
            return true;
        }

        if (item.LocationType == BaseItemDto_LocationType.Virtual && type != "Program")
        {
            return false;
        }

        if (type == "Program")
        {
            if (item.StartDate is not { } start || item.EndDate is not { } end)
            {
                return false;
            }

            var now = DateTimeOffset.FromUnixTimeMilliseconds(_timers.NowMs);
            if (now > end || now < start)
            {
                return false;
            }
        }

        return GetPlayer(item) is not null;
    }

    /// <inheritdoc />
    public bool CanQueue(BaseItemDto item)
    {
        var type = item.Type.ToApiString();
        var mediaType = type is "MusicAlbum" or "MusicArtist" or "MusicGenre" ? "Audio" : item.MediaType.ToApiString();
        return CurrentPlayer is { } player && player.CanPlayMediaType(mediaType);
    }

    /// <inheritdoc />
    public Task StartAsync() => Task.CompletedTask;

    /// <summary>Port of getPlayer(): the first player that can play the item's media type.</summary>
    private IMediaPlayer? GetPlayer(BaseItemDto item)
    {
        var mediaType = item.MediaType.ToApiString();
        if (CurrentPlayer is { IsLocalPlayer: false } remote && remote.CanPlayMediaType(mediaType))
        {
            return remote;
        }

        return _players
            .Where(p => p.IsLocalPlayer)
            .OrderBy(p => p.Priority)
            .FirstOrDefault(p => p.CanPlayMediaType(mediaType)
                && (p is not IItemMediaPlayer itemPlayer || itemPlayer.CanPlayItem(item)));
    }

    private PlayerData Data(IMediaPlayer player)
    {
        if (!_playerData.TryGetValue(player, out var data))
        {
            data = new PlayerData();
            _playerData[player] = data;
        }

        return data;
    }

    private void Raise(string name)
    {
        try
        {
            Event?.Invoke(name);
            if (name != PlaybackEvents.TimeUpdate)
            {
                StateChanged?.Invoke();
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] a handler of '{name}' failed: {ex}");
        }
    }

    private void ShowPlaybackInfoErrorMessage(string errorCode) =>
        _ = _dialogs.AlertAsync(_globalize.Translate(errorCode), _globalize.Translate("HeaderPlaybackError"));

    private async Task OnLocalUserSignedOutAsync()
    {
        // Signing out ends playback: stop, report and forget the queue and the connection data.
        // A remote target keeps playing; only the connection to it ends. (The target to return
        // to after signing in is kept, as in the original.)
        SetDefaultPlayerActive();
        try
        {
            if (CurrentPlayer is not null && IsPlaying)
            {
                await StopAsync();
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            AppLog.Warn($"[playback] stopping on sign-out failed: {ex.Message}");
        }

        _playQueue.Reset();
        _bitrateDetector.Reset();
    }
}
