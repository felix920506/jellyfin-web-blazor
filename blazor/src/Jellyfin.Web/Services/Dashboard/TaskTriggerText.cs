using System.Globalization;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>An option of the new trigger dialog: a tick count and its label.</summary>
public sealed record TickOption(long Ticks, string Name);

/// <summary>
/// Scheduled tasks and their triggers as text. Port of apps/dashboard/features/tasks/utils
/// (tasks.ts, edit.ts) and TaskTriggerCell.tsx.
/// </summary>
[AppService]
public sealed class TaskTriggerText(Globalize globalize, DateTimeText dateTime, IBrowser browser, RelativeTime relativeTime)
{
    private const long TicksPerHour = 36_000_000_000;

    /// <summary>The interval choices of the new trigger dialog, in ticks (15 minutes to 24 hours).</summary>
    public static readonly IReadOnlyList<long> IntervalDurations =
    [
        9_000_000_000, 18_000_000_000, 27_000_000_000, 36_000_000_000, 72_000_000_000, 108_000_000_000,
        144_000_000_000, 216_000_000_000, 288_000_000_000, 432_000_000_000, 864_000_000_000,
    ];

    private static readonly DateTimeOffset Midnight = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The task categories in alphabetical order.</summary>
    public static List<string> Categories(IEnumerable<TaskInfo> tasks) =>
        tasks.Select(t => t.Category).Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).Distinct()
            .Order(StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>The tasks of a category, by name.</summary>
    public static List<TaskInfo> InCategory(IEnumerable<TaskInfo> tasks, string category) =>
        tasks.Where(t => t.Category == category).OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    /// <summary>A time of day ("8:30 PM") from ticks since midnight.</summary>
    public string TimeOfDay(long ticks)
    {
        // The formatter shows local time, so hand it the instant that reads as this time of day locally.
        var wallClock = Midnight.AddTicks(ticks);
        return dateTime.GetDisplayTime(wallClock.AddMinutes(-browser.UtcOffsetMinutes(wallClock)));
    }

    /// <summary>Every quarter of an hour of the day.</summary>
    public List<TickOption> TimeOfDayOptions()
    {
        var options = new List<TickOption>();
        for (long ms = 0; ms < 86_400_000; ms += 900_000)
        {
            options.Add(new TickOption(ms * 10_000, TimeOfDay(ms * 10_000)));
        }

        return options;
    }

    /// <summary>"15 minutes" ... "24 hours".</summary>
    public List<TickOption> IntervalOptions() => IntervalDurations
        .Select(ticks => new TickOption(ticks, ticks < TicksPerHour
            ? relativeTime.Duration(ticks / 600_000_000d, "minute")
            : relativeTime.Duration(ticks / (double)TicksPerHour, "hour")))
        .ToList();

    /// <summary>"Daily at 8:30 PM", "Every 2 hours", ... Port of getTriggerFriendlyName().</summary>
    public string FriendlyName(TaskTriggerInfo trigger) => trigger.Type switch
    {
        TaskTriggerInfo_Type.DailyTrigger => globalize.Translate("DailyAt", TimeOfDay(trigger.TimeOfDayTicks ?? 0)),
        TaskTriggerInfo_Type.WeeklyTrigger => globalize.Translate("WeeklyAt", DayOfWeek(trigger.DayOfWeek), TimeOfDay(trigger.TimeOfDayTicks ?? 0)),
        TaskTriggerInfo_Type.IntervalTrigger => IntervalText(trigger.IntervalTicks ?? 0),
        TaskTriggerInfo_Type.StartupTrigger => globalize["OnApplicationStartup"],
        _ => trigger.Type?.ToApiString() ?? string.Empty,
    };

    /// <summary>"Time limit: 2 hours", or null for a trigger without a limit.</summary>
    public string? TimeLimit(TaskTriggerInfo trigger)
    {
        if (trigger.MaxRuntimeTicks is not { } ticks || ticks == 0)
        {
            return null;
        }

        var hours = ticks / (double)TicksPerHour;
        return Math.Abs(hours - 1) < 1e-9
            ? globalize["ValueTimeLimitSingleHour"]
            : globalize.Translate("ValueTimeLimitMultiHour", Number(hours));
    }

    /// <summary>The weekday's name in the user's locale.</summary>
    public string DayOfWeek(TaskTriggerInfo_DayOfWeek? day)
    {
        if (day is null)
        {
            return string.Empty;
        }

        // 2000-01-02 was a Sunday.
        var noon = new DateTimeOffset(2000, 1, 2, 12, 0, 0, TimeSpan.Zero).AddDays((int)day.Value);
        return globalize.FormatDate(noon.AddMinutes(-browser.UtcOffsetMinutes(noon)), new { weekday = "long" });
    }

    private string IntervalText(long ticks)
    {
        var hours = ticks / (double)TicksPerHour;
        return hours switch
        {
            0.25 => globalize.Translate("EveryXMinutes", "15"),
            0.5 => globalize.Translate("EveryXMinutes", "30"),
            0.75 => globalize.Translate("EveryXMinutes", "45"),
            1 => globalize["EveryHour"],
            _ => globalize.Translate("EveryXHours", Number(hours)),
        };
    }

    private static string Number(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
