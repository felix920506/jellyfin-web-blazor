using System.Globalization;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback.AirPlay;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Transport, tracks, volume, rate and the queue: what the playback UI calls.</summary>
public sealed partial class PlaybackManager
{
    /// <summary>The rates of the playback speed menu.</summary>
    public static readonly IReadOnlyList<double> PlaybackRates = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 3.5, 4];

    /// <summary>The aspect ratio modes: id and translation key.</summary>
    public static readonly IReadOnlyList<(string Id, string NameKey)> AspectRatios =
    [
        ("auto", "Auto"),
        ("cover", "AspectRatioCover"),
        ("fill", "AspectRatioFill"),
    ];

    /// <summary>The position in the current item, in ticks.</summary>
    public long CurrentTimeTicks => CurrentPlayer is { } player ? GetCurrentTicks(player) : 0;

    /// <summary>The run time of the current item in ticks, or null when unknown.</summary>
    public long? DurationTicks => CurrentPlayer is { } player ? GetDurationTicks(player) : null;

    public IReadOnlyList<BufferedRange> BufferedRanges => CurrentPlayer?.BufferedRanges ?? [];

    public int Volume => CurrentPlayer?.Volume ?? 100;

    public bool IsMuted => CurrentPlayer?.IsMuted == true;

    public double PlaybackRate => CurrentPlayer?.PlaybackRate ?? 1;

    public string AspectRatio => CurrentPlayer?.AspectRatio ?? "auto";

    public string RepeatMode => Remote is { } remote ? remote.RepeatMode : _playQueue.RepeatMode;

    public string ShuffleMode => Remote is { } remote ? remote.ShuffleMode : _playQueue.ShuffleMode;

    public int? AudioStreamIndex => CurrentPlayer switch
    {
        IRemotePlayer remote => remote.AudioStreamIndex,
        { } player => Data(player).AudioStreamIndex,
        _ => null,
    };

    public int? SubtitleStreamIndex => CurrentPlayer switch
    {
        IRemotePlayer remote => remote.SubtitleStreamIndex,
        { } player => Data(player).SubtitleStreamIndex,
        _ => null,
    };

    public int? SecondarySubtitleStreamIndex => CurrentPlayer switch
    {
        IRemotePlayer remote => remote.SecondarySubtitleStreamIndex,
        { } player => Data(player).SecondarySubtitleStreamIndex,
        _ => null,
    };

    public double SubtitleOffset => CurrentPlayer?.SubtitleOffset ?? 0;

    public bool SupportsSubtitleOffset => CurrentPlayer?.SupportsSubtitleOffset == true;

    public bool IsFullscreen => CurrentPlayer?.IsFullscreen == true;

    public bool IsPictureInPictureEnabled => CurrentPlayer?.IsPictureInPictureEnabled == true;

    public bool IsAirPlayEnabled => CurrentPlayer?.IsAirPlayEnabled == true;

    public string? CurrentPlaylistItemId => Remote is { } remote ? remote.CurrentPlaylistItemId : _playQueue.CurrentPlaylistItemId;

    public int CurrentPlaylistIndex => Remote is { } remote ? RemotePlaylistIndex(remote) : _playQueue.GetCurrentPlaylistIndex();

    /// <summary>Whether the current stream can be positioned.</summary>
    public bool CanSeek() => CurrentPlayer switch
    {
        IRemotePlayer remote => remote.Seekable == true,
        { } player => (CurrentMediaSource?.RunTimeTicks ?? 0) > 0 || CanSeek(player),
        _ => false,
    };

    public IReadOnlyList<BaseItemDto> GetPlaylist() => Remote is { } remote ? remote.Playlist : _playQueue.GetPlaylist();

    /// <summary>Port of audioTracks(): the audio streams of the current source, in menu order.</summary>
    public IReadOnlyList<MediaStream> AudioTracks() => Remote is { } remote ? remote.AudioTracks : SortTracks("Audio");

    /// <summary>Port of subtitleTracks().</summary>
    public IReadOnlyList<MediaStream> SubtitleTracks() => Remote is { } remote ? remote.SubtitleTracks : SortTracks("Subtitle");

    /// <summary>Whether the current primary subtitle can be paired with an external secondary track.</summary>
    public bool HasSecondarySubtitleSupport =>
        CurrentPlayer?.Supports("SecondarySubtitles") == true
        && SubtitleStreamIndex is not null
        && SecondarySubtitleTracks().Any(stream => stream.Index != SubtitleStreamIndex);

    public IReadOnlyList<MediaStream> SecondarySubtitleTracks() =>
        SubtitleTracks().Where(stream => stream.Index != SubtitleStreamIndex && SupportsSecondarySubtitle(stream)).ToList();

    /// <summary>
    /// Port of getSupportedCommands(): what this client can do for a local player, what the target
    /// announced for a remote one.
    /// </summary>
    public IReadOnlyList<string> GetSupportedCommands() =>
        Remote is not null ? CurrentTarget?.SupportedCommands ?? [] : LocalSupportedCommands(CurrentPlayer);

    private static List<string> LocalSupportedCommands(IMediaPlayer? current)
    {
        var list = new List<string>
        {
            "GoHome", "GoToSettings", "VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute", "SetVolume",
            "SetAudioStreamIndex", "SetSubtitleStreamIndex", "SetMaxStreamingBitrate", "DisplayContent",
            "GoToSearch", "DisplayMessage", "SetRepeatMode", "SetShuffleQueue", "PlayMediaSource", "PlayTrailers",
        };

        // Browsers without the Fullscreen API (iPhone) fall back to the element's own fullscreen.
        list.Add("ToggleFullscreen");

        if (current is { } player)
        {
            foreach (var feature in new[] { "PictureInPicture", AirPlayFeatures.Command, "SetBrightness", "SetAspectRatio", "PlaybackRate", "SecondarySubtitles" })
            {
                if (player.Supports(feature))
                {
                    list.Add(feature);
                }
            }
        }

        return list;
    }

    /// <inheritdoc />
    public Task PauseAsync()
    {
        CurrentPlayer?.Pause();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task UnpauseAsync()
    {
        CurrentPlayer?.Unpause();
        return Task.CompletedTask;
    }

    /// <summary>Opens the AirPlay picker or exits the selected target when the browser supports it.</summary>
    public void ToggleAirPlay()
    {
        if (CurrentPlayer is { } player && player.Supports("AirPlay"))
        {
            player.SetAirPlayEnabled(!player.IsAirPlayEnabled);
        }
    }

    /// <inheritdoc />
    public Task PlayPauseAsync()
    {
        if (Remote is { } remote)
        {
            return remote.PlayPauseAsync();
        }

        if (CurrentPlayer is { } player)
        {
            if (player.Paused)
            {
                player.Unpause();
            }
            else
            {
                player.Pause();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (CurrentPlayer is { } player)
        {
            if (player.IsLocalPlayer)
            {
                _playNextAfterEnded = false;
            }

            await player.StopAsync(destroyPlayer: true);
        }
    }

    /// <inheritdoc />
    public async Task NextTrackAsync()
    {
        if (Remote is { } remote)
        {
            await remote.NextTrackAsync();
            return;
        }

        if (CurrentPlayer is not { } player || _playQueue.GetNextItemInfo() is not { } next)
        {
            return;
        }

        await PlayInternalAsync(
            next.Item,
            GetOrCreateItemPlayOptions(next.Item),
            () => SetPlaylistState(next.Item.PlaylistItemId, next.Index),
            GetPreviousSource(player));
    }

    /// <inheritdoc />
    public async Task PreviousTrackAsync()
    {
        if (Remote is { } remote)
        {
            await remote.PreviousTrackAsync();
            return;
        }

        if (CurrentPlayer is not { } player)
        {
            return;
        }

        var newIndex = _playQueue.GetCurrentPlaylistIndex() - 1;
        var playlist = _playQueue.GetPlaylist();
        if (newIndex < 0 || newIndex >= playlist.Count)
        {
            return;
        }

        var newItem = playlist[newIndex];
        var options = GetOrCreateItemPlayOptions(newItem);
        options.StartPositionTicks = 0;
        await PlayInternalAsync(
            newItem,
            options,
            () => SetPlaylistState(newItem.PlaylistItemId, newIndex),
            GetPreviousSource(player));
    }

    /// <inheritdoc />
    public async Task SeekAsync(long positionTicks)
    {
        if (Remote is { } remote)
        {
            await remote.SeekAsync(Math.Max(0, positionTicks));
        }
        else if (CurrentPlayer is { } player)
        {
            await ChangeStreamAsync(player, Math.Max(0, positionTicks));
        }
    }

    public Task SeekRelativeAsync(long offsetTicks) =>
        CurrentPlayer is { } player ? SeekAsync(GetCurrentTicks(player) + offsetTicks) : Task.CompletedTask;

    public Task SeekPercentAsync(double percent)
    {
        var ticks = (long)((DurationTicks ?? 0) * (percent / 100));
        return SeekAsync(ticks);
    }

    public Task SeekMsAsync(long milliseconds) => SeekAsync(milliseconds * 10000);

    /// <summary>Port of fastForward(): forward by the user's skip length.</summary>
    public Task FastForwardAsync() => SeekRelativeAsync(_userSettings.SkipForwardLength * 10000L);

    /// <summary>Port of rewind(): back by the user's skip length.</summary>
    public Task RewindAsync() => SeekRelativeAsync(-(_userSettings.SkipBackLength * 10000L));

    /// <summary>Port of nextChapter(): the next chapter, or the next item when there is none.</summary>
    public Task NextChapterAsync()
    {
        if (CurrentPlayer is not { } player)
        {
            return Task.CompletedTask;
        }

        var ticks = GetCurrentTicks(player);
        var chapter = (CurrentItem?.Chapters ?? []).FirstOrDefault(c => c.StartPositionTicks > ticks);
        return chapter is not null ? SeekAsync(chapter.StartPositionTicks ?? 0) : NextTrackAsync();
    }

    /// <summary>Port of previousChapter(): ten seconds back counts as "this chapter".</summary>
    public Task PreviousChapterAsync()
    {
        if (CurrentPlayer is not { } player)
        {
            return Task.CompletedTask;
        }

        var ticks = GetCurrentTicks(player) - 100000000;

        // The first item has nothing to go back to: stay in it.
        if (_playQueue.GetCurrentPlaylistIndex() == 0)
        {
            ticks = Math.Max(ticks, 0);
        }

        var chapter = (CurrentItem?.Chapters ?? []).LastOrDefault(c => c.StartPositionTicks <= ticks);
        return chapter is not null ? SeekAsync(chapter.StartPositionTicks ?? 0) : PreviousTrackAsync();
    }

    public void SetVolume(int value) => CurrentPlayer?.SetVolume(Math.Clamp(value, 0, 100));

    public void VolumeUp()
    {
        if (Remote is { } remote)
        {
            remote.VolumeUp();
        }
        else
        {
            SetVolume(Math.Min(Volume + 2, 100));
        }
    }

    public void VolumeDown()
    {
        if (Remote is { } remote)
        {
            remote.VolumeDown();
        }
        else
        {
            SetVolume(Math.Max(Volume - 2, 0));
        }
    }

    public void SetMute(bool mute) => CurrentPlayer?.SetMute(mute);

    public void ToggleMute()
    {
        if (Remote is { } remote)
        {
            remote.ToggleMute();
        }
        else
        {
            SetMute(!IsMuted);
        }
    }

    /// <summary>Port of setPlaybackRate(): the rate is remembered for the browser session.</summary>
    public void SetPlaybackRate(double value)
    {
        if (CurrentPlayer is { } player && player.Supports("PlaybackRate"))
        {
            player.SetPlaybackRate(value);

            // Save the playback rate in the browser session, to restore it when playback restarts.
            _browser.StorageSet("playbackRateSpeed", value.ToString(CultureInfo.InvariantCulture), session: true);
        }
    }

    public void IncreasePlaybackRate() => StepPlaybackRate(1);

    public void DecreasePlaybackRate() => StepPlaybackRate(-1);

    public void SetAspectRatio(string value) => CurrentPlayer?.SetAspectRatio(value);

    /// <summary>Port of toggleAspectRatio(): cycles auto, cover, fill.</summary>
    public void ToggleAspectRatio()
    {
        if (CurrentPlayer is not { } player)
        {
            return;
        }

        var current = player.AspectRatio;
        var index = 0;
        for (var i = 0; i < AspectRatios.Count; i++)
        {
            if (AspectRatios[i].Id == current)
            {
                index = i;
                break;
            }
        }

        SetAspectRatio(AspectRatios[(index + 1) % AspectRatios.Count].Id);
    }

    public void ToggleFullscreen() => CurrentPlayer?.ToggleFullscreen();

    public void TogglePictureInPicture()
    {
        if (CurrentPlayer is { } player)
        {
            player.SetPictureInPictureEnabled(!player.IsPictureInPictureEnabled);
        }
    }

    public Task<PlayerStats?> GetStatsAsync() => CurrentPlayer?.GetStatsAsync() ?? Task.FromResult<PlayerStats?>(null);

    /// <summary>Port of setRepeatMode().</summary>
    public void SetRepeatMode(string value)
    {
        if (Remote is { } remote)
        {
            remote.SetRepeatMode(value);
            return;
        }

        _playQueue.SetRepeatMode(value);
        RaisePlayerEvent(PlaybackEvents.RepeatModeChange);
    }

    /// <summary>Port of setQueueShuffleMode().</summary>
    public void SetQueueShuffleMode(string value)
    {
        if (Remote is { } remote)
        {
            remote.SetQueueShuffleMode(value);
            return;
        }

        _playQueue.SetShuffleMode(value);
        RaisePlayerEvent(PlaybackEvents.ShuffleQueueModeChange);
    }

    /// <summary>Port of toggleQueueShuffleMode().</summary>
    public void ToggleQueueShuffleMode()
    {
        if (Remote is { } remote)
        {
            remote.SetQueueShuffleMode(remote.ShuffleMode == PlayQueue.Shuffled ? PlayQueue.Sorted : PlayQueue.Shuffled);
            return;
        }

        _playQueue.ToggleShuffleMode();
        RaisePlayerEvent(PlaybackEvents.ShuffleQueueModeChange);
    }

    /// <summary>Port of clearQueue().</summary>
    public void ClearQueue(bool clearCurrentItem = false)
    {
        if (Remote is not null)
        {
            // The queue of a remote target is its own.
            return;
        }

        _playQueue.ClearPlaylist(clearCurrentItem);
        RaisePlayerEvent(PlaybackEvents.PlaylistItemRemove);
    }

    /// <summary>Port of setCurrentPlaylistItem(): plays an item of the queue.</summary>
    public async Task SetCurrentPlaylistItemAsync(string playlistItemId)
    {
        if (Remote is { } remote)
        {
            await remote.SetCurrentPlaylistItemAsync(playlistItemId);
            return;
        }

        var playlist = _playQueue.GetPlaylist();
        for (var i = 0; i < playlist.Count; i++)
        {
            if (playlist[i].PlaylistItemId != playlistItemId)
            {
                continue;
            }

            var item = playlist[i];
            var index = i;
            await PlayInternalAsync(item, GetOrCreateItemPlayOptions(item), () => SetPlaylistState(playlistItemId, index), prevSource: null);
            return;
        }
    }

    /// <summary>Port of removeFromPlaylist().</summary>
    public async Task RemoveFromPlaylistAsync(IReadOnlyCollection<string> playlistItemIds)
    {
        ArgumentNullException.ThrowIfNull(playlistItemIds);
        if (Remote is not null)
        {
            // Not supported by the session player (the original's removeFromPlaylist() does nothing).
            return;
        }

        var result = _playQueue.RemoveFromPlaylist(playlistItemIds);
        if (result.Result == "empty")
        {
            await StopAsync();
            return;
        }

        RaisePlayerEvent(PlaybackEvents.PlaylistItemRemove);
        if (result.IsCurrentIndex && _playQueue.GetPlaylist() is { Count: > 0 } playlist && playlist[0].PlaylistItemId is { } first)
        {
            await SetCurrentPlaylistItemAsync(first);
        }
    }

    /// <summary>Port of movePlaylistItem().</summary>
    public void MovePlaylistItem(string playlistItemId, int newIndex)
    {
        if (Remote is { } remote)
        {
            _ = HandleAsync(remote.MovePlaylistItemAsync(playlistItemId, newIndex));
            return;
        }

        var result = _playQueue.MovePlaylistItem(playlistItemId, newIndex);
        if (result.Result != "noop")
        {
            RaisePlayerEvent(PlaybackEvents.PlaylistItemMove);
        }
    }

    /// <summary>Port of setAudioStreamIndex().</summary>
    public async Task SetAudioStreamIndexAsync(int index)
    {
        if (Remote is { } remote)
        {
            remote.SetAudioStreamIndex(index);
            return;
        }

        if (CurrentPlayer is not { } player || Data(player).StreamInfo is not { } streamInfo)
        {
            return;
        }

        var data = Data(player);
        if (streamInfo.PlayMethod == PlayMethods.Transcode || !player.CanSetAudioStreamIndex)
        {
            await ChangeStreamAsync(player, GetCurrentTicks(player), new StreamChangeParams { AudioStreamIndex = index });
            data.AudioStreamIndex = index;
        }
        else
        {
            // The player can switch tracks itself when it can decode the track.
            var profile = await player.GetDeviceProfileAsync(streamInfo.Item);
            if (streamInfo.MediaSource is { } mediaSource && IsAudioStreamSupported(mediaSource, index, profile))
            {
                player.SetAudioStreamIndex(index);
                data.AudioStreamIndex = index;
            }
            else
            {
                await ChangeStreamAsync(player, GetCurrentTicks(player), new StreamChangeParams { AudioStreamIndex = index });
                data.AudioStreamIndex = index;
            }
        }

        Raise(PlaybackEvents.MediaStreamsChange);
    }

    /// <summary>
    /// Port of setSubtitleStreamIndex(): text tracks delivered as files are switched in the player;
    /// everything else (burned in) needs a new stream. -1 turns subtitles off.
    /// </summary>
    public async Task SetSubtitleStreamIndexAsync(int index)
    {
        if (Remote is { } remote)
        {
            remote.SetSubtitleStreamIndex(index);
            return;
        }

        if (CurrentPlayer is not { } player || Data(player).StreamInfo is not { } streamInfo)
        {
            return;
        }

        var data = Data(player);
        var currentStream = GetSubtitleStream(data.SubtitleStreamIndex);
        var newStream = GetSubtitleStream(index);
        if (currentStream is null && newStream is null)
        {
            return;
        }

        var selectedTrackElementIndex = -1;
        var transcoding = streamInfo.PlayMethod == PlayMethods.Transcode;
        var ticks = GetCurrentTicks(player);

        if (currentStream is not null && newStream is null)
        {
            var delivery = GetDeliveryMethod(currentStream);
            if (delivery == "Encode" || (delivery == "Embed" && transcoding))
            {
                // Need to change the transcoded stream to remove subs.
                await ChangeStreamAsync(player, ticks, new StreamChangeParams { SubtitleStreamIndex = -1 });
            }
        }
        else if (currentStream is null && newStream is not null)
        {
            var delivery = GetDeliveryMethod(newStream);
            if (delivery == "External" || (delivery == "Embed" && !transcoding))
            {
                selectedTrackElementIndex = index;
            }
            else
            {
                // Need to change the transcoded stream to add subs.
                await ChangeStreamAsync(player, ticks, new StreamChangeParams { SubtitleStreamIndex = index });
            }
        }
        else if (currentStream is not null && newStream is not null)
        {
            // Switching tracks: if the new track is delivered as a file, the player shows it.
            var delivery = GetDeliveryMethod(newStream);
            if (delivery == "External" || (delivery == "Embed" && !transcoding))
            {
                selectedTrackElementIndex = index;

                // But the old one may have to come out of the picture.
                if (GetDeliveryMethod(currentStream) is not ("External" or "Embed"))
                {
                    await ChangeStreamAsync(player, ticks, new StreamChangeParams { SubtitleStreamIndex = -1 });
                }
            }
            else
            {
                // Need to change the transcoded stream to add subs.
                await ChangeStreamAsync(player, ticks, new StreamChangeParams { SubtitleStreamIndex = index });
            }
        }

        player.SetSubtitleStreamIndex(selectedTrackElementIndex);

        data.SubtitleStreamIndex = index;
        if (index < 0 || !SupportsSecondarySubtitle(index))
        {
            await SetSecondarySubtitleStreamIndexAsync(-1);
        }
        Raise(PlaybackEvents.MediaStreamsChange);
    }

    public async Task SetSecondarySubtitleStreamIndexAsync(int index)
    {
        if (Remote is { } remote)
        {
            remote.SetSecondarySubtitleStreamIndex(index);
            return;
        }

        if (CurrentPlayer is not { } player || !player.Supports("SecondarySubtitles") || Data(player).StreamInfo is null)
        {
            return;
        }

        var stream = index < 0 ? null : GetSubtitleStream(index);
        if (stream is not null && !SupportsSecondarySubtitle(stream))
        {
            return;
        }

        player.SetSecondarySubtitleStreamIndex(index);
        Data(player).SecondarySubtitleStreamIndex = index;
        Raise(PlaybackEvents.MediaStreamsChange);
    }

    public void SetSubtitleOffset(double seconds)
    {
        if (CurrentPlayer is not { } player || !player.SupportsSubtitleOffset)
        {
            return;
        }

        player.SetSubtitleOffset(Math.Clamp(seconds, -60, 60));
        Raise(PlaybackEvents.MediaStreamsChange);
    }

    private bool SupportsSecondarySubtitle(int index) =>
        GetSubtitleStream(index) is { } stream && SupportsSecondarySubtitle(stream);

    private static bool SupportsSecondarySubtitle(MediaStream stream) =>
        stream.DeliveryMethod == MediaStream_DeliveryMethod.External
        && stream.Codec?.ToLowerInvariant() is not ("ssa" or "ass" or "pgssub" or "dvdsub" or "vobsub");

    /// <summary>Port of changeAudioStream(): the next audio track, wrapping around.</summary>
    public Task ChangeAudioStreamAsync()
    {
        var tracks = AudioTracks();
        if (tracks.Count <= 1)
        {
            return Task.CompletedTask;
        }

        var current = AudioStreamIndex;
        var position = -1;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Index == current)
            {
                position = i;
                break;
            }
        }

        var next = tracks[(position + 1) % tracks.Count];
        return SetAudioStreamIndexAsync(next.Index ?? 0);
    }

    /// <summary>Port of changeSubtitleStream(): the next subtitle track, then off.</summary>
    public Task ChangeSubtitleStreamAsync()
    {
        var tracks = SubtitleTracks();
        if (tracks.Count == 0)
        {
            return Task.CompletedTask;
        }

        var current = SubtitleStreamIndex;
        var position = -1;
        for (var i = 0; i < tracks.Count; i++)
        {
            if (tracks[i].Index == current)
            {
                position = i;
                break;
            }
        }

        var next = position + 1 < tracks.Count ? tracks[position + 1].Index ?? -1 : -1;
        return SetSubtitleStreamIndexAsync(next);
    }

    /// <summary>Port of isAudioStreamSupported(): can the player decode the track in this container?</summary>
    internal static bool IsAudioStreamSupported(MediaSourceInfo mediaSource, int index, JsonObject deviceProfile)
    {
        var stream = (mediaSource.MediaStreams ?? []).FirstOrDefault(s => s.Type.ToApiString() == "Audio" && s.Index == index);
        if (stream is null)
        {
            return false;
        }

        var container = (mediaSource.Container ?? string.Empty).ToLowerInvariant();
        var codec = (stream.Codec ?? string.Empty).ToLowerInvariant();
        if (codec.Length == 0)
        {
            return false;
        }

        return Profiles(deviceProfile, "DirectPlayProfiles").Any(p =>
            (string?)p["Type"] == "Video"
            && IncludesAny(((string?)p["Container"] ?? string.Empty).ToLowerInvariant(), container)
            && IncludesAny(((string?)p["AudioCodec"] ?? string.Empty).ToLowerInvariant(), codec));
    }

    /// <summary>Port of the original's setPlaylistState(): marks the queue item that plays.</summary>
    private void SetPlaylistState(string? playlistItemId, int index)
    {
        if (playlistItemId is not null)
        {
            _playQueue.SetPlaylistState(playlistItemId);
        }
        else
        {
            _playQueue.SetPlaylistIndex(index);
        }
    }

    /// <summary>The item's play options; created (and kept) when the item has none, as the original does.</summary>
    private ItemPlayOptions GetOrCreateItemPlayOptions(BaseItemDto item)
    {
        if (!_itemOptions.TryGetValue(item, out var options))
        {
            options = new ItemPlayOptions();
            _itemOptions.AddOrUpdate(item, options);
        }

        return options;
    }

    /// <summary>The position of the item that plays in the queue of a remote target, or -1.</summary>
    private static int RemotePlaylistIndex(IRemotePlayer remote)
    {
        var playlist = remote.Playlist;
        var current = remote.CurrentPlaylistItemId;
        for (var i = 0; i < playlist.Count; i++)
        {
            if (playlist[i].PlaylistItemId == current)
            {
                return i;
            }
        }

        return -1;
    }

    private void RaisePlayerEvent(string name)
    {
        if (CurrentPlayer is { } player)
        {
            OnPlayerEvent(player, new PlayerEvent(name));
        }
        else
        {
            Raise(name);
        }
    }

    private void StepPlaybackRate(int step)
    {
        if (CurrentPlayer is not { } player)
        {
            return;
        }

        var current = player.PlaybackRate;
        var index = 0;
        var best = double.MaxValue;
        for (var i = 0; i < PlaybackRates.Count; i++)
        {
            var distance = Math.Abs(PlaybackRates[i] - current);
            if (distance < best)
            {
                best = distance;
                index = i;
            }
        }

        SetPlaybackRate(PlaybackRates[Math.Clamp(index + step, 0, PlaybackRates.Count - 1)]);
    }

    private MediaStream? GetSubtitleStream(int? index) =>
        index is null or -1
            ? null
            : (CurrentMediaSource?.MediaStreams ?? []).FirstOrDefault(s => s.Type.ToApiString() == "Subtitle" && s.Index == index);

    private static string GetDeliveryMethod(MediaStream stream) =>
        stream.DeliveryMethod.ToApiString() is { Length: > 0 } method
            ? method
            : stream.IsExternal == true ? "External" : "Embed";

    /// <summary>Port of sortTracks(): external last, forced and default first, then by index.</summary>
    private List<MediaStream> SortTracks(string type) =>
        (CurrentMediaSource?.MediaStreams ?? [])
            .Where(s => s.Type.ToApiString() == type)
            .OrderBy(s => s.IsExternal == true)
            .ThenByDescending(s => s.IsForced == true)
            .ThenByDescending(s => s.IsDefault == true)
            .ThenBy(s => s.Index ?? 0)
            .ToList();
}
