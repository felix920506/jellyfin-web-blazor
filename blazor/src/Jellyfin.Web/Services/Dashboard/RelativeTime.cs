using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>
/// Distances between two moments in words ("5 minutes ago", "2 hours"), where the original
/// client uses date-fns' <c>formatDistance</c>/<c>formatDistanceToNow</c>. The wording comes
/// from the browser (<c>Intl.RelativeTimeFormat</c>, <c>Intl.NumberFormat</c>) in the user's
/// locale; before the dashboard module is loaded, English text is produced.
/// <para>
/// Deviation from date-fns: no "about"/"less than" qualifiers ("1 hour ago", not "about 1 hour ago").
/// </para>
/// </summary>
[AppService]
public sealed class RelativeTime(IDashboardInterop interop, Globalize globalize, DateTimeText clock, IBrowser browser)
{
    private static readonly object Weekday = new { weekday = "long" };

    /// <summary>
    /// A date relative to today, where the original uses date-fns' <c>formatRelative</c>:
    /// "today at 5:30 PM", "yesterday at 5:30 PM", "last Friday at 5:30 PM", "Friday at 5:30 PM"
    /// (within the coming week), otherwise the plain date.
    /// <para>
    /// Deviation: the joining words ("at", "last") only exist in date-fns' own locale data, so
    /// languages other than English read "yesterday 17:30" and "Friday 17:30".
    /// </para>
    /// </summary>
    public string Calendar(DateTimeOffset date)
    {
        var days = (LocalDay(date) - LocalDay(clock.Now)).Days;
        if (days is < -6 or >= 7)
        {
            return clock.ToLocaleDateString(date);
        }

        var english = globalize.DateTimeLocale.StartsWith("en", StringComparison.OrdinalIgnoreCase);
        var day = days is >= -1 and <= 1
            ? interop.RelativeDay(days, globalize.DateTimeLocale) ?? EnglishDays[days + 1]
            : (english && days < 0 ? "last " : null) + globalize.FormatDate(date, Weekday);
        return day + (english ? " at " : " ") + clock.GetDisplayTime(date);
    }

    /// <summary>"5 minutes ago" (or "in 5 minutes" for a future date), relative to now.</summary>
    public string FromNow(DateTimeOffset date)
    {
        var now = clock.Now;
        var (value, unit) = Distance(now - date);
        var signed = date <= now ? -value : value;
        return interop.RelativeTime(signed, unit, globalize.DateTimeLocale) ?? EnglishRelative(signed, unit);
    }

    /// <summary>"5 minutes": how long it is from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public string Between(DateTimeOffset start, DateTimeOffset end)
    {
        var (value, unit) = Distance(end - start);
        return Duration(value, unit);
    }

    /// <summary>"15 minutes" / "1 hour": a number with a unit ("minute", "hour", "day", "month", "year").</summary>
    public string Duration(double value, string unit) =>
        interop.Duration(value, unit, globalize.DateTimeLocale) ?? EnglishDuration(value, unit);

    /// <summary>
    /// The unit and rounded amount for a time span, with the thresholds of date-fns'
    /// <c>formatDistance</c>: minutes below 45 minutes, hours below a day, days below 30 days,
    /// months below a year, then years.
    /// </summary>
    public static (int Value, string Unit) Distance(TimeSpan span)
    {
        var minutes = (int)Math.Round(Math.Abs(span.TotalSeconds) / 60, MidpointRounding.AwayFromZero);
        if (minutes < 45)
        {
            return (Math.Max(1, minutes), "minute");
        }

        if (minutes < 24 * 60)
        {
            return (Math.Max(1, (int)Math.Round(minutes / 60d, MidpointRounding.AwayFromZero)), "hour");
        }

        var days = (int)Math.Round(minutes / (24d * 60), MidpointRounding.AwayFromZero);
        if (days < 30)
        {
            return (Math.Max(1, days), "day");
        }

        return days < 365
            ? (Math.Max(1, (int)Math.Round(days / 30d, MidpointRounding.AwayFromZero)), "month")
            : (days / 365, "year");
    }

    private static readonly string[] EnglishDays = ["yesterday", "today", "tomorrow"];

    // The calendar day of a moment on the user's clock.
    private DateTime LocalDay(DateTimeOffset date) => date.ToOffset(TimeSpan.FromMinutes(browser.UtcOffsetMinutes(date))).Date;

    internal static string EnglishRelative(double signedValue, string unit)
    {
        var text = EnglishDuration(Math.Abs(signedValue), unit);
        return signedValue > 0 ? "in " + text : text + " ago";
    }

    internal static string EnglishDuration(double value, string unit) =>
        FormattableString.Invariant($"{value:0.##} {unit}{(Math.Abs(value - 1) < 1e-9 ? null : "s")}");
}
