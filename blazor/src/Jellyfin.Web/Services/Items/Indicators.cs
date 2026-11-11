using System.Globalization;

using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// Port of <c>components/indicators/indicators.js</c>. Each method returns the original's HTML
/// (or "" when the indicator does not apply); render it with <c>@((MarkupString)html)</c> or use
/// the <c>ItemIndicators</c> component.
/// </summary>
public static class Indicators
{
    public static bool EnableProgressIndicator(BaseItemDto item) =>
        (item.MediaTypeName() == "Video" && item.TypeName() != "TvChannel") || item.TypeName() is "Book" or "AudioBook";

    /// <summary>A progress bar filled to <paramref name="percent"/> (<c>getProgressHtml</c>).</summary>
    public static string GetProgressHtml(double percent, string? containerClass = null) =>
        $"<div class=\"{ContainerClass(containerClass)}\"><div class=\"itemProgressBarForeground\" style=\"width:{Number(percent)}%;\"></div></div>";

    /// <summary>
    /// The resume progress of an item, or for a programme, timer or recording that is on air the
    /// elapsed share of its time slot (<c>getProgressBarHtml</c>). items.js keeps the time based
    /// bars moving.
    /// </summary>
    public static string GetProgressBarHtml(BaseItemDto item, DateTimeOffset now, string? containerClass = null)
    {
        var type = item.TypeName();
        if (EnableProgressIndicator(item) && type != "Recording"
            && item.UserData?.PlayedPercentage is { } played && played != 0 && played < 100)
        {
            return GetProgressHtml(played, containerClass);
        }

        if (type is "Program" or "Timer" or "Recording" && item.StartDate is { } start && item.EndDate is { } end)
        {
            var total = (end - start).TotalMilliseconds;
            var percent = 100 * ((now - start).TotalMilliseconds / total);
            if (percent > 0 && percent < 100)
            {
                var recording = type is "Timer" or "Recording" || !string.IsNullOrEmpty(item.TimerId);
                var foreground = recording ? "itemProgressBarForeground itemProgressBarForeground-recording" : "itemProgressBarForeground";
                return $"<div is=\"emby-progressbar\" data-automode=\"time\" data-starttime=\"{start.ToUnixTimeMilliseconds()}\" data-endtime=\"{end.ToUnixTimeMilliseconds()}\" class=\"{ContainerClass(containerClass)}\"><div class=\"{foreground}\" style=\"width:{Number(percent)}%;\"></div></div>";
            }
        }

        return string.Empty;
    }

    /// <summary>The played check mark or the count of unplayed children (<c>getPlayedIndicatorHtml</c>).</summary>
    public static string GetPlayedIndicatorHtml(BaseItemDto item)
    {
        if (!ItemHelper.CanMarkPlayed(item) || item.UserData is not { } userData)
        {
            return string.Empty;
        }

        if (userData.UnplayedItemCount is { } count && count > 0)
        {
            return CountIndicator(count);
        }

        return userData.PlayedPercentage >= 100 || userData.Played == true
            ? "<div class=\"playedIndicator indicator\"><span class=\"material-icons indicatorIcon check\" aria-hidden=\"true\"></span></div>"
            : string.Empty;
    }

    /// <summary>The number of children when above <paramref name="minCount"/> (<c>getChildCountIndicatorHtml</c>).</summary>
    public static string GetChildCountIndicatorHtml(BaseItemDto item, int minCount = 0) =>
        item.ChildCount is { } count && count > minCount ? CountIndicator(count) : string.Empty;

    /// <summary>The recording dot of timers and programmes with a timer (<c>getTimerIndicator</c>).</summary>
    public static string GetTimerIndicator(BaseItemDto item)
    {
        var type = item.TypeName();
        string? status;
        if (type == "SeriesTimer")
        {
            return "<span class=\"material-icons timerIndicator indicatorIcon fiber_smart_record\" aria-hidden=\"true\"></span>";
        }

        if (!string.IsNullOrEmpty(item.TimerId) || !string.IsNullOrEmpty(item.SeriesTimerId))
        {
            status = string.IsNullOrEmpty(item.Status) ? "Cancelled" : item.Status;
        }
        else if (type == "Timer")
        {
            status = item.Status;
        }
        else
        {
            return string.Empty;
        }

        if (!string.IsNullOrEmpty(item.SeriesTimerId))
        {
            return status != "Cancelled"
                ? "<span class=\"material-icons timerIndicator indicatorIcon fiber_smart_record\" aria-hidden=\"true\"></span>"
                : "<span class=\"material-icons timerIndicator timerIndicator-inactive indicatorIcon fiber_smart_record\" aria-hidden=\"true\"></span>";
        }

        return "<span class=\"material-icons timerIndicator indicatorIcon fiber_manual_record\" aria-hidden=\"true\"></span>";
    }

    /// <summary>Offline sync state (<c>getSyncIndicator</c>); only hosts with downloads set it.</summary>
    public static string GetSyncIndicator(BaseItemDto item) => item.Extras()?.SyncPercent switch
    {
        null => string.Empty,
        100 => "<div class=\"syncIndicator indicator fullSyncIndicator\"><span class=\"material-icons indicatorIcon file_download\" aria-hidden=\"true\"></span></div>",
        _ => "<div class=\"syncIndicator indicator emptySyncIndicator\"><span class=\"material-icons indicatorIcon file_download\" aria-hidden=\"true\"></span></div>"
    };

    /// <summary>The media kind badge of videos, folders and photos (<c>getTypeIndicator</c>).</summary>
    public static string GetTypeIndicator(BaseItemDto item)
    {
        var icon = item.TypeName() switch
        {
            "Video" => "videocam",
            "Folder" => "folder",
            "PhotoAlbum" => "photo_album",
            "Photo" => "photo",
            _ => null
        };
        return icon is null
            ? string.Empty
            : $"<div class=\"indicator videoIndicator\"><span class=\"material-icons indicatorIcon {icon}\" aria-hidden=\"true\"></span></div>";
    }

    /// <summary>"Unaired" / "Missing" for episodes without a file (<c>getMissingIndicator</c>; not translated in the original either).</summary>
    public static string GetMissingIndicator(BaseItemDto item, DateTimeOffset now)
    {
        if (item.TypeName() != "Episode" || item.LocationType != BaseItemDto_LocationType.Virtual)
        {
            return string.Empty;
        }

        return item.PremiereDate is { } premiere && premiere > now
            ? "<div class=\"unairedIndicator\">Unaired</div>"
            : "<div class=\"missingIndicator\">Missing</div>";
    }

    private static string CountIndicator(int count) =>
        $"<div class=\"countIndicator indicator\">{(count >= 100 ? "99+" : count.ToString(CultureInfo.InvariantCulture))}</div>";

    private static string ContainerClass(string? containerClass) =>
        string.IsNullOrEmpty(containerClass) ? "itemProgressBar" : "itemProgressBar " + containerClass;

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
