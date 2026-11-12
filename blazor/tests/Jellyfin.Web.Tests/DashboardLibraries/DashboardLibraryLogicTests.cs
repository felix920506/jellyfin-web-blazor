using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Tests.Dashboard;

/// <summary>
/// The logic of the libraries, playback and networking pages that needs no component: named
/// configurations, published addresses, the transcoding lists and the library dialogs' options.
/// </summary>
public sealed class DashboardLibraryLogicTests
{
    private static JsonObject Config(string json) => (JsonObject)JsonNode.Parse(json)!;

    // ---- named configurations ----

    [Fact]
    public void A_named_configuration_is_read_by_the_kind_of_its_values()
    {
        var config = Config("""{ "Path": "/tmp", "Port": 8096, "Scale": 2.5, "Crf": "23", "On": true, "Off": false, "List": ["a", 1, "b"], "None": null }""");

        Assert.Equal("/tmp", config.Text("Path"));
        Assert.Null(config.Text("Port"));
        Assert.Null(config.Text("Missing"));

        Assert.Equal("/tmp", config.Shown("Path"));
        Assert.Equal("8096", config.Shown("Port"));
        Assert.Equal("2.5", config.Shown("Scale"));
        Assert.Null(config.Shown("On"));
        Assert.Null(config.Shown("None"));
        Assert.Null(config.Shown("Missing"));

        Assert.True(config.Flag("On"));
        Assert.False(config.Flag("Off"));
        Assert.False(config.Flag("Missing"));
        Assert.False(config.Flag("Path"));

        Assert.Equal(8096, config.Number("Port"));
        Assert.Equal(2.5, config.Number("Scale"));
        Assert.Equal(23, config.Number("Crf"));
        Assert.Null(config.Number("Path"));
        Assert.Null(config.Number("Missing"));

        Assert.Equal(8096, config.Whole("Port"));
        Assert.Null(config.Whole("Scale"));

        Assert.Equal(["a", "b"], config.Texts("List"));
        Assert.Empty(config.Texts("Missing"));
        Assert.True(config.HasList("List"));
        Assert.False(config.HasList("Path"));
        Assert.False(config.HasList("Missing"));
    }

    [Fact]
    public void A_list_of_texts_is_stored_as_an_array()
    {
        var config = new JsonObject();

        config.SetTexts("Codecs", ["h264", "hevc"]);

        Assert.Equal("""{"Codecs":["h264","hevc"]}""", config.ToJsonString());
    }

    // ---- networking ----

    [Fact]
    public void A_single_address_without_a_type_is_the_one_for_everyone() =>
        Assert.Equal(new PublishedServerUris(All: "https://media.example"), NetworkingLogic.GetPublishedServerUris(["https://media.example"]));

    [Fact]
    public void Typed_addresses_are_told_apart()
    {
        Assert.Equal(
            new PublishedServerUris(Internal: "http://attic:8096", External: "https://media.example"),
            NetworkingLogic.GetPublishedServerUris(["internal=http://attic:8096", "external=https://media.example", "other=ignored"]));
        Assert.Equal(new PublishedServerUris(All: "https://media.example"), NetworkingLogic.GetPublishedServerUris(["all=https://media.example"]));
        Assert.Equal(new PublishedServerUris(), NetworkingLogic.GetPublishedServerUris([]));
        // What follows a second "=" is dropped, as String.split does.
        Assert.Equal(new PublishedServerUris(All: "https://media.example/?a"), NetworkingLogic.GetPublishedServerUris(["all=https://media.example/?a=b"]));
    }

    [Fact]
    public void The_addresses_are_stored_with_their_type()
    {
        Assert.Equal(["all=https://media.example"], NetworkingLogic.EncodePublishedServerUris(new PublishedServerUris("in", "out", "https://media.example")));
        Assert.Equal(["internal=in", "external=out"], NetworkingLogic.EncodePublishedServerUris(new PublishedServerUris("in", "out")));
        Assert.Equal(["external=out"], NetworkingLogic.EncodePublishedServerUris(new PublishedServerUris(string.Empty, "out", string.Empty)));
        Assert.Empty(NetworkingLogic.EncodePublishedServerUris(new PublishedServerUris()));
    }

    [Fact]
    public void Lists_are_split_at_commas_and_joined_with_blanks()
    {
        Assert.Equal(["10.0.0.0/8", "192.168.1.1"], NetworkingLogic.SplitString(" 10.0.0.0/8 ,, 192.168.1.1,"));
        Assert.Empty(NetworkingLogic.SplitString(null));
        Assert.Empty(NetworkingLogic.SplitString(string.Empty));
        Assert.Equal("10.0.0.0/8, 192.168.1.1", NetworkingLogic.JoinList(["10.0.0.0/8", "192.168.1.1"]));
        Assert.Equal(string.Empty, NetworkingLogic.JoinList(null));
    }

    [Theory]
    [InlineData("8096", 8096)]
    [InlineData("  8096", 8096)]
    [InlineData("8096abc", 8096)]
    [InlineData("80.96", 80)]
    [InlineData("-1", -1)]
    [InlineData("+7", 7)]
    [InlineData("", null)]
    [InlineData("abc", null)]
    [InlineData("-", null)]
    [InlineData(null, null)]
    [InlineData("99999999999999999999", null)]
    public void A_port_is_read_as_parseInt_reads_it(string? text, int? expected) =>
        Assert.Equal(expected, NetworkingLogic.ParseInt(text));

    // ---- transcoding ----

    [Fact]
    public void No_stored_acceleration_type_means_none()
    {
        Assert.Equal("none", TranscodingLogic.AccelerationOf(null));
        Assert.Equal("none", TranscodingLogic.AccelerationOf(string.Empty));
        Assert.Equal("qsv", TranscodingLogic.AccelerationOf("qsv"));
    }

    [Theory]
    [InlineData("none", "")]
    [InlineData("amf", "h264,hevc,mpeg2video,vc1,vp9,av1")]
    [InlineData("nvenc", "h264,hevc,mpeg2video,mpeg4,vc1,vp8,vp9,av1")]
    [InlineData("qsv", "h264,hevc,mpeg2video,vc1,vp8,vp9,av1")]
    [InlineData("vaapi", "h264,hevc,mpeg2video,vc1,vp8,vp9,av1")]
    [InlineData("rkmpp", "h264,hevc,mpeg1video,mpeg2video,mpeg4,vp8,vp9,av1")]
    [InlineData("videotoolbox", "h264,hevc,vp8,vp9,av1")]
    [InlineData("v4l2m2m", "h264")]
    public void Each_acceleration_type_has_its_codecs(string acceleration, string codecs) =>
        Assert.Equal(codecs, string.Join(',', TranscodingLogic.CodecsFor(acceleration).Select(codec => codec.Codec)));

    [Theory]
    [InlineData("none", false, false, false, false, true)]
    [InlineData("amf", true, true, false, false, true)]
    [InlineData("nvenc", true, true, true, false, true)]
    [InlineData("qsv", true, true, true, true, true)]
    [InlineData("vaapi", true, true, true, true, true)]
    [InlineData("rkmpp", true, true, false, false, true)]
    [InlineData("videotoolbox", true, false, false, false, true)]
    [InlineData("v4l2m2m", false, false, false, false, false)]
    public void The_dependent_settings_follow_the_acceleration_type(string acceleration, bool hwa, bool tenBit, bool rext, bool intel, bool tonemapping)
    {
        Assert.Equal(hwa, TranscodingLogic.IsHwaSelected(acceleration));
        Assert.Equal(tenBit, TranscodingLogic.SupportsHevcVp9TenBit(acceleration));
        Assert.Equal(rext, TranscodingLogic.SupportsHevcRext(acceleration));
        Assert.Equal(intel, TranscodingLogic.IsIntel(acceleration));
        Assert.Equal(tonemapping, TranscodingLogic.ShowsTonemapping(acceleration));
    }

    [Fact]
    public void A_codec_is_appended_or_removed()
    {
        Assert.Equal(["h264", "hevc"], TranscodingLogic.ToggleCodec(["h264"], "hevc", true));
        // The original appends without looking.
        Assert.Equal(["h264", "h264"], TranscodingLogic.ToggleCodec(["h264"], "h264", true));
        Assert.Equal(["hevc"], TranscodingLogic.ToggleCodec(["h264", "hevc", "h264"], "h264", false));
    }

    [Fact]
    public void The_lists_of_the_transcoding_page_are_the_originals()
    {
        Assert.Equal(["none", "amf", "nvenc", "qsv", "vaapi", "rkmpp", "videotoolbox", "v4l2m2m"], TranscodingLogic.AccelerationTypes.Select(choice => choice.Value));
        Assert.Equal(["none", "clip", "linear", "gamma", "reinhard", "hable", "mobius", "bt2390"], TranscodingLogic.TonemappingAlgorithms.Select(choice => choice.Value));
        Assert.Equal(["auto", "max", "rgb", "lum", "itp"], TranscodingLogic.TonemappingModes.Select(choice => choice.Value));
        Assert.Equal(["auto", "tv", "pc"], TranscodingLogic.TonemappingRanges.Select(choice => choice.Value));
        Assert.Equal("-1", TranscodingLogic.ThreadCounts[0].Value);
        Assert.Equal("0", TranscodingLogic.ThreadCounts[^1].Value);
        Assert.Equal(["None", "Dave750", "NightmodeDialogue", "Rfc7845", "Ac4"], TranscodingLogic.DownMixAlgorithms.Select(choice => choice.Value));
        Assert.Equal(["TrimCopiedAudio", "TranscodeAudio"], TranscodingLogic.HlsAudioSeekStrategies.Select(choice => choice.Value));
        Assert.Equal(
            ["auto", "veryslow", "slower", "slow", "medium", "fast", "faster", "veryfast", "superfast", "ultrafast"],
            TranscodingLogic.EncoderPresets.Select(choice => choice.Value));
        Assert.Equal(["yadif", "bwdif"], TranscodingLogic.DeinterlaceMethods.Select(choice => choice.Value));
        // Every choice is named by a text or by a translation.
        Assert.All(
            TranscodingLogic.AccelerationTypes.Concat(TranscodingLogic.ThreadCounts).Concat(TranscodingLogic.EncoderPresets),
            choice => Assert.True(choice.Text is not null || choice.Key is not null));
    }

    // ---- libraries ----

    [Theory]
    [InlineData(1920, 1, 768)]
    [InlineData(390, 3, 468)]
    [InlineData(1366, 1.25, 683)]
    [InlineData(1000, 0, 400)]
    public void A_card_image_is_asked_for_at_two_fifths_of_the_screen(double screenWidth, double devicePixelRatio, int expected) =>
        Assert.Equal(expected, Jellyfin.Web.Services.DashboardLibraries.CardImageWidth(screenWidth, devicePixelRatio));

    [Fact]
    public void The_editor_lists_the_path_infos_or_else_the_locations()
    {
        var withInfos = new VirtualFolderInfo
        {
            Locations = ["/old"],
            LibraryOptions = new LibraryOptions { PathInfos = [new MediaPathInfo { Path = "/media/a" }, new MediaPathInfo { Path = string.Empty }, new MediaPathInfo { Path = "/media/b" }] },
        };
        var withLocations = new VirtualFolderInfo { Locations = ["/media/c"], LibraryOptions = new LibraryOptions { PathInfos = [] } };

        Assert.Equal(["/media/a", "/media/b"], Jellyfin.Web.Services.DashboardLibraries.PathsOf(withInfos));
        Assert.Equal(["/media/c"], Jellyfin.Web.Services.DashboardLibraries.PathsOf(withLocations));
        Assert.Empty(Jellyfin.Web.Services.DashboardLibraries.PathsOf(new VirtualFolderInfo()));
    }

    [Fact]
    public void Edited_options_replace_the_librarys_and_the_rest_stays()
    {
        var original = new LibraryOptions
        {
            EnableRealtimeMonitor = true,
            PreferredMetadataLanguage = "de",
            PathInfos = [new MediaPathInfo { Path = "/media/a" }],
        };
        var edited = new LibraryOptions { EnableRealtimeMonitor = false, MetadataCountryCode = "AT" };

        var merged = Jellyfin.Web.Services.DashboardLibraries.MergeOptions(original, edited);

        Assert.False(merged.EnableRealtimeMonitor);
        Assert.Equal("AT", merged.MetadataCountryCode);
        Assert.Equal("de", merged.PreferredMetadataLanguage);
        Assert.Equal("/media/a", Assert.Single(merged.PathInfos!).Path);
        Assert.Same(edited, Jellyfin.Web.Services.DashboardLibraries.MergeOptions(null, edited));
    }

    [Fact]
    public void Only_types_with_more_than_a_primary_image_have_image_settings()
    {
        Assert.False(Jellyfin.Web.Services.DashboardLibraries.HasImageOptions(null));
        Assert.False(Jellyfin.Web.Services.DashboardLibraries.HasImageOptions(new LibraryTypeOptionsDto()));
        Assert.False(Jellyfin.Web.Services.DashboardLibraries.HasImageOptions(new LibraryTypeOptionsDto { SupportedImageTypes = [ImageType.Primary] }));
        Assert.True(Jellyfin.Web.Services.DashboardLibraries.HasImageOptions(new LibraryTypeOptionsDto { SupportedImageTypes = [ImageType.Backdrop] }));
        Assert.True(Jellyfin.Web.Services.DashboardLibraries.HasImageOptions(new LibraryTypeOptionsDto { SupportedImageTypes = [ImageType.Primary, ImageType.Logo] }));
    }

    [Fact]
    public void The_options_of_an_image_type_are_the_librarys_then_the_defaults_then_one_primary_image()
    {
        var options = new TypeOptions { ImageOptions = [new ImageOption { Type = ImageOption_Type.Logo, Limit = 0, MinWidth = 5 }] };
        var available = new LibraryTypeOptionsDto
        {
            DefaultImageOptions = [new ImageOption { Type = ImageOption_Type.Logo, Limit = 1 }, new ImageOption { Type = ImageOption_Type.Backdrop, Limit = 3, MinWidth = 1280 }],
        };

        Assert.Equal(5, Jellyfin.Web.Services.DashboardLibraries.ImageConfigOf(options, available, ImageOption_Type.Logo).MinWidth);
        Assert.Equal(3, Jellyfin.Web.Services.DashboardLibraries.ImageConfigOf(options, available, ImageOption_Type.Backdrop).Limit);
        Assert.Equal(1, Jellyfin.Web.Services.DashboardLibraries.ImageConfigOf(options, available, ImageOption_Type.Primary).Limit);
        Assert.Equal(0, Jellyfin.Web.Services.DashboardLibraries.ImageConfigOf(options, null, ImageOption_Type.Thumb).Limit);
    }

    [Fact]
    public void The_image_options_editor_shows_the_supported_types_and_the_backdrop_settings()
    {
        var options = new TypeOptions { ImageOptions = [new ImageOption { Type = ImageOption_Type.Logo, Limit = 0 }] };
        var available = new LibraryTypeOptionsDto
        {
            SupportedImageTypes = [ImageType.Primary, ImageType.Logo, ImageType.Backdrop],
            DefaultImageOptions =
            [
                new ImageOption { Type = ImageOption_Type.Logo, Limit = 1 },
                new ImageOption { Type = ImageOption_Type.Thumb, Limit = 1 },
                new ImageOption { Type = ImageOption_Type.Backdrop, Limit = 3, MinWidth = 1280 },
            ],
        };

        var state = Jellyfin.Web.Services.DashboardLibraries.LoadImageOptions(options, available);

        Assert.Equal(
            ["Primary", "Art", "Back", "Banner", "Box", "Disc", "Logo", "Menu", "Thumb"],
            state.Types.Select(choice => choice.LabelKey));
        Assert.Equal([ImageOption_Type.Primary, ImageOption_Type.Logo], state.Types.Where(choice => choice.Supported).Select(choice => choice.Type));
        // The library's own "no logo" wins over the default; a type that is not supported keeps its default.
        Assert.Equal([ImageOption_Type.Primary, ImageOption_Type.Thumb], state.Types.Where(choice => choice.Checked).Select(choice => choice.Type));
        Assert.True(state.SupportsBackdrops);
        Assert.Equal("3", state.MaxBackdrops);
        Assert.Equal("1280", state.MinBackdropWidth);

        var none = Jellyfin.Web.Services.DashboardLibraries.LoadImageOptions(new TypeOptions(), null);
        Assert.False(none.SupportsBackdrops);
        Assert.DoesNotContain(none.Types, choice => choice.Supported);
        Assert.Equal("0", none.MaxBackdrops);
    }

    [Fact]
    public void The_image_options_editor_writes_every_type_and_the_backdrops()
    {
        var options = new TypeOptions();
        var state = Jellyfin.Web.Services.DashboardLibraries.LoadImageOptions(options, null);
        state.Types.Single(choice => choice.Type == ImageOption_Type.Banner).Checked = true;
        state.MaxBackdrops = "4";
        state.MinBackdropWidth = "abc";

        Jellyfin.Web.Services.DashboardLibraries.SaveImageOptions(state, options);

        Assert.Equal(10, options.ImageOptions!.Count);
        Assert.Equal(
            [ImageOption_Type.Primary, ImageOption_Type.Banner],
            options.ImageOptions.Where(option => option.Limit == 1).Select(option => option.Type!.Value));
        Assert.All(options.ImageOptions.Take(9), option => Assert.Equal(0, option.MinWidth));
        var backdrops = options.ImageOptions[^1];
        Assert.Equal(ImageOption_Type.Backdrop, backdrops.Type);
        Assert.Equal(4, backdrops.Limit);
        Assert.Equal(0, backdrops.MinWidth);
    }

    [Fact]
    public void The_trickplay_widths_are_read_as_the_original_reads_them()
    {
        Assert.Equal([320, 640], Jellyfin.Web.Services.DashboardLibraries.ParseWidthResolutions("320,640"));
        // Only the first blank is removed before splitting; the numbers are trimmed like Number() trims.
        Assert.Equal([320, 640, 800], Jellyfin.Web.Services.DashboardLibraries.ParseWidthResolutions("320, 640, 800"));
        Assert.Equal([320, 0], Jellyfin.Web.Services.DashboardLibraries.ParseWidthResolutions("320,"));
        Assert.Equal([0], Jellyfin.Web.Services.DashboardLibraries.ParseWidthResolutions(null));
        Assert.Equal([320, null], Jellyfin.Web.Services.DashboardLibraries.ParseWidthResolutions("320,wide"));
    }

    [Theory]
    [InlineData("320", true)]
    [InlineData("320,640", true)]
    [InlineData(",", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("320, 640", false)]
    [InlineData("320;640", false)]
    [InlineData("wide", false)]
    public void The_trickplay_widths_are_digits_and_commas(string? text, bool valid) =>
        Assert.Equal(valid, Jellyfin.Web.Services.DashboardLibraries.IsValidWidthResolutions(text));
}
