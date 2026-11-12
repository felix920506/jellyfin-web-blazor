using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>Receives the callbacks of <c>livetv.js</c>.</summary>
public sealed class GuideWatcher(Func<string, Task> onProgramClick)
{
    /// <summary>A programme cell was clicked or activated with the keyboard.</summary>
    [JSInvokable]
    public Task OnProgramClick(string programId) => onProgramClick(programId);
}

/// <summary>
/// The DOM work of the TV guide (<c>wwwroot/js/livetv.js</c>): scrolling the time header with the
/// programme grid, sliding programme names into view, sizing the guide to the window and reporting
/// clicks on the programme cells, which are plain markup.
/// </summary>
public interface ILiveTvInterop
{
    /// <summary>Starts the guide below <paramref name="root"/>; dispose the result to stop it.</summary>
    ValueTask<IAsyncDisposable> AttachGuideAsync(ElementReference root, Func<string, Task> onProgramClick);

    /// <summary>
    /// Tells the guide its rows changed; with <paramref name="scrollToFraction"/> (0..1 of the day
    /// shown) the grid scrolls sideways to that time.
    /// </summary>
    ValueTask RefreshGuideAsync(ElementReference root, double? scrollToFraction = null);

    /// <summary>How far the grid is scrolled sideways, as a fraction of its width.</summary>
    ValueTask<double> GetScrollFractionAsync(ElementReference root);
}

/// <summary><see cref="ILiveTvInterop"/> over the <c>js/livetv.js</c> module, loaded on first use.</summary>
[AppService(typeof(ILiveTvInterop))]
public sealed class LiveTvInterop(IJSRuntime js) : ILiveTvInterop
{
    private Task<IJSObjectReference?>? _module;

    public async ValueTask<IAsyncDisposable> AttachGuideAsync(ElementReference root, Func<string, Task> onProgramClick)
    {
        var registration = new Registration(this, root, DotNetObjectReference.Create(new GuideWatcher(onProgramClick)));
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("attach", root, registration.Reference);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Live TV: starting the guide failed: " + exception.Message);
        }

        return registration;
    }

    public async ValueTask RefreshGuideAsync(ElementReference root, double? scrollToFraction = null)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("refresh", root, scrollToFraction);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Live TV: updating the guide failed: " + exception.Message);
        }
    }

    public async ValueTask<double> GetScrollFractionAsync(ElementReference root)
    {
        try
        {
            var module = await GetModuleAsync();
            return module is null ? 0 : await module.InvokeAsync<double>("scrollFraction", root);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Live TV: reading the guide's position failed: " + exception.Message);
            return 0;
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/livetv.js");

    private async ValueTask DetachAsync(ElementReference root)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("detach", root);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The page is gone; nothing left to stop.
        }
    }

    private sealed class Registration(LiveTvInterop owner, ElementReference root, DotNetObjectReference<GuideWatcher> reference) : IAsyncDisposable
    {
        public DotNetObjectReference<GuideWatcher> Reference { get; } = reference;

        public async ValueTask DisposeAsync()
        {
            await owner.DetachAsync(root);
            Reference.Dispose();
        }
    }
}
