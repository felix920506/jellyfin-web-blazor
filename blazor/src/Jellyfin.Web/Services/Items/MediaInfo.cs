using System.Globalization;
using System.Net;
using System.Text;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>What <see cref="MediaInfo"/> includes; every part is on unless switched off, as in the original.</summary>
public sealed record MediaInfoOptions
{
    public bool Year { get; init; } = true;
    public bool Runtime { get; init; } = true;
    public bool OfficialRating { get; init; } = true;
    public bool StarRating { get; init; } = true;
    public bool CriticRating { get; init; } = true;
    public bool EndsAt { get; init; } = true;
    public bool OriginalAirDate { get; init; } = true;
    public bool EpisodeTitle { get; init; } = true;

    /// <summary>Whether a programme's episode title carries its number (item helper default when null).</summary>
    public bool? EpisodeTitleIndexNumber { get; init; }
    public bool ProgramIndicator { get; init; } = true;
    public bool Subtitles { get; init; } = true;
    public bool Container { get; init; } = true;

    // Secondary (programme) line.
    public bool ProgramTime { get; init; } = true;
    public bool StartDate { get; init; } = true;
    public bool TimerIndicator { get; init; } = true;

    /// <summary>Makes the channel name of the programme line a link.</summary>
    public bool Interactive { get; init; }
}

/// <summary>
/// Port of <c>components/mediainfo/mediainfo.js</c>: the line of facts under a title (year,
/// runtime, ratings, "Ends at"…). The methods return the original's HTML; the <c>MediaInfo</c>
/// component renders it and keeps the "Ends at" time current.
/// </summary>
[AppService]
public sealed class MediaInfo(Globalize globalize, DateTimeText dateTime, UserSettings userSettings, AppRouter router)
{
    private static readonly object ProgramDateOptions = new { weekday = "short", month = "short", day = "numeric" };

    /// <summary>The primary line (<c>getPrimaryMediaInfoHtml</c> / <c>getMediaInfoHtml</c>).</summary>
    public string GetPrimaryHtml(BaseItemDto item, MediaInfoOptions? options = null)
    {
        options ??= new MediaInfoOptions();
        var html = new StringBuilder();
        var type = item.TypeName();
        var mediaType = item.MediaTypeName();
        var extras = item.Extras();

        void Add(string? text, string? cssClass = null)
        {
            if (!string.IsNullOrEmpty(text))
            {
                html.Append(Item(text, cssClass));
            }
        }

        // The original also tests MediaType for values the server never sends (MusicArtist…).
        var showFolderRuntime = type is "MusicAlbum" or "Playlist";
        if (showFolderRuntime)
        {
            var count = item.SongCount is > 0 ? item.SongCount : item.ChildCount;
            if (count is > 0)
            {
                Add(globalize.Translate("TrackCount", count));
            }

            if (item.RunTimeTicks is > 0 and var folderTicks)
            {
                Add(DateTimeText.GetDisplayDuration(folderTicks));
            }
        }
        else if (type is "PhotoAlbum" or "BoxSet" && item.ChildCount is > 0)
        {
            Add(globalize.Translate("ItemCount", item.ChildCount));
        }

        if ((type == "Episode" || mediaType == "Photo") && options.OriginalAirDate && item.PremiereDate is { } premiere)
        {
            // Episodes store a calendar day: it must not shift with the time zone.
            Add(type == "Episode" ? dateTime.ToLocaleDateStringUnconverted(premiere) : dateTime.ToLocaleDateString(premiere));
        }

        if (type == "SeriesTimer")
        {
            Add(extras?.RecordAnyTime == true
                ? globalize.Translate("Anytime")
                : item.StartDate is { } timerStart ? dateTime.GetDisplayTime(timerStart) : null);
            Add(extras?.RecordAnyChannel == true
                ? globalize.Translate("AllChannels")
                : string.IsNullOrEmpty(item.ChannelName) ? globalize.Translate("OneChannel") : item.ChannelName);
        }

        if (item.StartDate is { } start && type is not ("Program" or "SeriesTimer" or "Timer"))
        {
            Add(dateTime.ToLocaleDateString(start));
            if (type != "Recording")
            {
                Add(dateTime.GetDisplayTime(start));
            }
        }

        if (options.Year && item.ProductionYear is > 0 and var seriesYear && type == "Series")
        {
            var text = dateTime.FormatPlain(seriesYear);
            if (item.Status == "Continuing")
            {
                Add(globalize.Translate("SeriesYearToPresent", text));
            }
            else
            {
                if (item.EndDate is { } endDate)
                {
                    var endYear = dateTime.FormatPlain(dateTime.LocalYear(endDate));
                    if (endYear != text)
                    {
                        text += " - " + endYear;
                    }
                }

                Add(text);
            }
        }

        if (type is "Program" or "Timer")
        {
            var program = type == "Timer" ? extras?.ProgramInfo : item;
            if (program is not null)
            {
                if (options.ProgramIndicator)
                {
                    html.Append(GetProgramIndicator(program));
                }

                if ((program.IsSeries == true || !string.IsNullOrEmpty(program.EpisodeTitle)) && options.EpisodeTitle)
                {
                    Add(ItemHelper.GetDisplayName(program, globalize, includeIndexNumber: options.EpisodeTitleIndexNumber));
                }
                else if (program.IsMovie == true && program.ProductionYear is > 0 && options.OriginalAirDate)
                {
                    Add(Plain(program.ProductionYear.Value));
                }
                else if (program.PremiereDate is { } programPremiere && options.OriginalAirDate)
                {
                    Add(globalize.Translate("OriginalAirDateValue", dateTime.ToLocaleDateString(programPremiere)));
                }
                else if (program.ProductionYear is > 0 && options.Year)
                {
                    Add(Plain(program.ProductionYear.Value));
                }
            }
        }

        if (options.Year && type is not ("Series" or "Episode" or "Person" or "Program" or "Season") && mediaType != "Photo")
        {
            if (item.ProductionYear is > 0)
            {
                Add(Plain(item.ProductionYear.Value));
            }
            else if (item.PremiereDate is { } premiereDate)
            {
                Add(dateTime.FormatPlain(dateTime.LocalYear(premiereDate)));
            }
        }

        if (item.RunTimeTicks is > 0 and var ticks && type is not ("Series" or "Program" or "Timer" or "Book") && !showFolderRuntime && options.Runtime)
        {
            Add(type == "Audio" ? DateTimeText.GetDisplayRunningTime(ticks) : DateTimeText.GetDisplayDuration(ticks));
        }

        if (options.OfficialRating && !string.IsNullOrEmpty(item.OfficialRating) && type is not ("Season" or "Episode"))
        {
            Add(item.OfficialRating, "mediaInfoText mediaInfoOfficialRating");
        }

        if (item.Video3DFormat is not null)
        {
            Add("3D");
        }

        if (mediaType == "Photo" && item.Width is > 0 && item.Height is > 0)
        {
            Add(string.Create(CultureInfo.InvariantCulture, $"{item.Width}x{item.Height}"));
        }

        if (options.Container && type == "Audio")
        {
            Add(item.Container);
        }

        if (options.StarRating && item.CommunityRating is { } rating && rating != 0)
        {
            html.Append("<div class=\"starRatingContainer mediaInfoItem\"><span class=\"material-icons starIcon star\" aria-hidden=\"true\"></span>")
                .Append(rating.ToString("0.0", CultureInfo.InvariantCulture))
                .Append("</div>");
        }

        if (item.HasSubtitles == true && options.Subtitles)
        {
            html.Append("<div class=\"mediaInfoItem mediaInfoText closedCaptionMediaInfoText\">CC</div>");
        }

        if (item.CriticRating is { } critic && critic != 0 && options.CriticRating)
        {
            var freshness = critic >= 60 ? "mediaInfoCriticRatingFresh" : "mediaInfoCriticRatingRotten";
            html.Append("<div class=\"mediaInfoItem mediaInfoCriticRating ").Append(freshness).Append("\">")
                .Append(critic.ToString(CultureInfo.InvariantCulture)).Append("</div>");
        }

        if (options.EndsAt && GetEndsAt(item) is { } endsAt)
        {
            html.Append("<div class=\"endsAt mediaInfoItem\">").Append(WebUtility.HtmlEncode(endsAt)).Append("</div>");
        }

        html.Append(Indicators.GetMissingIndicator(item, dateTime.Now));
        return html.ToString();
    }

    /// <summary>The programme line: air time, channel, timer icon (<c>getSecondaryMediaInfoHtml</c>). Empty for other types.</summary>
    public string GetSecondaryHtml(BaseItemDto item, MediaInfoOptions? options = null)
    {
        options ??= new MediaInfoOptions();
        if (item.TypeName() != "Program")
        {
            return string.Empty;
        }

        var html = new StringBuilder();
        if (item.StartDate is { } start && options.ProgramTime)
        {
            var text = options.StartDate ? dateTime.ToLocaleDateString(start, ProgramDateOptions) : string.Empty;
            text += " " + dateTime.GetDisplayTime(start);
            if (item.EndDate is { } end)
            {
                text += " - " + dateTime.GetDisplayTime(end);
            }

            html.Append(Item(text));
        }

        if (!string.IsNullOrEmpty(item.ChannelNumber))
        {
            html.Append(Item("CH " + item.ChannelNumber));
        }

        if (!string.IsNullOrEmpty(item.ChannelName))
        {
            if (options.Interactive && item.ChannelId is not null)
            {
                var url = router.GetRouteUrl(new BaseItemDto
                {
                    ServerId = item.ServerId,
                    Type = BaseItemDto_Type.TvChannel,
                    Name = item.ChannelName,
                    Id = item.ChannelId
                });
                html.Append("<a is=\"emby-linkbutton\" class=\"button-flat mediaInfoItem emby-button\" href=\"")
                    .Append(WebUtility.HtmlEncode(url)).Append("\">").Append(WebUtility.HtmlEncode(item.ChannelName)).Append("</a>");
            }
            else
            {
                html.Append(Item(item.ChannelName));
            }
        }

        if (options.TimerIndicator)
        {
            html.Append(GetTimerIcon(item));
        }

        return html.ToString();
    }

    /// <summary>"Ends at 9:45 PM" for a video that is not a broadcast (<c>getEndsAt</c>), else null.</summary>
    public string? GetEndsAt(BaseItemDto item)
    {
        if (item.MediaTypeName() == "Video" && item.RunTimeTicks is > 0 and var runtime && item.StartDate is null)
        {
            return GetEndsAtFromPosition(runtime, item.UserData?.PlaybackPositionTicks, 1, includeText: true);
        }

        return null;
    }

    /// <summary>When playback ends at the given rate (<c>getEndsAtFromPosition</c>).</summary>
    public string GetEndsAtFromPosition(long runtimeTicks, long? positionTicks, double playbackRate = 1, bool includeText = true)
    {
        var remaining = (runtimeTicks - (positionTicks ?? 0)) / 10000d / playbackRate;
        var displayTime = dateTime.GetDisplayTime(dateTime.Now.AddMilliseconds(remaining));
        return includeText ? globalize.Translate("EndsAtValue", displayTime) : displayTime;
    }

    /// <summary>"1080p", "4K", "SD"… of a video stream or item (<c>getResolutionText</c>).</summary>
    public static string? GetResolutionText(int? width, int? height, bool interlaced = false)
    {
        if (width is not > 0 || height is not > 0)
        {
            return null;
        }

        var suffix = interlaced ? "i" : "p";
        return (width, height) switch
        {
            ( >= 3800, _) or (_, >= 2000) => "4K",
            ( >= 2500, _) or (_, >= 1400) => "1440" + suffix,
            ( >= 1800, _) or (_, >= 1000) => "1080" + suffix,
            ( >= 1200, _) or (_, >= 700) => "720" + suffix,
            ( >= 700, _) or (_, >= 400) => "480" + suffix,
            _ => null
        };
    }

    private string GetProgramIndicator(BaseItemDto program)
    {
        bool Enabled(string name) => userSettings.Get("guide-indicator-" + name) == "true";

        string? cssClass = null;
        string? key = null;
        if (program.IsLive == true && Enabled("live"))
        {
            (cssClass, key) = ("liveTvProgram", "Live");
        }
        else if (program.IsPremiere == true && Enabled("premiere"))
        {
            (cssClass, key) = ("premiereTvProgram", "Premiere");
        }
        else if (program.IsSeries == true && program.IsRepeat != true && Enabled("new"))
        {
            (cssClass, key) = ("newTvProgram", "New");
        }
        else if (program.IsSeries == true && program.IsRepeat == true && Enabled("repeat"))
        {
            (cssClass, key) = ("repeatTvProgram", "Repeat");
        }

        return key is null
            ? string.Empty
            : $"<div class=\"mediaInfoProgramAttribute mediaInfoItem {cssClass}\">{WebUtility.HtmlEncode(globalize.Translate(key))}</div>";
    }

    private static string GetTimerIcon(BaseItemDto item)
    {
        const string Active = "<span class=\"material-icons mediaInfoItem mediaInfoIconItem mediaInfoTimerIcon fiber_smart_record\" aria-hidden=\"true\"></span>";
        var type = item.TypeName();
        string? status;
        if (type == "SeriesTimer")
        {
            return Active;
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
                ? Active
                : "<span class=\"material-icons mediaInfoItem mediaInfoIconItem fiber_smart_record\" aria-hidden=\"true\"></span>";
        }

        return "<span class=\"material-icons mediaInfoItem mediaInfoIconItem mediaInfoTimerIcon fiber_manual_record\" aria-hidden=\"true\"></span>";
    }

    private static string Item(string text, string? cssClass = null) =>
        $"<div class=\"{(cssClass is null ? "mediaInfoItem" : "mediaInfoItem " + cssClass)}\">{WebUtility.HtmlEncode(text)}</div>";

    private static string Plain(int value) => value.ToString(CultureInfo.InvariantCulture);
}
