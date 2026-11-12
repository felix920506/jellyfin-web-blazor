using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>Browser helpers of the admin dashboard (wwwroot/js/dashboard.js).</summary>
public interface IDashboardInterop
{
    /// <summary>Loads the module, after which the synchronous helpers work.</summary>
    ValueTask LoadAsync();

    /// <summary>
    /// <c>Intl.RelativeTimeFormat</c>: "5 minutes ago" for (-5, "minute"). Null until the module
    /// is loaded or when the browser cannot format it.
    /// </summary>
    string? RelativeTime(double value, string unit, string? locale);

    /// <summary><c>Intl.NumberFormat</c> with a unit: "5 minutes" for (5, "minute"). Null like <see cref="RelativeTime"/>.</summary>
    string? Duration(double value, string unit, string? locale);

    /// <summary>"yesterday", "today" or "tomorrow" for -1, 0 or 1 in the locale. Null like <see cref="RelativeTime"/>.</summary>
    string? RelativeDay(int days, string? locale);

    /// <summary>Copies text to the clipboard; false when the browser refused.</summary>
    ValueTask<bool> CopyTextAsync(string text);

    /// <summary>Offers text as a file download.</summary>
    ValueTask DownloadTextAsync(string text, string fileName);

    /// <summary>Whether the window is scrolled to the end of the page.</summary>
    ValueTask<bool> IsAtBottomAsync();

    ValueTask ScrollToBottomAsync();
}

[AppService(typeof(IDashboardInterop))]
public sealed class DashboardInterop(IJSRuntime js) : IDashboardInterop
{
    private Task<IJSObjectReference>? _loading;
    private IJSInProcessObjectReference? _module;

    public async ValueTask LoadAsync() => await ModuleAsync();

    public string? RelativeTime(double value, string unit, string? locale) => Format("relativeTime", value, unit, locale);

    public string? Duration(double value, string unit, string? locale) => Format("duration", value, unit, locale);

    public string? RelativeDay(int days, string? locale)
    {
        try
        {
            return _module?.Invoke<string?>("relativeDay", days, locale);
        }
        catch (JSException ex)
        {
            AppLog.Warn("[dashboard] relativeDay failed: " + ex.Message);
            return null;
        }
    }

    public async ValueTask<bool> CopyTextAsync(string text)
    {
        var module = await ModuleAsync();
        return module is not null && await module.InvokeAsync<bool>("copyText", text);
    }

    public async ValueTask DownloadTextAsync(string text, string fileName)
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync("downloadText", text, fileName);
        }
    }

    public async ValueTask<bool> IsAtBottomAsync()
    {
        var module = await ModuleAsync();
        return module is not null && await module.InvokeAsync<bool>("isAtBottom");
    }

    public async ValueTask ScrollToBottomAsync()
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync("scrollToBottom");
        }
    }

    private string? Format(string function, double value, string unit, string? locale)
    {
        try
        {
            return _module?.Invoke<string?>(function, value, unit, locale);
        }
        catch (JSException ex)
        {
            AppLog.Warn($"[dashboard] {function} failed: {ex.Message}");
            return null;
        }
    }

    private async Task<IJSObjectReference?> ModuleAsync()
    {
        try
        {
            var module = await (_loading ??= js.InvokeAsync<IJSObjectReference>("import", "./js/dashboard.js").AsTask());
            _module ??= module as IJSInProcessObjectReference;
            return module;
        }
        catch (JSException ex)
        {
            AppLog.Warn("[dashboard] loading js/dashboard.js failed: " + ex.Message);
            return null;
        }
    }
}
