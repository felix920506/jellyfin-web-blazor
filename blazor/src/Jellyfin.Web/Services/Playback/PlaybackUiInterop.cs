using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// The browser input the playback UI needs beyond Blazor's own events (wwwroot/js/playback/ui.js):
/// the document-level listeners of the video OSD and the hover position of a slider.
/// </summary>
public interface IPlaybackUiInterop
{
    /// <summary>
    /// Starts forwarding the OSD's document input to the target's <c>OnKeyDown</c>,
    /// <c>OnActivity</c>, <c>OnPointerMoved</c> and <c>OnMouseDownChanged</c> methods.
    /// </summary>
    Task AttachOsdAsync<T>(DotNetObjectReference<T> target)
        where T : class;

    void DetachOsd();

    /// <summary>Starts forwarding a slider's hover position to the target's <c>OnHover</c>/<c>OnHoverEnd</c>.</summary>
    Task AttachSliderAsync<T>(ElementReference input, DotNetObjectReference<T> target)
        where T : class;

    void DetachSlider(ElementReference input);
}

/// <summary><see cref="IPlaybackUiInterop"/> over <c>wwwroot/js/playback/ui.js</c>.</summary>
[AppService(typeof(IPlaybackUiInterop))]
public sealed class JsPlaybackUiInterop(IServiceProvider services) : IPlaybackUiInterop
{
    private Task<IJSObjectReference?>? _module;

    public Task AttachOsdAsync<T>(DotNetObjectReference<T> target)
        where T : class => SendAsync("attachOsd", target);

    public void DetachOsd() => _ = SendAsync("detachOsd");

    public Task AttachSliderAsync<T>(ElementReference input, DotNetObjectReference<T> target)
        where T : class => SendAsync("attachSlider", input, target);

    public void DetachSlider(ElementReference input) => _ = SendAsync("detachSlider", input);

    private Task<IJSObjectReference?> ModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync()
    {
        var js = services.GetService<IJSRuntime>();
        return js is null ? null : await js.InvokeAsync<IJSObjectReference?>("import", "./js/playback/ui.js");
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
            AppLog.Warn($"[playback] ui.{name} failed: {ex.Message}");
        }
    }
}
