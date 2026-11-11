using System.Globalization;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.User;

/// <summary>
/// The option lists of the playback settings (components/playbackSettings, components/qualityOptions)
/// and the streaming bitrate settings of scripts/settings/appSettings, under their original keys.
/// </summary>
public static class PlaybackOptions
{
    /// <summary>The plugin that asks "still watching?" (plugins/stillWatching), as listed in config.json.</summary>
    public const string StillWatchingPlugin = "stillWatching/plugin";

    public static readonly IReadOnlyList<(string Name, int Bitrate)> VideoBitrates =
    [
        ("120 Mbps", 120000000), ("80 Mbps", 80000000), ("60 Mbps", 60000000), ("40 Mbps", 40000000), ("20 Mbps", 20000000),
        ("15 Mbps", 15000000), ("10 Mbps", 10000000), ("8 Mbps", 8000000), ("6 Mbps", 6000000), ("4 Mbps", 4000000),
        ("3 Mbps", 3000000), ("1.5 Mbps", 1500000), ("720 kbps", 720000), ("420 kbps", 420000)
    ];

    public static readonly IReadOnlyList<(string Name, int Bitrate)> AudioBitrates =
    [
        ("2 Mbps", 2000000), ("1.5 Mbps", 1500000), ("1 Mbps", 1000000), ("320 kbps", 320000), ("256 kbps", 256000),
        ("192 kbps", 192000), ("128 kbps", 128000), ("96 kbps", 96000), ("64 kbps", 64000)
    ];

    /// <summary>The media segment types with a configurable action, in the original's order.</summary>
    public static readonly IReadOnlyList<string> MediaSegmentTypes = ["Intro", "Preview", "Recap", "Commercial", "Outro"];

    public static readonly IReadOnlyList<string> MediaSegmentActions = ["None", "AskToSkip", "Skip"];

    /// <summary>The still watching options with the item count and minutes of their prompt.</summary>
    public static readonly IReadOnlyList<(string Value, int Count, int Minutes)> StillWatchingOptions =
    [
        ("disabled", 0, 0), ("short", 2, 60), ("default", 3, 90), ("long", 5, 150), ("veryLong", 8, 240)
    ];

    /// <summary>"Auto" (empty value) followed by the bitrates.</summary>
    public static List<SelectOption> QualityOptions(IReadOnlyList<(string Name, int Bitrate)> bitrates, Globalize globalize) =>
    [
        new(string.Empty, globalize["Auto"]),
        .. bitrates.Select(b => new SelectOption(b.Bitrate.ToString(CultureInfo.InvariantCulture), b.Name))
    ];

    public static List<SelectOption> SkipLengths(Globalize globalize) =>
        new[] { 5, 10, 15, 20, 25, 30 }
            .Select(seconds => new SelectOption((seconds * 1000).ToString(CultureInfo.InvariantCulture), globalize.Translate("ValueSeconds", seconds)))
            .ToList();

    public static List<SelectOption> StillWatching(Globalize globalize) =>
        StillWatchingOptions
            .Select(o => new SelectOption(o.Value, o.Value == "disabled" ? globalize["Disabled"] : globalize.Translate("StillWatchingOption", o.Count, o.Minutes)))
            .ToList();

    public static List<SelectOption> SegmentActions(Globalize globalize) =>
        MediaSegmentActions.Select(action => new SelectOption(action, globalize["MediaSegmentAction." + action])).ToList();

    /// <summary>The settings key (and element id) of a media segment type's action.</summary>
    public static string SegmentActionId(string segmentType) => "segmentTypeAction__" + segmentType;

    /// <summary>The stored action of a segment type; intros and outros ask to skip by default.</summary>
    public static string SegmentAction(UserSettings settings, string segmentType)
    {
        var action = settings.Get(SegmentActionId(segmentType), false);
        if (!string.IsNullOrEmpty(action))
        {
            return action;
        }

        return segmentType is "Intro" or "Outro" ? "AskToSkip" : "None";
    }

    // --- appSettings.maxStreamingBitrate / enableAutomaticBitrateDetection ---

    private static string NetworkText(bool isInNetwork) => isInNetwork ? "true" : "false";

    public static string MaxBitrateKey(bool isInNetwork, string mediaType) => "maxbitrate-" + mediaType + "-" + NetworkText(isInNetwork);

    public static string AutoBitrateKey(bool isInNetwork, string mediaType) => "enableautobitratebitrate-" + mediaType + "-" + NetworkText(isInNetwork);

    public static int MaxStreamingBitrate(AppSettings settings, bool isInNetwork, string mediaType)
    {
        if (isInNetwork && mediaType == "Audio")
        {
            // A huge number, so that audio always plays directly on the home network.
            return 150000000;
        }

        var value = settings.GetInt(MaxBitrateKey(isInNetwork, mediaType), 0);
        return value != 0 ? value : 1500000;
    }

    public static bool EnableAutomaticBitrateDetection(AppSettings settings, bool isInNetwork, string mediaType) =>
        (isInNetwork && mediaType == "Audio") || settings.GetBool(AutoBitrateKey(isInNetwork, mediaType), true);

    /// <summary>The quality select's value: empty while the bitrate is detected automatically.</summary>
    public static string BitrateFieldValue(AppSettings settings, bool isInNetwork, string mediaType)
    {
        if (EnableAutomaticBitrateDetection(settings, isInNetwork, mediaType))
        {
            return string.Empty;
        }

        var bitrate = MaxStreamingBitrate(settings, isInNetwork, mediaType);
        var bitrates = mediaType == "Audio" ? AudioBitrates : VideoBitrates;

        // A stored bitrate that is not on offer leaves the select without a value.
        return bitrates.Any(b => b.Bitrate == bitrate) ? bitrate.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>Stores a quality select's value (setMaxBitrateFromField).</summary>
    public static void SaveBitrateField(AppSettings settings, string? value, bool isInNetwork, string mediaType)
    {
        var inNetworkAudio = isInNetwork && mediaType == "Audio";
        if (!string.IsNullOrEmpty(value))
        {
            if (!inNetworkAudio)
            {
                settings.Set(MaxBitrateKey(isInNetwork, mediaType), value);
            }

            settings.Set(AutoBitrateKey(isInNetwork, mediaType), inNetworkAudio);
        }
        else
        {
            settings.Set(AutoBitrateKey(isInNetwork, mediaType), true);
        }
    }

    /// <summary>The Chromecast quality select's value (appSettings.maxChromecastBitrate() || '').</summary>
    public static string ChromecastFieldValue(AppSettings settings)
    {
        var stored = settings.Get("chromecastBitrate1");
        if (string.IsNullOrEmpty(stored) || !int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bitrate) || bitrate == 0)
        {
            return string.Empty;
        }

        return VideoBitrates.Any(b => b.Bitrate == bitrate) ? bitrate.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }
}
