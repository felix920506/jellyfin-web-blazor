using System.Text.Json.Nodes;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class ActionSheetPositionTests
{
    private static ActionSheetMeasure Measure(
        double top = 400, double left = 500, double width = 40, double height = 40, double dialogWidth = 200, double dialogHeight = 100) =>
        new(top, left, width, height, dialogWidth, dialogHeight, WindowWidth: 1000, WindowHeight: 800);

    [Fact]
    public void The_sheet_is_centred_on_the_middle_of_the_anchor()
    {
        Assert.Equal((420, 370), ActionSheetPosition.Compute(Measure()));
    }

    [Fact]
    public void Position_top_centres_the_sheet_on_the_top_edge_of_the_anchor()
    {
        Assert.Equal((420, 350), ActionSheetPosition.Compute(Measure(), positionY: "top"));
    }

    [Fact]
    public void A_sheet_that_would_leave_the_window_is_moved_back_with_a_margin()
    {
        // Without correction: left 870 (70 too far right), top 730 (30 too far down).
        Assert.Equal((780, 680), ActionSheetPosition.Compute(Measure(top: 760, left: 950)));
    }

    [Fact]
    public void The_sheet_keeps_ten_pixels_from_the_left_and_top_edges()
    {
        Assert.Equal((10, 10), ActionSheetPosition.Compute(Measure(top: 0, left: 0)));
    }

    [Fact]
    public void The_offsets_are_added_after_the_sheet_was_kept_inside_the_window()
    {
        Assert.Equal((425, 363), ActionSheetPosition.Compute(Measure(), offsetLeft: 5, offsetTop: -7));
        Assert.Equal((790, 670), ActionSheetPosition.Compute(Measure(top: 760, left: 950), offsetLeft: 10, offsetTop: -10));
    }

    [Fact]
    public void A_sheet_that_is_not_laid_out_yet_counts_as_160_by_300()
    {
        Assert.Equal((440, 270), ActionSheetPosition.Compute(Measure(dialogWidth: 0, dialogHeight: 0)));
    }
}

public class ItemJsonTests
{
    private static readonly JsonNode Item = JsonNode.Parse("""
        {
            "Name": "Third Film", "Empty": "", "Year": 2008, "Rating": 7.5, "Zero": 0, "Played": true, "Locked": false, "Nothing": null,
            "Text": "true", "NumberText": "12", "Ids": { "Tmdb": "42" }, "Tags": ["a", 2, null, true], "Objects": [{ "Name": "x" }]
        }
        """)!;

    [Fact]
    public void Members_are_read_as_javascript_would_print_them()
    {
        Assert.Equal("Third Film", Item.Text("Name"));
        Assert.Equal("", Item.Text("Empty"));
        Assert.Equal("2008", Item.Text("Year"));
        Assert.Equal("7.5", Item.Text("Rating"));
        Assert.Equal("0", Item.Text("Zero"));
        Assert.Equal("true", Item.Text("Played"));
        Assert.Equal("false", Item.Text("Locked"));
        Assert.Null(Item.Text("Nothing"));
        Assert.Null(Item.Text("Missing"));
    }

    [Fact]
    public void Flags_and_numbers_are_only_what_the_server_sent_as_such()
    {
        Assert.True(Item.Flag("Played"));
        Assert.False(Item.Flag("Locked"));
        Assert.False(Item.Flag("Text"));
        Assert.False(Item.Flag("Missing"));
        Assert.True(Item.FlagOrNull("Played"));
        Assert.False(Item.FlagOrNull("Locked"));
        Assert.Null(Item.FlagOrNull("Text"));
        Assert.Null(Item.FlagOrNull("Missing"));

        Assert.Equal(2008, Item.Number("Year"));
        Assert.Equal(7.5, Item.Number("Rating"));
        Assert.Equal(0, Item.Number("Zero"));
        Assert.Null(Item.Number("NumberText"));
        Assert.Null(Item.Number("Nothing"));
        Assert.Null(Item.Number("Missing"));
    }

    [Fact]
    public void Objects_and_lists_are_empty_when_they_are_missing()
    {
        Assert.True(Item.Has("Nothing"));
        Assert.False(Item.Has("Missing"));
        Assert.Equal("42", Item.Child("Ids").Text("Tmdb"));
        Assert.Null(Item.Child("Name"));
        Assert.Null(Item.Child("Missing"));
        Assert.Equal("x", Assert.Single(Item.List("Objects")).Text("Name"));
        Assert.Empty(Item.List("Missing"));
        Assert.Empty(Item.List("Name"));
        Assert.Equal(new[] { "a", "2", "true" }, Item.Strings("Tags"));
        Assert.Empty(Item.Strings("Missing"));
    }

    [Fact]
    public void Nothing_can_be_read_from_nothing()
    {
        JsonNode? nothing = null;

        Assert.Null(nothing.Text("Name"));
        Assert.False(nothing.Flag("Played"));
        Assert.Null(nothing.Number("Year"));
        Assert.False(nothing.Has("Name"));
        Assert.Null(nothing.Child("Ids"));
        Assert.Empty(nothing.List("Tags"));
        Assert.Empty(nothing.Strings("Tags"));
    }
}

public class ImageEditorLogicTests
{
    private static readonly JsonNode Item = JsonNode.Parse("""
        { "Id": "abc", "ImageTags": { "Primary": "p1", "Logo": "l1" }, "BackdropImageTags": ["b0", "b1"] }
        """)!;

    [Theory]
    [InlineData(320, 300)]
    [InlineData(1279, 300)]
    [InlineData(1280, 320)]
    [InlineData(1922, 481)]
    [InlineData(2560, 640)]
    public void Images_are_a_quarter_of_a_wide_window_wide(int windowWidth, int size)
    {
        Assert.Equal(size, ImageEditorLogic.ImageSize(windowWidth));
    }

    [Fact]
    public void The_tag_of_an_image_comes_from_the_item()
    {
        Assert.Equal("p1", ImageEditorLogic.ImageTag(Item, "Primary", null));
        Assert.Equal("l1", ImageEditorLogic.ImageTag(Item, "Logo", null));
        Assert.Null(ImageEditorLogic.ImageTag(Item, "Banner", null));
        Assert.Equal("b0", ImageEditorLogic.ImageTag(Item, "Backdrop", null));
        Assert.Equal("b1", ImageEditorLogic.ImageTag(Item, "Backdrop", 1));
        Assert.Null(ImageEditorLogic.ImageTag(Item, "Backdrop", 2));

        // A user or a person carries the tag of its picture directly.
        Assert.Equal("direct", ImageEditorLogic.ImageTag(JsonNode.Parse("""{"PrimaryImageTag":"direct","ImageTags":{"Primary":"p1"}}"""), "Primary", null));
    }

    [Fact]
    public void Images_are_requested_scaled_to_the_screen()
    {
        var (path, query) = ImageEditorLogic.ImageRequest(Item, "Primary", null, 300, 2);
        Assert.Equal("Items/abc/Images/Primary", path);
        Assert.Equal(new (string, object?)[] { ("maxWidth", 600), ("tag", "p1"), ("quality", 90) }, query);

        (path, query) = ImageEditorLogic.ImageRequest(Item, "Backdrop", 1, 301, 1.5);
        Assert.Equal("Items/abc/Images/Backdrop/1", path);
        Assert.Equal(new (string, object?)[] { ("maxWidth", 452), ("tag", "b1"), ("quality", 80) }, query);
    }

    [Fact]
    public void Backdrops_are_listed_apart_in_their_order_and_chapter_images_not_at_all()
    {
        var images = JsonNode.Parse("""
            [
                { "ImageType": "Primary" }, { "ImageType": "Backdrop", "ImageIndex": 1 }, { "ImageType": "Chapter", "ImageIndex": 0 },
                { "ImageType": "Backdrop", "ImageIndex": 0 }, { "ImageType": "Logo" }
            ]
            """)!.AsArray();

        Assert.Equal(new[] { "Primary", "Logo" }, ImageEditorLogic.StandardImages(images).Select(image => image.Text("ImageType")));
        Assert.Equal(new[] { "0", "1" }, ImageEditorLogic.Backdrops(images).Select(image => image.Text("ImageIndex")));
    }

    [Theory]
    [InlineData("Backdrop", "Movie", "backdrop")]
    [InlineData("Art", "Movie", "backdrop")]
    [InlineData("Thumb", "Movie", "backdrop")]
    [InlineData("Logo", "Movie", "backdrop")]
    [InlineData("Banner", "Series", "banner")]
    [InlineData("Disc", "Movie", "square")]
    [InlineData("Primary", "Movie", "portrait")]
    [InlineData("Primary", "Episode", "backdrop")]
    [InlineData("Primary", "MusicAlbum", "square")]
    [InlineData("Primary", "MusicArtist", "square")]
    [InlineData("Box", "Series", "portrait")]
    [InlineData(null, null, "portrait")]
    public void Remote_images_are_shaped_by_their_kind_and_the_item(string? imageType, string? itemType, string shape)
    {
        Assert.Equal(shape, ImageEditorLogic.RemoteImageShape(imageType, itemType));
    }

    [Theory]
    [InlineData("""{"Width":1920,"Height":1080,"Language":"en"}""", "1920 x 1080 • en")]
    [InlineData("""{"Width":1920,"Height":1080}""", "1920 x 1080")]
    [InlineData("""{"Width":1920,"Language":"en"}""", "en")]
    [InlineData("""{"Language":"de"}""", "de")]
    [InlineData("""{}""", null)]
    public void Remote_images_show_their_size_and_language(string image, string? expected)
    {
        Assert.Equal(expected, ImageEditorLogic.RemoteImageSize(JsonNode.Parse(image)));
    }

    [Theory]
    [InlineData("""{"CommunityRating":3,"RatingType":"Likes"}""", "3 likes")]
    [InlineData("""{"CommunityRating":1,"RatingType":"Likes"}""", "1 like")]
    [InlineData("""{"CommunityRating":0,"RatingType":"Score"}""", "Unrated")]
    [InlineData("""{"CommunityRating":7.5,"RatingType":"Score"}""", "7.5")]
    [InlineData("""{"CommunityRating":7,"RatingType":"Score","VoteCount":0}""", "7.0")]
    [InlineData("""{"CommunityRating":7.46,"RatingType":"Score","VoteCount":12}""", "7.5 • 12 votes")]
    [InlineData("""{"CommunityRating":8,"VoteCount":1}""", "8.0 • 1 vote")]
    [InlineData("""{"VoteCount":12}""", null)]
    public void Remote_images_show_their_rating(string image, string? expected)
    {
        Assert.Equal(expected, ImageEditorLogic.RemoteImageRating(JsonNode.Parse(image)));
    }

    [Theory]
    [InlineData(0, 30, 55, 1, 30)]
    [InlineData(30, 30, 55, 31, 55)]
    [InlineData(0, 30, 12, 1, 12)]
    [InlineData(0, 30, 0, 0, 0)]
    public void The_paging_names_the_first_and_last_image_of_the_page(int startIndex, int limit, int total, int start, int end)
    {
        Assert.Equal((start, end), ImageEditorLogic.PageRange(startIndex, limit, total));
    }

    [Fact]
    public void Remote_searches_and_uploads_know_the_same_kinds_of_images()
    {
        Assert.Equal(
            new[] { "Primary", "Art", "Backdrop", "Banner", "Box", "BoxRear", "Disc", "Logo", "Menu", "Thumb" },
            ImageEditorLogic.BrowsableTypes);
    }
}

public class ItemIdentifierLogicTests
{
    [Theory]
    [InlineData("Episode", "backdrop")]
    [InlineData("MusicAlbum", "square")]
    [InlineData("MusicArtist", "square")]
    [InlineData("Movie", "portrait")]
    [InlineData(null, "portrait")]
    public void Search_results_are_shaped_like_the_item(string? itemType, string shape)
    {
        Assert.Equal(shape, ItemIdentifierLogic.CardShape(itemType));
    }

    [Theory]
    [InlineData("""{"Name":"Third Film","SearchProviderName":"TheMovieDb","ProductionYear":2008}""", "Movie", "Third Film|TheMovieDb|2008")]
    [InlineData("""{"Name":"Third Film","SearchProviderName":"TheMovieDb"}""", "Movie", "Third Film|TheMovieDb|")]
    [InlineData("""{"Name":"Third Film"}""", "Movie", "Third Film||")]
    [InlineData("""{"Name":"Third Film","SearchProviderName":"TheMovieDb","ProductionYear":0}""", "Movie", "Third Film|TheMovieDb|")]
    [InlineData("""{"Name":"Album","SearchProviderName":"MusicBrainz","AlbumArtist":{"Name":"Band"},"ProductionYear":1999}""", "MusicAlbum", "Album|MusicBrainz|Band|1999")]
    [InlineData("""{"Name":"Album","SearchProviderName":"MusicBrainz","ProductionYear":1999}""", "MusicAlbum", "Album|MusicBrainz|1999|")]
    [InlineData("""{"Name":"Album","SearchProviderName":"MusicBrainz","AlbumArtist":{"Name":"Band"},"ProductionYear":1999}""", "Movie", "Album|MusicBrainz|Band")]
    public void Search_results_have_three_lines_and_albums_four(string result, string itemType, string lines)
    {
        Assert.Equal(lines.Split('|'), ItemIdentifierLogic.ResultLines(JsonNode.Parse(result), itemType));
    }

    [Fact]
    public void A_search_needs_a_name_or_an_id()
    {
        Assert.Null(ItemIdentifierLogic.SearchBody("abc", "", "2008", [("Tmdb", ""), ("Imdb", "")]));
        Assert.Null(ItemIdentifierLogic.SearchBody("abc", null, null, []));
    }

    [Fact]
    public void The_search_sends_every_id_and_the_name_and_year_that_were_entered()
    {
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":"","Imdb":"tt1"}},"ItemId":"abc"}""",
            ItemIdentifierLogic.SearchBody("abc", "", "", [("Tmdb", ""), ("Imdb", "tt1")])!.ToJsonString());
        Assert.Equal(
            """{"SearchInfo":{"ProviderIds":{"Tmdb":""},"Name":"Third Film","Year":2008},"ItemId":"abc"}""",
            ItemIdentifierLogic.SearchBody("abc", "Third Film", "2008", [("Tmdb", "")])!.ToJsonString());

        // parseInt: the digits before the point, nothing for something that is not a number.
        Assert.Equal(2008, ItemIdentifierLogic.SearchBody("abc", "x", "2008.9", [])!["SearchInfo"]!["Year"]!.GetValue<int>());
        Assert.False(ItemIdentifierLogic.SearchBody("abc", "x", "soon", [])!["SearchInfo"]!.AsObject().ContainsKey("Year"));
    }
}

public class SubtitleLyricsLogicTests
{
    [Theory]
    [InlineData("/media/movies/Third Film/Third Film.mkv", "Third Film.mkv")]
    [InlineData(@"C:\media\Third Film.mkv", "Third Film.mkv")]
    [InlineData(@"\\nas\share/mixed\Third Film.mkv", "Third Film.mkv")]
    [InlineData("Third Film.mkv", "Third Film.mkv")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void The_file_of_a_path_is_what_follows_the_last_separator(string? path, string fileName)
    {
        Assert.Equal(fileName, SubtitleLyricsLogic.FileName(path));
    }

    [Theory]
    [InlineData("Third Film.srt", true)]
    [InlineData("Third Film.en.ass", true)]
    [InlineData("Third Film.mks", true)]
    [InlineData("Third Film.SRT", false)]
    [InlineData("Third Film.txt", false)]
    [InlineData("srt", false)]
    [InlineData(null, false)]
    public void Subtitles_are_recognised_by_their_extension_in_lower_case(string? fileName, bool accepted)
    {
        Assert.Equal(accepted, SubtitleLyricsLogic.HasExtension(fileName, SubtitleLyricsLogic.SubtitleExtensions));
    }

    [Theory]
    [InlineData("Song.lrc", true)]
    [InlineData("Song.txt", true)]
    [InlineData("Song.LRC", false)]
    [InlineData("Song.srt", false)]
    public void Lyrics_are_recognised_by_their_extension_in_lower_case(string fileName, bool accepted)
    {
        Assert.Equal(accepted, SubtitleLyricsLogic.HasExtension(fileName, SubtitleLyricsLogic.LyricsExtensions));
    }

    [Theory]
    [InlineData("Third Film.en.srt", "srt")]
    [InlineData("Third Film.VTT", "vtt")]
    [InlineData("noextension", "noextension")]
    public void The_format_of_a_subtitle_is_its_extension(string fileName, string format)
    {
        Assert.Equal(format, SubtitleLyricsLogic.SubtitleFormat(fileName));
    }

    [Fact]
    public void Remote_subtitles_name_their_features_in_a_fixed_order()
    {
        var all = JsonNode.Parse("""{"HearingImpaired":true,"Forced":true,"MachineTranslated":true,"AiTranslated":true,"IsHashMatch":true}""");

        Assert.Equal(
            new[] { "PerfectMatch", "AiTranslated", "MachineTranslated", "ForeignPartsOnly", "HearingImpairedShort" },
            SubtitleLyricsLogic.SubtitleFeatures(all));
        Assert.Equal(new[] { "ForeignPartsOnly" }, SubtitleLyricsLogic.SubtitleFeatures(JsonNode.Parse("""{"Forced":true,"IsHashMatch":false}""")));
        Assert.Empty(SubtitleLyricsLogic.SubtitleFeatures(JsonNode.Parse("{}")));
    }

    [Fact]
    public void A_new_group_starts_whenever_the_provider_changes()
    {
        var results = JsonNode.Parse("""[{"P":"A","N":1},{"P":"A","N":2},{"P":"B","N":3},{"P":"A","N":4},{"N":5}]""")!.AsArray();

        var groups = SubtitleLyricsLogic.GroupByProvider(results, result => result.Text("P"));

        Assert.Equal(new[] { "A", "B", "A", "" }, groups.Select(group => group.Provider));
        Assert.Equal(new[] { "1,2", "3", "4", "5" }, groups.Select(group => string.Join(',', group.Results.Select(result => result.Text("N")))));
        Assert.Empty(SubtitleLyricsLogic.GroupByProvider([], result => result.Text("P")));
    }

    [Theory]
    [InlineData("""{"Start":123400000}""", "[00:12.34] ")]
    [InlineData("""{"Start":754500000}""", "[01:15.45] ")]
    [InlineData("""{"Start":0}""", "[00:00.00] ")]
    [InlineData("""{"Start":60000000000}""", "[100:00.00] ")]
    [InlineData("""{"Text":"plain"}""", "")]
    public void Synced_lines_start_with_their_time(string line, string timestamp)
    {
        Assert.Equal(timestamp, SubtitleLyricsLogic.LyricTimestamp(JsonNode.Parse(line)));
    }

    [Fact]
    public void Lyrics_are_listed_line_by_line()
    {
        var lyrics = JsonNode.Parse("""{"Lyrics":[{"Start":123400000,"Text":"First line"},{"Text":"Second line"},{"Text":""}]}""");

        Assert.Equal(new[] { "[00:12.34] First line", "Second line", "" }, SubtitleLyricsLogic.LyricLines(lyrics));
        Assert.Empty(SubtitleLyricsLogic.LyricLines(JsonNode.Parse("{}")));
    }

    [Fact]
    public void Remote_lyrics_are_named_by_artist_album_and_title()
    {
        Assert.Equal("Band - First Album - Song", SubtitleLyricsLogic.LyricsTitle(JsonNode.Parse("""{"Artist":"Band","Album":"First Album","Title":"Song"}""")));
        Assert.Equal(" -  - Song", SubtitleLyricsLogic.LyricsTitle(JsonNode.Parse("""{"Title":"Song"}""")));
    }

    [Theory]
    [InlineData(2150000000d, "3:35")]
    [InlineData(599999999d, "0:59")]
    [InlineData(36000000000d, "60:00")]
    [InlineData(0d, "0:00")]
    [InlineData(null, "0:00")]
    public void The_length_of_remote_lyrics_is_shown_in_minutes_and_seconds(double? ticks, string duration)
    {
        Assert.Equal(duration, SubtitleLyricsLogic.LyricsDuration(ticks));
    }

    [Fact]
    public void Uploads_are_sent_as_base64_or_as_text()
    {
        Assert.Equal("aGVsbG8=", SubtitleLyricsLogic.Base64("hello"u8.ToArray()));
        Assert.Equal("héllo", SubtitleLyricsLogic.Utf8("héllo"u8.ToArray()));
    }
}

public class MetadataEditorLogicTests : ItemsTestContext
{
    private Globalize Globalize => Get<Globalize>();

    [Theory]
    [InlineData("2008-05-17T00:00:00.0000000Z", "2008-05-17")]
    [InlineData("2008-05-17T23:30:00Z", "2008-05-17")]
    [InlineData("2008-05-17T00:30:00+02:00", "2008-05-16")]
    [InlineData("2008-05-17", "2008-05-17")]
    [InlineData("soon", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Date_inputs_show_the_day_in_utc(string? date, string value)
    {
        Assert.Equal(value, MetadataEditorLogic.DateInputValue(date));
    }

    [Theory]
    [InlineData("", "2008-05-17T10:20:30.0000000Z", null)]
    [InlineData(null, "2008-05-17T10:20:30.0000000Z", null)]
    [InlineData("2008-05-17", "2008-05-17T10:20:30.1234567Z", "2008-05-17T10:20:30.123Z")]
    [InlineData("2008-05-18", "2008-05-17T10:20:30.0000000Z", "2008-05-18")]
    [InlineData("2008-05-17", null, "2008-05-17")]
    [InlineData("2008-05-17", "soon", "2008-05-17")]
    public void A_date_keeps_its_time_of_day_as_long_as_the_day_is_the_same(string? input, string? current, string? saved)
    {
        Assert.Equal(saved, MetadataEditorLogic.DateToSave(input, current));
    }

    [Fact]
    public void Artists_are_separated_by_semicolons()
    {
        Assert.Equal("""[{"Name":"One"},{"Name":"Two "},{"Name":"Three"}]""", MetadataEditorLogic.SplitNames(" One;Two ;;Three; ").ToJsonString());
        Assert.Empty(MetadataEditorLogic.SplitNames(""));
        Assert.Empty(MetadataEditorLogic.SplitNames(null));
    }

    [Fact]
    public void Lists_are_sorted_by_name_whatever_the_case()
    {
        var values = new List<string> { "beta", "Gamma", "Alpha", "delta" };

        MetadataEditorLogic.Sort(values);

        Assert.Equal(new[] { "Alpha", "beta", "delta", "Gamma" }, values);
    }

    [Theory]
    [InlineData(27000000000d, "45")]
    [InlineData(27300000000d, "46")]
    [InlineData(26900000000d, "45")]
    [InlineData(0d, "")]
    [InlineData(null, "")]
    public void The_runtime_of_a_series_is_shown_in_whole_minutes(double? ticks, string minutes)
    {
        Assert.Equal(minutes, MetadataEditorLogic.RuntimeMinutes(ticks));
    }

    [Theory]
    [InlineData("Movie", "Name Overview Genres OfficialRating Cast ProductionLocations Studios Tags", "ProductionLocations")]
    [InlineData("Person", "Name Overview Genres OfficialRating Cast ProductionLocations Studios Tags", "BirthLocation")]
    [InlineData("Series", "Name Overview Genres OfficialRating Cast ProductionLocations Runtime Studios Tags", "ProductionLocations")]
    [InlineData(null, "Name Overview Genres OfficialRating Cast ProductionLocations Studios Tags", "ProductionLocations")]
    public void The_fields_that_can_be_locked_depend_on_the_type(string? type, string values, string locationsKey)
    {
        var fields = MetadataEditorLogic.LockableFields(type);

        Assert.Equal(values.Split(' '), fields.Select(field => field.Value));
        Assert.Equal(locationsKey, fields.Single(field => field.Value == "ProductionLocations").Key);
        Assert.Equal("ParentalRating", fields.Single(field => field.Value == "OfficialRating").Key);
        Assert.Equal("People", fields.Single(field => field.Value == "Cast").Key);
    }

    [Fact]
    public void Fields_are_empty_for_what_the_item_does_not_have()
    {
        var fields = MetadataEditorLogic.FieldValues(JsonNode.Parse("""
            { "Type": "Episode", "Name": "Pilot", "CommunityRating": 0, "ProductionYear": 0, "IndexNumber": 0, "ParentIndexNumber": 0,
              "AirsBeforeSeasonNumber": 0, "SeriesName": "Not for episodes", "Taglines": [], "ProductionLocations": [] }
            """));

        Assert.Equal("Pilot", fields["txtName"]);

        // "x || ''" hides a zero, "x == null ? '' : x" does not.
        Assert.Equal("", fields["txtCommunityRating"]);
        Assert.Equal("", fields["txtProductionYear"]);
        Assert.Equal("0", fields["txtIndexNumber"]);
        Assert.Equal("0", fields["txtParentIndexNumber"]);
        Assert.Equal("0", fields["txtAirsBeforeSeason"]);
        Assert.Equal("", fields["txtAirsAfterSeason"]);
        Assert.Equal("", fields["txtseriesName"]);
        Assert.Equal("", fields["txtTagline"]);
        Assert.Equal("", fields["txtPlaceOfBirth"]);
        Assert.Equal("", fields["txtPremiereDate"]);
        Assert.Equal("", fields["txtSeriesRuntime"]);
        Assert.Equal("", fields["txtArtist"]);
        Assert.All(MetadataEditorLogic.ResetFields, field => Assert.True(fields.ContainsKey(field), field));
        Assert.DoesNotContain("txtPath", MetadataEditorLogic.ResetFields);
    }

    [Fact]
    public void External_ids_are_named_with_their_type_when_they_have_one()
    {
        Assert.Equal("TheMovieDb", MetadataEditorLogic.ExternalIdName(JsonNode.Parse("""{"Name":"TheMovieDb","Key":"Tmdb"}"""), Globalize));
        Assert.Equal(
            "TheMovieDb " + Globalize.Translate("BoxSet"),
            MetadataEditorLogic.ExternalIdName(JsonNode.Parse("""{"Name":"TheMovieDb","Key":"TmdbCollection","Type":"BoxSet"}"""), Globalize));
    }

    [Fact]
    public void Before_an_item_is_loaded_the_layout_has_the_labels_of_the_template_and_shows_nothing()
    {
        var layout = new MetadataEditorLayout();

        Assert.Equal("LabelTitle", layout.LabelKey("txtName"));
        Assert.Equal("LabelOriginalTitle", layout.LabelKey("txtOriginalName"));
        Assert.Equal("LabelSortTitle", layout.LabelKey("txtSortName"));
        Assert.Equal("LabelReleaseDate", layout.LabelKey("txtPremiereDate"));
        Assert.Equal("LabelYear", layout.LabelKey("txtProductionYear"));
        Assert.Equal("LabelEndDate", layout.LabelKey("txtEndDate"));
        Assert.Null(layout.LabelKey("txtIndexNumber"));
        Assert.Null(layout.LabelKey("txtParentIndexNumber"));
        Assert.False(layout.Shows("fldPath"));
        Assert.Empty(layout.DisplayOrderOptions);
    }

    [Fact]
    public void The_layout_of_an_item_names_its_fields()
    {
        var person = MetadataEditorLogic.Layout(JsonNode.Parse("""{"Type":"Person"}"""), Globalize);
        var episode = MetadataEditorLogic.Layout(JsonNode.Parse("""{"Type":"Episode","MediaType":"Video","ParentIndexNumber":0}"""), Globalize);
        var movie = MetadataEditorLogic.Layout(JsonNode.Parse("""{"Type":"Movie","MediaType":"Video","Path":"/m","EnableMediaSourceDisplay":true}"""), Globalize);

        Assert.Equal("LabelName", person.LabelKey("txtName"));
        Assert.Equal("LabelBirthDate", person.LabelKey("txtPremiereDate"));
        Assert.True(person.Shows("fldPlaceOfBirth"));
        Assert.False(person.Shows("fldPath"));
        Assert.Equal("LabelEpisodeNumber", episode.LabelKey("txtIndexNumber"));
        Assert.Equal("LabelSeasonNumber", episode.LabelKey("txtParentIndexNumber"));
        Assert.True(episode.Shows("collapsibleSpecialEpisodeInfo"));
        Assert.False(episode.Shows("fldTagline"));
        Assert.True(movie.Shows("fldPath"));
        Assert.True(movie.Shows("fldTagline"));
        Assert.Empty(movie.DisplayOrderOptions);
    }
}

public class MediaInfoBuilderTests : ItemsTestContext
{
    private static readonly JsonNode Item = JsonNode.Parse("""
        {
            "MediaSources": [
                {
                    "Name": "1080p", "Container": "mkv", "Formats": ["matroska", "webm"], "Path": "/media/movies/Third Film.mkv", "Size": 1048576,
                    "Timestamp": "None",
                    "MediaStreams": [
                        { "Type": "Subtitle", "Index": 2, "IsExternal": true, "Codec": "srt", "Language": "eng", "DisplayTitle": "English - SRT" },
                        { "Type": "Audio", "Index": 1, "Codec": "aac", "Language": "eng", "Channels": 2, "ChannelLayout": "stereo",
                          "SampleRate": 48000, "BitRate": 128999, "IsDefault": true },
                        { "Type": "Video", "Index": 0, "Codec": "h264", "Language": "eng", "IsAVC": true, "Profile": "High", "Level": 41,
                          "Width": 1920, "Height": 1080, "AspectRatio": "16:9", "IsInterlaced": false, "BitDepth": 8, "RefFrames": 0 },
                        { "Type": "Data", "Index": 3 },
                        { "Type": "EmbeddedImage", "Index": 4, "Codec": "mjpeg", "AspectRatio": "2:3" }
                    ]
                },
                { "Container": "mp4", "MediaStreams": [] }
            ]
        }
        """)!;

    private string T(string key) => Get<Globalize>().Translate(key);

    [Fact]
    public void Sources_list_their_container_formats_path_and_size()
    {
        var sources = MediaInfoBuilder.Build(Item, isAdmin: true, Get<Globalize>());

        Assert.Equal(2, sources.Count);
        Assert.Equal("1080p", sources[0].Name);
        Assert.Equal(
            new[] { T("MediaInfoContainer"), T("MediaInfoFormat"), T("MediaInfoPath"), T("MediaInfoSize") },
            sources[0].Attributes.Select(attribute => attribute.Label));
        Assert.Equal("mkv", sources[0].Attributes[0].Value);
        Assert.Equal("matroska,webm", sources[0].Attributes[1].Value);
        Assert.Equal(new MediaInfoAttribute(T("MediaInfoPath"), "/media/movies/Third Film.mkv", Ltr: true), sources[0].Attributes[2]);

        // A version without a name has no heading.
        Assert.Null(sources[1].Name);
        Assert.Equal(new[] { new MediaInfoAttribute(T("MediaInfoContainer"), "mp4") }, sources[1].Attributes);
        Assert.Empty(sources[1].Streams);
    }

    [Fact]
    public void Only_administrators_see_the_path()
    {
        var source = MediaInfoBuilder.Build(Item, isAdmin: false, Get<Globalize>())[0];

        Assert.DoesNotContain(source.Attributes, attribute => attribute.Label == T("MediaInfoPath"));
    }

    [Fact]
    public void Streams_are_listed_internal_first_and_default_first_without_data_streams()
    {
        var streams = MediaInfoBuilder.Build(Item, isAdmin: true, Get<Globalize>())[0].Streams;

        Assert.Equal(new[] { T("Audio"), T("Video"), T("Image"), T("Subtitle") }, streams.Select(stream => stream.Title));
    }

    [Fact]
    public void Streams_list_what_they_have()
    {
        var streams = MediaInfoBuilder.Build(Item, isAdmin: true, Get<Globalize>())[0].Streams;
        string[] Lines(int index) => streams[index].Attributes.Select(attribute => attribute.Label + ": " + attribute.Value).ToArray();

        Assert.Equal(
            new[]
            {
                T("MediaInfoLanguage") + ": eng", T("MediaInfoCodec") + ": AAC", T("MediaInfoLayout") + ": stereo", T("MediaInfoChannels") + ": 2 ch",
                T("MediaInfoBitrate") + ": 128 kbps", T("MediaInfoSampleRate") + ": 48000 Hz", T("MediaInfoDefault") + ": Yes",
                T("MediaInfoForced") + ": No", T("MediaInfoExternal") + ": No"
            },
            Lines(0));

        // A video stream has no language line, and zero counts as not set.
        Assert.Equal(
            new[]
            {
                T("MediaInfoCodec") + ": H264", "AVC: Yes", T("MediaInfoProfile") + ": High", T("MediaInfoLevel") + ": 41",
                T("MediaInfoResolution") + ": 1920x1080", T("MediaInfoAspectRatio") + ": 16:9", T("MediaInfoInterlaced") + ": No",
                T("MediaInfoBitDepth") + ": 8 bit", T("MediaInfoTimestamp") + ": None"
            },
            Lines(1));

        // The aspect ratio of an embedded picture is not shown.
        Assert.Equal(new[] { T("MediaInfoCodec") + ": MJPEG" }, Lines(2));
        Assert.Equal(
            new[]
            {
                T("MediaInfoTitle") + ": English - SRT", T("MediaInfoLanguage") + ": eng", T("MediaInfoCodec") + ": SRT",
                T("MediaInfoDefault") + ": No", T("MediaInfoForced") + ": No", T("MediaInfoExternal") + ": Yes"
            },
            Lines(3));
    }

    [Fact]
    public void A_block_is_copied_as_its_text()
    {
        var source = MediaInfoBuilder.Build(Item, isAdmin: false, Get<Globalize>())[1];
        var stream = MediaInfoBuilder.Build(Item, isAdmin: false, Get<Globalize>())[0].Streams[2];

        Assert.Equal(T("MediaInfoContainer") + ": mp4\n", source.CopyText);
        Assert.Equal("\n" + T("Image") + "\n" + T("MediaInfoCodec") + ": MJPEG\n", stream.CopyText);
        Assert.StartsWith("1080p\n" + T("MediaInfoContainer") + ": mkv\n", MediaInfoBuilder.Build(Item, isAdmin: false, Get<Globalize>())[0].CopyText, StringComparison.Ordinal);
    }
}
