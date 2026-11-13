namespace Jellyfin.Web.Services;

/// <summary>Pure idle-time rules used by the screensaver and its tests.</summary>
public static class ScreensaverEligibility
{
    public static bool IsIdle(DateTimeOffset now, DateTimeOffset lastInput, DateTimeOffset lastFunctionalEvent, TimeSpan minimumIdle) =>
        now - lastInput >= minimumIdle && now - lastFunctionalEvent >= minimumIdle;
}
