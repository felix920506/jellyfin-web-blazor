using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>A Cast session as the sender SDK describes it (chrome.cast.Session).</summary>
public sealed record CastSessionInfo
{
    public string? SessionId { get; init; }

    /// <summary>session.receiver.friendlyName.</summary>
    public string? ReceiverName { get; init; }

    /// <summary>session.receiver.volume.level, 0..1; null when the receiver does not tell.</summary>
    public double? VolumeLevel { get; init; }

    public bool? Muted { get; init; }
}

/// <summary>
/// The JS boundary of the Chromecast player (<c>wwwroot/js/playback/cast.js</c>): the Google Cast
/// sender SDK (cast_sender.js, loaded on demand) and the chrome.cast session. Everything the player
/// decides is in <see cref="CastPlayer"/>; tests replace this interface with a fake.
/// </summary>
public interface ICastInterop
{
    /// <summary>The receiverListener of the ApiConfig: true when receivers are available.</summary>
    event Action<bool>? ReceiverAvailabilityChanged;

    /// <summary>The sessionListener of the ApiConfig: an existing session was joined (page reload).</summary>
    event Action<CastSessionInfo>? SessionJoined;

    /// <summary>The session's update listener with isAlive = true (e.g. the receiver volume changed).</summary>
    event Action<CastSessionInfo>? SessionUpdated;

    /// <summary>The session's update listener with isAlive = false: the session is gone.</summary>
    event Action? SessionEnded;

    /// <summary>A message of the receiver on the <c>urn:x-cast:com.connectsdk</c> namespace, as received.</summary>
    event Action<string>? MessageReceived;

    /// <summary>
    /// Port of CastSenderApi.load() and the wait for chrome.cast.isAvailable: loads the sender SDK
    /// once. False when the browser cannot use it (no <c>window.chrome</c>, the script did not load,
    /// or the SDK reported that the API is not available).
    /// </summary>
    Task<bool> LoadAsync();

    /// <summary>chrome.cast.initialize with a SessionRequest for the receiver application; false on error.</summary>
    Task<bool> InitializeAsync(string applicationId);

    /// <summary>chrome.cast.requestSession (the browser's device picker); null on a launch error or cancel.</summary>
    Task<CastSessionInfo?> RequestSessionAsync();

    /// <summary>session.sendMessage on the Jellyfin namespace.</summary>
    Task SendMessageAsync(string message);

    /// <summary>session.setReceiverVolumeLevel, 0..1.</summary>
    Task SetReceiverVolumeLevelAsync(double level);

    /// <summary>session.stop: ends the receiver application.</summary>
    Task StopSessionAsync();
}

/// <summary><see cref="ICastInterop"/> over <c>wwwroot/js/playback/cast.js</c>.</summary>
[AppService(typeof(ICastInterop))]
public sealed class JsCastInterop(IServiceProvider services) : ICastInterop, IDisposable
{
    private DotNetObjectReference<JsCastInterop>? _self;
    private Task<IJSObjectReference?>? _module;

    public event Action<bool>? ReceiverAvailabilityChanged;

    public event Action<CastSessionInfo>? SessionJoined;

    public event Action<CastSessionInfo>? SessionUpdated;

    public event Action? SessionEnded;

    public event Action<string>? MessageReceived;

    [JSInvokable]
    public void OnReceiverAvailability(bool available) => ReceiverAvailabilityChanged?.Invoke(available);

    [JSInvokable]
    public void OnSessionJoined(CastSessionInfo session) => SessionJoined?.Invoke(session);

    [JSInvokable]
    public void OnSessionUpdated(CastSessionInfo session) => SessionUpdated?.Invoke(session);

    [JSInvokable]
    public void OnSessionEnded() => SessionEnded?.Invoke();

    [JSInvokable]
    public void OnMessage(string message) => MessageReceived?.Invoke(message);

    public async Task<bool> LoadAsync()
    {
        var module = await ModuleAsync();
        return module is not null && await module.InvokeAsync<bool>("load", _self);
    }

    public async Task<bool> InitializeAsync(string applicationId)
    {
        var module = await ModuleAsync();
        return module is not null && await module.InvokeAsync<bool>("initialize", applicationId);
    }

    public async Task<CastSessionInfo?> RequestSessionAsync()
    {
        var module = await ModuleAsync();
        return module is null ? null : await module.InvokeAsync<CastSessionInfo?>("requestSession");
    }

    public Task SendMessageAsync(string message) => SendAsync("sendMessage", message);

    public Task SetReceiverVolumeLevelAsync(double level) => SendAsync("setReceiverVolumeLevel", level);

    public Task StopSessionAsync() => SendAsync("stopSession");

    public void Dispose() => _self?.Dispose();

    private Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync()
    {
        var js = services.GetService<IJSRuntime>();
        if (js is null)
        {
            return null;
        }

        _self = DotNetObjectReference.Create(this);
        return await js.InvokeAsync<IJSObjectReference?>("import", "./js/playback/cast.js");
    }

    private async Task SendAsync(string name, params object?[] arguments)
    {
        try
        {
            var module = await ModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync(name, arguments);
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or TaskCanceledException or JSDisconnectedException)
        {
            AppLog.Warn($"[chromecastPlayer] cast.{name} failed: {ex.Message}");
        }
    }
}
