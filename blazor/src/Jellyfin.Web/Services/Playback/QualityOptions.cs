using System.Globalization;

namespace Jellyfin.Web.Services.Playback;

/// <summary>An entry of the quality menu.</summary>
public sealed record QualityOption
{
    public string Name { get; init; } = string.Empty;
    public int Bitrate { get; init; }
    public int? MaxHeight { get; init; }
    public bool Selected { get; init; }

    /// <summary>On the "Auto" entry: the name of the quality the automatic choice amounts to.</summary>
    public string? AutoText { get; init; }
}

/// <summary>Port of components/qualityOptions.js: the bitrates offered by the quality menus.</summary>
public static class QualityOptions
{
    private static readonly (string Name, int Bitrate, int MaxHeight)[] VideoConfigurations =
    [
        ("120 Mbps", 120000000, 2160),
        ("80 Mbps", 80000000, 2160),
        ("60 Mbps", 60000000, 2160),
        ("40 Mbps", 40000000, 2160),
        ("20 Mbps", 20000000, 2160),
        ("15 Mbps", 15000000, 1440),
        ("10 Mbps", 10000000, 1440),
        ("8 Mbps", 8000000, 1080),
        ("6 Mbps", 6000000, 1080),
        ("4 Mbps", 4000000, 720),
        ("3 Mbps", 3000000, 720),
        ("1.5 Mbps", 1500000, 720),
        ("720 kbps", 720000, 480),
        ("420 kbps", 420000, 360),
    ];

    private static readonly (string Name, int Bitrate)[] AudioConfigurations =
    [
        ("2 Mbps", 2000000),
        ("1.5 Mbps", 1500000),
        ("1 Mbps", 1000000),
        ("320 kbps", 320000),
        ("256 kbps", 256000),
        ("192 kbps", 192000),
        ("128 kbps", 128000),
        ("96 kbps", 96000),
        ("64 kbps", 64000),
    ];

    /// <summary>Port of getVideoQualityOptions().</summary>
    /// <param name="currentMaxBitrate">The bitrate limit in effect, if any.</param>
    /// <param name="isAutomaticBitrateEnabled">The limit is detected, not chosen.</param>
    /// <param name="videoBitRate">The bitrate of the source's video stream, if known.</param>
    /// <param name="videoCodec">The codec of the source's video stream.</param>
    /// <param name="enableAuto">Offer the "Auto" entry.</param>
    /// <param name="autoName">The translated name of the "Auto" entry.</param>
    public static IReadOnlyList<QualityOption> GetVideoQualityOptions(
        int? currentMaxBitrate,
        bool isAutomaticBitrateEnabled,
        int? videoBitRate,
        string? videoCodec,
        bool enableAuto,
        string autoName = "Auto")
    {
        var maxStreamingBitrate = currentMaxBitrate ?? 0;
        var sourceBitRate = videoBitRate ?? -1;
        var options = new List<QualityOption>();

        if (enableAuto)
        {
            options.Add(new QualityOption { Name = autoName, Bitrate = 0, Selected = isAutomaticBitrateEnabled });
        }

        // An approximation of how much another codec needs for the same picture, to offer the
        // quality just above the source as well.
        double referenceBitRate = sourceBitRate;
        if (sourceBitRate > 0 && sourceBitRate < VideoConfigurations[0].Bitrate)
        {
            // Slightly increase the reference bitrate for high efficiency codecs when it is not too high.
            if (videoCodec is "hevc" or "av1" or "vp9" && referenceBitRate <= 20000000)
            {
                referenceBitRate *= 1.5;
            }

            // Push one entry that has a higher bitrate than the source, to allow remuxing or
            // transcoding to H264 without losing quality.
            var above = VideoConfigurations.Where(c => c.Bitrate > referenceBitRate).ToArray();
            if (above.Length > 0)
            {
                var source = above[^1];
                options.Add(new QualityOption { Name = source.Name, Bitrate = source.Bitrate, MaxHeight = source.MaxHeight });
            }
        }

        foreach (var configuration in VideoConfigurations)
        {
            if (sourceBitRate <= 0 || configuration.Bitrate <= referenceBitRate)
            {
                options.Add(new QualityOption { Name = configuration.Name, Bitrate = configuration.Bitrate, MaxHeight = configuration.MaxHeight });
            }
        }

        return Select(options, maxStreamingBitrate, isAutomaticBitrateEnabled);
    }

    /// <summary>Port of getAudioQualityOptions().</summary>
    public static IReadOnlyList<QualityOption> GetAudioQualityOptions(
        int? currentMaxBitrate,
        bool isAutomaticBitrateEnabled,
        bool enableAuto,
        string autoName = "Auto")
    {
        var options = new List<QualityOption>();
        if (enableAuto)
        {
            options.Add(new QualityOption { Name = autoName, Bitrate = 0, Selected = isAutomaticBitrateEnabled });
        }

        options.AddRange(AudioConfigurations.Select(c => new QualityOption { Name = c.Name, Bitrate = c.Bitrate }));
        return Select(options, currentMaxBitrate ?? 0, isAutomaticBitrateEnabled);
    }

    /// <summary>
    /// Marks the entry that the current limit amounts to: selected when the limit was chosen, or
    /// named on the "Auto" entry when it was detected.
    /// </summary>
    private static List<QualityOption> Select(List<QualityOption> options, int maxStreamingBitrate, bool isAutomaticBitrateEnabled)
    {
        if (maxStreamingBitrate <= 0 || options.Count == 0)
        {
            return options;
        }

        var selectedIndex = options.FindIndex(o => o.Bitrate > 0 && o.Bitrate <= maxStreamingBitrate);
        if (selectedIndex == -1)
        {
            selectedIndex = options.Count - 1;
        }

        var selected = options[selectedIndex];
        if (!isAutomaticBitrateEnabled)
        {
            options[selectedIndex] = selected with { Selected = true };
        }
        else if (options[0].Bitrate == 0)
        {
            options[0] = options[0] with { AutoText = selected.Name };
        }

        return options;
    }

    /// <summary>The menu id of an option: its bitrate.</summary>
    public static string IdOf(QualityOption option) => option.Bitrate.ToString(CultureInfo.InvariantCulture);
}
