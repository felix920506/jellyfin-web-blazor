using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>
/// What the item dialogs need from the browser (<c>wwwroot/js/itemdialogs.js</c>): Web Share, the
/// clipboard, file downloads and opening a file picker. Tests substitute a fake.
/// </summary>
public interface IItemDialogsInterop
{
    /// <summary>
    /// Whether the browser has <c>navigator.share</c> (<c>appHost.supports('sharing')</c>); read once at
    /// startup, false until then.
    /// </summary>
    bool CanShare { get; }

    /// <summary><c>navigator.share({ title, text, url })</c>; false when the user cancelled or the browser refused.</summary>
    ValueTask<bool> ShareAsync(string? title, string? text, string url);

    /// <summary>Copies text to the clipboard (<c>scripts/clipboard.js</c>); false when the browser refused.</summary>
    ValueTask<bool> CopyTextAsync(string text);

    /// <summary>Starts one browser download per URL (<c>scripts/multiDownload.js</c>).</summary>
    ValueTask DownloadAsync(IReadOnlyList<string> urls);

    /// <summary>Clicks an element, e.g. a hidden file input to open its file picker.</summary>
    ValueTask ClickAsync(ElementReference element);
}

/// <summary><see cref="IItemDialogsInterop"/> over the <c>js/itemdialogs.js</c> module.</summary>
[AppService(typeof(IItemDialogsInterop))]
public sealed class ItemDialogsInterop(IJSRuntime js, AppHost appHost) : IItemDialogsInterop, IStartupTask
{
    private Task<IJSObjectReference?>? _module;

    public bool CanShare { get; private set; }

    /// <summary>Reads whether the browser can share: the item menu needs the answer without waiting.</summary>
    public async Task StartAsync()
    {
        try
        {
            var module = await GetModuleAsync();
            CanShare = module is not null && await module.InvokeAsync<bool>("canShare");
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Loading the item dialogs module failed: " + exception.Message);
        }
    }

    public async ValueTask<bool> ShareAsync(string? title, string? text, string url)
    {
        try
        {
            var module = await GetModuleAsync();
            return module is not null && await module.InvokeAsync<bool>("share", new ShareData(title, text, url));
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Sharing failed: " + exception.Message);
            return false;
        }
    }

    public async ValueTask<bool> CopyTextAsync(string text)
    {
        try
        {
            var module = await GetModuleAsync();
            return module is not null && await module.InvokeAsync<bool>("copyText", text);
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Copying to the clipboard failed: " + exception.Message);
            return false;
        }
    }

    public async ValueTask DownloadAsync(IReadOnlyList<string> urls)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("download", urls, appHost.Browser.Ios);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Starting a download failed: " + exception.Message);
        }
    }

    public async ValueTask ClickAsync(ElementReference element)
    {
        try
        {
            var module = await GetModuleAsync();
            if (module is not null)
            {
                await module.InvokeVoidAsync("click", element);
            }
        }
        catch (Exception exception) when (IsInteropFailure(exception))
        {
            AppLog.Warn("Opening the file picker failed: " + exception.Message);
        }
    }

    private static bool IsInteropFailure(Exception exception) =>
        exception is JSException or JSDisconnectedException or InvalidOperationException or TaskCanceledException;

    private Task<IJSObjectReference?> GetModuleAsync() => _module ??= ImportAsync();

    private async Task<IJSObjectReference?> ImportAsync() =>
        await js.InvokeAsync<IJSObjectReference?>("import", "./js/itemdialogs.js");

    // The module drops null members: navigator.share would share them as the text "null".
    private sealed record ShareData(string? Title, string? Text, string Url);
}
