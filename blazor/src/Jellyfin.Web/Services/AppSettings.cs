using System.Globalization;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// Device-local settings in localStorage. Port of scripts/settings/appSettings.js: the storage key
/// is the setting name, or <c>{userId}-{name}</c> for per-user values; booleans are "true"/"false".
/// </summary>
public sealed class AppSettings(IBrowser browser)
{
    /// <summary>Raised with the setting name after a value is written.</summary>
    public event Action<string>? Changed;

    public bool EnableAutoLogin
    {
        get => GetBool("enableAutoLogin", true);
        set => Set("enableAutoLogin", value);
    }

    public bool EnableGamepad
    {
        get => GetBool("enableGamepad", false);
        set => Set("enableGamepad", value);
    }

    public bool EnableSmoothScroll
    {
        get => GetBool("enableSmoothScroll", false);
        set => Set("enableSmoothScroll", value);
    }

    public bool EnableSystemExternalPlayers
    {
        get => GetBool("enableSystemExternalPlayers", false);
        set => Set("enableSystemExternalPlayers", value);
    }

    public int MaxStaticMusicBitrate
    {
        get => GetInt("maxStaticMusicBitrate", 320000);
        set => Set("maxStaticMusicBitrate", value);
    }

    public int MaxVideoWidth
    {
        get => GetInt("maxVideoWidth", 0);
        set => Set("maxVideoWidth", value);
    }

    public string? Get(string name, string? userId = null) => browser.StorageGet(Key(name, userId));

    public void Set(string name, string? value, string? userId = null)
    {
        var key = Key(name, userId);
        var current = browser.StorageGet(key);
        if (value is null)
        {
            browser.StorageRemove(key);
        }
        else
        {
            browser.StorageSet(key, value);
        }

        if (!string.Equals(current, value, StringComparison.Ordinal))
        {
            Changed?.Invoke(name);
        }
    }

    public void Set(string name, bool value, string? userId = null) => Set(name, value ? "true" : "false", userId);

    public void Set(string name, int value, string? userId = null) => Set(name, value.ToString(CultureInfo.InvariantCulture), userId);

    public bool GetBool(string name, bool defaultValue, string? userId = null) => ToBoolean(Get(name, userId), defaultValue);

    public int GetInt(string name, int defaultValue, string? userId = null) =>
        int.TryParse(Get(name, userId), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    /// <summary>Port of utils/string.ts toBoolean: only "true" and "false" are recognised.</summary>
    public static bool ToBoolean(string? value, bool defaultValue) => value switch
    {
        "true" => true,
        "false" => false,
        _ => defaultValue
    };

    private static string Key(string name, string? userId) => string.IsNullOrEmpty(userId) ? name : userId + "-" + name;
}
