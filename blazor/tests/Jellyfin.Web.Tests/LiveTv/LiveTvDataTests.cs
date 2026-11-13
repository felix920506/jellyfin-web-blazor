using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>The Live TV requests: paths, parameters and how the answers are read.</summary>
public class LiveTvDataTests
{
    private readonly TestApp _app = new();

    public LiveTvDataTests() => _app.SignIn();

    private LiveTvData Data => _app.Get<LiveTvData>();

    private FakeHttp Server => _app.Server;

    [Fact]
    public async Task A_programme_row_asks_for_twelve_programmes_with_the_channel()
    {
        Server.OnGet("/LiveTv/Programs", """{"Items":[]}""");

        await Data.GetSectionItemsAsync(LiveTvSections.UpcomingMovies);

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Programs"));
        Assert.Equal(TestApp.UserId.ToString("N"), request.Query("userId"));
        Assert.Equal("12", request.Query("limit"));
        Assert.Equal("1", request.Query("imageTypeLimit"));
        Assert.Equal("Primary,Thumb,Backdrop", request.Query("enableImageTypes"));
        Assert.Equal("false", request.Query("enableTotalRecordCount"));
        Assert.Equal("ChannelInfo,PrimaryImageAspectRatio", request.Query("fields"));
        Assert.Equal("false", request.Query("hasAired"));
        Assert.Equal("true", request.Query("isMovie"));
    }

    [Fact]
    public async Task A_series_programme_keeps_the_series_flag_for_recording_fields()
    {
        Server.OnGet("/LiveTv/Programs/program1", """{"Id":"program1","Type":"Program","Name":"Series","IsSeries":true}""");

        var program = await Data.GetProgramAsync("program1");

        Assert.NotNull(program);
        Assert.True(program.IsSeries);
    }

    [Fact]
    public async Task The_on_now_row_uses_the_recommended_programmes()
    {
        Server.OnGet("/LiveTv/Programs/Recommended", """{"Items":[]}""");

        await Data.GetSectionItemsAsync(LiveTvSections.ActivePrograms);

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Programs/Recommended"));
        Assert.Equal("true", request.Query("isAiring"));
        Assert.Equal("ChannelInfo,PrimaryImageAspectRatio,MediaSourceCount", request.Query("fields"));
    }

    [Fact]
    public async Task The_upcoming_recordings_are_the_scheduled_ones_that_are_not_running()
    {
        Server.OnGet("/LiveTv/Timers", """{"Items":[{"Id":"timer1","Name":"Tonight","ProgramId":"program1","Status":"New"}]}""");

        var timers = await Data.GetUpcomingRecordingsAsync();

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Timers"));
        Assert.Equal("false", request.Query("isActive"));
        Assert.Equal("true", request.Query("isScheduled"));
        Assert.Equal("Timer", Assert.Single(timers).TypeName());
        Assert.Equal("Tonight", timers[0].Name);
    }

    [Fact]
    public async Task The_series_timers_are_sorted_by_name()
    {
        Server.OnGet("/LiveTv/SeriesTimers", """{"Items":[{"Id":"series1","Name":"A show","ChannelName":"One"}]}""");

        var timers = await Data.GetSeriesTimersAsync();

        var request = Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers"));
        Assert.Equal("SortName", request.Query("sortBy"));
        Assert.Equal("Ascending", request.Query("sortOrder"));
        Assert.Equal("SeriesTimer", Assert.Single(timers).TypeName());
        Assert.Equal("series1", timers[0].ActionId());
    }

    [Fact]
    public async Task A_series_schedule_ignores_an_answer_that_is_not_the_series()
    {
        Server.OnGet("/LiveTv/Timers", """{"Items":[{"Id":"other","Name":"Other","SeriesTimerId":"someone-else"}]}""");

        var timers = await Data.GetSeriesTimerScheduleAsync("series1");

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Timers"));
        Assert.Equal("series1", request.Query("SeriesTimerId"));
        Assert.Equal("StartDate", request.Query("SortBy"));
        Assert.Empty(timers);
    }

    [Fact]
    public async Task A_channel_s_guide_asks_for_what_has_not_aired()
    {
        Server.OnGet("/LiveTv/Programs", """{"Items":[]}""");

        await Data.GetChannelProgramsAsync("channel1");

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Programs"));
        Assert.Equal("channel1", request.Query("ChannelIds"));
        Assert.Equal("false", request.Query("HasAired"));
        Assert.Equal("StartDate", request.Query("SortBy"));
        Assert.Equal("false", request.Query("EnableImages"));
        Assert.Equal("0", request.Query("ImageTypeLimit"));
    }

    [Fact]
    public async Task The_guide_info_is_the_span_of_the_data()
    {
        Server.OnGet("/LiveTv/GuideInfo", """{"StartDate":"2024-05-17T00:00:00.000Z","EndDate":"2024-05-20T00:00:00.000Z"}""");

        var info = await Data.GetGuideInfoAsync();

        Assert.NotNull(info);
        Assert.Equal(DateTimeOffset.Parse("2024-05-17T00:00:00Z"), info.StartDate);
        Assert.Equal(DateTimeOffset.Parse("2024-05-20T00:00:00Z"), info.EndDate);
    }

    [Fact]
    public async Task The_guide_channels_carry_the_total_and_the_category_filters()
    {
        Server.OnGet("/LiveTv/Channels", """{"Items":[{"Id":"00000000-0000-0000-0000-000000000001","Name":"One","Type":"TvChannel"}],"TotalRecordCount":1200}""");

        var page = await Data.GetGuideChannelsAsync(new GuideChannelQuery(500, 500, true, IsMovie: true, SortBy: "DatePlayed", SortOrder: "Descending"));

        var request = Assert.Single(Server.RequestsTo("/LiveTv/Channels"));
        Assert.Equal("500", request.Query("StartIndex"));
        Assert.Equal("500", request.Query("Limit"));
        Assert.Equal("true", request.Query("EnableFavoriteSorting"));
        Assert.Equal("false", request.Query("AddCurrentProgram"));
        Assert.Equal("false", request.Query("EnableUserData"));
        Assert.Equal("Primary", request.Query("EnableImageTypes"));
        Assert.Equal("true", request.Query("IsMovie"));
        Assert.Equal("DatePlayed", request.Query("SortBy"));
        Assert.Equal("Descending", request.Query("SortOrder"));
        Assert.Equal(1200, page.TotalRecordCount);
        Assert.Equal("One", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Short_channel_lists_travel_in_the_query()
    {
        Server.OnGet("/LiveTv/Programs", """{"Items":[{"Id":"p1","ChannelId":"c1","Name":"Now","StartDate":"2024-05-17T14:00:00.000Z","EndDate":"2024-05-17T15:00:00.000Z"}]}""");

        var programs = await Data.GetGuideProgramsAsync(
            ["c1"],
            DateTimeOffset.Parse("2024-05-17T14:00:01Z"),
            DateTimeOffset.Parse("2024-05-18T13:59:59.999Z"),
            withHd: true);

        var request = Assert.Single(Server.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("c1", request.Query("channelIds"));
        Assert.Equal("2024-05-17T14:00:01.000Z", request.Query("MinEndDate"));
        Assert.Equal("2024-05-18T13:59:59.999Z", request.Query("MaxStartDate"));
        Assert.Equal("IsHD", request.Query("Fields"));
        Assert.Equal("Now", Assert.Single(programs).Name);
    }

    [Fact]
    public async Task A_long_channel_list_is_posted()
    {
        Server.OnPost("/LiveTv/Programs", _ => FakeHttp.Json("""{"Items":[]}"""));
        var ids = Enumerable.Range(0, 60).Select(number => new string('a', 40) + number).ToList();

        await Data.GetGuideProgramsAsync(ids, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1), withHd: false);

        var request = Assert.Single(Server.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("channelIds", request.Body);
        Assert.Null(request.Query("channelIds"));
    }

    [Fact]
    public async Task Saving_a_timer_posts_the_server_s_own_json()
    {
        Server.OnGet("/LiveTv/Timers/timer1", """{"Id":"timer1","PrePaddingSeconds":0,"PostPaddingSeconds":0,"Name":"Tonight"}""");
        Server.OnPost("/LiveTv/Timers/timer1");

        var timer = await Data.GetTimerAsync("timer1");
        Assert.NotNull(timer);
        timer["PrePaddingSeconds"] = 120;
        await Data.UpdateTimerAsync(timer);

        var saved = Assert.Single(Server.RequestsTo("/LiveTv/Timers/timer1"), request => request.Method == HttpMethod.Post);
        Assert.Contains("\"PrePaddingSeconds\":120", saved.Body);
        Assert.Contains("\"Name\":\"Tonight\"", saved.Body);
    }

    [Fact]
    public async Task Cancelling_a_series_timer_deletes_it()
    {
        Server.OnDelete("/LiveTv/SeriesTimers/series1");

        await Data.CancelSeriesTimerAsync("series1");

        Assert.Equal(HttpMethod.Delete, Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers/series1")).Method);
    }
}
