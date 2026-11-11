using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>What the scroll buttons of a horizontal scroller should look like.</summary>
public readonly record struct ScrollButtonState(bool Visible, bool CanScrollBack, bool CanScrollForward);

/// <summary>Receives scroll state changes of a watched scroller (called from <c>items.js</c>).</summary>
public sealed class ScrollerWatcher(Action<ScrollButtonState> onChange)
{
    [JSInvokable]
    public void OnScrollState(bool visible, bool canScrollBack, bool canScrollForward) =>
        onChange(new ScrollButtonState(visible, canScrollBack, canScrollForward));
}

/// <summary>
/// The little DOM work the item components cannot do in C# (<c>wwwroot/js/items.js</c>):
/// lazy image loading with blurhash placeholders, and measuring and moving horizontal scrollers.
/// </summary>
public interface IItemsInterop
{
    /// <summary>
    /// Starts lazy loading for every <c>.lazy[data-src]</c> element below <paramref name="container"/>
    /// that is not loading yet, and draws the blurhash canvases found there.
    /// </summary>
    ValueTask LoadImagesAsync(ElementReference container);

    /// <summary>
    /// Reports whether the scroller overflows and can scroll either way, now and whenever that
    /// changes. Dispose the returned registration to stop.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchScrollerAsync(ElementReference scroller, Action<ScrollButtonState> onChange);

    /// <summary>Scrolls a scroller by one page of items (the scroll buttons).</summary>
    ValueTask ScrollPageAsync(ElementReference scroller, bool forward);
}

/// <summary><see cref="IItemsInterop"/> over the <c>js/items.js</c> module, loaded on first use.</summary>
[AppService(typeof(IItemsInterop))]
public sealed class ItemsInterop(IJSRuntime js, UserSettings settings) : IItemsInterop
{
    private Task<IJSObjectReference?>? _module;

    public async ValueTask LoadImagesAsync(ElementReference container)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("loadImages", container, settings.EnableBlurhash, settings.EnableFastFadein);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Lazy image loading failed: " + exception.Message);
        }
    }

    public async ValueTask<IAsyncDisposable> WatchScrollerAsync(ElementReference scroller, Action<ScrollButtonState> onChange)
    {
        var registration = new Registration(this, scroller, DotNetObjectReference.Create(new ScrollerWatcher(onChange)));
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("watchScroller", scroller, registration.Reference);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Watching a scroller failed: " + exception.Message);
        }

        return registration;
    }

    public async ValueTask ScrollPageAsync(ElementReference scroller, bool forward)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("scrollPage", scroller, forward);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Scrolling failed: " + exception.Message);
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/items.js");

    private async ValueTask UnwatchAsync(ElementReference scroller)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("unwatchScroller", scroller);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element is gone with its page; nothing left to stop.
        }
    }

    private sealed class Registration(ItemsInterop owner, ElementReference scroller, DotNetObjectReference<ScrollerWatcher> reference) : IAsyncDisposable
    {
        public DotNetObjectReference<ScrollerWatcher> Reference { get; } = reference;

        public async ValueTask DisposeAsync()
        {
            await owner.UnwatchAsync(scroller);
            Reference.Dispose();
        }
    }
}
