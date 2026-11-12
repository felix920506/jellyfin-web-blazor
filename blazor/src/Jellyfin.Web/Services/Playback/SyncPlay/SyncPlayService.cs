using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>
/// SyncPlay group, clock and local playback coordinator. A SyncPlay group drives the local player;
/// it is deliberately independent from the remote-session player.
/// </summary>
[AppService]
public sealed class SyncPlayService : IStartupTask, IDisposable
{
    private readonly JellyfinApi _api;
    private readonly ServerEvents _events;
    private readonly PlaybackManager _playback;
    private readonly IPlaybackTimers _timers;
    private readonly AppSettings _appSettings;
    private readonly ToastService _toasts;
    private readonly Globalize _globalize;
    private readonly TimeSyncCore _timeSync;
    private IDisposable? _commandSubscription;
    private IDisposable? _groupSubscription;
    private IDisposable? _groupPoller;
    private bool _suppressEcho;
    private bool _buffering;
    private bool _disposed;
    private long _enabledAt;
    private SyncPlaySettings _settings = new();

    public SyncPlayService(
        JellyfinApi api,
        ServerEvents events,
        PlaybackManager playback,
        IPlaybackTimers timers,
        AppSettings appSettings,
        ToastService toasts,
        Globalize globalize,
        TimeSyncServer timeSyncServer)
    {
        _api = api;
        _events = events;
        _playback = playback;
        _timers = timers;
        _appSettings = appSettings;
        _toasts = toasts;
        _globalize = globalize;
        _timeSync = new TimeSyncCore(timeSyncServer, appSettings);
        LoadSettings();
        _appSettings.Changed += OnSettingChanged;
        _playback.Event += OnPlaybackEvent;
    }

    public event Action? Changed;

    public bool IsEnabled => Group is not null;
    public SyncPlayGroupState? Group { get; private set; }
    public SyncPlayQueueState? Queue { get; private set; }
    public SyncPlayCommand? LastCommand { get; private set; }
    public bool IsFollowingGroupPlayback { get; private set; } = true;
    public bool IsBuffering => _buffering;
    public double PlaybackDifferenceMilliseconds { get; private set; }
    public string SyncMethod { get; private set; } = "None";
    public TimeSyncCore TimeSync => _timeSync;
    public SyncPlaySettings Settings => _settings;

    public Task StartAsync()
    {
        _commandSubscription = _events.Subscribe("SyncPlayCommand", data => Run(ProcessCommandAsync(data)));
        _groupSubscription = _events.Subscribe("SyncPlayGroupUpdate", data => Run(ProcessGroupUpdateAsync(data)));
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<SyncPlayGroup>> GetGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = await _api.GetJsonAsync<List<SyncPlayGroup>>("SyncPlay/List", cancellationToken);
        return groups ?? [];
    }

    public async Task CreateGroupAsync(string groupName)
    {
        var group = await PostJsonAsync<SyncPlayGroupState>("SyncPlay/New", new { GroupName = groupName });
        Enable(group);
    }

    public async Task JoinGroupAsync(string groupId)
    {
        var group = await PostJsonAsync<SyncPlayGroupState>("SyncPlay/Join", new { GroupId = groupId });
        Enable(group);
    }

    public async Task LeaveGroupAsync()
    {
        await PostAsync("SyncPlay/Leave");
        Disable(false);
    }

    public Task PauseAsync() => PostAsync("SyncPlay/Pause");

    public Task UnpauseAsync() => PostAsync("SyncPlay/Unpause");

    public Task SeekAsync(long positionTicks) => PostAsync("SyncPlay/Seek", new { PositionTicks = Math.Max(0, positionTicks) });

    public Task SetIgnoreWaitAsync(bool ignoreWait) => PostAsync("SyncPlay/SetIgnoreWait", new { IgnoreWait = ignoreWait });

    public Task SetNewQueueAsync(IReadOnlyList<string> itemIds, int itemPosition = 0, long startPositionTicks = 0) =>
        PostAsync("SyncPlay/SetNewQueue", new { PlayingQueue = itemIds, PlayingItemPosition = itemPosition, StartPositionTicks = startPositionTicks });

    public Task SetPlaylistItemAsync(string playlistItemId) => PostAsync("SyncPlay/SetPlaylistItem", new { PlaylistItemId = playlistItemId });

    public Task RequestQueueAsync(string itemId, int? count = null) => PostAsync("SyncPlay/Queue", new { ItemId = itemId, ItemCount = count });

    public Task RemoveFromPlaylistAsync(IReadOnlyList<string> playlistItemIds, bool clearPlaylist = false, bool clearPlayingItem = false) =>
        PostAsync("SyncPlay/RemoveFromPlaylist", new { PlaylistItemIds = playlistItemIds, ClearPlaylist = clearPlaylist, ClearPlayingItem = clearPlayingItem });

    public Task SetIgnoreWaitAndResumeAsync()
    {
        IsFollowingGroupPlayback = true;
        return SetIgnoreWaitAsync(false);
    }

    public async Task HaltLocalPlaybackAsync()
    {
        IsFollowingGroupPlayback = false;
        await SetIgnoreWaitAsync(true);
        await ApplyLocallyAsync(() => _playback.StopAsync());
    }

    public async Task ResumeLocalPlaybackAsync()
    {
        IsFollowingGroupPlayback = true;
        await SetIgnoreWaitAsync(false);
        if (Queue is { Playlist.Count: > 0 } queue)
        {
            await StartQueuePlaybackAsync(queue);
        }
    }

    public async Task ProcessCommandAsync(JsonElement data)
    {
        var command = Deserialize<SyncPlayCommand>(data);
        if (command is null || !IsEnabled || command.EmittedAt.ToUnixTimeMilliseconds() < _enabledAt)
        {
            return;
        }

        LastCommand = command;
        if (!IsFollowingGroupPlayback || command.Command != "Stop" && Queue?.CurrentPlaylistItemId != command.PlaylistItemId)
        {
            return;
        }

        var localWhen = _timeSync.RemoteToLocal(command.When);
        var delay = (int)Math.Clamp(localWhen.ToUnixTimeMilliseconds() - _timers.NowMs, 0, int.MaxValue);
        if (delay > 0)
        {
            await _timers.Delay(delay);
        }

        var position = command.PositionTicks;
        if (command.Command == "Unpause" && localWhen.ToUnixTimeMilliseconds() <= _timers.NowMs)
        {
            position = EstimateCurrentTicks(position, command.When);
        }

        await ApplyLocallyAsync(async () =>
        {
            switch (command.Command)
            {
                case "Unpause":
                    if (Math.Abs(_playback.CurrentTimeTicks - position) >= 400 * 10000L)
                    {
                        await _playback.SeekAsync(position);
                    }

                    await _playback.UnpauseAsync();
                    break;
                case "Pause":
                    await _playback.PauseAsync();
                    await _playback.SeekAsync(position);
                    break;
                case "Seek":
                    await _playback.UnpauseAsync();
                    await _playback.SeekAsync(position);
                    await _playback.PauseAsync();
                    break;
                case "Stop":
                    await _playback.StopAsync();
                    break;
            }
        });
    }

    public async Task ProcessGroupUpdateAsync(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var type = Text(data, "Type");
        var payload = data.TryGetProperty("Data", out var value) ? value : default;
        switch (type)
        {
            case "GroupJoined":
                Enable(Deserialize<SyncPlayGroupState>(payload));
                break;
            case "GroupUpdate":
                Group = Deserialize<SyncPlayGroupState>(payload);
                Changed?.Invoke();
                break;
            case "PlayQueue":
                await ProcessQueueAsync(payload);
                break;
            case "UserJoined":
                UpdateParticipants(Text(payload, null), joined: true);
                _toasts.Show(_globalize.Translate("MessageSyncPlayUserJoined", Text(payload, null)));
                break;
            case "UserLeft":
                UpdateParticipants(Text(payload, null), joined: false);
                _toasts.Show(_globalize.Translate("MessageSyncPlayUserLeft", Text(payload, null)));
                break;
            case "GroupLeft":
            case "NotInGroup":
                Disable(true);
                break;
            case "SyncPlayIsDisabled":
                _toasts.Show(_globalize["MessageSyncPlayIsDisabled"]);
                break;
            case "GroupDoesNotExist":
                _toasts.Show(_globalize["MessageSyncPlayGroupDoesNotExist"]);
                break;
            case "CreateGroupDenied":
                _toasts.Show(_globalize["MessageSyncPlayCreateGroupDenied"]);
                break;
            case "JoinGroupDenied":
                _toasts.Show(_globalize["MessageSyncPlayJoinGroupDenied"]);
                break;
            case "LibraryAccessDenied":
                _toasts.Show(_globalize["MessageSyncPlayLibraryAccessDenied"]);
                break;
        }
    }

    public long EstimateCurrentTicks(long positionTicks, DateTimeOffset when) =>
        positionTicks + (long)((_timeSync.LocalToRemote(DateTimeOffset.FromUnixTimeMilliseconds(_timers.NowMs)) - when).TotalMilliseconds * 10000);

    public void SaveSettings(SyncPlaySettings settings)
    {
        _settings = settings;
        Set("extraTimeOffset", settings.ExtraTimeOffset);
        Set("enableSyncCorrection", settings.EnableSyncCorrection);
        Set("minDelaySpeedToSync", settings.MinDelaySpeedToSync);
        Set("maxDelaySpeedToSync", settings.MaxDelaySpeedToSync);
        Set("speedToSyncDuration", settings.SpeedToSyncDuration);
        Set("minDelaySkipToSync", settings.MinDelaySkipToSync);
        SetBool("useSpeedToSync", settings.UseSpeedToSync);
        SetBool("useSkipToSync", settings.UseSkipToSync);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _commandSubscription?.Dispose();
        _groupSubscription?.Dispose();
        _groupPoller?.Dispose();
        _appSettings.Changed -= OnSettingChanged;
        _playback.Event -= OnPlaybackEvent;
    }

    private void Enable(SyncPlayGroupState? group)
    {
        if (group is null) return;
        Group = group;
        _enabledAt = group.LastUpdatedAt.ToUnixTimeMilliseconds();
        IsFollowingGroupPlayback = true;
        _groupPoller?.Dispose();
        _groupPoller = _timers.SetInterval(PingAsync, 1000);
        Run(_timeSync.Server.UpdateAsync().ContinueWith(_ => Changed?.Invoke()));
        Changed?.Invoke();
    }

    private void Disable(bool showMessage)
    {
        Group = null;
        Queue = null;
        LastCommand = null;
        _groupPoller?.Dispose();
        _groupPoller = null;
        _buffering = false;
        SyncMethod = "None";
        if (showMessage) _toasts.Show(_globalize["MessageSyncPlayDisabled"]);
        Changed?.Invoke();
    }

    private async Task ProcessQueueAsync(JsonElement payload)
    {
        var queue = Deserialize<SyncPlayQueueState>(payload);
        if (queue is null || Queue is { } old && queue.LastUpdate <= old.LastUpdate)
        {
            return;
        }

        var wasDifferent = Queue?.CurrentPlaylistItemId != queue.CurrentPlaylistItemId;
        Queue = queue;
        Changed?.Invoke();
        if (!IsFollowingGroupPlayback || queue.Playlist.Count == 0)
        {
            return;
        }

        if (wasDifferent || LastCommand is null)
        {
            await StartQueuePlaybackAsync(queue);
        }
    }

    private async Task StartQueuePlaybackAsync(SyncPlayQueueState queue)
    {
        var ids = queue.Playlist.Select(item => item.ItemId).Where(id => !string.IsNullOrEmpty(id)).ToList();
        if (ids.Count == 0) return;
        await ApplyLocallyAsync(() => _playback.PlayAsync(new PlayOptions
        {
            Ids = ids,
            StartIndex = Math.Max(0, queue.PlayingItemIndex),
            StartPositionTicks = EstimateCurrentTicks(queue.StartPositionTicks, queue.LastUpdate)
        }));
    }

    private void OnPlaybackEvent(string name)
    {
        if (!IsEnabled || _suppressEcho) return;
        switch (name)
        {
            case PlaybackEvents.Pause: Run(SendStateAsync("Pause")); break;
            case PlaybackEvents.Unpause: Run(SendStateAsync("Unpause")); break;
            case PlaybackEvents.PlaybackStart: Run(SendReadyAfterQueueAsync()); break;
            case PlaybackEvents.Playing: Run(SendReadyAsync(false)); break;
            case PlaybackEvents.Waiting: Run(SendReadyAsync(true)); break;
            case "seek": Run(SeekAsync(_playback.CurrentTimeTicks)); break;
            case PlaybackEvents.TimeUpdate: CorrectDrift(); break;
        }
    }

    private async Task SendReadyAfterQueueAsync()
    {
        if (Queue is null && _playback.CurrentItem?.Id is { } itemId)
        {
            await SetNewQueueAsync([itemId.ToString("N")]);
            for (var i = 0; i < 40 && Queue?.CurrentPlaylistItemId is null; i++)
            {
                await _timers.Delay(25);
            }
        }

        await SendReadyAsync(false);
    }

    private Task SendStateAsync(string command) => command == "Pause" ? PauseAsync() : UnpauseAsync();

    private Task SendReadyAsync(bool buffering)
    {
        _buffering = buffering;
        return PostAsync(buffering ? "SyncPlay/Buffering" : "SyncPlay/Ready", new
        {
            When = _timeSync.LocalToRemote(DateTimeOffset.FromUnixTimeMilliseconds(_timers.NowMs)),
            PositionTicks = _playback.CurrentTimeTicks,
            IsPlaying = _playback.IsPlaying && !_playback.IsPaused,
            PlaylistItemId = Queue?.CurrentPlaylistItemId
        });
    }

    private void CorrectDrift()
    {
        if (!_settings.EnableSyncCorrection || _buffering || LastCommand is not { Command: "Unpause" } command || command.PlaylistItemId != Queue?.CurrentPlaylistItemId)
        {
            return;
        }

        var expected = EstimateCurrentTicks(command.PositionTicks, command.When);
        var difference = (expected - _playback.CurrentTimeTicks) / 10000d;
        PlaybackDifferenceMilliseconds = difference;
        var absolute = Math.Abs(difference);
        if (_settings.UseSpeedToSync && absolute >= _settings.MinDelaySpeedToSync && absolute < _settings.MaxDelaySpeedToSync)
        {
            var rate = Math.Max(0.2, 1 + difference / _settings.SpeedToSyncDuration);
            _playback.SetPlaybackRate(rate);
            SyncMethod = $"SpeedToSync (x{rate.ToString("0.00", CultureInfo.InvariantCulture)})";
            Run(ResetRateAsync(_settings.SpeedToSyncDuration));
        }
        else if (_settings.UseSkipToSync && absolute >= _settings.MinDelaySkipToSync)
        {
            Run(ApplyLocallyAsync(() => _playback.SeekAsync(expected)));
            SyncMethod = "SkipToSync";
        }

        Changed?.Invoke();
    }

    private async Task ResetRateAsync(double milliseconds)
    {
        await _timers.Delay((int)milliseconds);
        _playback.SetPlaybackRate(1);
        SyncMethod = "None";
        Changed?.Invoke();
    }

    private async Task PingAsync()
    {
        if (!IsEnabled) return;
        try
        {
            await _timeSync.Server.UpdateAsync();
            await PostAsync("SyncPlay/Ping", new { Ping = _timeSync.PingMilliseconds });
            Changed?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            AppLog.Warn($"[syncplay] time sync failed: {ex.Message}");
        }
    }

    private void UpdateParticipants(string? participant, bool joined)
    {
        if (Group is null || string.IsNullOrEmpty(participant)) return;
        var participants = Group.Participants.ToList();
        if (joined && !participants.Contains(participant)) participants.Add(participant);
        if (!joined) participants.Remove(participant);
        Group = Group with { Participants = participants };
        Changed?.Invoke();
    }

    private async Task PostAsync(string path, object? body = null)
    {
        using var response = await _api.SendAsync(HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
    }

    private async Task<T?> PostJsonAsync<T>(string path, object? body = null)
    {
        using var response = await _api.SendAsync(HttpMethod.Post, path, body);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(content))
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>(content, JellyfinApi.Json);
    }

    private async Task ApplyLocallyAsync(Func<Task> operation)
    {
        _suppressEcho = true;
        try { await operation(); }
        finally { _suppressEcho = false; }
    }

    private void LoadSettings() => _settings = new SyncPlaySettings
    {
        ExtraTimeOffset = Number("extraTimeOffset", 0),
        EnableSyncCorrection = Bool("enableSyncCorrection", false),
        MinDelaySpeedToSync = Number("minDelaySpeedToSync", 60),
        MaxDelaySpeedToSync = Number("maxDelaySpeedToSync", 3000),
        SpeedToSyncDuration = Number("speedToSyncDuration", 1000),
        MinDelaySkipToSync = Number("minDelaySkipToSync", 400),
        UseSpeedToSync = Bool("useSpeedToSync", true),
        UseSkipToSync = Bool("useSkipToSync", true)
    };

    private void OnSettingChanged(string name)
    {
        if (name is not ("extraTimeOffset" or "enableSyncCorrection" or "minDelaySpeedToSync" or "maxDelaySpeedToSync" or "speedToSyncDuration" or "minDelaySkipToSync" or "useSpeedToSync" or "useSkipToSync")) return;
        LoadSettings();
        Changed?.Invoke();
    }

    private void Set(string name, double value) => _appSettings.Set(name, value.ToString(CultureInfo.InvariantCulture), "syncPlay");

    private void Set(string name, bool value) => SetBool(name, value);

    private void SetBool(string name, bool value) => _appSettings.Set(name, value, "syncPlay");

    private double Number(string name, double fallback) => double.TryParse(_appSettings.Get(name, "syncPlay"), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private bool Bool(string name, bool fallback) => AppSettings.ToBoolean(_appSettings.Get(name, "syncPlay"), fallback);

    private static T? Deserialize<T>(JsonElement value) => value.ValueKind == JsonValueKind.Object ? JsonSerializer.Deserialize<T>(value.GetRawText(), JellyfinApi.Json) : default;

    private static string? Text(JsonElement value, string? property)
    {
        if (property is null) return value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();
        return value.TryGetProperty(property, out var text) ? text.GetString() : null;
    }

    private static void Run(Task task) => _ = IgnoreAsync(task);

    private static async Task IgnoreAsync(Task task)
    {
        try { await task; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        { AppLog.Warn($"[syncplay] operation failed: {ex.Message}"); }
    }
}
