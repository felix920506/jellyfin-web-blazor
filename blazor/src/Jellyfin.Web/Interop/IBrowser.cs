namespace Jellyfin.Web.Interop;

/// <summary>Where a document-level class or attribute is applied.</summary>
public enum DocumentTarget
{
    /// <summary>The <c>&lt;html&gt;</c> element.</summary>
    Html,

    /// <summary>The <c>&lt;body&gt;</c> element.</summary>
    Body
}

/// <summary>A snapshot of the browser environment, read once at startup.</summary>
public sealed record BrowserEnvironment
{
    public string UserAgent { get; init; } = string.Empty;
    public string[] Languages { get; init; } = ["en-US"];
    public string Href { get; init; } = "http://localhost/";
    public string Origin { get; init; } = "http://localhost";
    public string Protocol { get; init; } = "http:";
    public bool Touch { get; init; }
    public double DevicePixelRatio { get; init; } = 1;
    public int ScreenWidth { get; init; } = 1920;
    public int ScreenHeight { get; init; } = 1080;
    public int InnerWidth { get; init; } = 1920;
    public int InnerHeight { get; init; } = 1080;
}

/// <summary>
/// The synchronous browser surface the core services need: storage, location hash, document
/// attributes and locale-aware formatting. Implemented by <see cref="JsBrowser"/> in the app and by
/// an in-memory fake in tests. Feature-specific JavaScript (players, scrolling helpers, ...) lives
/// in its own module and is called through <c>IJSRuntime</c> instead of being added here.
/// </summary>
public interface IBrowser
{
    BrowserEnvironment Environment { get; }

    // --- storage (localStorage unless session is true) ---
    string? StorageGet(string key, bool session = false);
    void StorageSet(string key, string value, bool session = false);
    void StorageRemove(string key, bool session = false);

    // --- location: the app routes on the URL fragment, e.g. "/details?id=1" for "#/details?id=1" ---
    /// <summary>The current fragment without the leading '#'.</summary>
    string Hash { get; }

    /// <summary>Raised with the new fragment (no leading '#') whenever it changes.</summary>
    event Action<string>? HashChanged;

    void Navigate(string hash, bool replace = false);
    void Back();
    void Reload();
    void OpenUrl(string url, string? target = null);

    // --- document ---
    void ToggleClass(DocumentTarget target, string className, bool on);
    void SetAttribute(DocumentTarget target, string name, string? value);
    void SetTitle(string title);
    void SetThemeColor(string color);
    void SetStylesheet(string linkId, string href);
    void EnableStylesheet(string linkId, bool enabled);
    void SetStyleText(string styleId, string? css);

    // --- locale-aware formatting (the WASM runtime runs in invariant globalization mode) ---
    /// <summary>Formats with <c>Date.prototype.toLocaleString(locale, options)</c>.</summary>
    string FormatDate(DateTimeOffset value, string? locale, object? options = null);

    /// <summary>Formats with <c>Number.prototype.toLocaleString(locale, options)</c>.</summary>
    string FormatNumber(double value, string? locale, object? options = null);

    /// <summary>Minutes to add to UTC to get the user's local time at the given instant.</summary>
    int UtcOffsetMinutes(DateTimeOffset at);

    // --- viewport ---
    /// <summary>Current width of the viewport in CSS pixels.</summary>
    int ViewportWidth { get; }

    /// <summary>Raised (debounced) after the viewport was resized.</summary>
    event Action? Resized;

    void ScrollTo(double x, double y);
}
