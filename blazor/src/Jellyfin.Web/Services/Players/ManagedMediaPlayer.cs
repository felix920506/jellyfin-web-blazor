using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

public interface IItemMediaPlayer
{
    bool CanPlayItem(BaseItemDto item);
}

public interface IUrlMediaPlayer
{
    bool CanPlayUrl(string url);
}

public interface IPlaylistMediaPlayer
{
    Task<StreamInfo> PlayItemsAsync(IReadOnlyList<BaseItemDto> items, ItemPlayOptions options);
}

/// <summary>Common IMediaPlayer adapter for players whose UI and rendering live in players.js.</summary>
public abstract class ManagedMediaPlayer : IMediaPlayer, IPlaylistMediaPlayer
{
    private PlayerSnapshot _snapshot = new() { Paused = true, Seekable = true };
    private string? _src;
    private StreamInfo? _stream;

    protected ManagedMediaPlayer(string kind, IPlayersInterop interop, JellyfinApi api)
    {
        Kind = kind;
        Interop = interop;
        Api = api;
        Interop.Event += OnInteropEvent;
    }

    protected string Kind { get; }
    protected IPlayersInterop Interop { get; }
    protected JellyfinApi Api { get; }

    public abstract string Name { get; }
    public abstract string Id { get; }
    public bool IsLocalPlayer => true;
    public virtual int Priority => 1;
    public event Action<IMediaPlayer, PlayerEvent>? Event;

    public abstract bool CanPlayMediaType(string? mediaType);
    public virtual bool CanPlayItem(BaseItemDto item) => false;
    public virtual bool CanPlayUrl(string url) => false;

    public Task<JsonObject> GetDeviceProfileAsync(BaseItemDto? item, bool isRetry = false) =>
        Task.FromResult(new JsonObject());

    public async Task<StreamInfo> PlayItemsAsync(IReadOnlyList<BaseItemDto> items, ItemPlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item is required.", nameof(items));
        }
        var item = items[Math.Clamp(options.StartIndex, 0, items.Count - 1)];
        var data = items.Select(ToPlayerItem).ToList();
        var startIndex = Math.Clamp(options.StartIndex, 0, data.Count - 1);
        var stream = CreateStreamInfo(item, options);
        _stream = stream;
        var snapshot = await Interop.OpenAsync(new PlayerOpenRequest(
            Kind,
            data,
            startIndex,
            options.StartPositionTicks ?? 0,
            options.Fullscreen,
            SlideshowIntervalSeconds));
        ApplySnapshot(snapshot);
        return stream;
    }

    public async Task PlayAsync(StreamInfo streamInfo)
    {
        _stream = streamInfo;
        _src = streamInfo.Url;
        var data = new[] { ToPlayerItem(streamInfo.Item) };
        var snapshot = await Interop.OpenAsync(new PlayerOpenRequest(
            Kind,
            data,
            0,
            streamInfo.PlayerStartPositionTicks ?? 0,
            streamInfo.Fullscreen,
            SlideshowIntervalSeconds));
        ApplySnapshot(snapshot);
    }

    public virtual async Task StopAsync(bool destroyPlayer)
    {
        await Interop.StopAsync(Kind, destroyPlayer);
        if (destroyPlayer)
        {
            Destroy();
        }
    }

    public virtual void Destroy()
    {
        _src = null;
        _stream = null;
        _snapshot = new PlayerSnapshot { Paused = true, Seekable = true };
    }

    public string? CurrentSrc => _src;
    public double? CurrentTimeMs => _src is null ? null : (_snapshot.CurrentTime ?? 0) * 1000;
    public void SetCurrentTime(double milliseconds) => Interop.SetCurrentTime(Kind, milliseconds / 1000);
    public double? DurationMs => _src is null || _snapshot.Duration is null ? null : _snapshot.Duration.Value * 1000;
    public bool? Seekable => _src is null ? null : _snapshot.Seekable;
    public bool Paused => _src is not null && _snapshot.Paused;
    public void Pause() => Interop.Pause(Kind);
    public void Unpause() => Interop.Unpause(Kind);
    public double PlaybackRate => _snapshot.PlaybackRate;
    public void SetPlaybackRate(double value)
    {
        // Readers do not expose playback speed; this remains a no-op like the original plugins.
    }

    public int Volume => 100;
    public void SetVolume(int value) { }
    public bool IsMuted => false;
    public void SetMute(bool mute) { }
    public IReadOnlyList<BufferedRange> BufferedRanges => [];
    public virtual bool Supports(string feature) => feature == "PlaybackRate" && Kind == "youtube";
    public bool CanSetAudioStreamIndex => false;
    public void SetAudioStreamIndex(int index) { }
    public void SetSubtitleStreamIndex(int index) { }
    public int? SecondarySubtitleStreamIndex => null;
    public void SetSecondarySubtitleStreamIndex(int index) { }
    public double SubtitleOffset => 0;
    public void SetSubtitleOffset(double seconds) { }
    public bool SupportsSubtitleOffset => false;
    public string AspectRatio => "auto";
    public void SetAspectRatio(string value) { }
    public bool IsFullscreen => _snapshot.Fullscreen;
    public void ToggleFullscreen() => Interop.ToggleFullscreen(Kind);
    public bool IsPictureInPictureEnabled => false;
    public void SetPictureInPictureEnabled(bool enabled) { }
    public Task<PlayerStats?> GetStatsAsync() => Task.FromResult<PlayerStats?>(null);
    public void OnNavigatedToOsd() { }

    protected virtual int SlideshowIntervalSeconds => 5;

    protected virtual PlayerItemData ToPlayerItem(BaseItemDto item)
    {
        var id = item.Id?.ToString("N");
        var path = item.Path ?? string.Empty;
        var url = UrlFor(item) ?? path;
        var imageUrl = id is null ? null : Api.GetUrl($"Items/{id}/Images/Primary", ("ApiKey", Api.AccessToken));
        var downloadUrl = id is null ? null : Api.GetUrl($"Items/{id}/Download", ("ApiKey", Api.AccessToken));
        return new PlayerItemData(id, item.Name ?? string.Empty, item.MediaType.ToApiString() ?? string.Empty, url, imageUrl, downloadUrl, path);
    }

    protected abstract string? UrlFor(BaseItemDto item);

    protected StreamInfo CreateStreamInfo(BaseItemDto item, ItemPlayOptions options) => new()
    {
        Url = UrlFor(item),
        PlayMethod = PlayMethods.DirectPlay,
        PlayerStartPositionTicks = options.StartPositionTicks,
        Item = item,
        MediaType = item.MediaType.ToApiString(),
        Title = item.Name,
        Fullscreen = options.Fullscreen,
    };

    protected void Raise(string name) => Event?.Invoke(this, new PlayerEvent(name));

    private void ApplySnapshot(PlayerSnapshot snapshot)
    {
        _snapshot = snapshot;
        _src = _stream?.Url ?? _src ?? Kind;
    }

    private void OnInteropEvent(string kind, string name, PlayerSnapshot snapshot)
    {
        if (!string.Equals(kind, Kind, StringComparison.Ordinal))
        {
            return;
        }

        _snapshot = snapshot;
        if (name == "stopped")
        {
            _src = null;
        }

        Raise(name);
    }

}
