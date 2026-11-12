using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <content>
/// Play targets: which player the commands go to. Port of the target functions of
/// playbackmanager.js (getTargets, trySetActivePlayer, setCurrentPlayerInternal, getPlayerInfo,
/// sendCommand, enableDisplayMirroring), of scripts/autocast.js and of
/// components/playback/remotecontrolautoplay.js.
/// </content>
public sealed partial class PlaybackManager
{
    /// <summary>The player name the original uses for "this browser"; never a target to pair with.</summary>
    public const string LocalTargetId = "localplayer";

    private const string AutoCastKey = "autocastPlayerId";
    private string? _currentPairingId;

    /// <summary>The target of the current player, or null without one.</summary>
    public PlayTarget? CurrentTarget { get; private set; }

    /// <summary>The current player when it keeps its own queue and state.</summary>
    private IRemotePlayer? Remote => CurrentPlayer as IRemotePlayer;

    /// <summary>Port of getPlayerInfo(): null without a current player.</summary>
    public PlayerInfo? GetPlayerInfo()
    {
        if (CurrentPlayer is not { } player)
        {
            return null;
        }

        var target = CurrentTarget;
        return new PlayerInfo
        {
            Name = player.Name,
            IsLocalPlayer = player.IsLocalPlayer,
            Id = target?.Id ?? player.Id,
            DeviceName = target?.DeviceName,
            PlayableMediaTypes = target?.PlayableMediaTypes ?? [],
            SupportedCommands = GetSupportedCommands(),
        };
    }

    /// <summary>Port of getTargets(): what the players that are not local offer, sorted by name.</summary>
    public async Task<IReadOnlyList<PlayTarget>> GetTargetsAsync()
    {
        var targets = new List<PlayTarget>();
        foreach (var player in _players.OfType<IRemotePlayer>())
        {
            try
            {
                targets.AddRange(await player.GetTargetsAsync());
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                AppLog.Warn($"[playback] the targets of {player.Name} could not be loaded: {ex.Message}");
            }
        }

        return targets
            .OrderBy(t => (t.IsLocalPlayer ? "0" : "1") + t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Port of trySetActivePlayer(): pairs with the target and makes its player the current one.
    /// The local target is ignored, as in the original (see <see cref="SetDefaultPlayerActive"/>).
    /// </summary>
    public async Task TrySetActivePlayerAsync(string playerName, PlayTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (playerName == LocalTargetId)
        {
            return;
        }

        var player = _players.FirstOrDefault(p => p.Name == playerName)
            ?? throw new InvalidOperationException("null player");
        if (_currentPairingId == target.Id)
        {
            return;
        }

        _currentPairingId = target.Id;
        _loading.Show();
        try
        {
            if (player is IRemotePlayer remote)
            {
                await remote.TryPairAsync(target);
            }

            SetCurrentPlayerInternal(player, target);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            AppLog.Warn($"[playback] pairing with {target.Name} failed: {ex.Message}");
            if (_currentPairingId == target.Id)
            {
                _currentPairingId = null;
            }
        }
        finally
        {
            _loading.Hide();
        }
    }

    /// <summary>Port of setDefaultPlayerActive(): leaves a remote target; playback goes to this device again.</summary>
    public void SetDefaultPlayerActive()
    {
        if (CurrentPlayer is null or { IsLocalPlayer: true })
        {
            return;
        }

        SetCurrentPlayerInternal(null, null);
    }

    /// <summary>Port of removeActivePlayer(): leaves the target when it belongs to the named player.</summary>
    public void RemoveActivePlayer(string name)
    {
        if (GetPlayerInfo()?.Name == name)
        {
            SetDefaultPlayerActive();
        }
    }

    /// <summary>Port of removeActiveTarget().</summary>
    public void RemoveActiveTarget(string id)
    {
        if (GetPlayerInfo()?.Id == id)
        {
            SetDefaultPlayerActive();
        }
    }

    /// <summary>Port of enableDisplayMirroring(): on unless switched off.</summary>
    public bool EnableDisplayMirroring
    {
        get => (_appSettings.Get("displaymirror") ?? string.Empty) != "0";
        set => _appSettings.Set("displaymirror", value ? "1" : "0");
    }

    /// <summary>Port of autocast.isEnabled(): the current target is the one to return to after signing in.</summary>
    public bool IsAutoCastEnabled
    {
        get
        {
            var stored = _browser.StorageGet(AutoCastKey);
            return !string.IsNullOrEmpty(stored) && GetPlayerInfo() is { } info && info.Id == stored;
        }
    }

    /// <summary>Port of autocast.enable().</summary>
    public void SetAutoCast(bool enabled)
    {
        if (enabled && GetPlayerInfo() is { } info)
        {
            _browser.StorageSet(AutoCastKey, info.Id);
        }
        else
        {
            _browser.StorageRemove(AutoCastKey);
        }
    }

    /// <summary>Port of autocast's onOpen(): returns to the remembered target when it is available.</summary>
    public async Task RestoreAutoCastTargetAsync()
    {
        var stored = _browser.StorageGet(AutoCastKey);
        if (string.IsNullOrEmpty(stored) || !_api.IsLoggedIn)
        {
            return;
        }

        var targets = await GetTargetsAsync();
        if (targets.FirstOrDefault(t => t.Id == stored) is { IsLocalPlayer: false } target)
        {
            await TrySetActivePlayerAsync(target.PlayerName, target);
        }
    }

    /// <summary>Port of displayContent(): asks the remote target to show an item.</summary>
    public Task DisplayContentAsync(string? itemName, string? itemId, string? itemType) =>
        Remote is { } remote
            ? remote.SendCommandAsync("DisplayContent", CommandArguments.Of(("ItemName", itemName), ("ItemId", itemId), ("ItemType", itemType)))
            : Task.CompletedTask;

    /// <summary>Ends the session of a target that supports "EndSession".</summary>
    public Task EndSessionAsync() => Remote?.EndSessionAsync() ?? Task.CompletedTask;

    /// <summary>
    /// Port of sendCommand(): the commands with a method of their own go there, so that they work
    /// for every player; everything else ("MoveUp", "DisplayMessage", ...) goes to a remote target.
    /// </summary>
    public async Task SendCommandAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
    {
        switch (name)
        {
            case "SetRepeatMode":
                SetRepeatMode(Text("RepeatMode") ?? PlayQueue.RepeatNone);
                break;
            case "SetShuffleQueue":
                SetQueueShuffleMode(Text("ShuffleMode") ?? PlayQueue.Sorted);
                break;
            case "VolumeUp":
                VolumeUp();
                break;
            case "VolumeDown":
                VolumeDown();
                break;
            case "Mute":
                SetMute(true);
                break;
            case "Unmute":
                SetMute(false);
                break;
            case "ToggleMute":
                ToggleMute();
                break;
            case "SetVolume":
                if (Number("Volume") is { } volume)
                {
                    SetVolume(volume);
                }

                break;
            case "SetAspectRatio":
                SetAspectRatio(Text("AspectRatio") ?? "auto");
                break;
            case "PlaybackRate":
                if (double.TryParse(Text("PlaybackRate"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rate))
                {
                    SetPlaybackRate(rate);
                }

                break;
            case "SetAudioStreamIndex":
                if (Number("Index") is { } audioIndex)
                {
                    await SetAudioStreamIndexAsync(audioIndex);
                }

                break;
            case "SetSubtitleStreamIndex":
                if (Number("Index") is { } subtitleIndex)
                {
                    await SetSubtitleStreamIndexAsync(subtitleIndex);
                }

                break;
            case "SetMaxStreamingBitrate":
                if (Number("Bitrate") is { } bitrate && Remote is null)
                {
                    await SetMaxStreamingBitrateAsync(enableAutomaticBitrateDetection: false, bitrate);
                }

                break;
            case "ToggleFullscreen":
                ToggleFullscreen();
                break;
            default:
                if (Remote is { } remote)
                {
                    await remote.SendCommandAsync(name, arguments);
                }

                break;
        }

        string? Text(string key) =>
            arguments is not null && arguments.TryGetValue(key, out var value)
                ? Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
                : null;

        int? Number(string key) =>
            int.TryParse(Text(key), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var number)
                ? number
                : null;
    }

    /// <summary>An item of the current user by id, or null when the server does not return one.</summary>
    public Task<BaseItemDto?> GetItemByIdAsync(string itemId) => GetItemAsync(itemId);

    /// <summary>The items of a remote queue, loaded like items to play. Port of the session player's updatePlaylist().</summary>
    internal async Task<List<BaseItemDto>> GetPlaylistItemsAsync(IReadOnlyList<string> ids)
    {
        var request = new PlayRequest(new PlayOptions { Ids = ids });
        var items = await GetItemsForPlaybackAsync(new ItemsQuery { ["ids"] = ids });
        return await TranslateItemsForPlaybackAsync(items, request);
    }

    /// <summary>
    /// Port of setCurrentPlayerInternal(): stores the player and its target, moves the state
    /// updates from the previous player to the new one and raises "playerchange" when the target
    /// is another one.
    /// </summary>
    private void SetCurrentPlayerInternal(IMediaPlayer? player, PlayTarget? target)
    {
        var previousPlayer = CurrentPlayer;
        var previousTarget = CurrentTarget;
        if (player is not null && target is null)
        {
            if (!player.IsLocalPlayer)
            {
                throw new InvalidOperationException("targetInfo cannot be null");
            }

            target = CreateLocalTarget(player);
        }

        _currentPairingId = null;
        if (ReferenceEquals(previousPlayer, player) && previousTarget?.Id == target?.Id)
        {
            return;
        }

        CurrentPlayer = player;
        CurrentTarget = target;

        (previousPlayer as IRemotePlayer)?.EndPlayerUpdates();
        (player as IRemotePlayer)?.BeginPlayerUpdates();

        if (target is not null && previousTarget is not null && target.Id == previousTarget.Id)
        {
            return;
        }

        Raise(PlaybackEvents.PlayerChange);

        if (previousPlayer is { IsLocalPlayer: true } && player is IRemotePlayer remote)
        {
            _ = HandleAsync(TransferPlaybackAsync(previousPlayer, remote));
        }
    }

    /// <summary>Port of createTarget() for a local player.</summary>
    private static PlayTarget CreateLocalTarget(IMediaPlayer player) => new()
    {
        Name = player.Name,
        Id = player.Id,
        PlayerName = player.Name,
        PlayableMediaTypes = new[] { "Audio", "Video", "Photo", "Book" }.Where(player.CanPlayMediaType).ToList(),
        IsLocalPlayer = player.IsLocalPlayer,
    };

    /// <summary>
    /// Port of remotecontrolautoplay.js: what plays here when a remote target is chosen continues
    /// there, at the same position.
    /// </summary>
    private async Task TransferPlaybackAsync(IMediaPlayer oldPlayer, IRemotePlayer newPlayer)
    {
        var state = GetPlayerState(oldPlayer);
        if (state.NowPlayingItem is not { } item)
        {
            return;
        }

        var ids = _playQueue.GetPlaylist().Select(IdOf).OfType<string>().ToList();
        if (ids.Count == 0 && IdOf(item) is { } id)
        {
            ids.Add(id);
        }

        var index = Math.Max(0, ids.IndexOf(IdOf(item) ?? string.Empty));
        _playNextAfterEnded = false;
        await oldPlayer.StopAsync(destroyPlayer: true);
        if (ReferenceEquals(CurrentPlayer, newPlayer))
        {
            await newPlayer.PlayAsync(new PlayOptions { Ids = ids, StartPositionTicks = state.PositionTicks, StartIndex = index });
        }
    }
}
