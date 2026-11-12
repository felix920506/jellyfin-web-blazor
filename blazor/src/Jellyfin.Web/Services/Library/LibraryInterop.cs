using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>Receives the callbacks of <c>library.js</c>.</summary>
public sealed class LibraryWatcher(Action<string?> onEvent)
{
    /// <summary>A card was long-pressed or ctrl-clicked: start multi-select with the item of this id.</summary>
    [JSInvokable]
    public void OnSelectionGesture(string? id) => onEvent(id);

    /// <summary>The watched sentinel element scrolled into view.</summary>
    [JSInvokable]
    public void OnVisible() => onEvent(null);
}

/// <summary>
/// The DOM work of the library pages that C# cannot do (<c>wwwroot/js/library.js</c>): the gestures that start
/// multi-select, the "load more" sentinel of endless lists, and focusing the search field.
/// </summary>
public interface ILibraryInterop
{
    /// <summary>
    /// Watches the document for the gestures that start multi-select on a card (<c>multiSelect.js</c>): a long
    /// press (550 ms, cancelled by moving 5 pixels), the context-menu event of touch browsers, and a
    /// ctrl/meta-click. Containers marked <c>data-multiselect="false"</c> are left alone.
    /// </summary>
    ValueTask<IAsyncDisposable> WatchSelectionGesturesAsync(Action<string?> onGesture);

    /// <summary>Calls <paramref name="onVisible"/> whenever the element comes within <paramref name="rootMargin"/> of the viewport.</summary>
    ValueTask<IAsyncDisposable> WatchVisibilityAsync(ElementReference sentinel, string rootMargin, Action onVisible);

    /// <summary>Moves the keyboard focus to an element.</summary>
    ValueTask FocusAsync(ElementReference element);
}

/// <summary><see cref="ILibraryInterop"/> over the <c>js/library.js</c> module, loaded on first use.</summary>
[AppService(typeof(ILibraryInterop))]
public sealed class LibraryInterop(IJSRuntime js) : ILibraryInterop
{
    private Task<IJSObjectReference?>? _module;
    private int _nextId;

    public ValueTask<IAsyncDisposable> WatchSelectionGesturesAsync(Action<string?> onGesture) =>
        WatchAsync("watchSelectionGestures", new LibraryWatcher(onGesture));

    public ValueTask<IAsyncDisposable> WatchVisibilityAsync(ElementReference sentinel, string rootMargin, Action onVisible) =>
        WatchAsync("watchVisibility", new LibraryWatcher(_ => onVisible()), sentinel, rootMargin);

    public async ValueTask FocusAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("focus", element);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Focusing an element failed: " + exception.Message);
        }
    }

    private async ValueTask<IAsyncDisposable> WatchAsync(string function, LibraryWatcher watcher, params object?[] arguments)
    {
        var registration = new Registration(this, ++_nextId, DotNetObjectReference.Create(watcher));
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync(function, [registration.Id, registration.Reference, .. arguments]);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Library interop failed: " + exception.Message);
        }

        return registration;
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/library.js");

    private async ValueTask UnwatchAsync(int id)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("unwatch", id);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The page is gone; nothing left to stop.
        }
    }

    private sealed class Registration(LibraryInterop owner, int id, DotNetObjectReference<LibraryWatcher> reference) : IAsyncDisposable
    {
        public int Id { get; } = id;

        public DotNetObjectReference<LibraryWatcher> Reference { get; } = reference;

        public async ValueTask DisposeAsync()
        {
            await owner.UnwatchAsync(Id);
            Reference.Dispose();
        }
    }
}
