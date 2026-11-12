using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>Scheduling and cancelling recordings (components/recordingcreator/recordinghelper.js).</summary>
public class RecordingHelperTests : ItemsTestContext
{
    private RecordingHelper Recordings => Get<RecordingHelper>();

    private Support.FakeHttp Server => App.Server;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    [Fact]
    public async Task Scheduling_a_programme_posts_the_server_s_defaults()
    {
        Server.OnGet("/LiveTv/Timers/Defaults", """{"ProgramId":"program1","PrePaddingSeconds":0}""");
        Server.OnPost("/LiveTv/Timers");

        Assert.True(await Recordings.CreateRecordingAsync("program1"));

        var created = Assert.Single(Server.RequestsTo("/LiveTv/Timers"), request => request.Method == HttpMethod.Post);
        Assert.Contains("\"ProgramId\":\"program1\"", created.Body);
        Assert.Equal(["Recording scheduled."], Toasts);
        Assert.False(Get<LoadingService>().IsVisible);
    }

    [Fact]
    public async Task Scheduling_a_series_posts_a_series_timer()
    {
        Server.OnGet("/LiveTv/Timers/Defaults", """{"ProgramId":"program1"}""");
        Server.OnPost("/LiveTv/SeriesTimers");

        Assert.True(await Recordings.CreateRecordingAsync("program1", isSeries: true));

        Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers"));
        Assert.DoesNotContain(Server.RequestsTo("/LiveTv/Timers"), request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task Cancelling_a_timer_deletes_it_and_says_so()
    {
        Server.OnDelete("/LiveTv/Timers/timer1");

        Assert.True(await Recordings.CancelTimerAsync("timer1"));

        Assert.Equal(HttpMethod.Delete, Assert.Single(Server.RequestsTo("/LiveTv/Timers/timer1")).Method);
        Assert.Equal(["Recording cancelled."], Toasts);
    }

    [Fact]
    public async Task A_failed_request_is_reported_as_unchanged()
    {
        Assert.False(await Recordings.CancelTimerAsync("missing"));

        Assert.Empty(Toasts);
        Assert.False(Get<LoadingService>().IsVisible);
    }

    [Fact]
    public async Task Turning_a_recording_into_a_series_creates_the_series_timer()
    {
        Server.OnGet("/Items/program1", """{"Id":"program1","Name":"Episode","Type":"Program","IsSeries":true}""");
        Server.OnGet("/LiveTv/Timers/Defaults", """{"ProgramId":"program1"}""");
        Server.OnPost("/LiveTv/SeriesTimers");

        Assert.True(await Recordings.ChangeRecordingToSeriesAsync("timer1", "program1"));

        Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers"));
        Assert.Equal(["Series recording scheduled."], Toasts);
    }

    [Fact]
    public async Task A_programme_that_is_no_series_has_its_recording_cancelled_instead()
    {
        Server.OnGet("/Items/program1", """{"Id":"program1","Name":"Film","Type":"Program","IsSeries":false}""");
        Server.OnDelete("/LiveTv/Timers/timer1");

        Assert.True(await Recordings.ChangeRecordingToSeriesAsync("timer1", "program1"));

        Assert.Single(Server.RequestsTo("/LiveTv/Timers/timer1"));
        Assert.Equal(["Recording cancelled."], Toasts);
    }

    [Fact]
    public async Task The_record_button_schedules_a_programme_that_has_no_timer()
    {
        Server.OnGet("/LiveTv/Timers/Defaults", """{"ProgramId":"program1"}""");
        Server.OnPost("/LiveTv/Timers");

        Assert.True(await Recordings.ToggleRecordingAsync("program1", timerId: null, timerStatus: null, seriesTimerId: null));

        Assert.Single(Server.RequestsTo("/LiveTv/Timers"), request => request.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task A_programme_recorded_as_part_of_a_series_asks_what_to_cancel()
    {
        Server.OnDelete("/LiveTv/SeriesTimers/series1");
        var closing = Recordings.ToggleRecordingAsync("program1", "timer1", "New", "series1");
        var dialog = await NextDialogAsync();

        dialog.Close("cancelseriestimer");
        Assert.True(await closing);

        Assert.Equal(HttpMethod.Delete, Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers/series1")).Method);
        Assert.Equal(["Series cancelled."], Toasts);
    }

    [Fact]
    public async Task Keeping_the_recording_changes_nothing()
    {
        var closing = Recordings.ToggleRecordingAsync("program1", "timer1", "InProgress", "series1");
        var dialog = await NextDialogAsync();

        dialog.Close("cancel");
        Assert.False(await closing);

        Assert.Empty(Server.Requests);
        Assert.Empty(Toasts);
    }

    [Fact]
    public async Task Confirming_a_cancellation_is_required()
    {
        Server.OnDelete("/LiveTv/Timers/timer1");
        var closing = Recordings.CancelTimerWithConfirmationAsync("timer1");
        var dialog = await NextDialogAsync();

        dialog.Close("cancel");
        Assert.False(await closing);
        Assert.Empty(Server.Requests);

        closing = Recordings.CancelTimerWithConfirmationAsync("timer1");
        dialog = await NextDialogAsync();
        dialog.Close("ok");
        Assert.True(await closing);
        Assert.Single(Server.RequestsTo("/LiveTv/Timers/timer1"));
    }
}
