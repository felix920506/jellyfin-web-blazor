using Microsoft.JSInterop;

namespace Jellyfin.Web.Interop;

/// <summary><see cref="IBrowser"/> over <c>wwwroot/js/interop.js</c> (window.jf).</summary>
public sealed class JsBrowser : IBrowser, IDisposable
{
    private readonly IJSInProcessRuntime _js;
    private readonly DotNetObjectReference<JsBrowser> _self;
    private BrowserEnvironment? _environment;
    private bool _listening;
    private Action<string>? _hashChanged;
    private Action? _resized;
    private bool _listeningResize;

    public JsBrowser(IJSRuntime js)
    {
        _js = (IJSInProcessRuntime)js;
        _self = DotNetObjectReference.Create(this);
    }

    public BrowserEnvironment Environment => _environment ??= _js.Invoke<BrowserEnvironment>("jf.environment");

    public double? ConnectionDownlinkMax => _js.Invoke<double?>("jf.connectionDownlinkMax");

    public string? StorageGet(string key, bool session = false) => _js.Invoke<string?>("jf.storageGet", key, session);

    public void StorageSet(string key, string value, bool session = false) => _js.InvokeVoid("jf.storageSet", key, value, session);

    public void StorageRemove(string key, bool session = false) => _js.InvokeVoid("jf.storageRemove", key, session);

    public string Hash => _js.Invoke<string>("jf.getHash");

    public event Action<string>? HashChanged
    {
        add
        {
            _hashChanged += value;
            if (!_listening)
            {
                _listening = true;
                _js.InvokeVoid("jf.listenHash", _self);
            }
        }
        remove => _hashChanged -= value;
    }

    [JSInvokable]
    public void OnHashChanged(string hash) => _hashChanged?.Invoke(hash);

    public void Navigate(string hash, bool replace = false) => _js.InvokeVoid("jf.navigate", hash, replace);

    public void Back() => _js.InvokeVoid("jf.historyBack");

    public void Reload() => _js.InvokeVoid("jf.reload");

    public void OpenUrl(string url, string? target = null) => _js.InvokeVoid("jf.openUrl", url, target);

    public void ToggleClass(DocumentTarget target, string className, bool on) => _js.InvokeVoid("jf.toggleClass", Name(target), className, on);

    public void SetAttribute(DocumentTarget target, string name, string? value) => _js.InvokeVoid("jf.setAttribute", Name(target), name, value);

    public void SetTitle(string title) => _js.InvokeVoid("jf.setTitle", title);

    public void SetThemeColor(string color) => _js.InvokeVoid("jf.setThemeColor", color);

    public void SetStylesheet(string linkId, string href) => _js.InvokeVoid("jf.setStylesheet", linkId, href);

    public void EnableStylesheet(string linkId, bool enabled) => _js.InvokeVoid("jf.enableStylesheet", linkId, enabled);

    public void SetStyleText(string styleId, string? css) => _js.InvokeVoid("jf.setStyleText", styleId, css);

    public string FormatDate(DateTimeOffset value, string? locale, object? options = null) =>
        _js.Invoke<string>("jf.formatDate", value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"), locale, options);

    public string FormatNumber(double value, string? locale, object? options = null) =>
        _js.Invoke<string>("jf.formatNumber", value, locale, options);

    public int UtcOffsetMinutes(DateTimeOffset at) =>
        _js.Invoke<int>("jf.utcOffsetMinutes", at.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));

    public int ViewportWidth => _js.Invoke<int>("jf.viewportWidth");

    public event Action? Resized
    {
        add
        {
            _resized += value;
            if (!_listeningResize)
            {
                _listeningResize = true;
                _js.InvokeVoid("jf.listenResize", _self);
            }
        }
        remove => _resized -= value;
    }

    [JSInvokable]
    public void OnResized() => _resized?.Invoke();

    public void ScrollTo(double x, double y) => _js.InvokeVoid("jf.scrollTo", x, y);

    public void Dispose() => _self.Dispose();

    private static string Name(DocumentTarget target) => target == DocumentTarget.Body ? "body" : "html";
}
