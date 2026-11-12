namespace Jellyfin.Web.Services;

/// <summary>A video codec the server can decode in hardware, and the acceleration types that can.</summary>
public sealed record HardwareCodec(string Name, string Codec, IReadOnlyList<string> Types);

/// <summary>A choice of one of the transcoding page's lists: the stored value and its text or translation key.</summary>
public sealed record TranscodingChoice(string Value, string? Text = null, string? Key = null);

/// <summary>
/// What the transcoding page shows for which hardware acceleration type.
/// Port of apps/dashboard/features/playback/constants/codecs.ts and of the conditions in
/// routes/playback/transcoding.tsx.
/// </summary>
public static class TranscodingLogic
{
    /// <summary>Port of CODECS: the codecs and the acceleration types that decode them.</summary>
    public static readonly IReadOnlyList<HardwareCodec> Codecs =
    [
        new("H264", "h264", ["amf", "nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox", "v4l2m2m"]),
        new("HEVC", "hevc", ["amf", "nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox"]),
        new("MPEG1", "mpeg1video", ["rkmpp"]),
        new("MPEG2", "mpeg2video", ["amf", "nvenc", "qsv", "vaapi", "rkmpp"]),
        new("MPEG4", "mpeg4", ["nvenc", "rkmpp"]),
        new("VC1", "vc1", ["amf", "nvenc", "qsv", "vaapi"]),
        new("VP8", "vp8", ["nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox"]),
        new("VP9", "vp9", ["amf", "nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox"]),
        new("AV1", "av1", ["amf", "nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox"]),
    ];

    /// <summary>Port of HEVC_VP9_HW_DECODING_TYPES: decoders which support 10-bit HEVC and VP9.</summary>
    public static readonly IReadOnlyList<string> HevcVp9DecodingTypes = ["amf", "nvenc", "qsv", "vaapi", "rkmpp"];

    /// <summary>Port of HEVC_REXT_DECODING_TYPES: decoders which support HEVC RExt.</summary>
    public static readonly IReadOnlyList<string> HevcRextDecodingTypes = ["nvenc", "qsv", "vaapi"];

    /// <summary>The acceleration types of the list, in the original's order.</summary>
    public static readonly IReadOnlyList<TranscodingChoice> AccelerationTypes =
    [
        new("none", Key: "None"),
        new("amf", "AMD AMF"),
        new("nvenc", "Nvidia NVENC"),
        new("qsv", "Intel Quicksync (QSV)"),
        new("vaapi", "Video Acceleration API (VAAPI)"),
        new("rkmpp", "Rockchip MPP (RKMPP)"),
        new("videotoolbox", "Apple VideoToolBox"),
        new("v4l2m2m", "Video4Linux2 (V4L2)"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> TonemappingAlgorithms =
    [
        new("none", Key: "None"),
        new("clip", "Clip"),
        new("linear", "Linear"),
        new("gamma", "Gamma"),
        new("reinhard", "Reinhard"),
        new("hable", "Hable"),
        new("mobius", "Mobius"),
        new("bt2390", "BT.2390"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> TonemappingModes =
    [
        new("auto", Key: "Auto"),
        new("max", "MAX"),
        new("rgb", "RGB"),
        new("lum", "LUM"),
        new("itp", "ITP"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> TonemappingRanges =
    [
        new("auto", Key: "Auto"),
        new("tv", "TV"),
        new("pc", "PC"),
    ];

    /// <summary>Automatic, one to sixteen threads, and "as many as there are".</summary>
    public static readonly IReadOnlyList<TranscodingChoice> ThreadCounts =
    [
        new("-1", Key: "Auto"),
        .. Enumerable.Range(1, 16).Select(count => new TranscodingChoice(
            count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            count.ToString(System.Globalization.CultureInfo.InvariantCulture))),
        new("0", Key: "OptionMax"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> DownMixAlgorithms =
    [
        new("None", Key: "None"),
        new("Dave750", "Dave750"),
        new("NightmodeDialogue", "NightmodeDialogue"),
        new("Rfc7845", "RFC7845"),
        new("Ac4", "AC-4"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> HlsAudioSeekStrategies =
    [
        new("TrimCopiedAudio", "TrimCopiedAudio"),
        new("TranscodeAudio", "TranscodeAudio"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> EncoderPresets =
    [
        new("auto", Key: "Auto"),
        new("veryslow", "veryslow"),
        new("slower", "slower"),
        new("slow", "slow"),
        new("medium", "medium"),
        new("fast", "fast"),
        new("faster", "faster"),
        new("veryfast", "veryfast"),
        new("superfast", "superfast"),
        new("ultrafast", "ultrafast"),
    ];

    public static readonly IReadOnlyList<TranscodingChoice> DeinterlaceMethods =
    [
        new("yadif", Key: "Yadif"),
        new("bwdif", Key: "Bwdif"),
    ];

    /// <summary>The acceleration type in effect: "none" when the server has none stored.</summary>
    public static string AccelerationOf(string? stored) => string.IsNullOrEmpty(stored) ? "none" : stored;

    /// <summary>Port of isHwaSelected: an acceleration type with tone mapping of its own (all but V4L2 and none).</summary>
    public static bool IsHwaSelected(string acceleration) =>
        acceleration is "amf" or "nvenc" or "qsv" or "vaapi" or "rkmpp" or "videotoolbox";

    /// <summary>Port of availableCodecs: the codecs the acceleration type decodes.</summary>
    public static IReadOnlyList<HardwareCodec> CodecsFor(string acceleration) =>
        Codecs.Where(codec => codec.Types.Contains(acceleration)).ToList();

    public static bool SupportsHevcVp9TenBit(string acceleration) => HevcVp9DecodingTypes.Contains(acceleration);

    public static bool SupportsHevcRext(string acceleration) => HevcRextDecodingTypes.Contains(acceleration);

    /// <summary>Intel's types: the low power encoders and VPP tone mapping.</summary>
    public static bool IsIntel(string acceleration) => acceleration is "qsv" or "vaapi";

    /// <summary>The software tone mapping settings show for everything but V4L2.</summary>
    public static bool ShowsTonemapping(string acceleration) => acceleration == "none" || IsHwaSelected(acceleration);

    /// <summary>
    /// Port of onCodecChange: the codec list with a codec switched on or off. Switching a codec on
    /// appends it even when it is there already, as the original does.
    /// </summary>
    public static List<string> ToggleCodec(IEnumerable<string> codecs, string codec, bool enabled) =>
        enabled ? [.. codecs, codec] : codecs.Where(existing => existing != codec).ToList();
}
