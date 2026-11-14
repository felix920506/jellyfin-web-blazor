using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Pages.User;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// The Google Cast target. Port of plugins/chromecastPlayer/plugin.js (ChromecastPlayer and
/// CastPlayer): the receiver application is started with the browser's Cast dialog, every command is
/// a JSON message on <c>urn:x-cast:com.connectsdk</c> carrying the server connection, and the
/// receiver's state messages are mirrored into the manager's events. The SDK itself is behind
/// <see cref="ICastInterop"/>.
/// </summary>
[AppService(typeof(IMediaPlayer))]
public sealed class CastPlayer : IRemotePlayer, IStartupTask
{
    /// <summary>The name the original gives the player; also the id and the player name of its target.</summary>
    public const string PlayerName = "Google Cast";

    /// <summary>The commands the target announces (getCurrentTargetInfo()).</summary>
    public static readonly IReadOnlyList<string> SupportedCommands =
    [
        "VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute", "SetVolume", "SetAudioStreamIndex",
        "SetSubtitleStreamIndex", "DisplayContent", "SetRepeatMode",
    ];

    /// <summary>The receiver messages that are passed on with the receiver's state, when they carry an item.</summary>
    private static readonly string[] RelayedEvents =
    [
        PlaybackEvents.TimeUpdate, PlaybackEvents.Pause, PlaybackEvents.Unpause, PlaybackEvents.VolumeChange,
        PlaybackEvents.RepeatModeChange, PlaybackEvents.ShuffleQueueModeChange,
    ];

    private readonly ICastInterop _interop;
    private readonly JellyfinApi _api;
    private readonly ServerConnections _connections;
    private readonly AppHost _appHost;
    private readonly AppSettings _appSettings;
    private readonly UserSettings _userSettings;
    private readonly Globalize _globalize;
    private readonly AppDialogs _dialogs;
    private readonly IPlaybackTimers _timers;

    private PlaybackManager? _manager;
    private bool _signInHandlerAdded;
    private bool _pairing;
    private CastSessionInfo? _session;
    private JsonObject? _lastPlayerData = [];
    private BaseItemDto? _nowPlayingItem;
    private bool _playNextAfterEnded;
    private (string Address, PublicServerInfo? Info)? _serverInfo;

    public CastPlayer(
        ICastInterop interop,
        JellyfinApi api,
        ServerConnections connections,
        AppHost appHost,
        AppSettings appSettings,
        UserSettings userSettings,
        Globalize globalize,
        AppDialogs dialogs,
        IPlaybackTimers timers)
    {
        _interop = interop;
        _api = api;
        _connections = connections;
        _appHost = appHost;
        _appSettings = appSettings;
        _userSettings = userSettings;
        _globalize = globalize;
        _dialogs = dialogs;
        _timers = timers;

        interop.ReceiverAvailabilityChanged += OnReceiverAvailability;
        interop.SessionJoined += OnSessionJoined;
        interop.SessionUpdated += session => _session = session;
        interop.SessionEnded += OnSessionEnded;
        interop.MessageReceived += OnMessage;
    }

    /// <summary>Port of DEVICE_STATE.</summary>
    public enum DeviceStates
    {
        Idle,
        Active,
        Warning,
        Error,
    }

    /// <inheritdoc />
    public event Action<IMediaPlayer, PlayerEvent>? Event;

    /// <summary>Raised when <see cref="IsAvailable"/> or <see cref="HasReceivers"/> changes.</summary>
    public event Action? AvailabilityChanged;

    public string Name => PlayerName;

    public string Id => "chromecast";

    public bool IsLocalPlayer => false;

    public int Priority => 0;

    /// <summary>
    /// The sender SDK is loaded and reports that the Cast API is available. This is what the
    /// original means by "the chromecast plugin is loaded" in the cast menus.
    /// </summary>
    public bool IsAvailable { get; private set; }

    /// <summary>chrome.cast.initialize succeeded for the user's receiver application.</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>The receiverListener reported receivers (the original's hasReceivers).</summary>
    public bool HasReceivers { get; private set; }

    public DeviceStates DeviceState { get; private set; } = DeviceStates.Idle;

    /// <summary>The connected Cast session, or null.</summary>
    public CastSessionInfo? Session => _session;

    public string? CurrentSrc => null;

    public double? CurrentTimeMs => Number(PlayState, "PositionTicks") is { } ticks ? ticks / 10000 : null;

    public double? DurationMs => _nowPlayingItem?.RunTimeTicks is { } ticks ? ticks / 10000d : null;

    public bool? Seekable => Bool(PlayState, "CanSeek");

    public bool Paused => Bool(PlayState, "IsPaused") == true;

    public double PlaybackRate => 1;

    public int Volume => Number(PlayState, "VolumeLevel") is { } level ? (int)level : 100;

    public bool IsMuted => Bool(PlayState, "IsMuted") == true;

    public IReadOnlyList<BufferedRange> BufferedRanges
    {
        get
        {
            var ranges = new List<BufferedRange>();
            if (PlayState?["BufferedRanges"] is JsonArray array)
            {
                foreach (var range in array.OfType<JsonObject>())
                {
                    ranges.Add(new BufferedRange((long)(Number(range, "start") ?? 0), (long)(Number(range, "end") ?? 0)));
                }
            }

            return ranges;
        }
    }

    public bool CanSetAudioStreamIndex => true;

    public string AspectRatio => "auto";

    public bool IsFullscreen => Bool(PlayState, "IsFullscreen") == true;

    public bool IsPictureInPictureEnabled => false;

    public BaseItemDto? NowPlayingItem => _nowPlayingItem;

    /// <summary>Port of getPlaylist(): the receiver's queue is not known here.</summary>
    public IReadOnlyList<BaseItemDto> Playlist => [];

    public string? CurrentPlaylistItemId => null;

    public string RepeatMode => Text(PlayState, "RepeatMode") ?? PlayQueue.RepeatNone;

    public string ShuffleMode => Text(PlayState, "ShuffleMode") ?? PlayQueue.Sorted;

    public int? AudioStreamIndex => Number(PlayState, "AudioStreamIndex") is { } index ? (int)index : null;

    public int? SubtitleStreamIndex => Number(PlayState, "SubtitleStreamIndex") is { } index ? (int)index : null;

    public int? SecondarySubtitleStreamIndex => null;

    public IReadOnlyList<MediaStream> AudioTracks => Streams(MediaStream_Type.Audio);

    public IReadOnlyList<MediaStream> SubtitleTracks => Streams(MediaStream_Type.Subtitle);

    public double SubtitleOffset => 0;

    public bool SupportsSubtitleOffset => false;

    /// <summary>The receiver's last state message (the original's lastPlayerData).</summary>
    internal JsonObject? LastPlayerData => _lastPlayerData;

    private JsonObject? PlayState => _lastPlayerData?["PlayState"] as JsonObject;

    /// <summary>The original loads the plugin only in Chromium based browsers (index.tsx).</summary>
    private bool IsSupportedBrowser => _appHost.Browser is { Chrome: true } or { EdgeChromium: true } or { Opera: true };

    /// <inheritdoc />
    public void Attach(PlaybackManager manager) => _manager = manager;

    /// <summary>Port of canPlayMediaType(): audio and video.</summary>
    public bool CanPlayMediaType(string? mediaType) =>
        string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Port of the ChromecastPlayer constructor: loads the sender SDK and initialises the Cast API
    /// for the signed-in user, again after every sign-in. Not awaited: the SDK may never become
    /// available.
    /// </summary>
    public Task StartAsync()
    {
        if (IsSupportedBrowser)
        {
            Run(LoadAsync());
        }

        return Task.CompletedTask;
    }

    /// <summary>Loads the SDK; then initialises for the current and every later user.</summary>
    internal async Task LoadAsync()
    {
        if (!await _interop.LoadAsync())
        {
            return;
        }

        IsAvailable = true;
        AvailabilityChanged?.Invoke();
        if (!_signInHandlerAdded)
        {
            _signInHandlerAdded = true;
            _connections.LocalUserSignedIn += _ => InitializeChromecastAsync();
        }

        if (_api.IsLoggedIn)
        {
            await InitializeChromecastAsync();
        }
    }

    /// <summary>
    /// Port of initializeChromecast() and CastPlayer.initializeCastPlayer(): chrome.cast.initialize
    /// with the receiver application of the user's settings (CastReceiverId).
    /// </summary>
    internal async Task InitializeChromecastAsync()
    {
        DeviceState = DeviceStates.Idle;
        IsInitialized = false;
        HasReceivers = false;
        if (!IsAvailable || !_api.IsLoggedIn)
        {
            return;
        }

        UserDto? user;
        try
        {
            using var response = await _api.SendAsync(HttpMethod.Get, $"Users/{_api.UserIdText}");
            response.EnsureSuccessStatusCode();
            user = KiotaJson.Parse(await response.Content.ReadAsStringAsync(), UserDto.CreateFromDiscriminatorValue);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            AppLog.Warn($"[chromecastPlayer] the user could not be loaded: {ex.Message}");
            return;
        }

        var applicationId = user?.Configuration?.CastReceiverId;
        if (string.IsNullOrEmpty(applicationId))
        {
            AppLog.Warn($"Not initializing chromecast: CastReceiverId is {applicationId}");
            return;
        }

        IsInitialized = await _interop.InitializeAsync(applicationId);
        AvailabilityChanged?.Invoke();
    }

    /// <summary>Port of getTargets(): the Cast target while receivers are available.</summary>
    public Task<IReadOnlyList<PlayTarget>> GetTargetsAsync() =>
        Task.FromResult<IReadOnlyList<PlayTarget>>(IsAvailable && HasReceivers ? [GetCurrentTargetInfo()] : []);

    /// <summary>Port of getCurrentTargetInfo().</summary>
    public PlayTarget GetCurrentTargetInfo() => new()
    {
        Name = PlayerName,
        Id = PlayerName,
        PlayerName = PlayerName,
        PlayableMediaTypes = ["Audio", "Video"],
        IsLocalPlayer = false,
        AppName = PlayerName,
        DeviceName = _session?.ReceiverName is { Length: > 0 } name ? name : null,
        DeviceType = "cast",
        SupportedCommands = SupportedCommands,
    };

    /// <summary>
    /// Port of tryPair(): opens the browser's Cast dialog (launchApp) and connects to the chosen
    /// receiver. Throws when the API is not initialised, a session is already active, or the dialog
    /// was cancelled.
    /// </summary>
    public async Task TryPairAsync(PlayTarget target)
    {
        if (DeviceState == DeviceStates.Active || !IsInitialized)
        {
            throw new InvalidOperationException("tryPair failed");
        }

        _pairing = true;
        try
        {
            var session = await _interop.RequestSessionAsync();
            if (session is null)
            {
                // onLaunchError()
                DeviceState = DeviceStates.Error;
                throw new InvalidOperationException("tryPair failed");
            }

            OnSessionConnected(session);
        }
        finally
        {
            _pairing = false;
        }
    }

    /// <inheritdoc />
    public void BeginPlayerUpdates()
    {
    }

    /// <inheritdoc />
    public void EndPlayerUpdates()
    {
    }

    /// <inheritdoc />
    public PlayerState GetPlayerState()
    {
        var playState = PlayState;
        return new PlayerState
        {
            VolumeLevel = Volume,
            IsMuted = IsMuted,
            IsPaused = Paused,
            RepeatMode = RepeatMode,
            ShuffleMode = ShuffleMode,
            MaxStreamingBitrate = Number(playState, "MaxStreamingBitrate") is { } bitrate ? (int)bitrate : null,
            PositionTicks = (long)(Number(playState, "PositionTicks") ?? 0),
            SubtitleStreamIndex = SubtitleStreamIndex,
            AudioStreamIndex = AudioStreamIndex,
            BufferedRanges = BufferedRanges,
            PlayMethod = Text(playState, "PlayMethod"),
            LiveStreamId = Text(playState, "LiveStreamId"),
            PlaySessionId = Text(playState, "PlaySessionId"),
            MediaSourceId = Text(playState, "MediaSourceId"),
            CanSeek = Seekable,
            NowPlayingItem = _nowPlayingItem,
            RunTimeTicks = _nowPlayingItem?.RunTimeTicks,
            IsFullscreen = IsFullscreen,
        };
    }

    /// <inheritdoc />
    public bool IsPlaying(string? mediaType = null) =>
        _nowPlayingItem is { } item && (string.IsNullOrEmpty(mediaType) || item.MediaType.ToApiString() == mediaType);

    /// <summary>Port of play(): the items, loaded when only ids are given, go to the receiver with "PlayNow".</summary>
    public async Task PlayAsync(PlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var items = options.Items ?? await GetItemsForPlaybackAsync(options.Ids ?? []);
        await PlayWithCommandAsync(options, items, "PlayNow");
    }

    /// <inheritdoc />
    public Task QueueAsync(IReadOnlyList<BaseItemDto> items) => PlayWithCommandAsync(null, items, "PlayLast");

    /// <inheritdoc />
    public Task QueueNextAsync(IReadOnlyList<BaseItemDto> items) => PlayWithCommandAsync(null, items, "PlayNext");

    /// <summary>Port of shuffle(): the item is loaded again and sent with "Shuffle".</summary>
    public Task ShuffleAsync(BaseItemDto item) => PlayFetchedAsync(item, "Shuffle");

    /// <summary>Port of instantMix(): the item is loaded again and sent with "InstantMix".</summary>
    public Task InstantMixAsync(BaseItemDto item) => PlayFetchedAsync(item, "InstantMix");

    /// <inheritdoc />
    public Task PlayTrailersAsync(BaseItemDto item)
    {
        AppLog.Warn("[chromecastPlayer] Playing trailers is not supported.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PlayAsync(StreamInfo streamInfo) =>
        throw new NotSupportedException("The Chromecast player plays on the receiver; it takes play options, not streams.");

    /// <inheritdoc />
    public Task<JsonObject> GetDeviceProfileAsync(BaseItemDto? item, bool isRetry = false) => Task.FromResult(new JsonObject());

    /// <summary>Port of stop(): no next item is to follow.</summary>
    public Task StopAsync(bool destroyPlayer)
    {
        _playNextAfterEnded = false;
        return SendCommandMessageAsync("Stop");
    }

    /// <inheritdoc />
    public void Destroy()
    {
    }

    /// <inheritdoc />
    public Task PlayPauseAsync() => SendCommandMessageAsync("PlayPause");

    /// <inheritdoc />
    public Task NextTrackAsync() => SendCommandMessageAsync("NextTrack");

    /// <inheritdoc />
    public Task PreviousTrackAsync() => SendCommandMessageAsync("PreviousTrack");

    /// <summary>Port of seek(): the receiver takes seconds.</summary>
    public Task SeekAsync(long positionTicks) =>
        SendCommandMessageAsync("Seek", new JsonObject { ["position"] = positionTicks / 10000000d });

    /// <inheritdoc />
    public void SetCurrentTime(double milliseconds) => _ = SeekAsync((long)(milliseconds * 10000));

    /// <inheritdoc />
    public void Pause() => _ = SendCommandMessageAsync("Pause");

    /// <inheritdoc />
    public void Unpause() => _ = SendCommandMessageAsync("Unpause");

    /// <inheritdoc />
    public void SetPlaybackRate(double value)
    {
    }

    /// <summary>Port of setVolume(): the receiver's own volume, 0..1.</summary>
    public void SetVolume(int value)
    {
        if (_session is null)
        {
            return;
        }

        _ = _interop.SetReceiverVolumeLevelAsync(Math.Clamp(value, 0, 100) / 100d);
    }

    /// <summary>Port of setMute(): "Mute" or "Unmute" messages.</summary>
    public void SetMute(bool mute) => _ = SendCommandMessageAsync(mute ? "Mute" : "Unmute");

    /// <inheritdoc />
    public void ToggleMute() => _ = SendCommandMessageAsync("ToggleMute");

    /// <summary>Port of volumeUp(): the receiver volume plus 5 %.</summary>
    public void VolumeUp() => ChangeReceiverVolume(0.05);

    /// <summary>Port of volumeDown(): the receiver volume minus 5 %.</summary>
    public void VolumeDown() => ChangeReceiverVolume(-0.05);

    /// <inheritdoc />
    public bool Supports(string feature) => false;

    /// <inheritdoc />
    public void SetAudioStreamIndex(int index) =>
        _ = SendCommandMessageAsync("SetAudioStreamIndex", new JsonObject { ["index"] = index });

    /// <inheritdoc />
    public void SetSubtitleStreamIndex(int index) =>
        _ = SendCommandMessageAsync("SetSubtitleStreamIndex", new JsonObject { ["index"] = index });

    /// <inheritdoc />
    public void SetSecondarySubtitleStreamIndex(int index)
    {
    }

    /// <inheritdoc />
    public void SetSubtitleOffset(double seconds)
    {
    }

    /// <inheritdoc />
    public void SetAspectRatio(string value)
    {
    }

    /// <inheritdoc />
    public void ToggleFullscreen()
    {
    }

    /// <inheritdoc />
    public void SetPictureInPictureEnabled(bool enabled)
    {
    }

    /// <inheritdoc />
    public Task<PlayerStats?> GetStatsAsync() => Task.FromResult<PlayerStats?>(null);

    /// <inheritdoc />
    public void OnNavigatedToOsd()
    {
    }

    /// <inheritdoc />
    public void SetRepeatMode(string value) =>
        _ = SendCommandMessageAsync("SetRepeatMode", new JsonObject { ["RepeatMode"] = value });

    /// <inheritdoc />
    public void SetQueueShuffleMode(string value) =>
        AppLog.Warn("[chromecastPlayer] Setting shuffle queue mode is not supported.");

    /// <summary>Port of setCurrentPlaylistItem(): not supported by the receiver protocol.</summary>
    public Task SetCurrentPlaylistItemAsync(string playlistItemId) => Task.CompletedTask;

    /// <summary>The original player has no movePlaylistItem().</summary>
    public Task MovePlaylistItemAsync(string playlistItemId, int newIndex) => Task.CompletedTask;

    /// <summary>
    /// The general commands of the manager that have no method of their own. The original player
    /// only knows displayContent(); everything else is dropped, as the original's sendCommand() does
    /// for a player without one.
    /// </summary>
    public Task SendCommandAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (name != "DisplayContent")
        {
            return Task.CompletedTask;
        }

        var options = new JsonObject();
        foreach (var (key, value) in arguments ?? new Dictionary<string, object?>())
        {
            options[key] = value is null ? null : JsonSerializer.SerializeToNode(value);
        }

        return SendCommandMessageAsync("DisplayContent", options);
    }

    /// <summary>Port of endSession(): stop, then end the receiver application a second later.</summary>
    public async Task EndSessionAsync()
    {
        await StopAsync(destroyPlayer: true);
        await _timers.Delay(1000);
        if (_session is not null)
        {
            await _interop.StopSessionAsync();

            // onStopAppSuccess()
            DeviceState = DeviceStates.Idle;
        }
    }

    /// <summary>
    /// Port of sendMessage(): adds the connection the receiver needs (server, user, token, device),
    /// the receiver's name and the playback settings of this device, and sends the JSON text.
    /// </summary>
    internal async Task SendMessageAsync(JsonObject message)
    {
        if (_session is not { } session)
        {
            AppLog.Warn($"[chromecastPlayer] no session for {message["command"]}");
            return;
        }

        var serverAddress = _api.ServerAddress ?? string.Empty;
        message["userId"] = _api.UserIdText;
        message["deviceId"] = _api.DeviceId;
        message["accessToken"] = _api.AccessToken;
        message["serverAddress"] = await GetServerLocalAddressAsync(serverAddress);
        message["serverId"] = _api.ServerId;
        message["serverVersion"] = (await GetServerInfoAsync(serverAddress))?.Version;
        message["receiverName"] = session.ReceiverName;

        if (MaxChromecastBitrate() is { } bitrate)
        {
            message["maxBitrate"] = bitrate;
        }

        if ((message["options"] as JsonObject)?["items"] is not null)
        {
            message["subtitleAppearance"] = SubtitleAppearance.Load(_userSettings).Values.DeepClone();
            message["subtitleBurnIn"] = _appSettings.Get("subtitleburnin") ?? string.Empty;
        }

        await _interop.SendMessageAsync(message.ToJsonString());
    }

    /// <summary>Port of messageListener() and the handlers initializeChromecast() binds to the cast player's events.</summary>
    internal void OnMessage(string text)
    {
        JsonObject? message;
        try
        {
            message = JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            AppLog.Warn("[chromecastPlayer] unreadable message from the receiver");
            return;
        }

        if (Text(message, "type") is not { Length: > 0 } type)
        {
            return;
        }

        var data = message?["data"];
        switch (type)
        {
            case "playbackerror":
                var code = data is JsonValue value ? value.ToString() : string.Empty;
                Run(AlertLaterAsync(_globalize.Translate("MessagePlaybackError" + code), _globalize.Translate("HeaderPlaybackError")));
                return;
            case "connectionerror":
                Run(AlertLaterAsync(_globalize.Translate("MessageChromecastConnectionError"), _globalize.Translate("HeaderError")));
                return;
            case PlaybackEvents.PlaybackStart:
                GetPlayerStateInternal(data as JsonObject);
                Raise(PlaybackEvents.PlaybackStart);

                // Be prepared that after this media item a next one may follow.
                _playNextAfterEnded = true;
                return;
            case PlaybackEvents.PlaybackStop:
                OnPlaybackStop(data as JsonObject);
                return;
            case "playbackprogress":
                GetPlayerStateInternal(data as JsonObject);
                Raise(PlaybackEvents.TimeUpdate);
                return;
            case "playstatechange":
                // Updates the player and the now playing bar to the current pause state.
                GetPlayerStateInternal(data as JsonObject);
                Raise(PlaybackEvents.Pause);
                return;
            default:
                // bindEventForRelay(): events without an item are skipped.
                if (RelayedEvents.Contains(type) && data is JsonObject state && Text(state, "ItemId") is { Length: > 0 })
                {
                    GetPlayerStateInternal(state);
                    Raise(type);
                }

                return;
        }
    }

    /// <summary>Port of normalizeImages(): the image fields of the receiver's item mapped to the ones the UI reads.</summary>
    internal static void NormalizeImages(JsonObject state)
    {
        if (state["NowPlayingItem"] is not JsonObject item)
        {
            return;
        }

        var primaryImageTag = Text(item, "PrimaryImageTag");
        if ((item["ImageTags"] as JsonObject)?["Primary"] is null && !string.IsNullOrEmpty(primaryImageTag))
        {
            if (item["ImageTags"] is not JsonObject imageTags)
            {
                imageTags = [];
                item["ImageTags"] = imageTags;
            }

            imageTags["Primary"] = primaryImageTag;
        }

        if (Text(item, "BackdropImageTag") is { Length: > 0 } backdropImageTag)
        {
            var backdropItemId = Text(item, "BackdropItemId");
            if (backdropItemId == Text(item, "Id"))
            {
                item["BackdropImageTags"] = new JsonArray(backdropImageTag);
            }
            else
            {
                item["ParentBackdropImageTags"] = new JsonArray(backdropImageTag);
                item["ParentBackdropItemId"] = backdropItemId;
            }
        }
    }

    private static double? Number(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

    private static bool? Bool(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : null;

    private static string? Text(JsonObject? obj, string name) =>
        obj?[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string IdOf(BaseItemDto item) => item.Id?.ToString("N") ?? string.Empty;

    private static async void Run(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException or Microsoft.JSInterop.JSException)
        {
            AppLog.Warn($"[chromecastPlayer] {ex.Message}");
        }
    }

    private List<MediaStream> Streams(MediaStream_Type type) =>
        (_nowPlayingItem?.MediaStreams ?? []).Where(s => s.Type == type).ToList();

    private void OnReceiverAvailability(bool available)
    {
        HasReceivers = available;
        AvailabilityChanged?.Invoke();
    }

    /// <summary>Port of sessionListener(): a session that exists already is joined (page reload).</summary>
    private void OnSessionJoined(CastSessionInfo session) => OnSessionConnected(session);

    /// <summary>
    /// Port of onSessionConnected() and the "connect" handler: the requested session completes the
    /// pairing; a joined one makes this player the active one by itself. The receiver is asked to
    /// identify itself.
    /// </summary>
    private void OnSessionConnected(CastSessionInfo session)
    {
        _session = session;
        DeviceState = DeviceStates.Active;
        if (!_pairing)
        {
            _manager?.SetActivePlayer(this, GetCurrentTargetInfo());
        }

        // Reset this so that statechange will fire.
        SetPlayerData(null);
        Run(SendCommandMessageAsync("Identify"));
    }

    /// <summary>Port of sessionUpdateListener(false) and sendConnectionResult(false).</summary>
    private void OnSessionEnded()
    {
        _session = null;
        DeviceState = DeviceStates.Idle;
        if (!_pairing)
        {
            _manager?.RemoveActivePlayer(PlayerName);
        }
    }

    private void OnPlaybackStop(JsonObject? data)
    {
        GetPlayerStateInternal(data);
        if (!_playNextAfterEnded && _lastPlayerData is { } stopped)
        {
            // Mark that no next media items are to be processed.
            stopped["nextItem"] = null;
            stopped["NextMediaType"] = null;
        }

        Raise(PlaybackEvents.PlaybackStop);

        var state = PlayState;
        var volume = Number(state, "VolumeLevel") is { } level && level != 0 ? level : 0.5;
        var mute = Bool(state, "IsMuted") == true;

        // Reset this so the next query doesn't make it appear like content is playing.
        SetPlayerData(new JsonObject { ["PlayState"] = new JsonObject { ["VolumeLevel"] = volume, ["IsMuted"] = mute } });
    }

    /// <summary>Port of getPlayerStateInternal(): keeps the receiver's state; the first one after connecting is a "statechange".</summary>
    private JsonObject? GetPlayerStateInternal(JsonObject? data)
    {
        var triggerStateChange = data is not null && _lastPlayerData is null;
        if (data is not null)
        {
            NormalizeImages(data);
            SetPlayerData(data);
        }

        if (triggerStateChange)
        {
            Raise(PlaybackEvents.StateChange);
        }

        return _lastPlayerData;
    }

    private void SetPlayerData(JsonObject? data)
    {
        _lastPlayerData = data;
        _nowPlayingItem = data?["NowPlayingItem"] is JsonObject item
            ? KiotaJson.Parse(item.ToJsonString(), BaseItemDto.CreateFromDiscriminatorValue)
            : null;
    }

    private void ChangeReceiverVolume(double delta)
    {
        if (_session is not { } session)
        {
            return;
        }

        var level = Math.Clamp((session.VolumeLevel ?? 0.5) + delta, 0, 1);
        _ = _interop.SetReceiverVolumeLevelAsync(level);
    }

    private Task SendCommandMessageAsync(string command, JsonObject? options = null) =>
        SendMessageAsync(new JsonObject { ["options"] = options ?? new JsonObject(), ["command"] = command });

    /// <summary>Port of playWithCommand() and loadMedia(): the items go to the receiver as small stubs.</summary>
    private async Task PlayWithCommandAsync(PlayOptions? options, IReadOnlyList<BaseItemDto> items, string command)
    {
        var ids = options?.Ids;
        if (items.Count > 1 && ids is not null)
        {
            // Use the original request id array for sorting the result in the proper order.
            var order = ids.Select(id => id.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant()).ToList();
            items = items.OrderBy(i => order.IndexOf(IdOf(i))).ToList();
        }

        if (_session is null)
        {
            AppLog.Warn("[chromecastPlayer] no session");
            return;
        }

        var json = new JsonObject();
        if (options is not null)
        {
            if (ids is not null)
            {
                json["ids"] = new JsonArray(ids.Select(id => (JsonNode?)id).ToArray());
            }

            Add(json, "startPositionTicks", options.StartPositionTicks);
            Add(json, "startIndex", options.StartIndex);
            Add(json, "mediaSourceId", options.MediaSourceId);
            Add(json, "audioStreamIndex", options.AudioStreamIndex);
            Add(json, "subtitleStreamIndex", options.SubtitleStreamIndex);
            if (options.Shuffle)
            {
                json["shuffle"] = true;
            }

            json["fullscreen"] = options.Fullscreen;
            Add(json, "serverId", _api.ServerId);
        }

        // Convert items to smaller stubs to send minimal amount of information.
        json["items"] = new JsonArray(items.Select(i => (JsonNode?)new JsonObject
        {
            ["Id"] = IdOf(i),
            ["ServerId"] = i.ServerId,
            ["Name"] = i.Name,
            ["Type"] = i.Type.ToApiString(),
            ["MediaType"] = i.MediaType.ToApiString(),
            ["IsFolder"] = i.IsFolder,
        }).ToArray());

        await SendMessageAsync(new JsonObject { ["options"] = json, ["command"] = command });

        static void Add(JsonObject target, string name, JsonNode? value)
        {
            if (value is not null)
            {
                target[name] = value;
            }
        }
    }

    private async Task PlayFetchedAsync(BaseItemDto item, string command)
    {
        if (_manager is null || await _manager.GetItemByIdAsync(IdOf(item)) is not { } fetched)
        {
            return;
        }

        await PlayWithCommandAsync(null, [fetched], command);
    }

    /// <summary>Port of getItemsForPlayback(): one item by id, more through /Items (at most 100, no virtual items).</summary>
    private async Task<IReadOnlyList<BaseItemDto>> GetItemsForPlaybackAsync(IReadOnlyList<string> ids)
    {
        if (_manager is null || ids.Count == 0)
        {
            return [];
        }

        if (ids.Count == 1)
        {
            return await _manager.GetItemByIdAsync(ids[0]) is { } item ? [item] : [];
        }

        return await _manager.GetItemsForPlaybackAsync(new PlaybackManager.ItemsQuery { ["ids"] = ids, ["limit"] = 100 });
    }

    /// <summary>The server's public information (version, local address), once per server.</summary>
    private async Task<PublicServerInfo?> GetServerInfoAsync(string serverAddress)
    {
        if (_serverInfo is { } cached && cached.Address == serverAddress)
        {
            return cached.Info;
        }

        var info = await _api.GetPublicInfoAsync(serverAddress);
        _serverInfo = (serverAddress, info);
        return info;
    }

    /// <summary>
    /// A server address of localhost cannot be used by the receiver: the server's local address
    /// (ULA, Unique Local Address) is sent in that case.
    /// </summary>
    private async Task<string?> GetServerLocalAddressAsync(string serverAddress)
    {
        if (!Uri.TryCreate(serverAddress, UriKind.Absolute, out var uri))
        {
            return serverAddress;
        }

        var hostname = uri.Host;
        var isLocalhost = hostname == "localhost" || hostname.StartsWith("127.", StringComparison.Ordinal) || hostname == "[::1]";
        if (!isLocalhost)
        {
            return serverAddress;
        }

        return _connections.CurrentServer?.LocalAddress is { Length: > 0 } localAddress
            ? localAddress
            : (await GetServerInfoAsync(serverAddress))?.LocalAddress;
    }

    /// <summary>Port of appSettings.maxChromecastBitrate().</summary>
    private int? MaxChromecastBitrate() =>
        int.TryParse(_appSettings.Get("chromecastBitrate1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrate) && bitrate != 0
            ? bitrate
            : null;

    private async Task AlertLaterAsync(string text, string title)
    {
        await _timers.Delay(300);
        await _dialogs.AlertAsync(text, title);
    }

    private void Raise(string name) => Event?.Invoke(this, new PlayerEvent(name));
}
