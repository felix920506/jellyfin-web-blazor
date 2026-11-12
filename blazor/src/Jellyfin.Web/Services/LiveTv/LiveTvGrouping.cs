using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>Timers of one local day, in the order the server sent them (<c>groupsTimers</c> of <c>useTimers.ts</c>).</summary>
/// <param name="Name">The heading: weekday, short month and day in the viewer's time; empty when the timer has no start.</param>
/// <param name="Items">The timers of that day.</param>
public sealed record TimerGroup(string Name, IReadOnlyList<BaseItemDto> Items);

/// <summary>
/// Grouping the schedule and the channel guide the way the original does: consecutive items that
/// share a local day stay together, and a timer without a start date stands in its own group.
/// </summary>
public static class LiveTvGrouping
{
    /// <summary>The heading of the upcoming recordings (<c>useTimers.ts</c>: weekday, short month, day).</summary>
    public static readonly object TimerDateOptions = new { weekday = "long", month = "short", day = "numeric" };

    /// <summary>The heading of a channel's programmes (<c>renderProgramsForChannel</c>: weekday, long month, day).</summary>
    public static readonly object ChannelDateOptions = new { weekday = "long", month = "long", day = "numeric" };

    /// <summary>Groups timers by the local day they start (<c>groupsTimers</c>, <c>indexByDate</c> left on).</summary>
    public static IReadOnlyList<TimerGroup> GroupTimers(IEnumerable<BaseItemDto> timers, DateTimeText dates) =>
        GroupByLocalDay(timers, dates, TimerDateOptions);

    /// <summary>Groups a channel's programmes by the local day they start (<c>renderProgramsForChannel</c>).</summary>
    public static IReadOnlyList<TimerGroup> GroupPrograms(IEnumerable<BaseItemDto> programs, DateTimeText dates) =>
        GroupByLocalDay(programs, dates, ChannelDateOptions);

    private static List<TimerGroup> GroupByLocalDay(IEnumerable<BaseItemDto> items, DateTimeText dates, object options)
    {
        var groups = new List<TimerGroup>();
        var current = new List<BaseItemDto>();
        var currentName = string.Empty;
        DateTime? currentDay = null;
        foreach (var item in items)
        {
            // The heading is the local calendar day (groupsTimers). The key is that day, not the
            // formatted text: a formatter that also prints the time must not split one day in two,
            // and formatting midnight UTC would show the previous day west of UTC.
            DateTime? day = item.StartDate is { } start ? dates.Local(start).Date : null;
            if (day != currentDay && current.Count > 0)
            {
                groups.Add(new TimerGroup(currentName, current));
                current = [];
            }

            if (current.Count == 0)
            {
                currentDay = day;
                currentName = item.StartDate is { } heading ? dates.ToLocaleDateString(heading, options) : string.Empty;
            }

            current.Add(item);
        }

        if (current.Count > 0)
        {
            groups.Add(new TimerGroup(currentName, current));
        }

        return groups;
    }
}
