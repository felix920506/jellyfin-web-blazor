using System.Globalization;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Tests.Support;

/// <summary>In-memory <see cref="IBrowser"/>. Navigation raises <see cref="HashChanged"/> synchronously.</summary>
public sealed class FakeBrowser : IBrowser
{
    private readonly List<string> _history = [string.Empty];
    private int _index;

    public BrowserEnvironment Environment { get; set; } = new()
    {
        UserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36",
        Href = "http://localhost/web/index.html",
        Origin = "http://localhost"
    };

    public double? ConnectionDownlinkMax { get; set; }

    public Dictionary<string, string> LocalStorage { get; } = [];

    public Dictionary<string, string> SessionStorage { get; } = [];

    /// <summary>Classes currently set, as "html:name" / "body:name".</summary>
    public HashSet<string> Classes { get; } = [];

    /// <summary>Attributes currently set, keyed "html:name" / "body:name".</summary>
    public Dictionary<string, string?> Attributes { get; } = [];

    public Dictionary<string, string> Stylesheets { get; } = [];

    public Dictionary<string, bool> StylesheetEnabled { get; } = [];

    public Dictionary<string, string?> StyleTexts { get; } = [];

    public List<(string Url, string? Target)> OpenedUrls { get; } = [];

    public string Title { get; private set; } = string.Empty;

    public string ThemeColor { get; private set; } = string.Empty;

    public int ReloadCount { get; private set; }

    public (double X, double Y) ScrollPosition { get; private set; }

    /// <summary>Minutes east of UTC reported by <see cref="UtcOffsetMinutes"/>.</summary>
    public int OffsetMinutes { get; set; }

    public string Hash => _history[_index];

    public event Action<string>? HashChanged;

    public event Action? Resized;

    public int ViewportWidth { get; private set; } = 1920;

    public string? StorageGet(string key, bool session = false) =>
        (session ? SessionStorage : LocalStorage).TryGetValue(key, out var value) ? value : null;

    public void StorageSet(string key, string value, bool session = false) => (session ? SessionStorage : LocalStorage)[key] = value;

    public void StorageRemove(string key, bool session = false) => (session ? SessionStorage : LocalStorage).Remove(key);

    public void Navigate(string hash, bool replace = false)
    {
        hash = hash.TrimStart('#');
        if (hash == Hash)
        {
            return;
        }

        if (replace)
        {
            _history[_index] = hash;
        }
        else
        {
            _history.RemoveRange(_index + 1, _history.Count - _index - 1);
            _history.Add(hash);
            _index++;
        }

        HashChanged?.Invoke(hash);
    }

    public void Back()
    {
        if (_index > 0)
        {
            _index--;
            HashChanged?.Invoke(Hash);
        }
    }

    public void Reload() => ReloadCount++;

    public void OpenUrl(string url, string? target = null) => OpenedUrls.Add((url, target));

    public void ToggleClass(DocumentTarget target, string className, bool on)
    {
        var key = Key(target, className);
        if (on)
        {
            Classes.Add(key);
        }
        else
        {
            Classes.Remove(key);
        }
    }

    public void SetAttribute(DocumentTarget target, string name, string? value) => Attributes[Key(target, name)] = value;

    public void SetTitle(string title) => Title = title;

    public void SetThemeColor(string color) => ThemeColor = color;

    public void SetStylesheet(string linkId, string href) => Stylesheets[linkId] = href;

    public void EnableStylesheet(string linkId, bool enabled) => StylesheetEnabled[linkId] = enabled;

    public void SetStyleText(string styleId, string? css) => StyleTexts[styleId] = css;

    public string FormatDate(DateTimeOffset value, string? locale, object? options = null) =>
        value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    public string FormatNumber(double value, string? locale, object? options = null) => value.ToString(CultureInfo.InvariantCulture);

    public int UtcOffsetMinutes(DateTimeOffset at) => OffsetMinutes;

    public void ScrollTo(double x, double y) => ScrollPosition = (x, y);

    /// <summary>Simulates a viewport resize.</summary>
    public void Resize(int width)
    {
        ViewportWidth = width;
        Resized?.Invoke();
    }

    private static string Key(DocumentTarget target, string name) => (target == DocumentTarget.Html ? "html:" : "body:") + name;
}
