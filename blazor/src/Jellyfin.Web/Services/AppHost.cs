using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>Layout modes this port renders. The TV layout of the original client is out of scope.</summary>
public enum LayoutMode
{
    Desktop,
    Mobile
}

/// <summary>Browser facts derived from the user agent (a subset of the original scripts/browser.js).</summary>
public sealed partial record BrowserInfo
{
    public bool Chrome { get; init; }
    public bool EdgeChromium { get; init; }
    public bool Firefox { get; init; }
    public bool Opera { get; init; }
    public bool Safari { get; init; }
    public bool Ios { get; init; }
    public bool IPad { get; init; }
    public bool IPhone { get; init; }
    public bool Android { get; init; }
    public bool Mobile { get; init; }
    public bool Touch { get; init; }

    public static BrowserInfo Parse(string userAgent, bool touch)
    {
        var ua = userAgent.ToLowerInvariant();
        var edge = ua.Contains("edg/", StringComparison.Ordinal);
        var opera = ua.Contains("opr/", StringComparison.Ordinal) || ua.Contains("opera", StringComparison.Ordinal);
        var firefox = ua.Contains("firefox", StringComparison.Ordinal);
        var chrome = !edge && !opera && ua.Contains("chrome", StringComparison.Ordinal);
        var ipad = ua.Contains("ipad", StringComparison.Ordinal);
        var iphone = ua.Contains("iphone", StringComparison.Ordinal) || ua.Contains("ipod", StringComparison.Ordinal);
        var android = ua.Contains("android", StringComparison.Ordinal);
        var safari = !chrome && !edge && !opera && !firefox && !android && ua.Contains("safari", StringComparison.Ordinal);

        return new BrowserInfo
        {
            Chrome = chrome,
            EdgeChromium = edge,
            Firefox = firefox,
            Opera = opera,
            Safari = safari,
            IPad = ipad,
            IPhone = iphone,
            Ios = ipad || iphone,
            Android = android,
            Mobile = MobileRegex().IsMatch(ua),
            Touch = touch
        };
    }

    [GeneratedRegex("android|iphone|ipod|ipad|mobile|blackberry|iemobile|opera mini|webos|silk|kindle")]
    private static partial Regex MobileRegex();
}

/// <summary>
/// Client identity (app name, version, device id and name), browser facts and the layout mode.
/// Port of components/apphost.js and components/layoutManager.js for the browser case.
/// </summary>
public sealed class AppHost
{
    public const string AppName = "Jellyfin Web";
    private const string DeviceIdKey = "_deviceId2";
    private const string LayoutKey = "layout";

    private readonly IBrowser _browser;
    private string? _deviceId;

    public AppHost(IBrowser browser)
    {
        _browser = browser;
        Browser = BrowserInfo.Parse(browser.Environment.UserAgent, browser.Environment.Touch);
        AppVersion = typeof(AppHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? "13.0.0";
    }

    public event Action? LayoutChanged;

    public BrowserInfo Browser { get; }

    public string AppVersion { get; }

    public LayoutMode Layout { get; private set; } = LayoutMode.Desktop;

    public bool IsMobileLayout => Layout == LayoutMode.Mobile;

    /// <summary>Stable per-browser id, shared with the original client (localStorage "_deviceId2").</summary>
    public string DeviceId
    {
        get
        {
            if (_deviceId is null)
            {
                _deviceId = _browser.StorageGet(DeviceIdKey);
                if (string.IsNullOrEmpty(_deviceId))
                {
                    var seed = _browser.Environment.UserAgent + "|" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _deviceId = Convert.ToBase64String(Encoding.Latin1.GetBytes(seed)).Replace('=', '1');
                    _browser.StorageSet(DeviceIdKey, _deviceId);
                }
            }

            return _deviceId;
        }
    }

    public string DeviceName
    {
        get
        {
            var name = Browser switch
            {
                { Chrome: true } => "Chrome",
                { EdgeChromium: true } => "Edge Chromium",
                { Firefox: true } => "Firefox",
                { Opera: true } => "Opera",
                { Safari: true } => "Safari",
                _ => "Web Browser"
            };

            if (Browser.IPad)
            {
                name += " iPad";
            }
            else if (Browser.IPhone)
            {
                name += " iPhone";
            }
            else if (Browser.Android)
            {
                name += " Android";
            }

            return name;
        }
    }

    /// <summary>The stored layout preference: "auto", "desktop" or "mobile".</summary>
    public string LayoutSetting => _browser.StorageGet(LayoutKey) is { Length: > 0 } value ? value : "auto";

    /// <summary>Applies the stored layout preference; called once at startup.</summary>
    public void Initialize() => ApplyLayout(LayoutSetting, save: false);

    /// <summary>Changes the layout preference ("auto", "desktop", "mobile").</summary>
    public void SetLayout(string setting) => ApplyLayout(setting, save: true);

    private void ApplyLayout(string setting, bool save)
    {
        if (save)
        {
            if (string.IsNullOrEmpty(setting) || setting == "auto")
            {
                _browser.StorageRemove(LayoutKey);
            }
            else
            {
                _browser.StorageSet(LayoutKey, setting);
            }
        }

        // Legacy and TV values stored by the original client fall back to automatic selection.
        var mode = setting switch
        {
            "mobile" => LayoutMode.Mobile,
            "desktop" => LayoutMode.Desktop,
            _ => Browser.Mobile ? LayoutMode.Mobile : LayoutMode.Desktop
        };

        Layout = mode;
        _browser.ToggleClass(DocumentTarget.Html, "layout-desktop", mode == LayoutMode.Desktop);
        _browser.ToggleClass(DocumentTarget.Html, "layout-mobile", mode == LayoutMode.Mobile);
        _browser.ToggleClass(DocumentTarget.Html, "layout-tv", false);
        LayoutChanged?.Invoke();
    }
}
