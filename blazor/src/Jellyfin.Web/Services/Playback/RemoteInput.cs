using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// What a remote control does to the page itself (wwwroot/js/playback/remoteinput.js): the focus
/// moves of the navigation commands, "Select" and "SendString".
/// </summary>
public interface IRemoteInput
{
    /// <summary>Moves the focus to the nearest element in the direction: "up", "down", "left" or "right".</summary>
    void MoveFocus(string direction);

    /// <summary>Presses the focused element.</summary>
    void Select();

    /// <summary>Puts the text into the focused input.</summary>
    void SendText(string text);
}

/// <summary><see cref="IRemoteInput"/> over <c>wwwroot/js/playback/remoteinput.js</c>.</summary>
[AppService(typeof(IRemoteInput))]
public sealed class JsRemoteInput(IServiceProvider services) : IRemoteInput
{
    private Task<IJSObjectReference?>? _module;

    public void MoveFocus(string direction) => _ = SendAsync("moveFocus", direction);

    public void Select() => _ = SendAsync("select");

    public void SendText(string text) => _ = SendAsync("sendText", text);

    private Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync()
    {
        var js = services.GetService<IJSRuntime>();
        return js is null ? null : await js.InvokeAsync<IJSObjectReference?>("import", "./js/playback/remoteinput.js");
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
            AppLog.Warn($"[playback] remoteinput.{name} failed: {ex.Message}");
        }
    }
}
