using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>One "label: value" line of the media info dialog.</summary>
/// <param name="Ltr">File paths are always left to right.</param>
public sealed record MediaInfoAttribute(string Label, string Value, bool Ltr = false);

/// <summary>The attributes of one stream of a media source.</summary>
public sealed record MediaInfoStream(string Title, IReadOnlyList<MediaInfoAttribute> Attributes)
{
    /// <summary>What the copy button of the stream puts on the clipboard (the block's <c>textContent</c>).</summary>
    public string CopyText => "\n" + Title + "\n" + MediaInfoBuilder.Lines(Attributes);
}

/// <summary>One media source (version) of an item.</summary>
/// <param name="Name">The name of the version; the heading with the copy button only exists when there is one.</param>
public sealed record MediaInfoSource(string? Name, IReadOnlyList<MediaInfoAttribute> Attributes, IReadOnlyList<MediaInfoStream> Streams)
{
    /// <summary>What the copy button of the source puts on the clipboard (the block's <c>textContent</c>).</summary>
    public string CopyText
    {
        get
        {
            var text = new StringBuilder();
            if (!string.IsNullOrEmpty(Name))
            {
                text.Append(Name).Append('\n');
            }

            text.Append(MediaInfoBuilder.Lines(Attributes));
            foreach (var stream in Streams)
            {
                text.Append(stream.CopyText);
            }

            return text.ToString();
        }
    }
}

/// <summary>
/// What the media info dialog shows for an item. Port of <c>getMediaSourceHtml()</c> of
/// components/itemMediaInfo/itemMediaInfo.js; the dialog only lays the result out.
/// </summary>
public static class MediaInfoBuilder
{
    /// <summary>The media sources of an item (the server's JSON), in the order and with the attributes of the original.</summary>
    /// <param name="isAdmin">Only administrators see file paths.</param>
    public static IReadOnlyList<MediaInfoSource> Build(JsonNode? item, bool isAdmin, Globalize globalize) =>
        [.. item.List("MediaSources").Select(source => BuildSource(source, isAdmin, globalize))];

    internal static string Lines(IReadOnlyList<MediaInfoAttribute> attributes) =>
        string.Concat(attributes.Select(attribute => attribute.Label + ": " + attribute.Value + "\n"));

    private static MediaInfoSource BuildSource(JsonNode? source, bool isAdmin, Globalize globalize)
    {
        var attributes = new List<MediaInfoAttribute>();
        if (source.Text("Container") is { Length: > 0 } container)
        {
            attributes.Add(new(globalize.Translate("MediaInfoContainer"), container));
        }

        var formats = source.Strings("Formats");
        if (formats.Count > 0)
        {
            attributes.Add(new(globalize.Translate("MediaInfoFormat"), string.Join(',', formats)));
        }

        if (isAdmin && source.Text("Path") is { Length: > 0 } path)
        {
            attributes.Add(new(globalize.Translate("MediaInfoPath"), path, true));
        }

        if (source.Number("Size") is { } size && size != 0)
        {
            attributes.Add(new(globalize.Translate("MediaInfoSize"), PlaybackText.ReadableSize((long)size)));
        }

        // itemHelper.sortTracks: internal before external, forced and default first, then by index.
        var streams = source.List("MediaStreams")
            .OrderBy(stream => stream.Flag("IsExternal"))
            .ThenByDescending(stream => stream.Flag("IsForced"))
            .ThenByDescending(stream => stream.Flag("IsDefault"))
            .ThenBy(stream => stream.Number("Index") ?? 0)
            .Where(stream => stream.Text("Type") != "Data")
            .Select(stream => BuildStream(stream, source, globalize))
            .ToList();

        return new MediaInfoSource(source.Text("Name") is { Length: > 0 } name ? name : null, attributes, streams);
    }

    private static MediaInfoStream BuildStream(JsonNode? stream, JsonNode? source, Globalize globalize)
    {
        var type = stream.Text("Type");
        var isVideo = type == "Video";
        var attributes = new List<MediaInfoAttribute>();

        void Add(string label, string? value) => attributes.Add(new(label, value ?? string.Empty));

        // A value that is set and not empty, zero or false: what "if (stream.X)" lets through.
        string? Truthy(string name) => stream.Text(name) is { Length: > 0 } text && text is not ("0" or "false") ? text : null;

        void AddIfSet(string key, string name)
        {
            if (Truthy(name) is { } value)
            {
                Add(globalize.Translate(key), value);
            }
        }

        void AddIfPresent(string key, string name)
        {
            if (stream.Text(name) is { } value)
            {
                Add(globalize.Translate(key), value);
            }
        }

        AddIfSet("MediaInfoTitle", "DisplayTitle");
        if (!isVideo)
        {
            AddIfSet("MediaInfoLanguage", "Language");
        }

        if (Truthy("Codec") is { } codec)
        {
            Add(globalize.Translate("MediaInfoCodec"), codec.ToUpperInvariant());
        }

        AddIfSet("MediaInfoCodecTag", "CodecTag");
        if (stream.FlagOrNull("IsAVC") is { } isAvc)
        {
            Add("AVC", YesNo(isAvc));
        }

        AddIfSet("MediaInfoProfile", "Profile");
        if (stream.Number("Level") > 0)
        {
            Add(globalize.Translate("MediaInfoLevel"), stream.Text("Level"));
        }

        if (Truthy("Width") is not null || Truthy("Height") is not null)
        {
            Add(globalize.Translate("MediaInfoResolution"), stream.Text("Width") + "x" + stream.Text("Height"));
        }

        if (Truthy("AspectRatio") is { } aspectRatio && stream.Text("Codec") != "mjpeg")
        {
            Add(globalize.Translate("MediaInfoAspectRatio"), aspectRatio);
        }

        if (isVideo)
        {
            if (stream.FlagOrNull("IsAnamorphic") is { } isAnamorphic)
            {
                Add(globalize.Translate("MediaInfoAnamorphic"), YesNo(isAnamorphic));
            }

            Add(globalize.Translate("MediaInfoInterlaced"), YesNo(stream.Flag("IsInterlaced")));
            AddIfSet("MediaInfoFramerate", "ReferenceFrameRate");
        }

        AddIfSet("MediaInfoLayout", "ChannelLayout");
        if (Truthy("Channels") is { } channels)
        {
            Add(globalize.Translate("MediaInfoChannels"), channels + " ch");
        }

        if (stream.Number("BitRate") is { } bitRate && bitRate != 0)
        {
            Add(globalize.Translate("MediaInfoBitrate"), ((long)(bitRate / 1000)).ToString(CultureInfo.InvariantCulture) + " kbps");
        }

        if (Truthy("SampleRate") is { } sampleRate)
        {
            Add(globalize.Translate("MediaInfoSampleRate"), sampleRate + " Hz");
        }

        if (Truthy("BitDepth") is { } bitDepth)
        {
            Add(globalize.Translate("MediaInfoBitDepth"), bitDepth + " bit");
        }

        if (isVideo)
        {
            AddIfSet("MediaInfoVideoRange", "VideoRange");
            AddIfSet("MediaInfoVideoRangeType", "VideoRangeType");
        }

        if (Truthy("VideoDoViTitle") is { } doViTitle)
        {
            Add(globalize.Translate("MediaInfoDoViTitle"), doViTitle);
            AddIfPresent("MediaInfoDvVersionMajor", "DvVersionMajor");
            AddIfPresent("MediaInfoDvVersionMinor", "DvVersionMinor");
            AddIfPresent("MediaInfoDvProfile", "DvProfile");
            AddIfPresent("MediaInfoDvLevel", "DvLevel");
            AddIfPresent("MediaInfoRpuPresentFlag", "RpuPresentFlag");
            AddIfPresent("MediaInfoElPresentFlag", "ElPresentFlag");
            AddIfPresent("MediaInfoBlPresentFlag", "BlPresentFlag");
            AddIfPresent("MediaInfoDvBlSignalCompatibilityId", "DvBlSignalCompatibilityId");
        }

        AddIfSet("MediaInfoColorSpace", "ColorSpace");
        AddIfSet("MediaInfoColorTransfer", "ColorTransfer");
        AddIfSet("MediaInfoColorPrimaries", "ColorPrimaries");
        AddIfSet("MediaInfoPixelFormat", "PixelFormat");
        AddIfSet("MediaInfoRefFrames", "RefFrames");
        if (isVideo)
        {
            AddIfSet("MediaInfoRotation", "Rotation");
        }

        if (Truthy("NalLengthSize") is { } nalLengthSize)
        {
            Add("NAL", nalLengthSize);
        }

        if (type is "Subtitle" or "Audio")
        {
            Add(globalize.Translate("MediaInfoDefault"), YesNo(stream.Flag("IsDefault")));
            Add(globalize.Translate("MediaInfoForced"), YesNo(stream.Flag("IsForced")));
            Add(globalize.Translate("MediaInfoExternal"), YesNo(stream.Flag("IsExternal")));
        }

        if (isVideo && source.Text("Timestamp") is { Length: > 0 } timestamp)
        {
            Add(globalize.Translate("MediaInfoTimestamp"), timestamp);
        }

        var title = type switch
        {
            "EmbeddedImage" => globalize.Translate("Image"),
            null or "" => string.Empty,
            _ => globalize.Translate(type)
        };
        return new MediaInfoStream(title, attributes);
    }

    // Not translated in the original either.
    private static string YesNo(bool value) => value ? "Yes" : "No";
}
