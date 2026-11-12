using Jellyfin.Web.Services.Playback;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

[AppService(typeof(IPlayersInterop))]
public sealed class PlayersInterop(IServiceProvider services) : IPlayersInterop, IDisposable
{
    private DotNetObjectReference<PlayersInterop>? _self;
    private Task<IJSObjectReference?>? _module;

    public event Action<string, string, PlayerSnapshot>? Event;

    [JSInvokable]
    public void OnPlayerEvent(string kind, string name, PlayerSnapshot snapshot) => Event?.Invoke(kind, name, snapshot);

    public async Task<PlayerSnapshot> OpenAsync(PlayerOpenRequest request)
    {
        var module = await ModuleAsync();
        return module is null
            ? new PlayerSnapshot { Paused = false, Seekable = true }
            : await module.InvokeAsync<PlayerSnapshot>("open", request);
    }

    public async Task StopAsync(string kind, bool destroy)
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync("stop", kind, destroy);
        }
    }

    public void Pause(string kind) => Send("pause", kind);
    public void Unpause(string kind) => Send("unpause", kind);
    public void Next(string kind) => Send("next", kind);
    public void Previous(string kind) => Send("previous", kind);
    public void SetCurrentTime(string kind, double seconds) => Send("setCurrentTime", kind, seconds);
    public void ToggleFullscreen(string kind) => Send("toggleFullscreen", kind);

    public void Dispose() => _self?.Dispose();

    private Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync()
    {
        var js = services.GetService<IJSRuntime>();
        if (js is null)
        {
            return null;
        }

        var module = await js.InvokeAsync<IJSObjectReference?>("import", "./js/players/players.js");
        if (module is not null)
        {
            _self = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("init", _self);
        }

        return module;
    }

    private void Send(string name, params object?[] args) => _ = SendAsync(name, args);

    private async Task SendAsync(string name, object?[] args)
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync(name, args);
        }
    }
}
