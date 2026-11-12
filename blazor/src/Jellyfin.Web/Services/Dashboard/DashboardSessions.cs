using System.Globalization;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>What a device card shows about the item a session plays.</summary>
public sealed record NowPlayingName(string? TopText, string BottomText, string? LogoUrl);

/// <summary>
/// The active sessions of the dashboard's devices widget. Port of
/// apps/dashboard/features/sessions (utils and useLiveSessions).
/// </summary>
[AppService]
public sealed class DashboardSessions(JellyfinApi api, IBrowser browser, Globalize globalize, RelativeTime relativeTime)
{
    /// <summary>Sessions seen within this many seconds count as active.</summary>
    public const int ActiveWithinSeconds = 960;

    private const int MinSessionActiveSeconds = 95;

    /// <summary>
    /// Sessions of a web socket update that are worth showing: recently active and either
    /// playing something or signed in. Port of filterSessions().
    /// </summary>
    public static List<SessionInfoDto> Filter(IEnumerable<SessionInfoDto> sessions, DateTimeOffset now)
    {
        var minActiveDate = now.AddSeconds(-MinSessionActiveSeconds);
        return sessions
            .Where(s => s.LastActivityDate is { } last && last >= minActiveDate && (s.NowPlayingItem is not null || s.UserId is not null))
            .ToList();
    }

    /// <summary>Applies an update to the shown list: known devices are replaced in place, new ones are appended.</summary>
    public static List<SessionInfoDto> Merge(IReadOnlyList<SessionInfoDto> current, IEnumerable<SessionInfoDto> update, DateTimeOffset now)
    {
        var merged = current.ToList();
        foreach (var session in Filter(update, now))
        {
            var index = merged.FindIndex(s => s.DeviceId == session.DeviceId);
            if (index < 0)
            {
                merged.Add(session);
            }
            else
            {
                merged[index] = session;
            }
        }

        return merged;
    }

    /// <summary>Port of getNowPlayingName().</summary>
    public NowPlayingName GetNowPlayingName(SessionInfoDto session)
    {
        if (session.NowPlayingItem is not { } item)
        {
            var lastSeen = session.LastActivityDate is { } last ? relativeTime.FromNow(last) : string.Empty;
            return new NowPlayingName(null, globalize.Translate("LastSeen", lastSeen), null);
        }

        var topText = ItemHelper.GetDisplayName(item, globalize);
        var bottomText = string.Empty;
        if (item.Artists is { Count: > 0 } artists)
        {
            bottomText = topText;
            topText = artists[0];
        }
        else if (!string.IsNullOrEmpty(item.SeriesName) || !string.IsNullOrEmpty(item.Album))
        {
            bottomText = topText;
            topText = !string.IsNullOrEmpty(item.SeriesName) ? item.SeriesName : item.Album;
        }
        else if (item.ProductionYear is { } year and not 0)
        {
            bottomText = year.ToString(CultureInfo.InvariantCulture);
        }

        string? logoUrl = null;
        if (item.ImageTag("Logo") is { } logoTag)
        {
            logoUrl = ImageUrl(item.Id, "Logo", logoTag, ("maxHeight", 24), ("maxWidth", 130));
        }
        else if (!string.IsNullOrEmpty(item.ParentLogoImageTag))
        {
            logoUrl = ImageUrl(item.ParentLogoItemId, "Logo", item.ParentLogoImageTag, ("maxHeight", 24), ("maxWidth", 130));
        }

        return new NowPlayingName(topText, bottomText, logoUrl);
    }

    /// <summary>The picture behind a device card. Port of getNowPlayingImageUrl().</summary>
    public string? GetNowPlayingImageUrl(BaseItemDto? item)
    {
        if (item is null)
        {
            return null;
        }

        // A fifth of the screen: the original cannot measure the card before it exists either.
        var maxWidth = ("maxWidth", (int)Math.Round(browser.Environment.ScreenWidth * 0.20, MidpointRounding.AwayFromZero));

        if (item.BackdropImageTags is { Count: > 0 } backdrops && item.Id is not null)
        {
            return ImageUrl(item.Id, "Backdrop", backdrops[0], maxWidth);
        }

        if (item.ParentBackdropImageTags is { Count: > 0 } parentBackdrops && item.ParentBackdropItemId is not null)
        {
            return ImageUrl(item.ParentBackdropItemId, "Backdrop", parentBackdrops[0], maxWidth);
        }

        if (item.Id is not null && item.ImageTag("Thumb") is { } thumb)
        {
            return ImageUrl(item.Id, "Thumb", thumb, maxWidth);
        }

        if (!string.IsNullOrEmpty(item.ParentThumbImageTag) && item.ParentThumbItemId is not null)
        {
            return ImageUrl(item.ParentThumbItemId, "Thumb", item.ParentThumbImageTag, maxWidth);
        }

        if (item.Id is not null && item.ImageTag("Primary") is { } primary)
        {
            return ImageUrl(item.Id, "Primary", primary, maxWidth);
        }

        if (!string.IsNullOrEmpty(item.AlbumPrimaryImageTag) && item.AlbumId is not null)
        {
            return ImageUrl(item.AlbumId, "Primary", item.AlbumPrimaryImageTag, maxWidth);
        }

        return null;
    }

    /// <summary>"12:34" position and length of what plays. Port of getSessionNowPlayingTime().</summary>
    public static (string Start, string End) GetNowPlayingTime(SessionInfoDto session)
    {
        var start = "0:00";
        var end = "0:00";
        if (session.NowPlayingItem is { } item)
        {
            if (session.PlayState?.PositionTicks is { } position and not 0)
            {
                start = DateTimeText.GetDisplayRunningTime(position);
            }

            if (item.RunTimeTicks is { } runtime and not 0)
            {
                end = DateTimeText.GetDisplayRunningTime(runtime);
            }
        }

        return (start, end);
    }

    /// <summary>How the stream is delivered, in a few lines. Port of getSessionNowPlayingStreamInfo().</summary>
    public string GetStreamInfo(SessionInfoDto session)
    {
        switch (PlayerStatsBuilder.GetDisplayPlayMethod(session))
        {
            case "DirectPlay":
                return globalize["DirectPlaying"];
            case "Remux":
                return globalize["Remuxing"];
            case "DirectStream":
                return globalize["DirectStreaming"];
            case "Transcode":
                break;
            default:
                return string.Empty;
        }

        var text = string.Empty;
        var info = session.TranscodingInfo;
        if (info?.Framerate is { } framerate and not 0)
        {
            text += $"{globalize["Framerate"]}: {framerate.ToString(CultureInfo.InvariantCulture)}fps";
        }

        var line = new List<string>();
        if (info is not null)
        {
            if (info.Bitrate is { } bitrate and not 0)
            {
                line.Add(bitrate > 1e6
                    ? (bitrate / 1e6).ToString("F1", CultureInfo.InvariantCulture) + " Mbps"
                    : (bitrate / 1000).ToString(CultureInfo.InvariantCulture) + " Kbps");
            }

            if (!string.IsNullOrEmpty(info.Container))
            {
                line.Add(info.Container.ToUpperInvariant());
            }

            if (!string.IsNullOrEmpty(info.VideoCodec))
            {
                line.Add(info.VideoCodec.ToUpperInvariant());
            }

            if (!string.IsNullOrEmpty(info.AudioCodec) && info.AudioCodec != info.Container)
            {
                line.Add(info.AudioCodec.ToUpperInvariant());
            }
        }

        if (line.Count > 0)
        {
            text += "\n\n" + string.Join(' ', line);
        }

        return text;
    }

    /// <summary>Title and explanation of the play method, for the card's info button.</summary>
    public (string Title, string Text)? GetPlaybackInfo(SessionInfoDto session)
    {
        switch (PlayerStatsBuilder.GetDisplayPlayMethod(session))
        {
            case "Remux":
                return (globalize["Remuxing"], globalize["RemuxHelp1"] + "\n" + globalize["RemuxHelp2"]);
            case "DirectStream":
                return (globalize["DirectStreaming"], globalize["DirectStreamHelp1"] + "\n" + globalize["DirectStreamHelp2"]);
            case "DirectPlay":
                return (globalize["DirectPlaying"], globalize["DirectPlayHelp"]);
            case "Transcode":
                var reasons = session.TranscodingInfo?.TranscodeReasons?
                    .Where(reason => reason is not null)
                    .Select(reason => globalize[reason!.Value.ToApiString()]) ?? [];
                return (
                    globalize["Transcoding"],
                    globalize["MediaIsBeingConverted"]
                    + "\n\n" + GetStreamInfo(session)
                    + "\n\n" + globalize["LabelReasonForTranscoding"]
                    + "\n" + string.Join('\n', reasons));
            default:
                return null;
        }
    }

    // apiClient.getScaledImageUrl(): sizes are multiplied by the device pixel ratio.
    private string ImageUrl(Guid? itemId, string type, string? tag, params (string Key, int Value)[] sizes)
    {
        var dpr = browser.Environment.DevicePixelRatio;
        var query = sizes
            .Select(size => (size.Key, (object?)(int)Math.Round(size.Value * dpr, MidpointRounding.AwayFromZero)))
            .Append(("tag", tag))
            .Append(("quality", type == "Backdrop" ? 80 : 90))
            .ToArray();
        return api.GetUrl($"Items/{itemId.IdText()}/Images/{type}", query);
    }
}
