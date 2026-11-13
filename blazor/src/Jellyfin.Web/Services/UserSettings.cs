using System.Globalization;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// Per-user settings. Port of scripts/settings/userSettings.js: every value is written to
/// localStorage under <c>{userId}-{name}</c>; values with <c>enableOnServer</c> are also kept in
/// the server's display preferences ("usersettings", client "emby") so they roam between devices.
/// </summary>
public sealed class UserSettings(AppSettings appSettings, JellyfinApi api)
{
    private const string DisplayPreferencesId = "usersettings";
    private const string DisplayPreferencesClient = "emby";

    private string? _userId;
    private Guid? _userGuid;
    private DisplayPreferencesDto? _preferences;
    private CancellationTokenSource? _saveDelay;

    /// <summary>Raised with the setting name after a value changes.</summary>
    public event Action<string>? Changed;

    public string? UserId => _userId;

    // --- display ---
    public string? Language
    {
        get => Get("language", false);
        set => Set("language", value, false);
    }

    public string? DateTimeLocale
    {
        get => Get("datetimelocale", false);
        set => Set("datetimelocale", value, false);
    }

    /// <summary>Theme id for the main app ("appTheme").</summary>
    public string? Theme
    {
        get => Get("appTheme", false);
        set => Set("appTheme", value, false);
    }

    /// <summary>Theme id for the admin dashboard.</summary>
    public string? DashboardTheme
    {
        get => Get("dashboardTheme");
        set => Set("dashboardTheme", value);
    }

    public string? Screensaver
    {
        get => Get("screensaver", false);
        set => Set("screensaver", value, false);
    }

    public int ScreensaverTime => Math.Max(1, GetInt("screensaverTime", 180, false));

    public int BackdropScreensaverInterval => Math.Max(1, GetInt("backdropScreensaverInterval", 5, false));

    public string? CustomCss
    {
        get => Get("customCss", false);
        set => Set("customCss", value, false);
    }

    public bool DisableCustomCss
    {
        get => GetBool("disableCustomCss", false, false);
        set => Set("disableCustomCss", value, false);
    }

    public bool EnableBackdrops
    {
        get => GetBool("enableBackdrops", false, false);
        set => Set("enableBackdrops", value, false);
    }

    public bool EnableBlurhash
    {
        get => GetBool("blurhash", true, false);
        set => Set("blurhash", value, false);
    }

    public bool EnableFastFadein
    {
        get => GetBool("fastFadein", true, false);
        set => Set("fastFadein", value, false);
    }

    public bool DetailsBanner
    {
        get => GetBool("detailsBanner", true, false);
        set => Set("detailsBanner", value, false);
    }

    public bool UseEpisodeImagesInNextUpAndResume
    {
        get => GetBool("useEpisodeImagesInNextUpAndResume", false);
        set => Set("useEpisodeImagesInNextUpAndResume", value);
    }

    /// <summary>Items per page in library views; 0 disables paging.</summary>
    public int LibraryPageSize
    {
        get
        {
            var size = GetInt("libraryPageSize", 100, false);
            return size < 0 ? 100 : size;
        }
        set => Set("libraryPageSize", value, false);
    }

    public int MaxDaysForNextUp
    {
        get
        {
            var days = GetInt("maxDaysForNextUp", 365, false);
            return days < 0 ? 365 : days;
        }
        set => Set("maxDaysForNextUp", value, false);
    }

    public bool EnableRewatchingInNextUp
    {
        get => GetBool("enableRewatchingInNextUp", false, false);
        set => Set("enableRewatchingInNextUp", value, false);
    }

    // --- playback ---
    public int SkipBackLength
    {
        get => GetInt("skipBackLength", 10000);
        set => Set("skipBackLength", value);
    }

    public int SkipForwardLength
    {
        get => GetInt("skipForwardLength", 30000);
        set => Set("skipForwardLength", value);
    }

    public bool EnableCinemaMode
    {
        get => GetBool("enableCinemaMode", true, false);
        set => Set("enableCinemaMode", value, false);
    }

    public bool EnableNextVideoInfoOverlay
    {
        get => GetBool("enableNextVideoInfoOverlay", true, false);
        set => Set("enableNextVideoInfoOverlay", value);
    }

    public bool EnableVideoRemainingTime
    {
        get => GetBool("enableVideoRemainingTime", true, false);
        set => Set("enableVideoRemainingTime", value);
    }

    public bool EnableThemeSongs
    {
        get => GetBool("enableThemeSongs", false, false);
        set => Set("enableThemeSongs", value, false);
    }

    public bool EnableThemeVideos
    {
        get => GetBool("enableThemeVideos", false, false);
        set => Set("enableThemeVideos", value, false);
    }

    /// <summary>Binds the settings to a user and loads their display preferences. Null signs out.</summary>
    public async Task SetUserAsync(Guid? userId)
    {
        _saveDelay?.Cancel();
        _userGuid = userId;
        _userId = userId?.ToString("N");
        _preferences = null;

        if (userId is null || !api.IsLoggedIn)
        {
            return;
        }

        try
        {
            _preferences = await api.Client.DisplayPreferences[DisplayPreferencesId].GetAsync(request =>
            {
                request.QueryParameters.UserId = userId;
                request.QueryParameters.Client = DisplayPreferencesClient;
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[UserSettings] loading display preferences failed: {ex.Message}");
        }
    }

    public string? Get(string name, bool enableOnServer = true)
    {
        if (enableOnServer && _preferences?.CustomPrefs?.AdditionalData is { } prefs)
        {
            return prefs.TryGetValue(name, out var value) ? value?.ToString() : null;
        }

        return appSettings.Get(name, _userId);
    }

    public void Set(string name, string? value, bool enableOnServer = true)
    {
        var current = Get(name, enableOnServer);
        appSettings.Set(name, value, _userId);

        if (enableOnServer && _preferences is not null)
        {
            _preferences.CustomPrefs ??= new DisplayPreferencesDto_CustomPrefs();
            if (value is null)
            {
                _preferences.CustomPrefs.AdditionalData.Remove(name);
            }
            else
            {
                _preferences.CustomPrefs.AdditionalData[name] = value;
            }

            QueueSave();
        }

        if (!string.Equals(current, value, StringComparison.Ordinal))
        {
            Changed?.Invoke(name);
        }
    }

    public void Set(string name, bool value, bool enableOnServer = true) => Set(name, value ? "true" : "false", enableOnServer);

    public void Set(string name, int value, bool enableOnServer = true) => Set(name, value.ToString(CultureInfo.InvariantCulture), enableOnServer);

    public bool GetBool(string name, bool defaultValue, bool enableOnServer = true) => AppSettings.ToBoolean(Get(name, enableOnServer), defaultValue);

    public int GetInt(string name, int defaultValue, bool enableOnServer = true) =>
        int.TryParse(Get(name, enableOnServer), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : defaultValue;

    // Port of the 50 ms debounce around updateDisplayPreferences.
    private void QueueSave()
    {
        _saveDelay?.Cancel();
        var delay = _saveDelay = new CancellationTokenSource();
        var preferences = _preferences;
        var userId = _userGuid;
        _ = SaveAsync();

        async Task SaveAsync()
        {
            try
            {
                await Task.Delay(50, delay.Token);
                if (preferences is not null && userId is not null)
                {
                    await api.Client.DisplayPreferences[DisplayPreferencesId].PostAsync(preferences, request =>
                    {
                        request.QueryParameters.UserId = userId;
                        request.QueryParameters.Client = DisplayPreferencesClient;
                    });
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer save.
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[UserSettings] saving display preferences failed: {ex.Message}");
            }
        }
    }
}
