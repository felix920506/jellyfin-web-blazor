using System.Globalization;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Signals that playback was intentionally stopped by a pre-play interceptor.</summary>
internal sealed class PlaybackInterceptException(bool reported, bool suppressError = false, string reason = "playback intercepted") : Exception(reason)
{
    public bool Reported { get; } = reported;

    public bool SuppressError { get; } = suppressError;
}

/// <summary>Playback could not start; <see cref="Reported"/> when the user has been told why.</summary>
internal sealed class PlaybackRejectedException(bool reported, string reason = "playback rejected") : Exception(reason)
{
    public bool Reported { get; } = reported;
}

/// <summary>Tracks document activity for the still-watching playback interceptor.</summary>
[AppService]
public sealed class PlaybackActivity(IPlaybackTimers timers)
{
    private long _lastActivity = timers.NowMs;

    public long IdleMilliseconds => Math.Max(0, timers.NowMs - _lastActivity);

    public void NotifyActivity() => _lastActivity = timers.NowMs;
}

/// <summary>
/// The pre-play checks formerly supplied by the playback plugins. They run for every item before
/// stream negotiation, in the same priority order as the original plugin manager.
/// </summary>
[AppService]
public sealed class PlaybackInterceptors(
    AppDialogs dialogs,
    Globalize globalize,
    JellyfinApi api,
    UserSettings userSettings,
    PlaybackActivity activity,
    IPlaybackTimers timers)
{
    private DateTimeOffset? _stillWatchingSessionStart;
    private readonly HashSet<string> _playedItems = new(StringComparer.OrdinalIgnoreCase);

    public async Task BeforePlayAsync(BaseItemDto item, ItemPlayOptions options)
    {
        await ValidateAccessAsync(item, options.Fullscreen);
        await ShowExperimentalWarningAsync(item);
        await CheckStillWatchingAsync(item, options.IsFirstItem);
    }

    private async Task ValidateAccessAsync(BaseItemDto item, bool fullscreen)
    {
        if (item.ServerId is null || api.User?.Policy?.EnableMediaPlayback != false)
        {
            return;
        }

        if (!fullscreen)
        {
            throw new PlaybackInterceptException(reported: false, suppressError: true, "media playback is disabled");
        }

        await dialogs.AlertAsync(globalize["MessagePlayAccessRestricted"]);
        throw new PlaybackInterceptException(reported: true, reason: "media playback is disabled");
    }

    private async Task ShowExperimentalWarningAsync(BaseItemDto item)
    {
        var key = item.VideoType?.ToApiString() switch
        {
            "Iso" => "isoexpirementalinfo",
            "BluRay" => "blurayexpirementalinfo",
            "Dvd" => "dvdexpirementalinfo",
            _ => null
        };
        if (key is null)
        {
            return;
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(timers.NowMs);
        key += now.Year.ToString(CultureInfo.InvariantCulture) + "-w" + ISOWeek.GetWeekOfYear(now.DateTime).ToString(CultureInfo.InvariantCulture);
        if (userSettings.Get(key, false) == "1")
        {
            return;
        }

        userSettings.Set(key, "1", false);
        await dialogs.AlertAsync(globalize["UnsupportedPlayback"]);
    }

    private async Task CheckStillWatchingAsync(BaseItemDto item, bool isFirstItem)
    {
        if (isFirstItem)
        {
            _stillWatchingSessionStart = DateTimeOffset.FromUnixTimeMilliseconds(timers.NowMs);
            _playedItems.Clear();
        }

        var option = userSettings.Get("stillWatchingPrompt", false) ?? "default";
        var config = option switch
        {
            "short" => (Count: 2, Duration: TimeSpan.FromHours(1)),
            "long" => (Count: 5, Duration: TimeSpan.FromHours(2.5)),
            "veryLong" => (Count: 8, Duration: TimeSpan.FromHours(4)),
            "disabled" => (Count: int.MaxValue, Duration: Timeout.InfiniteTimeSpan),
            _ => (Count: 3, Duration: TimeSpan.FromHours(1.5))
        };

        var id = item.IdText();
        if (item.TypeName() != "Episode" || id.Length == 0 || option == "disabled")
        {
            return;
        }

        var sessionDuration = _stillWatchingSessionStart is { } start
            ? DateTimeOffset.FromUnixTimeMilliseconds(timers.NowMs) - start
            : TimeSpan.Zero;
        if (sessionDuration >= config.Duration && (activity.IdleMilliseconds >= config.Duration.TotalMilliseconds || _playedItems.Count >= config.Count))
        {
            await dialogs.ConfirmAsync(
                globalize["ConfirmStillWatching"],
                cancelText: globalize["StopWatching"],
                confirmText: globalize["ContinueWatching"]);
            _stillWatchingSessionStart = DateTimeOffset.FromUnixTimeMilliseconds(timers.NowMs);
            _playedItems.Clear();
        }

        _playedItems.Add(id);
    }
}
