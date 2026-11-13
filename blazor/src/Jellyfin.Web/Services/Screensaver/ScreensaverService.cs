using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>
/// Owns the idle screensaver state. The original manager listens for document input, does not start
/// while an unpaused video is playing, and lets the selected screensaver supply its own markup.
/// </summary>
[AppService]
public sealed class ScreensaverService : IDisposable
{
    private readonly UserSettings settings;
    private readonly JellyfinApi api;
    private readonly ItemQueries items;
    private readonly Backdrop backdrop;
    private readonly IPlaybackManager playback;
    private readonly TimeProvider clock;
    private DateTimeOffset _lastInput;
    private DateTimeOffset _lastFunctionalEvent;
    private bool _wasPlayingVideo;
    private bool _blocked;
    private bool _loading;
    private int _generation;

    public event Action? Changed;

    public string? Active { get; private set; }

    public IReadOnlyList<ScreensaverSlide> Slides { get; private set; } = [];

    public int ActiveSlide { get; private set; }

    public bool IsShowing => Active is not null;

    public string? Selected => settings.Screensaver;

    public int BackdropIntervalSeconds => settings.BackdropScreensaverInterval;

    public ScreensaverService(UserSettings settings, JellyfinApi api, ItemQueries items, Backdrop backdrop,
        IPlaybackManager playback, TimeProvider clock)
    {
        this.settings = settings;
        this.api = api;
        this.items = items;
        this.backdrop = backdrop;
        this.playback = playback;
        this.clock = clock;
        _lastInput = clock.GetUtcNow();
        _lastFunctionalEvent = _lastInput;
        playback.StateChanged += OnPlaybackChanged;
        settings.Changed += OnSettingChanged;
    }

    /// <summary>Processes a browser input event, waking an active screensaver.</summary>
    public void Input()
    {
        _lastInput = clock.GetUtcNow();
        Hide();
    }

    /// <summary>Temporarily prevents activation while a managed player or dialog owns input.</summary>
    public void Block() => _blocked = true;

    public void Unblock() => _blocked = false;

    /// <summary>Runs one idle check. Keeping this synchronous makes fake-time component tests deterministic.</summary>
    public void Tick()
    {
        if (IsShowing || _loading || _blocked || string.IsNullOrEmpty(Selected) || Selected == "none")
        {
            return;
        }

        var now = clock.GetUtcNow();
        var minimum = TimeSpan.FromSeconds(settings.ScreensaverTime);
        if (!ScreensaverEligibility.IsIdle(now, _lastInput, _lastFunctionalEvent, minimum))
        {
            return;
        }

        if (playback.IsPlayingVideo && !playback.IsPaused)
        {
            return;
        }

        _loading = true;
        _ = ShowAsync(Selected);
    }

    public void NextSlide()
    {
        if (Slides.Count > 0)
        {
            ActiveSlide = (ActiveSlide + 1) % Slides.Count;
            Changed?.Invoke();
        }
    }

    public void Hide()
    {
        if (!IsShowing)
        {
            return;
        }

        _generation++;
        Active = null;
        Slides = [];
        ActiveSlide = 0;
        backdrop.Clear();
        Changed?.Invoke();
    }

    public void Dispose()
    {
        playback.StateChanged -= OnPlaybackChanged;
        settings.Changed -= OnSettingChanged;
        Hide();
    }

    private async Task ShowAsync(string type)
    {
        if (IsShowing || type == "none")
        {
            _loading = false;
            return;
        }

        if (type == "logoscreensaver")
        {
            Active = type;
            Slides = [];
            ActiveSlide = 0;
            Changed?.Invoke();
            _loading = false;
            return;
        }

        if (type != "backdropscreensaver" || !api.IsLoggedIn)
        {
            _loading = false;
            return;
        }

        var generation = ++_generation;
        IReadOnlyList<BaseItemDto> result;
        try
        {
            result = await items.GetUserItemsAsync(
                ("ImageTypes", "Backdrop"),
                ("EnableImageTypes", "Backdrop"),
                ("IncludeItemTypes", "Movie,Series,MusicArtist"),
                ("SortBy", "Random"),
                ("Recursive", true),
                ("Fields", "Taglines"),
                ("ImageTypeLimit", 10),
                ("StartIndex", 0),
                ("Limit", 200));
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            AppLog.Warn("[Screensaver] loading backdrops failed: " + ex.Message);
            _loading = false;
            return;
        }

        if (generation != _generation)
        {
            _loading = false;
            return;
        }

        var slides = result.SelectMany(item => backdrop.GetImageUrls(item).Select(url => new ScreensaverSlide(url, item.Name))).ToList();
        if (slides.Count == 0)
        {
            _loading = false;
            return;
        }

        Active = type;
        Slides = slides;
        ActiveSlide = 0;
        Changed?.Invoke();
        _loading = false;
    }

    private void OnPlaybackChanged()
    {
        var playingVideo = playback.IsPlayingVideo;
        if (_wasPlayingVideo && !playingVideo)
        {
            _lastFunctionalEvent = clock.GetUtcNow();
        }

        _wasPlayingVideo = playingVideo;
        if (playingVideo && !playback.IsPaused)
        {
            Hide();
        }
    }

    private void OnSettingChanged(string name)
    {
        if (name is "screensaver" or "screensaverTime" or "backdropScreensaverInterval")
        {
            Hide();
            Changed?.Invoke();
        }
    }
}

public sealed record ScreensaverSlide(string Url, string? Title);
