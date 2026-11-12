using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>The DOM work of the details page (<c>wwwroot/js/details.js</c>).</summary>
public interface IDetailsInterop
{
    /// <summary>
    /// Turns the overview of an item into HTML: Markdown with raw HTML allowed, sanitised, links
    /// opening in a new tab. Null when the libraries could not be loaded.
    /// </summary>
    ValueTask<string?> RenderOverviewAsync(string markdown);

    /// <summary>Whether a clamped text is cut off.</summary>
    ValueTask<bool> IsClampedAsync(ElementReference element);

    /// <summary>Scrolls the scroller of a section so that the card of an item is at its start.</summary>
    ValueTask ScrollToCardAsync(ElementReference section, string itemId);

    /// <summary>
    /// Starts lazy loading for the images (<c>.lazy[data-src]</c>) below the parent of an element:
    /// the logo and the header backdrop of the page.
    /// </summary>
    ValueTask LoadSiblingImagesAsync(ElementReference element);

    /// <summary>Saves a file of the server under the given name.</summary>
    ValueTask DownloadAsync(string url, string? filename);
}

/// <summary><see cref="IDetailsInterop"/> over the <c>js/details.js</c> module, loaded on first use.</summary>
[AppService(typeof(IDetailsInterop))]
public sealed class DetailsInterop(IJSRuntime js, UserSettings settings) : IDetailsInterop
{
    private Task<IJSObjectReference?>? _module;

    public async ValueTask LoadSiblingImagesAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("loadSiblingImages", element, settings.EnableBlurhash, settings.EnableFastFadein);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element is gone with its page.
        }
    }

    public async ValueTask<string?> RenderOverviewAsync(string markdown)
    {
        try
        {
            var module = await GetModuleAsync();
            return module is null ? null : await module.InvokeAsync<string?>("renderOverview", markdown);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Rendering the overview failed: " + exception.Message);
            return null;
        }
    }

    public async ValueTask<bool> IsClampedAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            return module is not null && await module.InvokeAsync<bool>("isClamped", element);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element is gone with its page.
            return false;
        }
    }

    public async ValueTask ScrollToCardAsync(ElementReference section, string itemId)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("scrollToCard", section, itemId);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            // The element is gone with its page.
        }
    }

    public async ValueTask DownloadAsync(string url, string? filename)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("download", url, filename);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Starting the download failed: " + exception.Message);
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/details.js");
}
