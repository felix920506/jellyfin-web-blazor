using System.Globalization;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// Text and image helpers of the playback UI. Ports of the parts of scripts/datetime.js,
/// components/itemHelper.js, components/mediainfo/mediainfo.js and the now-playing helpers that the
/// OSD, the now-playing bar and the queue page use.
/// </summary>
[AppService]
public sealed class PlaybackText(Globalize globalize, JellyfinApi api, IPlaybackTimers timers, IBrowser browser)
{
    private const long TicksPerHour = 36000000000;
    private const long TicksPerMinute = 600000000;
    private const long TicksPerSecond = 10000000;

    /// <summary>Port of datetime.getDisplayRunningTime(): "1:02:03" or "2:03".</summary>
    public string RunningTime(long ticks)
    {
        ticks = Math.Max(ticks, 0);
        var parts = new List<string>(3);
        var hours = ticks / TicksPerHour;
        if (hours > 0)
        {
            parts.Add(Number(hours));
        }

        ticks -= hours * TicksPerHour;
        var minutes = ticks / TicksPerMinute;
        ticks -= minutes * TicksPerMinute;
        parts.Add(minutes < 10 && hours > 0 ? Number(0) + Number(minutes) : Number(minutes));

        var seconds = ticks / TicksPerSecond;
        parts.Add(seconds < 10 ? Number(0) + Number(seconds) : Number(seconds));
        return string.Join(':', parts);
    }

    /// <summary>Port of datetime.getDisplayDuration(): "1h 32m".</summary>
    public static string DisplayDuration(long ticks)
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

    /// <summary>Port of datetime.getDisplayTime(): the time of day in the user's locale.</summary>
    public string DisplayTime(DateTimeOffset value) => globalize.FormatDate(value, new { hour = "numeric", minute = "2-digit" });

    /// <summary>The programme shown by the OSD for a live channel, or the item itself otherwise.</summary>
    public static BaseItemDto DisplayItem(BaseItemDto item) =>
        item.TypeName() == "TvChannel" ? item.CurrentProgram ?? item : item;

    /// <summary>Whether the OSD should render the wall-clock programme progress bar.</summary>
    public static bool UsesLiveTvProgress(BaseItemDto? item) =>
        item?.TypeName() == "TvChannel" && item.CurrentProgram?.StartDate is not null && item.CurrentProgram.EndDate is not null;

    /// <summary>Returns the current live programme's wall-clock progress, clamped to 0..100.</summary>
    public LiveTvProgress? LiveTvProgress(BaseItemDto? item)
    {
        if (!UsesLiveTvProgress(item) || item!.CurrentProgram is not { StartDate: { } start, EndDate: { } end } || end <= start)
        {
            return null;
        }

        var now = DateTimeOffset.FromUnixTimeMilliseconds(timers.NowMs);
        var percent = Math.Clamp((now - start).TotalMilliseconds / (end - start).TotalMilliseconds * 100, 0, 100);
        return new LiveTvProgress(start, end, percent, DisplayTime(start), DisplayTime(end));
    }

    /// <summary>Port of mediaInfo.getEndsAtFromPosition().</summary>
    public string EndsAt(long runtimeTicks, long positionTicks, double playbackRate)
    {
        var rate = playbackRate > 0 ? playbackRate : 1;
        var endMs = timers.NowMs + (long)((1 / rate) * ((runtimeTicks - positionTicks) / 10000d));
        return globalize.Translate("EndsAtValue", DisplayTime(DateTimeOffset.FromUnixTimeMilliseconds(endMs)));
    }

    /// <summary>Port of itemHelper.getDisplayName().</summary>
    public string DisplayName(BaseItemDto item, bool includeParentInfo = true, bool includeIndexNumber = true)
    {
        var type = item.Type.ToApiString();
        var name = (type is "Program" or "Recording" && (item.IsSeries == true || !string.IsNullOrEmpty(item.EpisodeTitle))
            ? item.EpisodeTitle
            : item.Name) ?? string.Empty;

        if (type == "TvChannel")
        {
            return string.IsNullOrEmpty(item.ChannelNumber) ? name : item.ChannelNumber + " " + name;
        }

        if (type == "Episode" && item.ParentIndexNumber == 0)
        {
            return globalize.Translate("ValueSpecialEpisodeName", name);
        }

        if (type is "Episode" or "Program" or "Recording" && item.IndexNumber is { } index && item.ParentIndexNumber is { } parentIndex && includeIndexNumber)
        {
            var number = index.ToString(CultureInfo.InvariantCulture);
            var separator = " - ";
            if (includeParentInfo)
            {
                number = "S" + parentIndex.ToString(CultureInfo.InvariantCulture) + ":E" + number;
            }
            else
            {
                separator = ". ";
            }

            if (item.IndexNumberEnd is { } end && end != 0)
            {
                number += "-" + end.ToString(CultureInfo.InvariantCulture);
            }

            name = name.Length > 0 ? number + separator + name : number;
        }

        return name;
    }

    /// <summary>Port of the video OSD's title: "Series - S1:E2 - Name (2015)" or "Movie (2015)".</summary>
    public string OsdTitle(BaseItemDto item)
    {
        var type = item.Type.ToApiString();
        var parentName = !string.IsNullOrEmpty(item.SeriesName) ? item.SeriesName : item.Album;
        if (!string.IsNullOrEmpty(item.EpisodeTitle) || item.IsSeries == true)
        {
            parentName = item.Name;
        }

        var showParentInfo = type != "Program";
        var title = DisplayName(item, includeParentInfo: showParentInfo, includeIndexNumber: showParentInfo);
        if (!string.IsNullOrEmpty(parentName))
        {
            title = parentName + " - " + title;
        }

        // A movie shows its production year; anything else with a premiere date shows that year.
        int? year = null;
        if (type == "Movie" && item.ProductionYear is { } productionYear && productionYear != 0)
        {
            year = productionYear;
        }
        else if (item.PremiereDate is { } premiere)
        {
            year = premiere.ToOffset(TimeSpan.FromMinutes(browser.UtcOffsetMinutes(premiere))).Year;
        }

        if (year is { } value)
        {
            title += " (" + value.ToString(CultureInfo.InvariantCulture) + ")";
        }

        return title;
    }

    /// <summary>Port of nowPlayingHelper.getNowPlayingNames(): one or two lines of text for an item.</summary>
    public static IReadOnlyList<string> ItemTextLines(BaseItemDto item, bool includeYear = true)
    {
        var topText = item.Name ?? string.Empty;
        if (item.MediaType.ToApiString() == "Video")
        {
            if (item.IndexNumber is { } index)
            {
                topText = index.ToString(CultureInfo.InvariantCulture) + " - " + topText;
            }

            if (item.ParentIndexNumber is { } parentIndex)
            {
                topText = parentIndex.ToString(CultureInfo.InvariantCulture) + "." + topText;
            }
        }

        string? bottomText = null;
        if (item.ArtistItems is { Count: > 0 } artistItems)
        {
            bottomText = string.Join(", ", artistItems.Select(a => a.Name));
        }
        else if (item.Artists is { Count: > 0 } artists)
        {
            bottomText = string.Join(", ", artists);
        }
        else if (!string.IsNullOrEmpty(item.SeriesName) || !string.IsNullOrEmpty(item.Album))
        {
            bottomText = topText;
            topText = !string.IsNullOrEmpty(item.SeriesName) ? item.SeriesName : item.Album!;
        }
        else if (item.ProductionYear is { } year && year != 0 && includeYear)
        {
            bottomText = year.ToString(CultureInfo.InvariantCulture);
        }

        return string.IsNullOrEmpty(bottomText) ? [topText] : [topText, bottomText];
    }

    /// <summary>
    /// Port of the now-playing image lookup (seriesImageUrl()/imageUrl()): the series image for an
    /// episode, the item's own image, or its album's.
    /// </summary>
    public string? ImageUrl(BaseItemDto item, int? height = null, int? maxHeight = null)
    {
        string? itemId = null;
        string? tag = null;
        if (item.Type.ToApiString() == "Episode" && item.SeriesId is { } seriesId && !string.IsNullOrEmpty(item.SeriesPrimaryImageTag))
        {
            itemId = seriesId.ToString("N");
            tag = item.SeriesPrimaryImageTag;
        }
        else if (item.ImageTags?.AdditionalData is { } tags && tags.TryGetValue("Primary", out var primary) && primary?.ToString() is { Length: > 0 } primaryTag)
        {
            itemId = item.Id?.ToString("N");
            tag = primaryTag;
        }
        else if (item.AlbumId is { } albumId && !string.IsNullOrEmpty(item.AlbumPrimaryImageTag))
        {
            itemId = albumId.ToString("N");
            tag = item.AlbumPrimaryImageTag;
        }

        if (itemId is null || tag is null)
        {
            return null;
        }

        return api.GetUrl($"Items/{itemId}/Images/Primary", ("height", height), ("maxHeight", maxHeight), ("tag", tag));
    }

    /// <summary>Port of itemHelper.canRate().</summary>
    public static bool CanRate(BaseItemDto item) =>
        item.Type.ToApiString() is not ("Program" or "Timer" or "SeriesTimer" or "CollectionFolder" or "UserView" or "Channel")
        && item.UserData is not null;

    /// <summary>Port of the bitrate text of the stats overlay: "4.2 Mbps" or "320 Kbps".</summary>
    public static string DisplayBitrate(long bitrate) =>
        bitrate > 1000000
            ? (bitrate / 1000000d).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps"
            : (bitrate / 1000).ToString(CultureInfo.InvariantCulture) + " Kbps";

    /// <summary>Port of getReadableSize(): "1.4 GiB".</summary>
    public static string ReadableSize(long value)
    {
        if (value <= 0)
        {
            return "0 Bytes";
        }

        var power = (int)(Math.Log(value) / Math.Log(1024));
        var number = (value / Math.Pow(1024, power)).ToString("0.0", CultureInfo.InvariantCulture);
        return number + " " + (power > 0 ? "KMGTPEZY"[power - 1] + "iB" : "Bytes");
    }

    private string Number(long value) => globalize.FormatNumber(value);
}

/// <summary>The time-of-day values needed to render a live programme in the video OSD.</summary>
public sealed record LiveTvProgress(
    DateTimeOffset Start,
    DateTimeOffset End,
    double Percent,
    string StartText,
    string EndText);
