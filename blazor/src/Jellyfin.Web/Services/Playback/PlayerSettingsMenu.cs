using System.Globalization;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Port of components/playback/playersettingsmenu.js: the settings menu of the video OSD.</summary>
[AppService]
public sealed class PlayerSettingsMenu(PlaybackManager playback, AppDialogs dialogs, Globalize globalize, JellyfinApi api)
{
    /// <summary>Shows the menu; returns "stats" when the playback info overlay was chosen.</summary>
    /// <param name="quality">Offer the quality menu (the source can be transcoded).</param>
    /// <param name="stats">Offer the playback info overlay.</param>
    public async Task<string?> ShowAsync(bool quality, bool stats)
    {
        var supportedCommands = playback.GetSupportedCommands();
        var items = new List<ActionSheetItem>();

        if (supportedCommands.Contains("SetAspectRatio"))
        {
            var current = PlaybackManager.AspectRatios.FirstOrDefault(a => a.Id == playback.AspectRatio);
            items.Add(new ActionSheetItem
            {
                Name = globalize["AspectRatio"],
                Id = "aspectratio",
                AsideText = current.NameKey is null ? null : globalize[current.NameKey],
            });
        }

        if (supportedCommands.Contains("PlaybackRate"))
        {
            var rate = playback.PlaybackRate;
            items.Add(new ActionSheetItem
            {
                Name = globalize["PlaybackRate"],
                Id = "playbackrate",
                AsideText = PlaybackManager.PlaybackRates.Contains(rate) ? RateName(rate) : null,
            });
        }

        if (quality && supportedCommands.Contains("SetMaxStreamingBitrate") && api.User?.Policy?.EnableVideoPlaybackTranscoding == true)
        {
            items.Add(new ActionSheetItem { Name = globalize["Quality"], Id = "quality", AsideText = QualitySecondaryText() });
        }

        var repeatMode = playback.RepeatMode;
        if (supportedCommands.Contains("SetRepeatMode") && (playback.CurrentMediaSource?.RunTimeTicks ?? 0) != 0)
        {
            items.Add(new ActionSheetItem
            {
                Name = globalize["RepeatMode"],
                Id = "repeatmode",
                AsideText = repeatMode == PlayQueue.RepeatNone ? globalize["None"] : globalize[repeatMode],
            });
        }

        if (playback.SupportsSubtitleOffset)
        {
            items.Add(new ActionSheetItem
            {
                Name = globalize["SubtitleOffset"],
                Id = "suboffset",
                AsideText = playback.SubtitleOffset.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture) + "s",
            });
        }
        if (stats)
        {
            items.Add(new ActionSheetItem { Name = globalize["PlaybackData"], Id = "stats" });
        }

        var id = await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = items });
        switch (id)
        {
            case "quality":
                await ShowQualityMenuAsync();
                break;
            case "aspectratio":
                await ShowAspectRatioMenuAsync();
                break;
            case "playbackrate":
                await ShowPlaybackRateMenuAsync();
                break;
            case "repeatmode":
                await ShowRepeatModeMenuAsync();
                break;
            case "stats":
                return "stats";
            case "suboffset":
                return "suboffset";
        }

        return null;
    }

    /// <summary>The quality options for the current video.</summary>
    public IReadOnlyList<QualityOption> VideoQualityOptions()
    {
        var videoStream = (playback.CurrentMediaSource?.MediaStreams ?? []).FirstOrDefault(s => s.Type.ToApiString() == "Video");
        return QualityOptions.GetVideoQualityOptions(
            playback.GetMaxStreamingBitrate(),
            playback.EnableAutomaticBitrateDetection(),
            videoStream?.BitRate,
            videoStream?.Codec,
            enableAuto: true,
            autoName: globalize["Auto"]);
    }

    /// <summary>Port of getQualitySecondaryText().</summary>
    public string? QualitySecondaryText()
    {
        var selected = VideoQualityOptions().FirstOrDefault(o => o.Selected);
        if (selected is null)
        {
            return null;
        }

        var text = selected.Name;
        if (!string.IsNullOrEmpty(selected.AutoText))
        {
            text += playback.PlayMethod != PlayMethods.Transcode ? " - Direct" : " " + selected.AutoText;
        }

        return text;
    }

    private static string RateName(double rate) => rate.ToString("0.##", CultureInfo.InvariantCulture) + "x";

    private async Task ShowQualityMenuAsync()
    {
        var options = VideoQualityOptions();
        var items = options
            .Select(o => new ActionSheetItem { Name = o.Name, Id = QualityOptions.IdOf(o), Selected = o.Selected })
            .ToList();
        int? selectedBitrate = options.FirstOrDefault(o => o.Selected)?.Bitrate;

        var id = await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = items });
        if (id is null || !int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrate))
        {
            return;
        }

        if (bitrate != selectedBitrate)
        {
            await playback.SetMaxStreamingBitrateAsync(enableAutomaticBitrateDetection: bitrate == 0, bitrate);
        }
    }

    private async Task ShowRepeatModeMenuAsync()
    {
        var current = playback.RepeatMode;
        var items = new List<ActionSheetItem>
        {
            new() { Name = globalize["RepeatAll"], Id = PlayQueue.RepeatAll, Selected = current == PlayQueue.RepeatAll },
            new() { Name = globalize["RepeatOne"], Id = PlayQueue.RepeatOne, Selected = current == PlayQueue.RepeatOne },
            new() { Name = globalize["None"], Id = PlayQueue.RepeatNone, Selected = current == PlayQueue.RepeatNone },
        };

        if (await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = items }) is { Length: > 0 } mode)
        {
            playback.SetRepeatMode(mode);
        }
    }

    private async Task ShowAspectRatioMenuAsync()
    {
        var current = playback.AspectRatio;
        var items = PlaybackManager.AspectRatios
            .Select(a => new ActionSheetItem { Name = globalize[a.NameKey], Id = a.Id, Selected = a.Id == current })
            .ToList();

        if (await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = items }) is { Length: > 0 } id)
        {
            playback.SetAspectRatio(id);
        }
    }

    private async Task ShowPlaybackRateMenuAsync()
    {
        var current = playback.PlaybackRate;
        var items = PlaybackManager.PlaybackRates
            .Select(r => new ActionSheetItem { Name = RateName(r), Id = r.ToString(CultureInfo.InvariantCulture), Selected = r.Equals(current) })
            .ToList();

        var id = await dialogs.ActionSheetAsync(new ActionSheetOptions { Items = items });
        if (id is not null && double.TryParse(id, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
        {
            playback.SetPlaybackRate(rate);
        }
    }

}
