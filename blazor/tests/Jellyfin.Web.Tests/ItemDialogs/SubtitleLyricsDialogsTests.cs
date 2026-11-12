using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components.Forms;

namespace Jellyfin.Web.Tests.ItemDialogs;

public class SubtitleEditorTests : ItemDialogsTestContext
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHallo\n";
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string ItemId = TestItems.Id(1);

    private void HaveMovie()
    {
        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new
        {
            Id = ItemId,
            Type = "Movie",
            Path = "/media/movies/Movie (2008)/Movie.mkv",
            MediaStreams = new object[]
            {
                new { Type = "Video", Index = 0, DisplayTitle = "1080p H264" },
                new { Type = "Subtitle", Index = 2, DisplayTitle = "English - SUBRIP" },
                new { Type = "Subtitle", Index = 3, DisplayTitle = "German - SRT - External", Path = "/media/movies/Movie (2008)/Movie.ger.srt" }
            }
        });
        Server.OnGet("/Localization/Cultures", new[]
        {
            new { DisplayName = "English", ThreeLetterISOLanguageName = "eng" },
            new { DisplayName = "German", ThreeLetterISOLanguageName = "ger" },
            new { DisplayName = "French", ThreeLetterISOLanguageName = "fre" }
        });
    }

    private void HaveResults(string language) => Server.OnGet($"/Items/{ItemId}/RemoteSearch/Subtitles/{language}", new object[]
    {
        new { Id = "s1", ProviderName = "OpenSubtitles", Name = "Movie.2008.srt", Format = "srt", DownloadCount = 120, FrameRate = 23.976, Comment = "Good sync", IsHashMatch = true, HearingImpaired = true },
        new { Id = "s2", ProviderName = "OpenSubtitles", Name = "Movie.alt.srt", Format = "srt", DownloadCount = 0, FrameRate = 0 },
        new { Id = "s3", ProviderName = "Podnapisi", Name = "Movie.pod.ass", Format = "ass", Forced = true, AiTranslated = true, MachineTranslated = true }
    });

    private Task<bool> OpenEditor()
    {
        var pending = Open(() => SubtitleEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(3, Options(".subtitleEditorDialog #selectLanguage").Length));
        return pending;
    }

    private int Loads => Server.RequestsTo($"/Users/{UserId}/Items/{ItemId}").Count;

    [Fact]
    public async Task The_subtitles_of_the_video_are_listed_and_only_external_ones_can_be_deleted()
    {
        SignIn(admin: true);
        HaveMovie();

        var pending = OpenEditor();

        Assert.Equal(T("Subtitles"), DialogTitle);
        Assert.Equal(new[] { $"/Users/{UserId}/Items/{ItemId}", "/Localization/Cultures" }, Server.Requests.Select(request => request.Path));
        Assert.False(IsHidden(".subtitleList"));
        Assert.Equal(T("MySubtitles"), Find(".subtitleList h2").TextContent);
        var streams = FindAll(".subtitleList .listItem");
        Assert.Equal(new[] { "2", "3" }, streams.Select(stream => stream.GetAttribute("data-index")));
        Assert.Equal("English - SUBRIP", streams[0].QuerySelector(".listItemBody > div")!.TextContent);
        Assert.Null(streams[0].QuerySelector(".btnDelete"));
        Assert.Null(streams[0].QuerySelector(".listItemBodyText"));
        Assert.Equal("/media/movies/Movie (2008)/Movie.ger.srt", streams[1].QuerySelector(".listItemBodyText")!.TextContent);
        Assert.Equal("3", streams[1].QuerySelector(".btnDelete")!.GetAttribute("data-index"));

        Assert.False(IsHidden("p.originalFile"));
        Assert.Equal("Movie.mkv", Find(".pathValue").TextContent);
        Assert.Equal(new[] { "eng", "ger", "fre" }, Options("#selectLanguage"));
        Assert.Equal("eng", Find("#selectLanguage").Value());
        Assert.True(IsHidden(".noSearchResults"));
        Assert.False(IsLoading);

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public void A_video_without_subtitles_or_path_hides_the_list_and_the_file_line()
    {
        SignIn();
        HaveMovie();
        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new { Id = ItemId, Type = "Movie", MediaStreams = new object[] { new { Type = "Audio", Index = 1 } } });

        _ = OpenEditor();

        Assert.True(IsHidden(".subtitleList"));
        Assert.Empty(FindAll(".subtitleList h2"));
        Assert.True(IsHidden("p.originalFile"));
    }

    [Theory]
    [InlineData(null, null, "eng")]
    [InlineData(null, "ger", "ger")]
    [InlineData("fre", "ger", "fre")]
    public void The_language_is_the_last_searched_one_or_the_preferred_subtitle_language(string? last, string? preferred, string expected)
    {
        SignIn(configure: user => user.Configuration!.SubtitleLanguagePreference = preferred);
        HaveMovie();
        if (last is not null)
        {
            Get<UserSettings>().Set("subtitleeditor-language", last);
        }

        _ = OpenEditor();

        Assert.Equal(expected, Find("#selectLanguage").Value());
    }

    [Fact]
    public async Task Searching_lists_the_results_by_provider_and_remembers_the_language()
    {
        SignIn();
        HaveMovie();
        HaveResults("ger");

        _ = OpenEditor();
        Pick("#selectLanguage", "ger");
        await Submit("form.subtitleSearchForm");

        Eventually(() => Assert.Equal(3, FindAll(".subtitleResults .listItem").Count));
        Assert.Equal($"/Items/{ItemId}/RemoteSearch/Subtitles/ger", Server.Requests.Last().Uri.PathAndQuery);
        Assert.Equal("ger", Get<UserSettings>().Get("subtitleeditor-language"));
        Assert.Equal(new[] { "OpenSubtitles", "Podnapisi" }, FindAll(".subtitleResults h2").Select(heading => heading.TextContent));
        Assert.True(IsHidden(".noSearchResults"));
        Assert.False(IsLoading);

        var results = FindAll(".subtitleResults .listItem");
        Assert.Equal(new[] { "s1", "s2", "s3" }, results.Select(result => result.GetAttribute("data-subid")));

        // A comment or a feature needs the third line.
        Assert.Equal(new[] { true, false, true }, results.Select(result => result.QuerySelector(".listItemBody")!.HasClass("three-line")));
        Assert.Equal("Movie.2008.srt", results[0].QuerySelector(".listItemBody > div")!.TextContent);
        Assert.Equal(
            new[] { T("FormatValue", "srt"), T("DownloadsValue", 120), T("Framerate") + ": 23.976" },
            results[0].QuerySelector(".listItemBodyText")!.QuerySelectorAll("span").Select(span => span.TextContent));
        Assert.Equal("Good sync", results[0].QuerySelectorAll(".listItemBodyText")[1].TextContent);
        Assert.Equal(
            new[] { T("PerfectMatch"), T("HearingImpairedShort") },
            results[0].QuerySelectorAll(".subtitleFeaturePillow").Select(pill => pill.TextContent));

        // No frame rate line for 0, no downloads line when the provider does not count them.
        Assert.Equal(
            new[] { T("FormatValue", "srt"), T("DownloadsValue", 0) },
            results[1].QuerySelector(".listItemBodyText")!.QuerySelectorAll("span").Select(span => span.TextContent));
        Assert.Empty(results[1].QuerySelectorAll(".subtitleFeaturePillow"));
        Assert.Equal(
            new[] { T("FormatValue", "ass") },
            results[2].QuerySelector(".listItemBodyText")!.QuerySelectorAll("span").Select(span => span.TextContent));
        Assert.Equal(
            new[] { T("AiTranslated"), T("MachineTranslated"), T("ForeignPartsOnly") },
            results[2].QuerySelectorAll(".subtitleFeaturePillow").Select(pill => pill.TextContent));
    }

    [Fact]
    public async Task A_search_without_results_says_so_and_a_failed_one_keeps_what_was_found()
    {
        SignIn();
        HaveMovie();
        Server.OnGet($"/Items/{ItemId}/RemoteSearch/Subtitles/eng", Array.Empty<object>());
        HaveResults("ger");

        _ = OpenEditor();
        await Submit("form.subtitleSearchForm");
        Eventually(() => Assert.False(IsHidden(".noSearchResults")));
        Assert.Equal(T("NoSubtitleSearchResultsFound"), Find(".noSearchResults").TextContent);

        Pick("#selectLanguage", "ger");
        await Submit("form.subtitleSearchForm");
        Eventually(() => Assert.True(IsHidden(".noSearchResults")));
        Assert.Equal(3, FindAll(".subtitleResults .listItem").Count);

        // The server has no answer for French.
        Pick("#selectLanguage", "fre");
        await Submit("form.subtitleSearchForm");
        Eventually(() => Assert.EndsWith("/Subtitles/fre", Server.Requests.Last().Path));
        Eventually(() => Assert.False(IsLoading));
        Assert.Equal(3, FindAll(".subtitleResults .listItem").Count);
    }

    [Fact]
    public async Task Downloading_a_result_queues_it_at_the_server()
    {
        SignIn();
        HaveMovie();
        HaveResults("eng");
        Server.OnPost($"/Items/{ItemId}/RemoteSearch/Subtitles/s3");

        var pending = OpenEditor();
        await Submit("form.subtitleSearchForm");
        Eventually(() => Assert.Equal(3, FindAll(".btnDownload").Count));

        // The server refuses the first one: nothing is reported.
        await Click(".btnDownload[data-subid=s1]");
        Assert.Empty(Toasts);

        await Click(".btnDownload[data-subid=s3]");
        Assert.Equal(new[] { $"POST /Items/{ItemId}/RemoteSearch/Subtitles/s1", $"POST /Items/{ItemId}/RemoteSearch/Subtitles/s3" }, Writes);
        Assert.Equal("", Server.Requests.Last().Body ?? "");
        Assert.Equal(new[] { T("MessageDownloadQueued") }, Toasts);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task Deleting_a_subtitle_asks_first_and_reloads()
    {
        SignIn(admin: true);
        HaveMovie();

        var pending = OpenEditor();
        await Click(".btnDelete");
        Assert.Equal(T("ConfirmDeletion"), DialogTitle);
        Assert.Equal(T("MessageAreYouSureDeleteSubtitles"), Find(".dialog .text").TextContent.Trim());
        Assert.Equal(T("Delete"), Find("button.btnOption[data-id=ok]").TextContent.Trim());
        await Answer("cancel");
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Empty(Writes);

        // The server answers 404: nothing changed.
        await Click(".btnDelete");
        await Answer("ok");
        Eventually(() => Assert.Single(Writes));
        Eventually(() => Assert.False(IsLoading));
        Assert.Equal(1, Loads);

        Server.OnDelete($"/Videos/{ItemId}/Subtitles/3");
        await Click(".btnDelete");
        await Answer("ok");
        Eventually(() => Assert.Equal(2, Loads));
        Assert.Equal($"DELETE /Videos/{ItemId}/Subtitles/3", Writes.Last());

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task The_add_button_uploads_a_subtitle_in_the_selected_language_and_reloads()
    {
        SignIn(admin: true);
        HaveMovie();
        Server.OnPost($"/Videos/{ItemId}/Subtitles");

        var pending = OpenEditor();
        Pick("#selectLanguage", "ger");
        await Click(".btnOpenUploadMenu");

        Eventually(() => Assert.Equal(T("HeaderUploadSubtitle"), DialogTitle));
        Assert.Equal(new[] { "eng", "ger", "fre" }, Options(".subtitleUploaderDialog #selectLanguage"));
        Assert.Equal("ger", Find(".subtitleUploaderDialog #selectLanguage").Value());

        // Closing the uploader changes nothing.
        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(1, Loads);

        await Click(".btnOpenUploadMenu");
        Eventually(() => Assert.Equal(2, Dialogs.Dialogs.Count));
        View.FindComponent<InputFile>().UploadFiles(TextFile("Movie.ger.srt", Srt));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadSubtitleForm");

        Eventually(() => Assert.Equal(2, Loads));
        Assert.Equal(new[] { $"POST /Videos/{ItemId}/Subtitles" }, Writes);
        Assert.Single(Dialogs.Dialogs);
        await Click(".btnCancel");
        Assert.True(await pending);
    }
}

public class SubtitleUploaderTests : ItemDialogsTestContext
{
    private const string Srt = "1\n00:00:01,000 --> 00:00:02,000\nHallo\n";
    private static readonly string ItemId = TestItems.Id(1);

    private static readonly IReadOnlyList<JsonNode?> Languages =
    [
        new JsonObject { ["DisplayName"] = "English", ["ThreeLetterISOLanguageName"] = "eng" },
        new JsonObject { ["DisplayName"] = "German", ["ThreeLetterISOLanguageName"] = "ger" }
    ];

    private Task<bool> OpenUploader(string? language = null)
    {
        var pending = Open(() => SubtitleUploader.ShowAsync(Dialogs, ItemId, Languages, language));
        Eventually(() => Assert.Equal(T("HeaderUploadSubtitle"), DialogTitle));
        return pending;
    }

    private void PickFile(InputFileContent file) => View.FindComponent<InputFile>().UploadFiles(file);

    [Fact]
    public async Task A_subtitle_file_is_uploaded_as_base64_with_its_format_and_flags()
    {
        SignIn(admin: true);
        Server.OnPost($"/Videos/{ItemId}/Subtitles");

        var pending = OpenUploader();

        Assert.True(IsHidden("#fldUpload"));
        Assert.False(IsHidden("#labelDropSubtitle"));
        Assert.Equal(".sub,.srt,.vtt,.ass,.ssa,.mks", Find("#uploadSubtitle").GetAttribute("accept"));
        Assert.Equal("eng", Find("#selectLanguage").Value());
        await Click(".btnBrowse");
        Assert.Equal(1, Browser.Clicks);

        PickFile(TextFile("Movie.forced.SRT.srt", Srt));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        Assert.True(IsHidden("#labelDropSubtitle"));
        Assert.Equal("Movie.forced.SRT.srt", Find("#subtitleOutput").TextContent.Trim());

        Pick("#selectLanguage", "ger");
        Check("#chkIsForced", true);
        await Submit("form.uploadSubtitleForm");

        Assert.True(await pending);
        var upload = Assert.Single(Server.Requests);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal($"/Videos/{ItemId}/Subtitles", upload.Uri.PathAndQuery);
        var body = upload.JsonBody();
        Assert.Equal(new[] { "Data", "Language", "IsForced", "Format", "IsHearingImpaired" }, body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes(Srt)), body.Text("Data"));
        Assert.Equal("ger", body.Text("Language"));
        Assert.Equal("srt", body.Text("Format"));
        Assert.True(body.Flag("IsForced"));
        Assert.False(body.Flag("IsHearingImpaired"));
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task The_given_language_is_selected_and_hearing_impaired_can_be_marked()
    {
        SignIn(admin: true);
        Server.OnPost($"/Videos/{ItemId}/Subtitles");

        var pending = OpenUploader("ger");
        Assert.Equal("ger", Find("#selectLanguage").Value());
        PickFile(TextFile("Movie.ASS", Srt));
        Eventually(() => Assert.True(IsHidden("#fldUpload")));

        // The extension is compared as it is written.
        PickFile(TextFile("Movie.ass", Srt));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        Check("#chkIsHearingImpaired", true);
        await Submit("form.uploadSubtitleForm");

        Assert.True(await pending);
        var body = Assert.Single(Server.Requests).JsonBody();
        Assert.Equal("ger", body.Text("Language"));
        Assert.Equal("ass", body.Text("Format"));
        Assert.False(body.Flag("IsForced"));
        Assert.True(body.Flag("IsHearingImpaired"));
    }

    [Fact]
    public async Task A_file_of_another_kind_is_not_taken_and_submitting_without_one_names_the_allowed_kinds()
    {
        SignIn(admin: true);

        var pending = OpenUploader();
        PickFile(TextFile("Movie.srt", Srt));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));

        PickFile(TextFile("notes.txt", "hello"));
        Eventually(() => Assert.True(IsHidden("#fldUpload")));
        Assert.False(IsHidden("#labelDropSubtitle"));
        Assert.Equal("", Find("#subtitleOutput").TextContent.Trim());

        await Submit("form.uploadSubtitleForm");
        Assert.Equal(new[] { T("MessageSubtitleFileTypeAllowed") }, Toasts);
        Assert.Empty(Server.Requests);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task A_failed_upload_leaves_the_dialog_open()
    {
        SignIn(admin: true);

        var pending = OpenUploader();
        PickFile(TextFile("Movie.srt", Srt));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadSubtitleForm");

        Assert.Equal(new[] { $"POST /Videos/{ItemId}/Subtitles" }, Writes);
        Assert.False(pending.IsCompleted);
        Assert.False(IsLoading);

        await Click(".btnCancel");
        Assert.False(await pending);
    }
}

public class LyricsEditorTests : ItemDialogsTestContext
{
    private static readonly string UserId = TestApp.UserId.ToString("N");
    private static readonly string ItemId = TestItems.Id(1);

    private void HaveSong(bool lyrics = true)
    {
        Server.OnGet($"/Users/{UserId}/Items/{ItemId}", new { Id = ItemId, Type = "Audio", Path = @"D:\Music\Artist\Album\01 Song.flac" });
        if (lyrics)
        {
            Server.OnGet($"/Audio/{ItemId}/Lyrics", new
            {
                Metadata = new { },
                Lyrics = new object[] { new { Text = "First line", Start = 123400000 }, new { Text = "Second line" } }
            });
        }
    }

    private void HaveResults() => Server.OnGet($"/Audio/{ItemId}/RemoteSearch/Lyrics", new object[]
    {
        new
        {
            Id = "l1",
            ProviderName = "LrcLib",
            Lyrics = new
            {
                Metadata = new { Artist = "Artist", Album = "Album", Title = "Song", Length = 2250000000L, IsSynced = true },
                Lyrics = new object[] { new { Text = "Remote line", Start = 600000000 } }
            }
        },
        new
        {
            Id = "l2",
            ProviderName = "LrcLib",
            Lyrics = new
            {
                Metadata = new { Artist = "Artist", Album = "Album", Title = "Song (live)", Length = 50000000L, IsSynced = false },
                Lyrics = new object[] { new { Text = "Plain line" } }
            }
        }
    });

    private Task<bool> OpenEditor()
    {
        var pending = Open(() => LyricsEditor.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal("01 Song.flac", Find(".pathValue").TextContent));
        Eventually(() => Assert.False(IsLoading));
        return pending;
    }

    private int LyricsLoads => Server.Requests.Count(request => request.Method == HttpMethod.Get && request.Path == $"/Audio/{ItemId}/Lyrics");

    [Fact]
    public async Task The_lyrics_of_the_song_are_shown_with_their_timestamps()
    {
        SignIn(admin: true);
        HaveSong();

        var pending = OpenEditor();

        Assert.Equal(T("Lyrics"), DialogTitle);
        Assert.Equal(new[] { $"/Users/{UserId}/Items/{ItemId}", $"/Audio/{ItemId}/Lyrics" }, Server.Requests.Select(request => request.Path));
        Assert.Equal(T("Lyrics"), Find(".currentLyrics h2").TextContent);
        Assert.Equal("[00:12.34] First line<br>Second line<br>", Find(".currentLyrics > div").InnerHtml);
        Assert.False(IsHidden("p.originalFile"));
        Assert.True(IsHidden(".noSearchResults"));

        await Click(".btnCancel");
        Assert.False(await pending);
    }

    [Fact]
    public void A_song_without_lyrics_shows_none()
    {
        SignIn();
        HaveSong(lyrics: false);

        _ = OpenEditor();

        Assert.Empty(FindAll(".currentLyrics h2"));
        Assert.Equal("", Find(".currentLyrics").TextContent.Trim());
    }

    [Fact]
    public async Task Searching_lists_the_remote_lyrics_which_can_be_previewed()
    {
        SignIn();
        HaveSong();
        HaveResults();

        _ = OpenEditor();
        await Submit("form.lyricsSearchForm");

        Eventually(() => Assert.Equal(2, FindAll(".lyricsResults .listItem").Count));
        Assert.Equal($"/Audio/{ItemId}/RemoteSearch/Lyrics", Server.Requests.Last().Uri.PathAndQuery);
        Assert.Equal(new[] { "LrcLib" }, FindAll(".lyricsResults > h2").Select(heading => heading.TextContent));
        var results = FindAll(".lyricsResults .listItem");
        Assert.Equal(new[] { "l1", "l2" }, results.Select(result => result.GetAttribute("data-lyricsid")));
        Assert.Equal("Artist - Album - Song", results[0].QuerySelector(".listItemBody > div")!.TextContent);
        Assert.Equal(
            new[] { T("LabelDuration") + ": 3:45", T("LabelIsSynced") + ": True" },
            results[0].QuerySelectorAll(".listItemBodyText").Select(line => line.TextContent));
        Assert.Equal(
            new[] { T("LabelDuration") + ": 0:05", T("LabelIsSynced") + ": False" },
            results[1].QuerySelectorAll(".listItemBodyText").Select(line => line.TextContent));
        Assert.Equal("[01:00.00] Remote line<br>", results[0].QuerySelector(".hiddenLyrics > div")!.InnerHtml);
        Assert.True(IsHidden(".noSearchResults"));

        await Click(".btnPreview[data-lyricsid=l2]");
        Assert.Equal(2, Dialogs.Dialogs.Count);
        Assert.Equal(T("HeaderPreviewLyrics"), DialogTitle);
        Assert.Equal("Plain line<br>", Find(".lyricsPreview > div").InnerHtml);
        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
    }

    [Fact]
    public async Task A_search_without_results_says_so()
    {
        SignIn();
        HaveSong();
        Server.OnGet($"/Audio/{ItemId}/RemoteSearch/Lyrics", Array.Empty<object>());

        _ = OpenEditor();
        await Submit("form.lyricsSearchForm");

        Eventually(() => Assert.False(IsHidden(".noSearchResults")));
        Assert.Equal(T("NoLyricsSearchResultsFound"), Find(".noSearchResults").TextContent);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task Downloading_a_result_queues_it_at_the_server()
    {
        SignIn();
        HaveSong();
        HaveResults();
        Server.OnPost($"/Audio/{ItemId}/RemoteSearch/Lyrics/l2");

        var pending = OpenEditor();
        await Submit("form.lyricsSearchForm");
        Eventually(() => Assert.Equal(2, FindAll(".btnDownload").Count));

        // The server refuses the first one: nothing is reported.
        await Click(".btnDownload[data-lyricsid=l1]");
        Assert.Empty(Toasts);

        await Click(".btnDownload[data-lyricsid=l2]");
        Assert.Equal(new[] { $"POST /Audio/{ItemId}/RemoteSearch/Lyrics/l1", $"POST /Audio/{ItemId}/RemoteSearch/Lyrics/l2" }, Writes);
        Assert.Equal(new[] { T("MessageDownloadQueued") }, Toasts);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task Deleting_the_lyrics_asks_first_and_reports_a_failure()
    {
        SignIn(admin: true);
        HaveSong();

        var pending = OpenEditor();
        await Click(".btnDeleteLyrics");
        Assert.Equal(T("HeaderDeleteLyrics"), DialogTitle);
        Assert.Equal(T("ConfirmDeleteLyrics"), Find(".dialog .text").TextContent.Trim());
        await Answer("cancel");
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Empty(Writes);

        // The server answers 404.
        await Click(".btnDeleteLyrics");
        await Answer("ok");
        Eventually(() => Assert.Equal(T("ErrorDeletingLyrics"), Find(".dialog .text").TextContent.Trim()));
        await Answer("ok");
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(1, LyricsLoads);

        Server.OnDelete($"/Audio/{ItemId}/Lyrics");
        Fail(HttpMethod.Get, $"/Audio/{ItemId}/Lyrics", HttpStatusCode.NotFound);
        await Click(".btnDeleteLyrics");
        await Answer("ok");
        Eventually(() => Assert.Equal(2, LyricsLoads));
        Eventually(() => Assert.Empty(FindAll(".currentLyrics h2")));
        Assert.Equal(new[] { $"DELETE /Audio/{ItemId}/Lyrics", $"DELETE /Audio/{ItemId}/Lyrics" }, Writes);

        await Click(".btnCancel");
        Assert.True(await pending);
    }

    [Fact]
    public async Task The_add_button_uploads_lyrics_and_reloads()
    {
        SignIn(admin: true);
        HaveSong(lyrics: false);
        Server.OnPost($"/Audio/{ItemId}/Lyrics");

        var pending = OpenEditor();
        await Click(".btnOpenUploadMenu");
        Eventually(() => Assert.Equal(T("HeaderUploadLyrics"), DialogTitle));
        Dialogs.Dialogs[^1].Cancel();
        Eventually(() => Assert.Single(Dialogs.Dialogs));
        Assert.Equal(1, LyricsLoads);

        await Click(".btnOpenUploadMenu");
        Eventually(() => Assert.Equal(2, Dialogs.Dialogs.Count));
        View.FindComponent<InputFile>().UploadFiles(TextFile("song.lrc", "[00:01.00] Hello"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadLyricsForm");

        Eventually(() => Assert.Equal(2, LyricsLoads));
        Assert.Equal(new[] { $"POST /Audio/{ItemId}/Lyrics" }, Writes);
        await Click(".btnCancel");
        Assert.True(await pending);
    }
}

public class LyricsUploaderTests : ItemDialogsTestContext
{
    private const string Lrc = "[00:01.00] Hello\n[00:02.50] Wörld\n";
    private static readonly string ItemId = TestItems.Id(1);

    private Task<bool> OpenUploader()
    {
        var pending = Open(() => LyricsUploader.ShowAsync(Dialogs, ItemId));
        Eventually(() => Assert.Equal(T("HeaderUploadLyrics"), DialogTitle));
        return pending;
    }

    private void PickFile(InputFileContent file) => View.FindComponent<InputFile>().UploadFiles(file);

    [Fact]
    public async Task A_lyrics_file_is_uploaded_as_text_under_its_file_name()
    {
        SignIn(admin: true);
        Server.OnPost($"/Audio/{ItemId}/Lyrics");

        var pending = OpenUploader();

        Assert.True(IsHidden("#fldUpload"));
        Assert.False(IsHidden("#labelDropLyrics"));
        Assert.Equal(".lrc,.txt", Find("#uploadLyrics").GetAttribute("accept"));
        await Click(".btnBrowse");
        Assert.Equal(1, Browser.Clicks);

        PickFile(TextFile("My song.lrc", Lrc));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        Assert.True(IsHidden("#labelDropLyrics"));
        Assert.Equal("My song.lrc", Find("#lyricsOutput").TextContent.Trim());
        await Submit("form.uploadLyricsForm");

        Assert.True(await pending);
        var upload = Assert.Single(Server.Requests);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.Equal($"/Audio/{ItemId}/Lyrics", upload.Path);
        Assert.Equal("My song.lrc", upload.Query("fileName"));
        Assert.Equal(Lrc, upload.Body);
        Assert.Contains("Token=\"test-token\"", upload.Headers["Authorization"]);
        Assert.False(IsLoading);
    }

    [Fact]
    public async Task A_file_of_another_kind_is_not_taken_and_submitting_without_one_names_the_allowed_kinds()
    {
        SignIn(admin: true);

        var pending = OpenUploader();
        PickFile(TextFile("song.txt", "Hello"));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));

        PickFile(TextFile("song.LRC", Lrc));
        Eventually(() => Assert.True(IsHidden("#fldUpload")));
        Assert.Equal("", Find("#lyricsOutput").TextContent.Trim());

        await Submit("form.uploadLyricsForm");
        Assert.Equal(new[] { T("MessageLyricsFileTypeAllowed") }, Toasts);
        Assert.Empty(Server.Requests);
        Assert.False(pending.IsCompleted);
    }

    [Fact]
    public async Task A_failed_upload_leaves_the_dialog_open()
    {
        SignIn(admin: true);

        var pending = OpenUploader();
        PickFile(TextFile("song.lrc", Lrc));
        Eventually(() => Assert.False(IsHidden("#fldUpload")));
        await Submit("form.uploadLyricsForm");

        Assert.Equal(new[] { $"POST /Audio/{ItemId}/Lyrics" }, Writes);
        Assert.False(pending.IsCompleted);
        Assert.False(IsLoading);

        await Click(".btnCancel");
        Assert.False(await pending);
    }
}
