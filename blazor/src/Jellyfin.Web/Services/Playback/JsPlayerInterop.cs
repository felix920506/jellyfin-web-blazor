using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// <see cref="IPlayerInterop"/> over <c>wwwroot/js/playback/player.js</c>. The JS runtime is resolved
/// on first use so that the service can be constructed where there is none (plain unit tests).
/// </summary>
[AppService(typeof(IPlayerInterop))]
public sealed class JsPlayerInterop(IServiceProvider services) : IPlayerInterop, IDisposable
{
    private DotNetObjectReference<JsPlayerInterop>? _self;
    private Task<IJSObjectReference?>? _module;

    public event Action<string, string, PlayerSnapshot, string?>? PlayerEvent;

    public event Action<string, double?>? MediaSessionAction;

    [JSInvokable]
    public void OnPlayerEvent(string kind, string name, PlayerSnapshot state, string? errorType) =>
        PlayerEvent?.Invoke(kind, name, state, errorType);

    [JSInvokable]
    public void OnMediaSessionAction(string action, double? seekTime) => MediaSessionAction?.Invoke(action, seekTime);

    public async Task<PlayerLoadResult> LoadAsync(string kind, PlayerLoadRequest request)
    {
        var module = await ModuleAsync();
        if (module is null)
        {
            return new PlayerLoadResult { Ok = false, ErrorType = MediaErrors.PlayerError };
        }

        return await module.InvokeAsync<PlayerLoadResult>("load", kind, request);
    }

    public async Task StopAsync(string kind, bool destroy, bool fade)
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync("stop", kind, destroy, fade);
        }
    }

    public void Destroy(string kind) => Send("destroy", kind);

    public void Pause(string kind) => Send("pause", kind);

    public void Unpause(string kind) => Send("unpause", kind);

    public void SetCurrentTime(string kind, double seconds) => Send("setCurrentTime", kind, seconds);

    public void SetVolume(string kind, double elementVolume) => Send("setVolume", kind, elementVolume);

    public void SetMute(string kind, bool mute) => Send("setMute", kind, mute);

    public void SetPlaybackRate(string kind, double rate) => Send("setPlaybackRate", kind, rate);

    public void SetAspectRatio(string value) => Send("setAspectRatio", value);

    public void SetAudioTrack(int position) => Send("setAudioTrack", position);

    public void SetSubtitleCues(IReadOnlyList<SubtitleCue>? cues) => Send("setSubtitleCues", cues);

    public void SetSecondarySubtitleCues(IReadOnlyList<SubtitleCue>? cues) => Send("setSecondarySubtitleCues", cues);

    public void SetSubtitleRenderer(SubtitleRendererRequest? request, bool secondary) => Send("setSubtitleRenderer", request, secondary);

    public void SetSubtitleRendererOffset(double seconds, bool secondary) => Send("setSubtitleRendererOffset", seconds, secondary);

    public void ToggleFullscreen() => Send("toggleFullscreen");

    public void SetPictureInPicture(bool enabled) => Send("setPictureInPicture", enabled);

    public void NavigatedToOsd() => Send("navigatedToOsd");

    public async Task<PlayerStats?> GetStatsAsync(string kind)
    {
        var module = await ModuleAsync();
        return module is null ? null : await module.InvokeAsync<PlayerStats?>("getStats", kind);
    }

    public void SetMediaSession(MediaSessionInfo? info) => Send("setMediaSession", info);

    public void Dispose() => _self?.Dispose();

    private Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync()
    {
        var js = services.GetService<IJSRuntime>();
        if (js is null)
        {
            return null;
        }

        var module = await js.InvokeAsync<IJSObjectReference?>("import", "./js/playback/player.js");
        if (module is not null)
        {
            _self = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", _self);
        }

        return module;
    }

    private void Send(string name, params object?[] arguments) => _ = SendAsync(name, arguments);

    private async Task SendAsync(string name, object?[] arguments)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync(name, arguments);
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException)
        {
            AppLog.Warn($"[playback] player.{name} failed: {ex.Message}");
        }
    }
}

/// <summary><see cref="IMediaCapabilityProbe"/> over <c>wwwroot/js/playback/capabilities.js</c>.</summary>
[AppService(typeof(IMediaCapabilityProbe))]
public sealed class JsMediaCapabilityProbe(IServiceProvider services) : IMediaCapabilityProbe
{
    private Task<MediaCapabilities>? _pending;

    public MediaCapabilities? Current { get; private set; }

    public Task<MediaCapabilities> GetAsync() => _pending ??= ProbeAsync();

    private async Task<MediaCapabilities> ProbeAsync()
    {
        MediaCapabilities? result = null;
        var js = services.GetService<IJSRuntime>();
        if (js is not null)
        {
            try
            {
                var module = await js.InvokeAsync<IJSObjectReference?>("import", "./js/playback/capabilities.js");
                if (module is not null)
                {
                    result = await module.InvokeAsync<MediaCapabilities?>("probe", MediaProbeTypes.Video, MediaProbeTypes.Audio);
                }
            }
            catch (JSException ex)
            {
                AppLog.Warn($"[playback] capability probe failed: {ex.Message}");
            }
        }

        Current = result ?? new MediaCapabilities();
        return Current;
    }
}
