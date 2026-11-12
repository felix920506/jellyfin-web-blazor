using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// The state of a player as the original's getPlayerState() describes it: the PlayState values that
/// are reported to the server plus what is playing.
/// </summary>
public sealed class PlayerState
{
    public int VolumeLevel { get; init; }
    public bool IsMuted { get; init; }
    public bool IsPaused { get; init; }
    public string RepeatMode { get; init; } = PlayQueue.RepeatNone;
    public string ShuffleMode { get; init; } = PlayQueue.Sorted;
    public string PlaybackOrder => ShuffleMode == PlayQueue.Shuffled ? "Shuffle" : "Default";
    public int? MaxStreamingBitrate { get; init; }
    public long PositionTicks { get; init; }
    public long? PlaybackStartTimeTicks { get; init; }
    public double PlaybackRate { get; init; } = 1;
    public int? SubtitleStreamIndex { get; init; }
    public int? SecondarySubtitleStreamIndex { get; init; }
    public int? AudioStreamIndex { get; init; }
    public IReadOnlyList<BufferedRange> BufferedRanges { get; init; } = [];
    public string? PlayMethod { get; init; }
    public string? LiveStreamId { get; init; }
    public string? PlaySessionId { get; init; }
    public string? PlaylistItemId { get; init; }
    public string? MediaSourceId { get; init; }
    public bool? CanSeek { get; init; }

    /// <summary>The item that plays. Its run time is in <see cref="RunTimeTicks"/>.</summary>
    public BaseItemDto? NowPlayingItem { get; init; }

    /// <summary>The run time of the media source, or what the player reports.</summary>
    public long? RunTimeTicks { get; init; }

    public MediaSourceInfo? MediaSource { get; init; }
    public BaseItemDto? NextItem { get; set; }
    public string? NextMediaType { get; set; }
    public bool IsFirstItem { get; set; }
    public bool IsFullscreen { get; set; }

    /// <summary>
    /// The body of a playback report (Sessions/Playing, /Progress, /Stopped): the PlayState values in
    /// the original's order plus ItemId, EventName and the queue. Values the original leaves
    /// undefined are omitted.
    /// </summary>
    public JsonObject ToReport(string? eventName = null, IReadOnlyList<BaseItemDto>? playlist = null)
    {
        var info = new JsonObject
        {
            ["VolumeLevel"] = VolumeLevel,
            ["IsMuted"] = IsMuted,
            ["IsPaused"] = IsPaused,
            ["RepeatMode"] = RepeatMode,
            ["ShuffleMode"] = ShuffleMode,
            ["PlaybackOrder"] = PlaybackOrder,
        };

        Add(info, "MaxStreamingBitrate", MaxStreamingBitrate);
        info["PositionTicks"] = PositionTicks;
        Add(info, "PlaybackStartTimeTicks", PlaybackStartTimeTicks);
        info["PlaybackRate"] = PlaybackRate;
        Add(info, "SubtitleStreamIndex", SubtitleStreamIndex);
        Add(info, "SecondarySubtitleStreamIndex", SecondarySubtitleStreamIndex);
        Add(info, "AudioStreamIndex", AudioStreamIndex);
        info["BufferedRanges"] = new JsonArray(BufferedRanges
            .Select(r => (JsonNode)new JsonObject { ["start"] = r.Start, ["end"] = r.End })
            .ToArray());
        Add(info, "PlayMethod", PlayMethod);
        Add(info, "LiveStreamId", LiveStreamId);
        Add(info, "PlaySessionId", PlaySessionId);
        Add(info, "PlaylistItemId", PlaylistItemId);
        Add(info, "MediaSourceId", MediaSourceId);
        Add(info, "CanSeek", CanSeek);
        Add(info, "ItemId", NowPlayingItem?.Id?.ToString("N"));
        Add(info, "EventName", eventName);

        if (playlist is not null)
        {
            info["NowPlayingQueue"] = new JsonArray(playlist
                .Select(i => (JsonNode)new JsonObject { ["Id"] = i.Id?.ToString("N"), ["PlaylistItemId"] = i.PlaylistItemId })
                .ToArray());
        }

        return info;
    }

    private static void Add(JsonObject target, string name, JsonNode? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }
}

/// <summary>Telling the server: playback reports, their timing, and what happens when playback ends.</summary>
public sealed partial class PlaybackManager
{
    /// <summary>The interval of the "timeupdate" reports while playing.</summary>
    public const int ProgressIntervalMs = 10000;

    private const int TimeUpdateRateLimitMs = 10000;
    private const int VolumeChangeRateLimitMs = 3000;

    private readonly HashSet<IMediaPlayer> _stoppedUnbound = [];
    private readonly Lock _reportGate = new();
    private Task _reportQueue = Task.CompletedTask;
    private long _lastProgressReport;
    private long? _lastProgressReportTicks;
    private JsonObject? _lastProgressOptions;
    private bool _progressPending;
    private int? _progressPendingLimit;
    private int _progressToken;

    /// <summary>Port of getPlayerState(): null when nothing is the current player.</summary>
    public PlayerState? GetPlayerState() => CurrentPlayer is { } player ? GetPlayerState(player) : null;

    /// <summary>Port of getPlayerState().</summary>
    public PlayerState GetPlayerState(IMediaPlayer player, BaseItemDto? item = null, MediaSourceInfo? mediaSource = null)
    {
        if (player is IRemotePlayer remote)
        {
            return remote.GetPlayerState();
        }

        var data = Data(player);
        var streamInfo = data.StreamInfo;
        item ??= streamInfo?.Item;
        mediaSource ??= streamInfo?.MediaSource;
        long? runTime = mediaSource?.RunTimeTicks is { } ticks && ticks > 0 ? ticks : null;
        runTime ??= player.DurationMs is { } duration && duration > 0 ? (long)(duration * 10000) : item?.RunTimeTicks;

        return new PlayerState
        {
            VolumeLevel = player.Volume,
            IsMuted = player.IsMuted,
            IsPaused = player.Paused,
            RepeatMode = _playQueue.RepeatMode,
            ShuffleMode = _playQueue.ShuffleMode,
            MaxStreamingBitrate = data.MaxStreamingBitrate is > 0 ? data.MaxStreamingBitrate : GetSavedMaxStreamingBitrate(streamInfo?.MediaType),
            PositionTicks = GetCurrentTicks(player),
            PlaybackStartTimeTicks = streamInfo?.PlaybackStartTimeTicks,
            PlaybackRate = player.PlaybackRate,
            SubtitleStreamIndex = data.SubtitleStreamIndex,
            SecondarySubtitleStreamIndex = data.SecondarySubtitleStreamIndex,
            AudioStreamIndex = data.AudioStreamIndex,
            BufferedRanges = player.BufferedRanges,
            PlayMethod = streamInfo?.PlayMethod,
            LiveStreamId = mediaSource?.LiveStreamId,
            PlaySessionId = streamInfo?.PlaySessionId,
            PlaylistItemId = _playQueue.CurrentPlaylistItemId,
            MediaSourceId = mediaSource?.Id,
            CanSeek = mediaSource is null ? null : (mediaSource.RunTimeTicks ?? 0) > 0 || CanSeek(player),
            NowPlayingItem = item,
            RunTimeTicks = runTime,
            MediaSource = mediaSource,
        };
    }

    /// <summary>
    /// Port of the manager's player event handlers: progress reports for what changes the session,
    /// the end of playback, errors; everything is passed on to <see cref="Event"/>.
    /// </summary>
    internal void OnPlayerEvent(IMediaPlayer player, PlayerEvent playerEvent)
    {
        if (player is IRemotePlayer)
        {
            // The target reports its own playback; its events only refresh what is shown here.
            if (ReferenceEquals(CurrentPlayer, player))
            {
                Raise(playerEvent.Name);
            }

            return;
        }

        switch (playerEvent.Name)
        {
            case "error":
                _ = HandleAsync(OnPlaybackErrorAsync(player, playerEvent.ErrorType ?? MediaErrors.PlayerError, Data(player).StreamInfo));
                break;
            case "stopped":
                if (!_stoppedUnbound.Contains(player))
                {
                    _ = HandleAsync(OnPlaybackStoppedAsync(player, null));
                }

                break;
            case PlaybackEvents.TimeUpdate:
            case PlaybackEvents.Pause:
            case PlaybackEvents.Unpause:
            case PlaybackEvents.VolumeChange:
            case PlaybackEvents.RepeatModeChange:
            case PlaybackEvents.ShuffleQueueModeChange:
                SendProgressUpdate(player, playerEvent.Name);
                Raise(playerEvent.Name);
                break;
            case PlaybackEvents.PlaylistItemMove:
            case PlaybackEvents.PlaylistItemRemove:
            case PlaybackEvents.PlaylistItemAdd:
                SendProgressUpdate(player, playerEvent.Name, reportPlaylist: true);
                Raise(playerEvent.Name);
                break;
            default:
                Raise(playerEvent.Name);
                break;
        }
    }

    /// <summary>Port of sendProgressUpdate().</summary>
    private void SendProgressUpdate(IMediaPlayer player, string eventName, bool reportPlaylist = false)
    {
        // PORT-TODO(playback): the original refreshes the media info of live streams every ten
        // minutes here (getLiveStreamMediaInfo()).
        if (Data(player).StreamInfo is not { Started: true, Ended: false })
        {
            return;
        }

        var state = GetPlayerState(player);
        if (state.NowPlayingItem is null)
        {
            return;
        }

        ReportPlayback(state, reportPlaylist, "Sessions/Playing/Progress", eventName);
    }

    /// <summary>Port of reportPlayback() and the api client's reportPlayback* methods.</summary>
    private void ReportPlayback(PlayerState state, bool reportPlaylist, string path, string? progressEventName = null)
    {
        if (!_api.IsLoggedIn || state.NowPlayingItem?.Id is null)
        {
            return;
        }

        var info = state.ToReport(progressEventName, reportPlaylist ? _playQueue.GetPlaylist() : null);
        if (path == "Sessions/Playing/Progress")
        {
            ReportPlaybackProgress(info);
            return;
        }

        // Start and stop reset the rate limiting of the progress reports.
        _lastProgressReport = 0;
        _lastProgressReportTicks = null;
        CancelPendingProgressReport();
        _ = PostReportAsync(path, info);
    }

    /// <summary>
    /// The api client's reportPlaybackProgress(): "timeupdate" goes out at most every ten seconds
    /// (unless the position jumped), "volumechange" every three; anything else at once. A report
    /// that has to wait is replaced by the newest one.
    /// </summary>
    private void ReportPlaybackProgress(JsonObject options)
    {
        var eventName = (string?)options["EventName"] ?? PlaybackEvents.TimeUpdate;
        var limit = eventName switch
        {
            PlaybackEvents.TimeUpdate => TimeUpdateRateLimitMs,
            PlaybackEvents.VolumeChange => VolumeChangeRateLimitMs,
            _ => 0,
        };

        var msSinceLastReport = _timers.NowMs - _lastProgressReport;
        var positionTicks = (long?)options["PositionTicks"] ?? 0;
        if (msSinceLastReport < limit && eventName == PlaybackEvents.TimeUpdate && positionTicks != 0)
        {
            var expectedTicks = (10000 * msSinceLastReport) + (_lastProgressReportTicks ?? 0);
            if (Math.Abs(positionTicks - expectedTicks) >= 50000000)
            {
                // The position is five seconds off: a seek. Report it now.
                limit = 0;
            }
        }

        if (limit < (_progressPendingLimit ?? 1000000))
        {
            CancelPendingProgressReport();
        }

        _lastProgressOptions = options;
        if (_progressPending)
        {
            return;
        }

        var delay = (int)Math.Max(0, limit - msSinceLastReport);
        if (delay == 0)
        {
            SendPendingProgressReport();
            return;
        }

        _progressPending = true;
        _progressPendingLimit = limit;
        _ = SendProgressReportLaterAsync(++_progressToken, delay);
    }

    private async Task SendProgressReportLaterAsync(int token, int delay)
    {
        await _timers.Delay(delay);
        if (token == _progressToken && _progressPending)
        {
            SendPendingProgressReport();
        }
    }

    private void SendPendingProgressReport()
    {
        _progressPending = false;
        _progressPendingLimit = null;
        if (_lastProgressOptions is not { } options)
        {
            return;
        }

        _lastProgressOptions = null;
        _lastProgressReport = _timers.NowMs;
        _lastProgressReportTicks = (long?)options["PositionTicks"];
        _ = PostReportAsync("Sessions/Playing/Progress", options);
    }

    private void CancelPendingProgressReport()
    {
        _progressToken++;
        _progressPending = false;
        _progressPendingLimit = null;
    }

    /// <summary>
    /// Posts a report after the ones before it have been answered. The end of an item raises "pause"
    /// and "stopped" back to back; a progress report that reached the server after the stop report
    /// would make the session play again, so the reports are kept in order. (The original sends them
    /// independently; with an idle queue this sends at once, exactly like it.)
    /// </summary>
    private Task PostReportAsync(string path, JsonObject body)
    {
        lock (_reportGate)
        {
            return _reportQueue = PostReportAfterAsync(_reportQueue, path, body);
        }
    }

    private async Task PostReportAfterAsync(Task previous, string path, JsonObject body)
    {
        // Never faults: every failure of a report is caught below.
        await previous;
        try
        {
            using var response = await _api.SendAsync(HttpMethod.Post, path, body);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Warn($"[playback] {path} answered {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            AppLog.Warn($"[playback] {path} failed: {ex.Message}");
        }
    }

    private void StartProgressTimer(IMediaPlayer player)
    {
        StopProgressTimer();
        _progressTimer = _timers.SetInterval(
            () =>
            {
                SendProgressUpdate(player, PlaybackEvents.TimeUpdate);
                SendPlayingPing(player);
                return Task.CompletedTask;
            },
            ProgressIntervalMs);
    }

    private void SendPlayingPing(IMediaPlayer player)
    {
        if (Data(player).StreamInfo is not { Started: true, Ended: false } || GetPlayerState(player) is not { } state)
        {
            return;
        }

        var playSessionId = state.PlaySessionId;
        if (string.IsNullOrEmpty(playSessionId))
        {
            return;
        }

        _ = PostReportAsync($"Sessions/Playing/Ping?playSessionId={Uri.EscapeDataString(playSessionId)}", state.ToReport());
    }

    private void StopProgressTimer()
    {
        _progressTimer?.Dispose();
        _progressTimer = null;
    }

    /// <summary>Port of onPlaybackStarted(): reports the start and begins the progress reports.</summary>
    private void OnPlaybackStarted(IMediaPlayer player, ItemPlayOptions playOptions, StreamInfo streamInfo, PlaybackSource? mediaSource)
    {
        SetCurrentPlayer(player);

        var data = Data(player);
        data.StreamInfo = streamInfo;
        streamInfo.PlaybackStartTimeTicks = _timers.NowMs * 10000;
        data.AudioStreamIndex = mediaSource?.Info.DefaultAudioStreamIndex;
        data.SubtitleStreamIndex = mediaSource?.Info.DefaultSubtitleStreamIndex;
        data.SecondarySubtitleStreamIndex = playOptions.SecondarySubtitleStreamIndex;

        _playNextAfterEnded = true;

        var state = GetPlayerState(player, streamInfo.Item, streamInfo.MediaSource);
        ReportPlayback(state, reportPlaylist: true, "Sessions/Playing");

        state.IsFirstItem = playOptions.IsFirstItem;
        state.IsFullscreen = playOptions.Fullscreen;
        LastStartState = state;

        streamInfo.Started = true;
        StartProgressTimer(player);
        Raise(PlaybackEvents.PlaybackStart);
    }

    /// <summary>The state passed with the last "playbackstart".</summary>
    public PlayerState? LastStartState { get; private set; }

    /// <summary>
    /// Port of onPlaybackError(): a source the player cannot play is retried as a transcode once the
    /// server offers one; otherwise playback ends with the error's message.
    /// </summary>
    private async Task OnPlaybackErrorAsync(IMediaPlayer player, string errorType, StreamInfo? streamInfo)
    {
        AppLog.Warn($"[playback] playback error: {errorType}");

        if (!string.IsNullOrEmpty(streamInfo?.Url))
        {
            var url = streamInfo.Url.ToLowerInvariant();
            var isAlreadyFallbacking = url.Contains("transcodereasons", StringComparison.Ordinal);
            var preventsVideoCopy = url.Contains("allowvideostreamcopy=false", StringComparison.Ordinal);
            var preventsAudioCopy = url.Contains("allowaudiostreamcopy=false", StringComparison.Ordinal);

            // Retry as long as the server has something left to change about the stream.
            if (streamInfo.MediaSource?.SupportsTranscoding == true && (!preventsVideoCopy || !preventsAudioCopy))
            {
                var startTime = GetCurrentTicks(player);
                if (startTime == 0)
                {
                    startTime = streamInfo.PlayerStartPositionTicks ?? 0;
                }

                // Remote streams are first tried without re-encoding the video.
                var tryVideoStreamCopy = streamInfo.Item.LocationType == BaseItemDto_LocationType.Remote && !isAlreadyFallbacking;
                await ChangeStreamAsync(player, startTime, new StreamChangeParams
                {
                    EnableDirectPlay = false,
                    EnableDirectStream = tryVideoStreamCopy,
                    AllowVideoStreamCopy = tryVideoStreamCopy,
                    AllowAudioStreamCopy = preventsAudioCopy || preventsVideoCopy ? false : null,
                });
                return;
            }
        }

        Raise(PlaybackEvents.PlaybackError);
        await OnPlaybackStoppedAsync(player, "." + errorType);
    }

    /// <summary>
    /// Port of onPlaybackStopped(): reports the stop, then plays the next item of the queue or
    /// releases the player.
    /// </summary>
    private async Task OnPlaybackStoppedAsync(IMediaPlayer player, string? displayErrorCode)
    {
        var data = Data(player);
        if (data.IsChangingStream)
        {
            return;
        }

        StopProgressTimer();

        // The state is read before the player is released.
        var state = GetPlayerState(player);
        var streamInfo = data.StreamInfo;
        var errorOccurred = !string.IsNullOrEmpty(displayErrorCode);

        var nextItem = _playNextAfterEnded && !errorOccurred ? _playQueue.GetNextItemInfo() : null;
        var nextMediaType = nextItem?.Item.MediaType.ToApiString();
        state.NextMediaType = nextMediaType;

        if (streamInfo is not null && IsServerItem(streamInfo.Item) && !streamInfo.Ended)
        {
            // Only used internally, to stop the progress reports.
            streamInfo.Ended = true;
            ReportPlayback(state, reportPlaylist: true, "Sessions/Playing/Stopped");
        }

        state.NextItem = nextItem?.Item;

        if (nextItem is null)
        {
            _playQueue.Reset();
        }

        var newPlayer = nextItem is not null ? GetPlayer(nextItem.Item) : null;
        var stopInfo = new PlaybackStoppedInfo(player, state, nextItem?.Item, nextMediaType);
        if (newPlayer is null)
        {
            data.StreamInfo = null;
            player.Destroy();
        }

        try
        {
            PlaybackStopped?.Invoke(stopInfo);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] a handler of 'playbackstop' failed: {ex}");
        }

        if (newPlayer is null)
        {
            // Raises "playerchange" when there was a current player.
            if (ReferenceEquals(CurrentPlayer, player))
            {
                SetCurrentPlayer(null);
            }
        }

        Raise(PlaybackEvents.PlaybackStop);

        if (errorOccurred)
        {
            ShowPlaybackInfoErrorMessage("PlaybackError" + displayErrorCode);
        }
        else if (newPlayer is not null
            && (_api.User?.Configuration?.EnableNextEpisodeAutoPlay == true || nextMediaType != "Video"))
        {
            await NextTrackAsync();
        }
    }

    /// <summary>
    /// Port of onPlaybackChanging(): the active player is stopped for another item, without the
    /// "stopped" handling that would move on in the queue.
    /// </summary>
    private async Task OnPlaybackChangingAsync(IMediaPlayer activePlayer, IMediaPlayer? newPlayer, BaseItemDto newItem)
    {
        var data = Data(activePlayer);
        var streamInfo = data.StreamInfo;

        // After a natural end the stop has been reported already (the original reports it twice).
        var report = streamInfo is { Ended: false };
        var state = GetPlayerState(activePlayer);

        StopProgressTimer();

        _stoppedUnbound.Add(activePlayer);
        try
        {
            // The player is kept when it plays the new item as well.
            await activePlayer.StopAsync(!ReferenceEquals(activePlayer, newPlayer));
        }
        finally
        {
            _stoppedUnbound.Remove(activePlayer);
        }

        data.StreamInfo = null;
        if (!report || streamInfo is null)
        {
            return;
        }

        streamInfo.Ended = true;
        if (IsServerItem(streamInfo.Item))
        {
            ReportPlayback(state, reportPlaylist: true, "Sessions/Playing/Stopped");
        }

        var nextMediaType = newItem.MediaType.ToApiString();
        state.NextItem = newItem;
        state.NextMediaType = nextMediaType;
        try
        {
            PlaybackStopped?.Invoke(new PlaybackStoppedInfo(activePlayer, state, newItem, nextMediaType));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] a handler of 'playbackstop' failed: {ex}");
        }

        Raise(PlaybackEvents.PlaybackStop);
    }

    private static async Task HandleAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] handling a player event failed: {ex}");
        }
    }
}
