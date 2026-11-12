using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Services;

/// <summary>Browser helpers of the session, wizard and user settings pages (wwwroot/js/sessionui.js).</summary>
public interface ISessionInterop
{
    /// <summary>
    /// Renders the login disclaimer the way the original client does: markdown-it (raw HTML
    /// allowed) sanitised by DOMPurify, links opening in a new tab. Returns safe HTML.
    /// </summary>
    ValueTask<string> RenderDisclaimerAsync(string markdown);
    ValueTask<string> RenderMarkdownAsync(string markdown);

    /// <summary>Clicks an element, e.g. a hidden file input to open the file picker.</summary>
    ValueTask ClickAsync(ElementReference element);
}

[AppService(typeof(ISessionInterop))]
public sealed class SessionInterop(IJSRuntime js) : ISessionInterop
{
    private Task<IJSObjectReference>? _module;

    public async ValueTask<string> RenderDisclaimerAsync(string markdown)
    {
        var module = await ModuleAsync();
        return module is null ? string.Empty : await module.InvokeAsync<string>("renderDisclaimer", markdown) ?? string.Empty;
    }

    public async ValueTask<string> RenderMarkdownAsync(string markdown)
    {
        var module = await ModuleAsync();
        return module is null ? string.Empty : await module.InvokeAsync<string>("renderMarkdown", markdown) ?? string.Empty;
    }

    public async ValueTask ClickAsync(ElementReference element)
    {
        var module = await ModuleAsync();
        if (module is not null)
        {
            await module.InvokeVoidAsync("click", element);
        }
    }

    private Task<IJSObjectReference> ModuleAsync() =>
        _module ??= js.InvokeAsync<IJSObjectReference>("import", "./js/sessionui.js").AsTask();
}
