using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// The tabs of the current page, shown in the app bar by <c>AppBarTabs</c>. A page declares them
/// with the <c>PageTabs</c> component; nothing else needs to touch this service.
/// </summary>
[AppService]
public sealed class PageTabsState(IBrowser browser)
{
    /// <summary>Set on the body while a page shows tabs in the app bar.</summary>
    public const string BodyClass = "jf-with-tabs";

    private object? _owner;
    private Action<int>? _select;

    /// <summary>Raised when the tabs or the selected tab change.</summary>
    public event Action? Changed;

    /// <summary>The tab names, already translated; empty when the page has no visible tabs.</summary>
    public IReadOnlyList<string> Tabs { get; private set; } = [];

    public int SelectedIndex { get; private set; }

    public bool Visible => Tabs.Count > 0;

    /// <summary>Declares the tabs of the page <paramref name="owner"/> (any object that identifies it).</summary>
    public void Set(object owner, IReadOnlyList<string> tabs, int selectedIndex, Action<int> select)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(tabs);

        var wasVisible = Visible;
        var changed = !ReferenceEquals(_owner, owner) || SelectedIndex != selectedIndex || !Tabs.SequenceEqual(tabs);
        _owner = owner;
        _select = select;
        if (!changed)
        {
            return;
        }

        Tabs = tabs.ToArray();
        SelectedIndex = selectedIndex;
        Apply(wasVisible);
    }

    /// <summary>Removes the tabs when <paramref name="owner"/> still owns them (call when the page goes away).</summary>
    public void Clear(object owner)
    {
        if (!ReferenceEquals(_owner, owner))
        {
            return;
        }

        var wasVisible = Visible;
        _owner = null;
        _select = null;
        Tabs = [];
        SelectedIndex = 0;
        Apply(wasVisible);
    }

    /// <summary>Selects a tab on behalf of the user (a click in the app bar).</summary>
    public void Select(int index)
    {
        if (index >= 0 && index < Tabs.Count && index != SelectedIndex)
        {
            _select?.Invoke(index);
        }
    }

    private void Apply(bool wasVisible)
    {
        if (wasVisible != Visible)
        {
            browser.ToggleClass(DocumentTarget.Body, BodyClass, Visible);
        }

        Changed?.Invoke();
    }
}
