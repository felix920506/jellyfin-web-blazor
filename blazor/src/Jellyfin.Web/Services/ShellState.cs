using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// State pages share with the app shell: the page title and header appearance.
/// Port of the page-facing parts of scripts/libraryMenu.js.
/// </summary>
public sealed class ShellState(IBrowser browser, JellyfinApi api)
{
    public event Action? Changed;

    /// <summary>The title shown in the header on small screens; null shows the server name.</summary>
    public string? Title { get; private set; }

    /// <summary>The header is drawn over the page content (login, details backdrop).</summary>
    public bool TransparentHeader { get; private set; }

    /// <summary>Sets the page title (header and browser tab). Null or "-" resets to the default.</summary>
    public void SetTitle(string? title)
    {
        title = title is null or "-" ? null : title;
        browser.SetTitle(string.IsNullOrEmpty(title) ? api.ServerName ?? "Jellyfin" : title);
        if (Title != title)
        {
            Title = title;
            Changed?.Invoke();
        }
    }

    public void SetTransparentHeader(bool transparent)
    {
        if (TransparentHeader != transparent)
        {
            TransparentHeader = transparent;
            Changed?.Invoke();
        }
    }
}
