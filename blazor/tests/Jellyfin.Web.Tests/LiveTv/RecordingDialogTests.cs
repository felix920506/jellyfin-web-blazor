using System.Text.Json.Nodes;
using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.ItemDialogs;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>The recording dialogs: the programme, the options of one recording and of a series.</summary>
public class RecordingDialogTests : ItemDialogsTestContext
{
    private const string ProgramId = "program1";
    private const string TimerId = "timer1";
    private const string SeriesId = "series1";

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    [Fact]
    public async Task The_programme_dialog_shows_the_programme_and_records_it()
    {
        Server.OnGet($"/LiveTv/Programs/{ProgramId}", Program());
        Server.OnGet("/LiveTv/Timers/Defaults", """{"ProgramId":"program1"}""");
        Server.OnPost("/LiveTv/Timers");

        var closing = Open(() => RecordingCreator.ShowAsync(Dialogs, ProgramId));
        Eventually(() => Assert.Contains("The News", Find(".programDialog-itemName").TextContent));
        Assert.NotNull(Find(".recordingFields .singleRecordingButton"));
        Assert.True(Find(".btnPlay").Closest(".formDialogFooter")!.ClassList.Contains("hide"));

        await Click(".singleRecordingButton");
        Eventually(() => Assert.Contains("Recording scheduled.", Toasts));

        await Click(".btnCancel");
        Assert.True(await closing);
        Assert.Single(Server.RequestsTo("/LiveTv/Timers"), request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Play_is_offered_only_while_the_programme_is_on()
    {
        Get<DateTimeText>().Clock = static () => new DateTimeOffset(2024, 5, 17, 14, 30, 0, TimeSpan.Zero);
        Server.OnGet($"/LiveTv/Programs/{ProgramId}", Program(start: "2024-05-17T14:00:00.000Z", end: "2024-05-17T15:00:00.000Z", channel: TestItems.Id(7)));

        var closing = Open(() => RecordingCreator.ShowAsync(Dialogs, ProgramId));
        Eventually(() => Assert.False(Find(".btnPlay").Closest(".formDialogFooter")!.ClassList.Contains("hide")));

        await Click(".btnPlay");
        Assert.False(await closing);
        var played = Assert.Single(Playback.PlayRequests);
        Assert.Equal([TestItems.Id(7)], played.Ids);
    }

    [Fact]
    public async Task Closing_the_recording_editor_saves_the_padding()
    {
        Server.OnGet($"/LiveTv/Timers/{TimerId}", """{"Id":"timer1","Name":"Tonight","PrePaddingSeconds":60,"PostPaddingSeconds":120}""");
        Server.OnPost($"/LiveTv/Timers/{TimerId}");

        var closing = Open(() => RecordingEditor.ShowAsync(Dialogs, TimerId));
        Eventually(() => Assert.Equal("1", Find("#txtPrePaddingMinutes").GetAttribute("value")));
        Assert.Equal("2", Find("#txtPostPaddingMinutes").GetAttribute("value"));

        Enter("#txtPrePaddingMinutes", "5");
        Enter("#txtPostPaddingMinutes", "10");
        await Click(".btnCancel");
        var result = await closing;

        Assert.True(result.Updated);
        Assert.False(result.Deleted);
        var saved = Assert.Single(Server.RequestsTo($"/LiveTv/Timers/{TimerId}"), request => request.Method == HttpMethod.Post);
        Assert.Contains("\"PrePaddingSeconds\":300", saved.Body);
        Assert.Contains("\"PostPaddingSeconds\":600", saved.Body);
        Assert.Contains("\"Name\":\"Tonight\"", saved.Body);
    }

    [Fact]
    public async Task Cancelling_the_recording_does_not_save_it()
    {
        Server.OnGet($"/LiveTv/Timers/{TimerId}", """{"Id":"timer1","PrePaddingSeconds":0,"PostPaddingSeconds":0}""");
        Server.OnDelete($"/LiveTv/Timers/{TimerId}");

        var closing = Open(() => RecordingEditor.ShowAsync(Dialogs, TimerId));
        Eventually(() => Assert.NotNull(Find(".btnCancelRecording")));

        await Click(".btnCancelRecording");
        await Answer("ok");
        var result = await closing;

        Assert.True(result.Deleted);
        Assert.DoesNotContain(Server.Requests, request => request.Method == HttpMethod.Post);
        Assert.Contains("Recording cancelled.", Toasts);
    }

    [Fact]
    public async Task The_settings_link_hides_the_cancel_button()
    {
        Server.OnGet($"/LiveTv/Timers/{TimerId}", """{"Id":"timer1","PrePaddingSeconds":0,"PostPaddingSeconds":0}""");

        _ = Open(() => RecordingEditor.ShowAsync(Dialogs, TimerId, enableCancel: false));
        Eventually(() => Assert.True(Find(".formDialogFooter").ClassList.Contains("hide")));
        Assert.Empty(FindAll(".btnCancelRecording"));
    }

    [Fact]
    public async Task The_series_editor_saves_every_option()
    {
        Server.OnGet($"/LiveTv/SeriesTimers/{SeriesId}", Series());
        Server.OnPost($"/LiveTv/SeriesTimers/{SeriesId}");

        var closing = Open(() => SeriesRecordingEditor.ShowAsync(Dialogs, SeriesId));
        Eventually(() => Assert.Equal("all", Find(".selectShowType").GetAttribute("value")));
        Assert.Equal("Channel Channel One only", Find(".optionChannelOnly").TextContent);
        Assert.Contains("51", FindAll(".selectKeepUpTo option").Count.ToString());

        Pick(".selectShowType", "new");
        Check(".chkSkipEpisodesInLibrary", true);
        Pick(".selectChannels", "all");
        Pick(".selectAirTime", "any");
        Pick(".selectKeepUpTo", "3");
        Enter("#txtPrePaddingMinutes", "2");
        Enter("#txtPostPaddingMinutes", "4");
        await Click(".btnCancel");
        var result = await closing;

        Assert.True(result.Updated);
        var saved = Assert.Single(Server.RequestsTo($"/LiveTv/SeriesTimers/{SeriesId}"), request => request.Method == HttpMethod.Post);
        var body = JsonNode.Parse(saved.Body!)!.AsObject();
        Assert.True(body.Flag("RecordNewOnly"));
        Assert.True(body.Flag("SkipEpisodesInLibrary"));
        Assert.True(body.Flag("RecordAnyChannel"));
        Assert.True(body.Flag("RecordAnyTime"));
        Assert.Equal(3, body.Number("KeepUpTo"));
        Assert.Equal(120, body.Number("PrePaddingSeconds"));
        Assert.Equal(240, body.Number("PostPaddingSeconds"));
        Assert.Equal("A show", body.Text("Name"));
    }

    [Fact]
    public async Task The_embedded_editor_saves_as_soon_as_a_field_changes()
    {
        Server.OnPost($"/LiveTv/SeriesTimers/{SeriesId}");
        var timer = JsonNode.Parse(Series())!.AsObject();

        var cut = Render<SeriesRecordingEditor>(parameters => parameters
            .Add(editor => editor.Timer, timer)
            .Add(editor => editor.SeriesTimerId, SeriesId)
            .Add(editor => editor.Embedded, true));
        cut.Find(".selectShowType").Change("new");

        cut.Eventually(() => Assert.Contains(Server.Requests, request => request.Method == HttpMethod.Post)); // wait for the save
        Assert.Empty(cut.FindAll(".formDialogHeader"));
        Assert.True(cut.Find(".formDialogFooter").ClassList.Contains("hide"));
        var saved = Server.Requests.Last(request => request.Method == HttpMethod.Post);
        Assert.True(JsonNode.Parse(saved.Body!)!.AsObject().Flag("RecordNewOnly"));
    }

    [Fact]
    public async Task The_record_buttons_follow_the_programme()
    {
        Server.OnGet($"/LiveTv/Programs/{ProgramId}", Program(series: true, timer: TimerId, seriesTimer: SeriesId));

        var cut = Render<RecordingFields>(parameters => parameters.Add(fields => fields.ProgramId, ProgramId));
        cut.Eventually(() => Assert.Contains("Do not record", cut.Find(".singleRecordingButton").TextContent)); // the programme has loaded

        Assert.Contains("Cancel series", cut.Find(".seriesRecordingButton").TextContent);
        Assert.Contains("recordingIcon-active", cut.Find(".seriesRecordingButton").InnerHtml);
        Assert.False(cut.Find(".btnManageRecording").ClassList.Contains("hide"));
        Assert.False(cut.Find(".recordSeriesContainer").ClassList.Contains("hide"));
    }

    [Fact]
    public async Task The_bare_buttons_have_no_second_wrapper()
    {
        Server.OnGet($"/LiveTv/Programs/{ProgramId}", Program());

        var cut = Render<RecordingFields>(parameters => parameters
            .Add(fields => fields.ProgramId, ProgramId)
            .Add(fields => fields.Bare, true));
        cut.Eventually(() => Assert.NotNull(cut.Find(".singleRecordingButton"))); // the programme has loaded

        Assert.Empty(cut.FindAll(".recordingFields"));
        Assert.Contains("Record", cut.Find(".singleRecordingButton").TextContent);
        Assert.True(cut.Find(".recordSeriesContainer").ClassList.Contains("hide"));
    }

    private static string Program(string? start = null, string? end = null, string? channel = null, bool series = false, string? timer = null, string? seriesTimer = null) => $$"""
        {
            "Id": "{{ProgramId}}", "Name": "The News", "Type": "Program", "Overview": "What happened.",
            "StartDate": "{{start ?? "2024-05-17T18:00:00.000Z"}}", "EndDate": "{{end ?? "2024-05-17T19:00:00.000Z"}}",
            "ChannelId": "{{channel}}", "IsSeries": {{(series ? "true" : "false")}},
            "TimerId": {{(timer is null ? "null" : $"\"{timer}\"")}}, "Status": "New",
            "SeriesTimerId": {{(seriesTimer is null ? "null" : $"\"{seriesTimer}\"")}},
            "Genres": ["News"]
        }
        """;

    private static string Series() => $$"""
        {
            "Id": "{{SeriesId}}", "Name": "A show", "ChannelName": "Channel One", "StartDate": "2024-05-17T20:00:00.000Z",
            "PrePaddingSeconds": 0, "PostPaddingSeconds": 0, "RecordAnyChannel": false, "RecordAnyTime": false,
            "RecordNewOnly": false, "SkipEpisodesInLibrary": false, "KeepUpTo": 0
        }
        """;
}
