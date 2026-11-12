using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Remote control of another session of the server. Port of plugins/sessionPlayer/plugin.js: the
/// targets come from <c>/Sessions</c>, the state from the web socket's "Sessions" messages, and
/// every command is a request to <c>/Sessions/{id}/...</c>.
/// </summary>
[AppService(typeof(IMediaPlayer))]
public sealed class SessionPlayer(JellyfinApi api, ServerEvents events, IPlaybackTimers timers) : IRemotePlayer
{
    /// <summary>The name the original gives the player; targets carry it as their player name.</summary>
    public const string PlayerName = "Remote Control";

    /// <summary>Initial delay and interval of the "Sessions" messages, in milliseconds.</summary>
    private const string SessionsInterval = "100,800";

    /// <summary>The subscription is renewed when no "Sessions" message arrived for this long.</summary>
    private const int WatchdogMs = 5000;

    private PlaybackManager? _manager;
    private int _listenerCount;
    private IDisposable? _subscription;
    private IDisposable? _watchdog;
    private long _lastMessageMs;
    private SessionInfoDto? _session;
    private JsonObject? _sessionJson;
    private List<BaseItemDto> _playlist = [];
    private string? _queueKey;
    private int _queueVersion;

    /// <inheritdoc />
    public event Action<IMediaPlayer, PlayerEvent>? Event;

    public string Name => PlayerName;

    public string Id => "remoteplayer";

    public bool IsLocalPlayer => false;

    public int Priority => 100;

    public string? CurrentSrc => null;

    public double? CurrentTimeMs => _session?.PlayState?.PositionTicks is { } ticks ? ticks / 10000d : null;

    public double? DurationMs => NowPlayingItem?.RunTimeTicks is { } ticks ? ticks / 10000d : null;

    public bool? Seekable => _session?.PlayState?.CanSeek;

    public bool Paused => _session?.PlayState?.IsPaused == true;

    public double PlaybackRate => 1;

    public int Volume => _session?.PlayState?.VolumeLevel ?? 100;

    public bool IsMuted => _session?.PlayState?.IsMuted == true;

    public IReadOnlyList<BufferedRange> BufferedRanges => [];

    public bool CanSetAudioStreamIndex => true;

    public string AspectRatio => "auto";

    public bool IsFullscreen => false;

    public bool IsPictureInPictureEnabled => false;

    public BaseItemDto? NowPlayingItem => _session?.NowPlayingItem;

    public IReadOnlyList<BaseItemDto> Playlist => _playlist;

    public string? CurrentPlaylistItemId => _session?.PlaylistItemId;

    public string RepeatMode => _session?.PlayState?.RepeatMode.ToApiString() ?? PlayQueue.RepeatNone;

    public string ShuffleMode =>
        _session?.PlayState?.PlaybackOrder == PlayerStateInfo_PlaybackOrder.Shuffle ? PlayQueue.Shuffled : PlayQueue.Sorted;

    public int? AudioStreamIndex => _session?.PlayState?.AudioStreamIndex;

    public int? SubtitleStreamIndex => _session?.PlayState?.SubtitleStreamIndex;

    public IReadOnlyList<MediaStream> AudioTracks => Streams(MediaStream_Type.Audio);

    public IReadOnlyList<MediaStream> SubtitleTracks => Streams(MediaStream_Type.Subtitle);

    /// <summary>The id of the session that is the active target of the manager, if it is one of this player's.</summary>
    private string? ActiveSessionId =>
        _manager?.GetPlayerInfo() is { IsLocalPlayer: false } info && info.Name == Name ? info.Id : null;

    /// <inheritdoc />
    public void Attach(PlaybackManager manager) => _manager = manager;

    /// <inheritdoc />
    public bool CanPlayMediaType(string? mediaType) =>
        string.Equals(mediaType, "audio", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaType, "video", StringComparison.OrdinalIgnoreCase);

    /// <summary>Port of getTargets(): the sessions the user may control, without this device.</summary>
    public async Task<IReadOnlyList<PlayTarget>> GetTargetsAsync()
    {
        if (!api.IsLoggedIn)
        {
            return [];
        }

        using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl("Sessions", ("ControllableByUserId", api.UserIdText)));
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var targets = new List<PlayTarget>();
        foreach (var session in document.RootElement.EnumerateArray())
        {
            if (Text(session, "DeviceId") == api.DeviceId || Text(session, "Id") is not { Length: > 0 } id)
            {
                continue;
            }

            var deviceName = Text(session, "DeviceName");
            var userId = Text(session, "UserId");
            targets.Add(new PlayTarget
            {
                Name = deviceName ?? string.Empty,
                DeviceName = deviceName,
                DeviceType = Text(session, "DeviceType"),
                Id = id,
                PlayerName = Name,
                AppName = Text(session, "Client"),
                PlayableMediaTypes = Strings(session, "PlayableMediaTypes"),
                IsLocalPlayer = false,
                SupportedCommands = session.TryGetProperty("Capabilities", out var capabilities) && capabilities.ValueKind == JsonValueKind.Object
                    ? Strings(capabilities, "SupportedCommands")
                    : [],
                UserId = userId,
                UserName = string.IsNullOrEmpty(userId) ? null : Text(session, "UserName"),
                UserPrimaryImageTag = string.IsNullOrEmpty(userId) ? null : Text(session, "UserPrimaryImageTag"),
            });
        }

        return targets;
    }

    /// <inheritdoc />
    public Task TryPairAsync(PlayTarget target) => Task.CompletedTask;

    /// <inheritdoc />
    public void BeginPlayerUpdates()
    {
        if (_listenerCount <= 0)
        {
            _listenerCount = 0;
            Subscribe();
        }

        _listenerCount++;
    }

    /// <inheritdoc />
    public void EndPlayerUpdates()
    {
        _listenerCount--;
        if (_listenerCount <= 0)
        {
            Unsubscribe();
            _listenerCount = 0;
        }
    }

    /// <inheritdoc />
    public PlayerState GetPlayerState()
    {
        var playState = _session?.PlayState;
        return new PlayerState
        {
            VolumeLevel = Volume,
            IsMuted = IsMuted,
            IsPaused = Paused,
            RepeatMode = RepeatMode,
            ShuffleMode = ShuffleMode,
            PositionTicks = playState?.PositionTicks ?? 0,
            SubtitleStreamIndex = playState?.SubtitleStreamIndex,
            AudioStreamIndex = playState?.AudioStreamIndex,
            PlayMethod = playState?.PlayMethod.ToApiString(),
            LiveStreamId = playState?.LiveStreamId,
            PlaylistItemId = _session?.PlaylistItemId,
            MediaSourceId = playState?.MediaSourceId,
            CanSeek = playState?.CanSeek,
            NowPlayingItem = NowPlayingItem,
            RunTimeTicks = NowPlayingItem?.RunTimeTicks,
            IsFullscreen = true,
        };
    }

    /// <inheritdoc />
    public bool IsPlaying(string? mediaType = null) =>
        NowPlayingItem is { } item && (string.IsNullOrEmpty(mediaType) || item.MediaType.ToApiString() == mediaType);

    /// <summary>Port of play(): the ids go to the target, which builds its own queue.</summary>
    public Task PlayAsync(PlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SendPlayCommandAsync(
            IdsOf(options),
            "PlayNow",
            options.StartPositionTicks,
            options.MediaSourceId,
            options.AudioStreamIndex,
            options.SubtitleStreamIndex,
            options.StartIndex);
    }

    /// <inheritdoc />
    public Task QueueAsync(IReadOnlyList<BaseItemDto> items) => SendPlayCommandAsync(IdsOf(items), "PlayLast");

    /// <inheritdoc />
    public Task QueueNextAsync(IReadOnlyList<BaseItemDto> items) => SendPlayCommandAsync(IdsOf(items), "PlayNext");

    /// <inheritdoc />
    public Task ShuffleAsync(BaseItemDto item) => SendPlayCommandAsync(IdsOf([item]), "PlayShuffle");

    /// <inheritdoc />
    public Task InstantMixAsync(BaseItemDto item) => SendPlayCommandAsync(IdsOf([item]), "PlayInstantMix");

    /// <inheritdoc />
    public Task PlayTrailersAsync(BaseItemDto item) =>
        SendCommandAsync("PlayTrailers", CommandArguments.Of(("ItemId", item.Id?.ToString("N"))));

    /// <inheritdoc />
    public Task PlayAsync(StreamInfo streamInfo) =>
        throw new NotSupportedException("The session player plays on another device; it takes play options, not streams.");

    /// <inheritdoc />
    public Task<JsonObject> GetDeviceProfileAsync(BaseItemDto? item, bool isRetry = false) => Task.FromResult(new JsonObject());

    /// <inheritdoc />
    public Task StopAsync(bool destroyPlayer) => SendPlayStateCommandAsync("stop");

    /// <inheritdoc />
    public void Destroy()
    {
    }

    /// <inheritdoc />
    public Task PlayPauseAsync() => SendPlayStateCommandAsync("PlayPause");

    /// <inheritdoc />
    public Task NextTrackAsync() => SendPlayStateCommandAsync("nextTrack");

    /// <inheritdoc />
    public Task PreviousTrackAsync() => SendPlayStateCommandAsync("previousTrack");

    /// <inheritdoc />
    public Task SeekAsync(long positionTicks) => SendPlayStateCommandAsync("seek", positionTicks);

    /// <inheritdoc />
    public void SetCurrentTime(double milliseconds) => _ = SeekAsync((long)(milliseconds * 10000));

    /// <inheritdoc />
    public void Pause() => _ = SendPlayStateCommandAsync("Pause");

    /// <inheritdoc />
    public void Unpause() => _ = SendPlayStateCommandAsync("Unpause");

    /// <inheritdoc />
    public void SetPlaybackRate(double value)
    {
    }

    /// <inheritdoc />
    public void SetVolume(int value) => _ = SendCommandAsync("SetVolume", CommandArguments.Of(("Volume", value)));

    /// <inheritdoc />
    public void SetMute(bool mute) => _ = SendCommandAsync(mute ? "Mute" : "Unmute");

    /// <inheritdoc />
    public void ToggleMute() => _ = SendCommandAsync("ToggleMute");

    /// <inheritdoc />
    public void VolumeUp() => _ = SendCommandAsync("VolumeUp");

    /// <inheritdoc />
    public void VolumeDown() => _ = SendCommandAsync("VolumeDown");

    /// <inheritdoc />
    public bool Supports(string feature) => false;

    /// <inheritdoc />
    public void SetAudioStreamIndex(int index) => _ = SendCommandAsync("SetAudioStreamIndex", CommandArguments.Of(("Index", index)));

    /// <inheritdoc />
    public void SetSubtitleStreamIndex(int index) => _ = SendCommandAsync("SetSubtitleStreamIndex", CommandArguments.Of(("Index", index)));

    /// <inheritdoc />
    public void SetAspectRatio(string value)
    {
    }

    /// <inheritdoc />
    public void ToggleFullscreen() => _ = SendCommandAsync("ToggleFullscreen");

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
    public void SetRepeatMode(string value) => _ = SendCommandAsync("SetRepeatMode", CommandArguments.Of(("RepeatMode", value)));

    /// <inheritdoc />
    public void SetQueueShuffleMode(string value) => _ = SendCommandAsync("SetShuffleQueue", CommandArguments.Of(("ShuffleMode", value)));

    /// <summary>Port of setCurrentPlaylistItem(): the whole queue is sent again, starting at the item.</summary>
    public Task SetCurrentPlaylistItemAsync(string playlistItemId)
    {
        var index = _playlist.FindIndex(i => i.PlaylistItemId == playlistItemId);
        return SendPlayCommandAsync(IdsOf(_playlist), "PlayNow", startIndex: index < 0 ? null : index);
    }

    /// <summary>Port of movePlaylistItem(): the reordered queue is sent again, keeping the current item.</summary>
    public Task MovePlaylistItemAsync(string playlistItemId, int newIndex)
    {
        var index = _playlist.FindIndex(i => i.PlaylistItemId == playlistItemId);
        if (index < 0 || index == newIndex)
        {
            return Task.CompletedTask;
        }

        var current = CurrentPlaylistItemId;
        var currentIndex = current == playlistItemId ? newIndex : 0;
        var append = newIndex + 1 >= _playlist.Count;
        if (newIndex > index)
        {
            newIndex++;
        }

        var ids = new List<string>();
        var moved = IdOf(_playlist[index]);
        for (var i = 0; i < _playlist.Count; i++)
        {
            if (i == index)
            {
                continue;
            }

            if (i == newIndex)
            {
                ids.Add(moved);
            }

            if (_playlist[i].PlaylistItemId == current)
            {
                currentIndex = ids.Count;
            }

            ids.Add(IdOf(_playlist[i]));
        }

        if (append)
        {
            ids.Add(moved);
        }

        return SendPlayCommandAsync(ids, "PlayNow", startIndex: currentIndex);
    }

    /// <summary>Port of sendCommand(): <c>POST Sessions/{id}/Command</c> with the name and the arguments.</summary>
    public Task SendCommandAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        if (ActiveSessionId is not { } sessionId)
        {
            return Task.CompletedTask;
        }

        var command = new JsonObject { ["Name"] = name };
        if (arguments is not null)
        {
            var values = new JsonObject();
            foreach (var (key, value) in arguments)
            {
                values[key] = value is null ? null : JsonSerializer.SerializeToNode(value);
            }

            command["Arguments"] = values;
        }

        return PostAsync($"Sessions/{sessionId}/Command", command);
    }

    /// <summary>Handles a "Sessions" message. Port of processUpdatedSessions().</summary>
    internal void ProcessUpdatedSessions(JsonElement data)
    {
        _lastMessageMs = timers.NowMs;
        var targetId = ActiveSessionId;
        JsonObject? json = null;
        if (targetId is not null && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in data.EnumerateArray())
            {
                if (Text(element, "Id") == targetId)
                {
                    json = JsonNode.Parse(element.GetRawText()) as JsonObject;
                    break;
                }
            }
        }

        if (json is null)
        {
            // The target is gone: back to this device.
            _session = null;
            _sessionJson = null;
            _manager?.SetDefaultPlayerActive();
            return;
        }

        NormalizeImages(json, api.ServerId);
        var session = KiotaJson.Parse(json.ToJsonString(), SessionInfoDto.CreateFromDiscriminatorValue);
        if (session is null)
        {
            return;
        }

        UpdateCurrentQueue(session);
        var names = GetChangedEvents(_sessionJson, json);
        _session = session;
        _sessionJson = json;
        foreach (var name in names)
        {
            Raise(name);
        }
    }

    /// <summary>
    /// Port of getChangedEvents(): the events a new session state stands for. Position, pause,
    /// volume and repeat mode have events of their own; any other difference is a "statechange".
    /// </summary>
    internal static IReadOnlyList<string> GetChangedEvents(JsonObject? oldData, JsonObject newData)
    {
        var oldState = oldData?["PlayState"] as JsonObject;
        var newState = newData["PlayState"] as JsonObject;
        var oldTranscoding = oldData?["TranscodingInfo"] as JsonObject;
        var newTranscoding = newData["TranscodingInfo"] as JsonObject;
        if (oldData is null || oldState is null || newState is null || (oldTranscoding is null) != (newTranscoding is null))
        {
            return [PlaybackEvents.StateChange];
        }

        var names = new List<string>();
        if (Differs(oldState, newState, "PositionTicks"))
        {
            names.Add(PlaybackEvents.TimeUpdate);
        }

        if (Differs(oldState, newState, "IsPaused"))
        {
            names.Add(newState["IsPaused"]?.GetValueKind() == JsonValueKind.True ? PlaybackEvents.Pause : PlaybackEvents.Unpause);
        }

        if (Differs(oldState, newState, "IsMuted") || Differs(oldState, newState, "VolumeLevel"))
        {
            names.Add(PlaybackEvents.VolumeChange);
        }

        if (Differs(oldState, newState, "RepeatMode"))
        {
            names.Add(PlaybackEvents.RepeatModeChange);
        }

        // What the events above cover, and what changes with time anyway, does not count as a change of state.
        var oldRest = (JsonObject)oldData.DeepClone();
        var newRest = (JsonObject)newData.DeepClone();
        foreach (var rest in new[] { oldRest, newRest })
        {
            rest.Remove("LastActivityDate");
            rest.Remove("LastPlaybackCheckIn");
            rest.Remove("LastPausedDate");
            if (rest["PlayState"] is JsonObject state)
            {
                foreach (var name in new[] { "PositionTicks", "IsPaused", "IsMuted", "VolumeLevel", "RepeatMode", "OrderMode" })
                {
                    state.Remove(name);
                }
            }

            if (rest["TranscodingInfo"] is JsonObject transcoding)
            {
                transcoding.Remove("CompletionPercentage");
                transcoding.Remove("Framerate");
            }
        }

        return JsonNode.DeepEquals(oldRest, newRest) ? names : [PlaybackEvents.StateChange];
    }

    /// <summary>
    /// Port of normalizeImages(): sessions of older servers describe the images of the playing item
    /// with their own fields; they are mapped to the item fields the UI reads.
    /// </summary>
    internal static void NormalizeImages(JsonObject session, string? serverId)
    {
        if (session["NowPlayingItem"] is not JsonObject item)
        {
            return;
        }

        var imageTags = item["ImageTags"] as JsonObject;
        var primaryImageTag = (item["PrimaryImageTag"] as JsonValue)?.ToString();
        if (imageTags is null || (imageTags["Primary"] is null && !string.IsNullOrEmpty(primaryImageTag)))
        {
            if (imageTags is null)
            {
                imageTags = [];
                item["ImageTags"] = imageTags;
            }

            if (!string.IsNullOrEmpty(primaryImageTag))
            {
                imageTags["Primary"] = primaryImageTag;
            }
        }

        var backdropImageTag = (item["BackdropImageTag"] as JsonValue)?.ToString();
        if (!string.IsNullOrEmpty(backdropImageTag))
        {
            var backdropItemId = (item["BackdropItemId"] as JsonValue)?.ToString();
            if (backdropItemId == (item["Id"] as JsonValue)?.ToString())
            {
                item["BackdropImageTags"] = new JsonArray(backdropImageTag);
            }
            else
            {
                item["ParentBackdropImageTags"] = new JsonArray(backdropImageTag);
                item["ParentBackdropItemId"] = backdropItemId;
            }
        }

        if (!string.IsNullOrEmpty(serverId))
        {
            item["ServerId"] = serverId;
        }
    }

    private static bool Differs(JsonObject oldState, JsonObject newState, string name) =>
        !JsonNode.DeepEquals(oldState[name], newState[name]);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<string> Strings(JsonElement element, string name)
    {
        var result = new List<string>();
        if (element.TryGetProperty(name, out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text)
                {
                    result.Add(text);
                }
            }
        }

        return result;
    }

    private static string IdOf(BaseItemDto item) => item.Id?.ToString("N") ?? string.Empty;

    private static List<string> IdsOf(IEnumerable<BaseItemDto> items) => items.Select(IdOf).Where(id => id.Length > 0).ToList();

    private static List<string> IdsOf(PlayOptions options) =>
        options.Ids is { } ids ? ids.ToList() : IdsOf(options.Items ?? []);

    private List<MediaStream> Streams(MediaStream_Type type) =>
        (NowPlayingItem?.MediaStreams ?? []).Where(s => s.Type == type).ToList();

    private void Subscribe()
    {
        _subscription = events.Subscribe("Sessions", ProcessUpdatedSessions);
        events.ConnectionChanged += OnConnectionChanged;
        _lastMessageMs = timers.NowMs;
        _ = events.SendAsync("SessionsStart", SessionsInterval);

        // The server keeps one "Sessions" subscription per socket, and another page of the app (the
        // dashboard) may end it; a reconnect drops it as well. Without messages it is started again.
        // Only administrators get these messages at all (the server refuses the others), so only
        // they ask again.
        _watchdog = timers.SetInterval(
            () => api.IsAdmin && timers.NowMs - _lastMessageMs >= WatchdogMs
                ? events.SendAsync("SessionsStart", SessionsInterval)
                : Task.CompletedTask,
            WatchdogMs);
    }

    private void Unsubscribe()
    {
        if (_subscription is null)
        {
            return;
        }

        _subscription.Dispose();
        _subscription = null;
        _watchdog?.Dispose();
        _watchdog = null;
        events.ConnectionChanged -= OnConnectionChanged;
        _ = events.SendAsync("SessionsStop");
        _session = null;
        _sessionJson = null;
        _playlist = [];
        _queueKey = null;
        _queueVersion++;
    }

    private void OnConnectionChanged()
    {
        if (events.IsConnected && _subscription is not null)
        {
            _ = events.SendAsync("SessionsStart", SessionsInterval);
        }
    }

    /// <summary>Port of updateCurrentQueue(): loads the items of the target's queue when it changed.</summary>
    private void UpdateCurrentQueue(SessionInfoDto session)
    {
        var queue = session.NowPlayingQueue ?? [];

        // The original compares the queue with the loaded items; comparing with the queue that was
        // loaded last does the same and does not load again and again when the server returns
        // fewer items than the queue has (an item that is in it twice, or one that was deleted).
        var key = string.Join('|', queue.Select(i => $"{i.Id:N}:{i.PlaylistItemId}"));
        if (key == _queueKey)
        {
            return;
        }

        _queueKey = key;
        _ = UpdatePlaylistAsync(queue, ++_queueVersion);
    }

    private async Task UpdatePlaylistAsync(List<QueueItem> queue, int version)
    {
        try
        {
            var ids = queue.Select(i => i.Id?.ToString("N") ?? string.Empty).Where(id => id.Length > 0).ToList();
            var items = ids.Count == 0 || _manager is null ? [] : await _manager.GetPlaylistItemsAsync(ids);
            if (version != _queueVersion)
            {
                return;
            }

            // /Items deduplicates ids and can omit deleted/inaccessible items. Reconstruct from
            // the wire queue instead of assigning playlist ids by the response index.
            _playlist = [];
            foreach (var entry in queue)
            {
                if (items.FirstOrDefault(item => item.Id == entry.Id) is { } item)
                {
                    var copy = KiotaJson.Parse(KiotaJson.Serialize(item), BaseItemDto.CreateFromDiscriminatorValue)!;
                    copy.PlaylistItemId = entry.PlaylistItemId;
                    _playlist.Add(copy);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or Microsoft.Kiota.Abstractions.ApiException)
        {
            AppLog.Warn($"[sessionPlayer] loading the queue failed: {ex.Message}");
            if (version == _queueVersion)
            {
                _queueKey = null;
            }
        }

        // Unlike the legacy promise-based getPlaylist, Blazor needs an event after the load, also
        // when the target is paused and its subsequent websocket state does not change.
        if (_subscription is not null && version == _queueVersion)
        {
            Raise(PlaybackEvents.StateChange);
        }
    }

    private Task SendPlayCommandAsync(
        IReadOnlyList<string> ids,
        string playCommand,
        long? startPositionTicks = null,
        string? mediaSourceId = null,
        int? audioStreamIndex = null,
        int? subtitleStreamIndex = null,
        int? startIndex = null)
    {
        if (ActiveSessionId is not { } sessionId)
        {
            return Task.CompletedTask;
        }

        return PostAsync(api.GetUrl(
            $"Sessions/{sessionId}/Playing",
            ("ItemIds", string.Join(',', ids)),
            ("PlayCommand", playCommand),
            ("StartPositionTicks", startPositionTicks is > 0 ? startPositionTicks : null),
            ("MediaSourceId", mediaSourceId),
            ("AudioStreamIndex", audioStreamIndex),
            ("SubtitleStreamIndex", subtitleStreamIndex),
            ("StartIndex", startIndex)));
    }

    private Task SendPlayStateCommandAsync(string command, long? seekPositionTicks = null)
    {
        if (ActiveSessionId is not { } sessionId)
        {
            return Task.CompletedTask;
        }

        return PostAsync(api.GetUrl($"Sessions/{sessionId}/Playing/{command}", ("SeekPositionTicks", seekPositionTicks)));
    }

    private async Task PostAsync(string path, JsonObject? body = null)
    {
        try
        {
            using var response = await api.SendAsync(HttpMethod.Post, path, body);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn($"[sessionPlayer] {path} answered {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            AppLog.Warn($"[sessionPlayer] {path} failed: {ex.Message}");
        }
    }

    private void Raise(string name) => Event?.Invoke(this, new PlayerEvent(name));
}
