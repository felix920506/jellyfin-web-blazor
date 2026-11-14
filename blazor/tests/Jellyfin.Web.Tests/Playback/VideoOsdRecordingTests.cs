using System.Net;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Pages.Playback;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>The live programme and record toggle, through the real OSD and recording API services.</summary>
public class VideoOsdRecordingTests : PlaybackTestContext
{
    private const string ChannelId = "cccccccccccccccccccccccccccccccc";
    private const string ProgramId = "dddddddddddddddddddddddddddddddd";
    private const string TimerId = "timer-osd";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private BaseItemDto _program = default!;
    private BaseItemDto _channel = default!;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Services.GetRequiredService<JellyfinApi>().User!.Policy!.EnableLiveTvManagement = true;
        _program = new BaseItemDto
        {
            Id = Guid.Parse(ProgramId), Type = BaseItemDto_Type.Program, Name = "Current programme",
            ChannelId = Guid.Parse(ChannelId), MediaType = BaseItemDto_MediaType.Video,
            StartDate = DateTimeOffset.FromUnixTimeMilliseconds(Fakes.Timers.NowMs).AddMinutes(-10),
            EndDate = DateTimeOffset.FromUnixTimeMilliseconds(Fakes.Timers.NowMs).AddMinutes(50),
        };
        _channel = new BaseItemDto
        {
            Id = Guid.Parse(ChannelId), Type = BaseItemDto_Type.TvChannel, Name = "Channel One",
            MediaType = BaseItemDto_MediaType.Video, CurrentProgram = _program,
        };
        PlaybackServer.ScriptItems(Server, _channel, _program);
        PlaybackServer.ScriptPlaybackInfo(Server, ChannelId);
        PlaybackServer.ScriptPlaybackInfo(Server, ProgramId);
        Server.OnGet($"/LiveTv/Channels/{ChannelId}", _ => PlaybackServer.ToJson(_channel));
        Server.OnGet($"/LiveTv/Programs/{ProgramId}", _ => PlaybackServer.ToJson(_program));
        Server.OnGet("/LiveTv/Timers/Defaults", new JsonObject { ["ProgramId"] = ProgramId, ["PrePaddingSeconds"] = 60 });
        Server.OnPost("/LiveTv/Timers", _ =>
        {
            _program.TimerId = TimerId;
            _program.Status = "New";
            return FakeHttp.Status(HttpStatusCode.NoContent);
        });
        Server.On(HttpMethod.Delete, $"/LiveTv/Timers/{TimerId}", _ =>
        {
            _program.TimerId = null;
            _program.Status = "Cancelled";
            return FakeHttp.Status(HttpStatusCode.NoContent);
        });
    }

    private async Task<IRenderedComponent<VideoOsd>> PlayAsync(BaseItemDto? item = null)
    {
        await Manager.PlayAsync(new PlayOptions { Items = [item ?? _channel] });
        var cut = Render<VideoOsd>();
        cut.WaitForAssertion(() => Assert.NotNull(Fakes.Ui.Osd), Timeout);
        return cut;
    }

    private void AssertButton(IRenderedComponent<VideoOsd> cut, string title, bool active)
    {
        var button = cut.Find(".btnRecord");
        Assert.Equal(Services.GetRequiredService<Globalize>()[title], button.GetAttribute("title"));
        Assert.Equal(button.GetAttribute("title"), button.GetAttribute("aria-label"));
        Assert.Equal(active, button.ClassList.Contains("recordingIcon-active"));
        Assert.Equal(active, button.QuerySelector(".material-icons")!.ClassList.Contains("recordingIcon-active"));
        Assert.False(button.HasAttribute("disabled"));
    }

    [Fact]
    public async Task Non_live_video_has_no_record_button()
    {
        await PlayMovieAsync();
        var cut = Render<VideoOsd>();
        Assert.Empty(cut.FindAll(".btnRecord"));
        Assert.Empty(Server.RequestsTo($"/LiveTv/Programs/{ProgramId}"));
    }

    [Fact]
    public async Task Live_TV_management_permission_is_required()
    {
        Services.GetRequiredService<JellyfinApi>().User!.Policy!.EnableLiveTvManagement = false;
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => Assert.Equal("Current programme", cut.Find(".pageTitle").TextContent), Timeout);
        Assert.Empty(cut.FindAll(".btnRecord"));
    }

    [Fact]
    public async Task A_channel_without_a_current_programme_has_no_record_button()
    {
        _channel.CurrentProgram = null;
        var cut = await PlayAsync();
        Assert.Empty(cut.FindAll(".btnRecord"));
        Assert.Equal("Channel One", cut.Find(".pageTitle").TextContent);
    }

    [Fact]
    public async Task The_current_programme_is_shown_and_record_is_first_in_the_transport_group()
    {
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        Assert.Equal("Current programme", cut.Find(".pageTitle").TextContent);
        Assert.Equal("btnRecord", cut.Find(".buttons > div[dir=ltr] > button:first-child").ClassList[0]);
        Assert.Contains("fiber_manual_record", cut.Find(".btnRecord .material-icons").ClassList);
        Assert.Contains("xlargePaperIconButton", cut.Find(".btnRecord .material-icons").ClassList);
    }

    [Theory]
    [InlineData("New", "DoNotRecord")]
    [InlineData("InProgress", "StopRecording")]
    public async Task Creating_then_cancelling_a_timer_refreshes_the_button(string status, string title)
    {
        Server.OnPost("/LiveTv/Timers", _ =>
        {
            _program.TimerId = TimerId;
            _program.Status = status;
            return FakeHttp.Status(HttpStatusCode.NoContent);
        });
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);

        cut.Find(".btnRecord").Click();
        cut.WaitForAssertion(() => AssertButton(cut, title, true), Timeout);
        var created = Assert.Single(Server.RequestsTo("/LiveTv/Timers"), r => r.Method == HttpMethod.Post);
        var body = JsonNode.Parse(created.Body!)!;
        Assert.Equal(ProgramId, body["ProgramId"]!.GetValue<string>());
        Assert.Equal(60, body["PrePaddingSeconds"]!.GetValue<int>());

        cut.Find(".btnRecord").Click();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        Assert.Single(Server.RequestsTo($"/LiveTv/Timers/{TimerId}"), r => r.Method == HttpMethod.Delete);
        Assert.Equal(3, Server.RequestsTo($"/LiveTv/Programs/{ProgramId}").Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_requests_preserve_the_state_and_leave_the_button_enabled(bool cancelling)
    {
        if (cancelling)
        {
            _program.TimerId = TimerId;
            _program.Status = "InProgress";
            Server.On(HttpMethod.Delete, $"/LiveTv/Timers/{TimerId}", _ => FakeHttp.Status(HttpStatusCode.InternalServerError));
        }
        else
        {
            Server.OnPost("/LiveTv/Timers", _ => FakeHttp.Status(HttpStatusCode.InternalServerError));
        }
        var cut = await PlayAsync();
        var title = cancelling ? "StopRecording" : "Record";
        cut.WaitForAssertion(() => AssertButton(cut, title, cancelling), Timeout);
        cut.Find(".btnRecord").Click();
        cut.WaitForAssertion(() => AssertButton(cut, title, cancelling), Timeout);
        Assert.Single(Server.RequestsTo($"/LiveTv/Programs/{ProgramId}"));
        Assert.Single(Server.RequestsTo(cancelling ? $"/LiveTv/Timers/{TimerId}" : "/LiveTv/Timers"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Cancelled")]
    public async Task Missing_or_cancelled_timer_state_is_not_active(string? status)
    {
        _program.TimerId = TimerId;
        _program.Status = status;
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
    }

    [Fact]
    public async Task An_active_series_timer_uses_the_original_smart_record_indicator()
    {
        _program.TimerId = TimerId;
        _program.SeriesTimerId = "series-osd";
        _program.Status = "New";
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "DoNotRecord", true), Timeout);
        Assert.Contains("fiber_smart_record", cut.Find(".btnRecord .material-icons").ClassList);
    }

    [Fact]
    public async Task Playing_a_programme_resolves_its_channel_and_shows_the_current_programme()
    {
        _channel.CurrentProgram = new BaseItemDto { Id = Guid.NewGuid(), Type = BaseItemDto_Type.Program, Name = "Different programme" };
        Server.OnGet("/Items", new JsonObject { ["Items"] = new JsonArray(PlaybackServer.ToJson(_channel)) });
        Server.OnGet($"/LiveTv/Programs/{_channel.CurrentProgram.Id:N}", _ => PlaybackServer.ToJson(_channel.CurrentProgram));
        var cut = await PlayAsync(_program);
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        Assert.Equal("Different programme", cut.Find(".pageTitle").TextContent);
        Assert.Equal(BaseItemDto_Type.TvChannel, Manager.CurrentItem!.Type);
    }

    [Fact]
    public async Task Guide_refresh_removes_a_finished_programme_instead_of_using_the_stale_playback_item()
    {
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        _channel.CurrentProgram = null;
        Fakes.Timers.NowMs += 30_000;
        Player.Tick("video", 30);
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".btnRecord")), Timeout);
        Assert.Equal("Channel One", cut.Find(".pageTitle").TextContent);
    }

    [Fact]
    public async Task Changing_from_a_channel_to_a_movie_removes_the_record_button_and_programme_title()
    {
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        await PlayMovieAsync();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".btnRecord")), Timeout);
        Assert.Equal("Test Movie (2015)", cut.Find(".pageTitle").TextContent);
    }

    [Fact]
    public async Task A_failed_programme_refresh_keeps_the_channels_programme_recordable()
    {
        Server.On(HttpMethod.Get, $"/LiveTv/Programs/{ProgramId}", _ => FakeHttp.Status(HttpStatusCode.ServiceUnavailable));
        var cut = await PlayAsync();
        cut.WaitForAssertion(() => AssertButton(cut, "Record", false), Timeout);
        Assert.Equal("Current programme", cut.Find(".pageTitle").TextContent);
        cut.Find(".btnRecord").Click();
        cut.WaitForAssertion(() => Assert.Single(Server.RequestsTo("/LiveTv/Timers")), Timeout);
        Assert.False(cut.Find(".btnRecord").HasAttribute("disabled"));
    }

    [Fact]
    public async Task Live_stream_track_refresh_uses_the_supported_item_playback_info_endpoint()
    {
        var source = PlaybackServer.MediaSource(ChannelId);
        source["LiveStreamId"] = "live-osd";
        PlaybackServer.ScriptPlaybackInfo(Server, ChannelId, source);
        var refreshed = source.DeepClone().AsObject();
        refreshed["MediaStreams"] = new JsonArray(new JsonObject { ["Index"] = 2, ["Type"] = "Audio", ["Codec"] = "opus" });
        Server.OnGet($"/Items/{ChannelId}/PlaybackInfo", PlaybackServer.PlaybackInfo(refreshed));
        var cut = await PlayAsync();
        Player.Tick("video", 1);
        cut.WaitForAssertion(() => Assert.Single(Server.RequestsTo($"/Items/{ChannelId}/PlaybackInfo"), r => r.Method == HttpMethod.Get), Timeout);
        Assert.Equal("opus", Assert.Single(Manager.CurrentMediaSource!.MediaStreams!).Codec);
        Assert.DoesNotContain(Server.Requests, r => r.Path.Contains("/MediaInfo", StringComparison.Ordinal));
    }
}
