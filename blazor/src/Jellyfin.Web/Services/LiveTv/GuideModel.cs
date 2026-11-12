using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Web.Services;

/// <summary>The channel request of the guide (<c>channelQuery</c> of <c>reloadGuide</c> in <c>components/guide/guide.js</c>).</summary>
public sealed record GuideChannelQuery(
    int StartIndex,
    int Limit,
    bool EnableFavoriteSorting,
    bool? IsNews = null,
    bool? IsSports = null,
    bool? IsKids = null,
    bool? IsMovie = null,
    bool? IsSeries = null,
    string? SortBy = null,
    string? SortOrder = null);

/// <summary>
/// A programme of the guide. The guide shows thousands of them, so they are read straight from
/// the response instead of becoming full <c>BaseItemDto</c>s.
/// </summary>
public sealed class GuideProgram
{
    public string Id { get; init; } = string.Empty;

    public string? ServerId { get; init; }

    public string ChannelId { get; init; } = string.Empty;

    public string? Name { get; init; }

    public string? EpisodeTitle { get; init; }

    public string Type { get; init; } = "Program";

    /// <summary>The start as the server wrote it (the cell's <c>data-startdate</c>).</summary>
    public string? StartText { get; init; }

    /// <summary>The end as the server wrote it (the cell's <c>data-enddate</c>).</summary>
    public string? EndText { get; init; }

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public bool IsKids { get; init; }

    public bool IsSports { get; init; }

    public bool IsNews { get; init; }

    public bool IsMovie { get; init; }

    public bool IsSeries { get; init; }

    public bool IsLive { get; init; }

    public bool IsPremiere { get; init; }

    public bool IsRepeat { get; init; }

    public bool IsHD { get; init; }

    /// <summary>The timer recording the programme; changes with the server's timer messages.</summary>
    public string? TimerId { get; set; }

    /// <summary>The series timer recording the programme.</summary>
    public string? SeriesTimerId { get; set; }

    /// <summary>The status of the timer ("Cancelled" draws the series icon inactive).</summary>
    public string? Status { get; set; }

    /// <summary>The programmes of a response body (<c>{ Items: [...] }</c>), in the order the server sent them.</summary>
    public static IReadOnlyList<GuideProgram> ParseList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Items", out var items))
        {
            root = items;
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var programs = new List<GuideProgram>(root.GetArrayLength());
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object && Date(element, "StartDate") is { } start && Date(element, "EndDate") is { } end)
            {
                programs.Add(new GuideProgram
                {
                    Id = Compact(Text(element, "Id")) ?? string.Empty,
                    ServerId = Text(element, "ServerId"),
                    ChannelId = Compact(Text(element, "ChannelId")) ?? string.Empty,
                    Name = Text(element, "Name"),
                    EpisodeTitle = Text(element, "EpisodeTitle"),
                    Type = Text(element, "Type") ?? "Program",
                    StartText = Text(element, "StartDate"),
                    EndText = Text(element, "EndDate"),
                    Start = start,
                    End = end,
                    IsKids = Flag(element, "IsKids"),
                    IsSports = Flag(element, "IsSports"),
                    IsNews = Flag(element, "IsNews"),
                    IsMovie = Flag(element, "IsMovie"),
                    IsSeries = Flag(element, "IsSeries"),
                    IsLive = Flag(element, "IsLive"),
                    IsPremiere = Flag(element, "IsPremiere"),
                    IsRepeat = Flag(element, "IsRepeat"),
                    IsHD = Flag(element, "IsHD"),
                    TimerId = Text(element, "TimerId"),
                    SeriesTimerId = Text(element, "SeriesTimerId"),
                    Status = Text(element, "Status")
                });
            }
        }

        return programs;
    }

    /// <summary>A date member of a JSON object; null when absent or not a date.</summary>
    public static DateTimeOffset? Date(JsonElement element, string name) =>
        Text(element, name) is { Length: > 0 } text
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value)
            ? value
            : null;

    /// <summary>An id without dashes, the way the items of the port write theirs.</summary>
    public static string? Compact(string? id) => Guid.TryParse(id, out var value) ? value.ToString("N") : id;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}

/// <summary>
/// What the guide draws, from the user's settings (<c>guide-indicator-*</c>,
/// <c>guide-colorcodedbackgrounds</c>, <c>livetv-*</c>) and the categories of the settings dialog,
/// which the original keeps in memory only.
/// </summary>
public sealed record GuideOptions
{
    public bool ShowHdIcon { get; init; }

    public bool ShowLiveIndicator { get; init; }

    public bool ShowPremiereIndicator { get; init; }

    public bool ShowNewIndicator { get; init; }

    public bool ShowRepeatIndicator { get; init; }

    public bool ShowEpisodeTitle { get; init; } = true;

    public bool ColorCodedBackgrounds { get; init; }

    public bool FavoriteChannelsAtTop { get; init; } = true;

    /// <summary>"Number" or "DatePlayed".</summary>
    public string ChannelOrder { get; init; } = "Number";

    /// <summary>The categories picked in the settings dialog; empty shows everything.</summary>
    public IReadOnlyList<string> Categories { get; init; } = [];

    public bool DisplayMovies => Displays("movies");

    public bool DisplaySports => Displays("sports");

    public bool DisplayNews => Displays("news");

    public bool DisplayKids => Displays("kids");

    public bool DisplaySeries => Displays("series");

    /// <summary>The window width below which the guide draws no indicators (<c>allowIndicators</c>).</summary>
    public const int MinIndicatorWidth = 600;

    /// <summary>The options for a window of the given width.</summary>
    public static GuideOptions From(UserSettings settings, IReadOnlyList<string> categories, int viewportWidth)
    {
        var indicators = viewportWidth >= MinIndicatorWidth;
        return new GuideOptions
        {
            ShowHdIcon = indicators && settings.Get("guide-indicator-hd") == "true",
            ShowLiveIndicator = indicators && settings.Get("guide-indicator-live") != "false",
            ShowPremiereIndicator = indicators && settings.Get("guide-indicator-premiere") != "false",
            ShowNewIndicator = indicators && settings.Get("guide-indicator-new") != "false",
            ShowRepeatIndicator = indicators && settings.Get("guide-indicator-repeat") == "true",
            ColorCodedBackgrounds = settings.Get("guide-colorcodedbackgrounds") == "true",
            FavoriteChannelsAtTop = settings.Get("livetv-favoritechannelsattop") != "false",
            ChannelOrder = settings.Get("livetv-channelorder") is { Length: > 0 } order ? order : "Number",
            Categories = categories
        };
    }

    /// <summary>The channel request of one page of the guide.</summary>
    public GuideChannelQuery ChannelQuery(int startIndex, int limit)
    {
        var all = DisplayMovies && DisplaySports && DisplayNews && DisplayKids;
        var byDate = ChannelOrder == "DatePlayed";
        return new GuideChannelQuery(
            startIndex,
            limit,
            FavoriteChannelsAtTop,
            IsNews: !all && DisplayNews ? true : null,
            IsSports: !all && DisplaySports ? true : null,
            IsKids: !all && DisplayKids ? true : null,
            IsMovie: !all && DisplayMovies ? true : null,
            IsSeries: !all && DisplaySeries ? true : null,
            SortBy: byDate ? "DatePlayed" : null,
            SortOrder: byDate ? "Descending" : null);
    }

    private bool Displays(string category) => Categories.Count == 0 || Categories.Contains(category);
}

/// <summary>A day of the guide's date tabs.</summary>
/// <param name="Date">The moment the day's grid starts: the current hour for the first day, midnight for the others.</param>
/// <param name="Day">The day of the month in the viewer's time.</param>
/// <param name="Active">Whether it is the day the guide shows.</param>
public sealed record GuideDateTab(DateTimeOffset Date, int Day, bool Active);

/// <summary>The result of <c>setDateRange</c>: the tabs and where the grid starts and scrolls to.</summary>
public sealed record GuideDateRange(IReadOnlyList<GuideDateTab> Tabs, DateTimeOffset Date, double ScrollToTimeMs, double StartTimeOfDayMs);

/// <summary>
/// The dates of the guide in the viewer's time. The runtime has no time zones (invariant
/// globalization), so local times are plain <see cref="DateTime"/>s shifted by the browser's
/// offset (<see cref="IBrowser.UtcOffsetMinutes"/>).
/// </summary>
public sealed class GuideClock(Func<DateTimeOffset, int> utcOffsetMinutes)
{
    public const double MsPerDay = 86400000;

    /// <summary>The length of a timeslot of the header, half an hour.</summary>
    public const double CellDurationMs = 30 * 60 * 1000;

    /// <summary>The viewer's wall clock at a moment.</summary>
    public DateTime ToLocal(DateTimeOffset moment) =>
        DateTime.SpecifyKind(moment.UtcDateTime.AddMinutes(utcOffsetMinutes(moment)), DateTimeKind.Unspecified);

    /// <summary>
    /// The moment the viewer's wall clock shows a time. The original builds these dates with
    /// <c>new Date()</c> and then changes the hours, so the offset is applied once: the lookup
    /// is made at the UTC reading of the wall-clock numbers, and that offset is attached without
    /// moving the numbers again.
    /// </summary>
    public DateTimeOffset FromLocal(DateTime local)
    {
        var reading = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Utc));
        return new DateTimeOffset(
            DateTime.SpecifyKind(local, DateTimeKind.Unspecified),
            TimeSpan.FromMinutes(utcOffsetMinutes(reading)));
    }

    /// <summary>The time of day of a moment in milliseconds, without seconds (<c>getHours() / getMinutes()</c>).</summary>
    public double TimeOfDayMs(DateTimeOffset moment)
    {
        var local = ToLocal(moment);
        return ((local.Hour * 60) + local.Minute) * 60000d;
    }

    /// <summary>Rounds a moment down to the half hour (<c>normalizeDateToTimeslot</c>).</summary>
    public DateTimeOffset NormalizeToTimeslot(DateTimeOffset moment)
    {
        var local = ToLocal(moment);
        return FromLocal(new DateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute >= 30 ? 30 : 0, 0));
    }

    /// <summary>
    /// The date tabs for the span of the guide data and the day to show (<c>setDateRange</c>):
    /// from the current hour of today to the last day of the data, at least one day.
    /// </summary>
    public GuideDateRange DateRange(GuideInfo info, DateTimeOffset now, DateTimeOffset? currentDate)
    {
        // setDateRange: the current hour of today, the guide start at that hour, the guide end
        // at local midnight, then start = max(today, start). A span that does not cover a day
        // still yields one tab (end.setDate(start.getDate() + 1) keeps the cleared hour).
        var nowLocal = ToLocal(now);
        var hour = nowLocal.Hour;
        var today = nowLocal.Date.AddHours(hour);
        var start = ToLocal(info.StartDate).Date.AddHours(hour);
        var end = ToLocal(info.EndDate).Date;
        if (start >= end)
        {
            end = start.AddDays(1).Date;
        }

        if (today > start)
        {
            start = today;
        }

        var date = currentDate is { } selected ? ToLocal(selected) : nowLocal;
        date = date.Date.AddHours(hour);
        var startTimeOfDayMs = ((start.Hour * 60) + start.Minute) * 60000d;
        var tabs = new List<GuideDateTab>();
        while (start <= end)
        {
            tabs.Add(new GuideDateTab(FromLocal(start), start.Day, start.Date == date.Date));
            // setDate() keeps the hour, then the original clears it for every day after the first.
            start = start.AddDays(1).Date;
        }

        var scrollToTimeMs = (nowLocal.Hour * 3600000d) + (nowLocal.Minute >= 30 ? 30 * 60000d : 0);
        return new GuideDateRange(tabs, NormalizeToTimeslot(FromLocal(date)), scrollToTimeMs, startTimeOfDayMs);
    }

    /// <summary>The 48 half hours of the header from the start of the grid (<c>getTimeslotHeadersHtml</c>).</summary>
    public static IReadOnlyList<DateTimeOffset> Timeslots(DateTimeOffset start)
    {
        var slots = new List<DateTimeOffset>(48);
        for (var slot = start; slot < start.AddMilliseconds(MsPerDay); slot = slot.AddMilliseconds(CellDurationMs))
        {
            slots.Add(slot);
        }

        return slots;
    }
}

/// <summary>The translated words of the programme cells.</summary>
public sealed record GuideLabels(string Live, string Premiere, string New, string Repeat);

/// <summary>
/// The markup of the programme rows, written as text exactly as <c>getChannelProgramsHtml</c> of
/// <c>components/guide/guide.js</c> writes it: a day of a few hundred channels is tens of
/// thousands of elements, too many for the renderer to diff one by one.
/// </summary>
public static class GuideHtml
{
    /// <summary>The cells of one channel's row (the inside of <c>.channelPrograms</c>).</summary>
    /// <param name="programs">The channel's programmes by start.</param>
    /// <param name="date">The moment the grid starts.</param>
    /// <param name="options">What to draw.</param>
    /// <param name="now">The current moment, for <c>programCell-active</c>.</param>
    /// <param name="labels">The words of the indicators.</param>
    public static string ChannelPrograms(IEnumerable<GuideProgram> programs, DateTimeOffset date, GuideOptions options, DateTimeOffset now, GuideLabels labels)
    {
        var html = new StringBuilder();
        var startMs = (double)date.ToUnixTimeMilliseconds();
        var endMs = startMs + GuideClock.MsPerDay - 1;
        var nowMs = (double)now.ToUnixTimeMilliseconds();
        foreach (var program in programs)
        {
            var programStart = (double)program.Start.ToUnixTimeMilliseconds();
            var programEnd = (double)program.End.ToUnixTimeMilliseconds();
            if (programEnd < startMs)
            {
                continue;
            }

            if (programStart > endMs)
            {
                break;
            }

            var startPercent = Math.Max(0, (programStart - startMs) / GuideClock.MsPerDay * 100);
            var endPercent = (Math.Min(programEnd, endMs) - Math.Max(programStart, startMs)) / GuideClock.MsPerDay * 100;
            var (displayed, accent) = Category(program, options);
            var cssClass = "programCell itemAction";
            if (displayed && options.ColorCodedBackgrounds && accent is not null)
            {
                cssClass += " programCell-" + accent;
            }

            if (nowMs >= programStart && nowMs < programEnd)
            {
                cssClass += " programCell-active";
            }

            html.Append("<button");

            // Only cells wide enough to matter slide their name into view while the grid scrolls.
            if (endPercent >= 2)
            {
                html.Append(" is=\"emby-programcell\"");
            }

            html.Append(" data-action=\"programdialog\"");
            Attribute(html, "data-timerid", program.TimerId);
            Attribute(html, "data-seriestimerid", program.SeriesTimerId);
            Attribute(html, "data-channelid", program.ChannelId);
            Attribute(html, "data-id", program.Id);
            Attribute(html, "data-serverid", program.ServerId);
            Attribute(html, "data-startdate", program.StartText);
            Attribute(html, "data-enddate", program.EndText);
            Attribute(html, "data-type", program.Type);
            html.Append(" class=\"").Append(cssClass).Append("\" style=\"left:").Append(Number(startPercent)).Append("%;width:")
                .Append(Number(endPercent)).Append("%;\">");
            if (displayed)
            {
                AppendContent(html, program, options, labels);
            }

            html.Append("</button>");
        }

        return html.ToString();
    }

    /// <summary>Whether a programme's text is shown under the picked categories, and its colour class.</summary>
    public static (bool Displayed, string? Accent) Category(GuideProgram program, GuideOptions options)
    {
        if (program.IsKids)
        {
            return (options.DisplayKids, "kids");
        }

        if (program.IsSports)
        {
            return (options.DisplaySports, "sports");
        }

        if (program.IsNews)
        {
            return (options.DisplayNews, "news");
        }

        if (program.IsMovie)
        {
            return (options.DisplayMovies, "movie");
        }

        return program.IsSeries
            ? (options.DisplaySeries, null)
            : (options.DisplayMovies && options.DisplayNews && options.DisplaySports && options.DisplayKids && options.DisplaySeries, null);
    }

    /// <summary>The timer icon of a programme (<c>getTimerIndicator</c>); empty without a timer.</summary>
    public static string TimerIndicator(GuideProgram program)
    {
        if (!string.IsNullOrEmpty(program.SeriesTimerId))
        {
            var inactive = program.Status is null or "Cancelled" ? " seriesTimerIcon-inactive" : string.Empty;
            return $"<span class=\"material-icons programIcon seriesTimerIcon{inactive} fiber_smart_record\" aria-hidden=\"true\"></span>";
        }

        return !string.IsNullOrEmpty(program.TimerId)
            ?"<span class=\"material-icons programIcon timerIcon fiber_manual_record\" aria-hidden=\"true\"></span>"
            : string.Empty;
    }

    private static void AppendContent(StringBuilder html, GuideProgram program, GuideOptions options, GuideLabels labels)
    {
        html.Append("<div class=\"guideProgramName\"><div class=\"guide-programNameCaret hide\">")
            .Append("<span class=\"guideProgramNameCaretIcon material-icons keyboard_arrow_left\" aria-hidden=\"true\"></span></div>")
            .Append("<div class=\"guideProgramNameText\">").Append(WebUtility.HtmlEncode(program.Name));

        if (program.IsLive && options.ShowLiveIndicator)
        {
            Indicator(html, "liveTvProgram", labels.Live);
        }
        else if (program.IsPremiere && options.ShowPremiereIndicator)
        {
            Indicator(html, "premiereTvProgram", labels.Premiere);
        }
        else if (program.IsSeries && !program.IsRepeat && options.ShowNewIndicator)
        {
            Indicator(html, "newTvProgram", labels.New);
        }
        else if (program.IsSeries && program.IsRepeat && options.ShowRepeatIndicator)
        {
            Indicator(html, "repeatTvProgram", labels.Repeat);
        }

        if (options.ShowEpisodeTitle && !string.IsNullOrEmpty(program.EpisodeTitle))
        {
            html.Append("<div class=\"guideProgramSecondaryInfo\"><span class=\"programSecondaryTitle\">")
                .Append(WebUtility.HtmlEncode(program.EpisodeTitle)).Append("</span></div>");
        }

        html.Append("</div>");
        if (program.IsHD && options.ShowHdIcon)
        {
            html.Append("<div class=\"programIcon guide-programTextIcon\">HD</div>");
        }

        html.Append(TimerIndicator(program)).Append("</div>");
    }

    private static void Indicator(StringBuilder html, string cssClass, string text) =>
        html.Append("<span class=\"").Append(cssClass).Append(" guideProgramIndicator\">").Append(WebUtility.HtmlEncode(text)).Append("</span>");

    private static void Attribute(StringBuilder html, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            html.Append(' ').Append(name).Append("=\"").Append(WebUtility.HtmlEncode(value)).Append('"');
        }
    }

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
