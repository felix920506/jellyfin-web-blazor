using System.Globalization;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// Port of the parts of <c>scripts/datetime.js</c> the item components use. Locale-aware output
/// goes through the browser (<see cref="Globalize.FormatDate"/>); durations are computed here.
/// </summary>
[AppService]
public sealed class DateTimeText(Globalize globalize, IBrowser browser)
{
    private static readonly object TimeOptions = new { hour = "numeric", minute = "2-digit" };
    private static readonly object DateOptions = new { year = "numeric", month = "numeric", day = "numeric" };
    private static readonly object NoGrouping = new { useGrouping = false };

    /// <summary>The clock the components read; tests replace it.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = static () => DateTimeOffset.UtcNow;

    public DateTimeOffset Now => Clock();

    /// <summary>"1h 35m" / "35m" (<c>getDisplayDuration</c>).</summary>
    public static string GetDisplayDuration(long ticks)
    {
        var totalMinutes = (long)Math.Round(ticks / 600000000d, MidpointRounding.AwayFromZero);
        if (totalMinutes == 0)
        {
            totalMinutes = 1;
        }

        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}h {minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m");
    }

    /// <summary>"1:05:09" / "5:09" (<c>getDisplayRunningTime</c>; digits are not localized).</summary>
    public static string GetDisplayRunningTime(long ticks)
    {
        const long TicksPerHour = 36000000000;
        const long TicksPerMinute = 600000000;
        const long TicksPerSecond = 10000000;

        var hours = ticks / TicksPerHour;
        ticks -= hours * TicksPerHour;
        var minutes = ticks / TicksPerMinute;
        ticks -= minutes * TicksPerMinute;
        var seconds = ticks / TicksPerSecond;

        return hours > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{hours}:{minutes:00}:{seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}:{seconds:00}");
    }

    /// <summary>The time of day in the user's locale, e.g. "8:30 PM" (<c>getDisplayTime</c>).</summary>
    public string GetDisplayTime(DateTimeOffset date) => globalize.FormatDate(date, TimeOptions);

    /// <summary><c>datetime.toLocaleDateString(date, options)</c>; without options a numeric date.</summary>
    public string ToLocaleDateString(DateTimeOffset date, object? options = null) => globalize.FormatDate(date, options ?? DateOptions);

    /// <summary>
    /// A date that must show the same calendar day everywhere (episode air dates): the original
    /// parses these without converting to local time.
    /// </summary>
    public string ToLocaleDateStringUnconverted(DateTimeOffset date, object? options = null) =>
        ToLocaleDateString(date.AddMinutes(-browser.UtcOffsetMinutes(date)), options);

    /// <summary>A number without thousands separators, e.g. a year.</summary>
    public string FormatPlain(int value) => globalize.FormatNumber(value, NoGrouping);

    /// <summary>The calendar year of an instant in the user's time zone.</summary>
    public int LocalYear(DateTimeOffset date) => date.ToOffset(TimeSpan.FromMinutes(browser.UtcOffsetMinutes(date))).Year;
}
