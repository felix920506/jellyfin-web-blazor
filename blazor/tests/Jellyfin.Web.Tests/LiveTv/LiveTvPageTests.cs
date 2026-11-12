using Jellyfin.Web.Components;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Session;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>The Live TV views: the section rows, the guide and the series timers.</summary>
public class LiveTvPageTests : ItemsTestContext
{
    private FakeHttp Server => App.Server;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    [Fact]
    public void The_page_opens_on_the_programs_view()
    {
        Server.OnGet(string.Empty, _ => FakeHttp.Json("""{"Items":[]}"""));
        var cut = RenderAt("/livetv");

        cut.WaitForElement("#liveTvPage");
        Assert.Contains("Programs", cut.Find(".jf-library-view-label").TextContent);
    }

    [Fact]
    public void An_empty_programs_view_says_there_is_nothing()
    {
        Server.OnGet(string.Empty, _ => FakeHttp.Json("""{"Items":[]}"""));
        var cut = Render<LiveTvSectionsView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Programs)));

        cut.WaitForElement(".noItemsMessage");
        Assert.Empty(Server.Requests.Where(request => request.Path.Contains("/LiveTv/", StringComparison.Ordinal)).Select(request => request.Path).Except(
            Server.Requests.Select(request => request.Path)));
    }

    [Fact]
    public void The_programs_view_keeps_only_the_rows_that_have_items()
    {
        Server.OnGet("/LiveTv/Programs/Recommended", _ => Items(("On now", "Program")));
        Server.OnGet("/LiveTv/Programs", request =>
            request.Query("isMovie") == "true" ? Items(("A film", "Program")) : """{"Items":[]}""");

        var cut = Render<LiveTvSectionsView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Programs)));

        cut.Eventually(() => Assert.False(Get<LoadingService>().IsVisible));
        Assert.NotNull(cut.Find("[data-section=upcomingmovies]"));
        Assert.Empty(cut.FindAll("[data-section=upcomingepisodes]"));
        Assert.Contains("On now", cut.Find("[data-section=activeprograms]").TextContent);
        Assert.Contains("#/list?", cut.Find("[data-section=activeprograms] a").GetAttribute("href"));
    }

    [Fact]
    public void The_recordings_view_asks_for_movies_shows_sports_kids_and_folders()
    {
        Server.OnGet("/LiveTv/Recordings", request =>
            request.Query("isMovie") == "true" ? Items(("Recorded film", "Recording")) : """{"Items":[]}""");
        Server.OnGet("/LiveTv/Recordings/Folders", """{"Items":[{"Id":"00000000-0000-0000-0000-000000000009","Name":"All","Type":"Folder"}]}""");

        var cut = Render<LiveTvSectionsView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Recordings)));

        cut.Eventually(() => Assert.False(Get<LoadingService>().IsVisible));
        cut.WaitForElement("[data-section=recordingmovies]");
        Assert.NotNull(cut.Find("[data-section=recordingfolders]"));
        var queries = Server.RequestsTo("/LiveTv/Recordings").Select(request => request.Uri.Query).ToList();
        Assert.Contains(queries, query => query.Contains("isMovie=true", StringComparison.Ordinal));
        Assert.Contains(queries, query => query.Contains("isSeries=true", StringComparison.Ordinal));
        Assert.Contains(queries, query => query.Contains("isSports=true", StringComparison.Ordinal));
        Assert.Contains(queries, query => query.Contains("isKids=true", StringComparison.Ordinal));
    }

    [Fact]
    public void The_schedule_groups_the_upcoming_recordings_by_day()
    {
        Server.OnGet("/LiveTv/Recordings", """{"Items":[]}""");
        Server.OnGet("/LiveTv/Timers", """
            {"Items":[
              {"Id":"t1","Name":"Tonight","ProgramId":"p1","StartDate":"2024-05-17T20:00:00.000Z"},
              {"Id":"t2","Name":"Tomorrow","ProgramId":"p2","StartDate":"2024-05-18T20:00:00.000Z"}
            ]}
            """);

        var cut = Render<LiveTvSectionsView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Schedule)));

        cut.Eventually(() => Assert.False(Get<LoadingService>().IsVisible));
        cut.WaitForElement("[data-section=upcomingrecordings]");
        Assert.Equal(2, cut.FindAll("[data-section=upcomingrecordings]").Count);
        var request = Assert.Single(Server.RequestsTo("/LiveTv/Timers"));
        Assert.Equal("false", request.Query("isActive"));
        Assert.Equal("true", request.Query("isScheduled"));
    }

    [Fact]
    public void The_series_view_lists_the_series_timers_by_name()
    {
        Server.OnGet("/LiveTv/SeriesTimers", """{"Items":[{"Id":"s1","Name":"A show","ChannelName":"One"}]}""");

        var cut = Render<LiveTvSeriesTimersView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.SeriesTimers)));

        cut.WaitForElement(".itemsContainer");
        Assert.Contains("A show", cut.Find(".itemsContainer").TextContent);
        var request = Assert.Single(Server.RequestsTo("/LiveTv/SeriesTimers"));
        Assert.Equal("SortName", request.Query("sortBy"));
        Assert.Equal("Ascending", request.Query("sortOrder"));
    }

    [Fact]
    public void The_guide_draws_the_day_the_channels_and_the_programmes()
    {
        Get<DateTimeText>().Clock = static () => new DateTimeOffset(2024, 5, 17, 14, 40, 0, TimeSpan.Zero);
        Server.OnGet("/LiveTv/GuideInfo", """{"StartDate":"2024-05-17T00:00:00.000Z","EndDate":"2024-05-19T00:00:00.000Z"}""");
        Server.OnGet("/LiveTv/Channels", """
            {"Items":[{"Id":"00000000-0000-0000-0000-000000000001","Name":"Channel One","Number":"1","Type":"TvChannel"}],"TotalRecordCount":1}
            """);
        Server.OnGet("/LiveTv/Programs", """
            {"Items":[{"Id":"00000000-0000-0000-0000-000000000002","ChannelId":"00000000-0000-0000-0000-000000000001","Name":"The News","StartDate":"2024-05-17T14:00:00.000Z","EndDate":"2024-05-17T15:00:00.000Z","Type":"Program"}]}
            """);

        var cut = Render<LiveTvGuideView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Guide)));

        cut.WaitForElement(".programCell");
        Assert.Contains("emby-tab-button-active", cut.Find(".guide-date-tab-button").ClassName ?? string.Empty);
        Assert.Equal(48, cut.FindAll(".timeslotHeader").Count);
        Assert.Contains("Channel One", cut.Find(".guide-channelHeaderCell").GetAttribute("title"));
        Assert.Contains("programCell-active", cut.Find(".programCell").ClassName ?? string.Empty);
        Assert.Contains("The News", cut.Find(".programCell").TextContent);
        Assert.True(cut.Find(".guideOptions").ClassList.Contains("hide"));
        var programs = Assert.Single(Server.RequestsTo("/LiveTv/Programs"));
        Assert.Equal("500", Assert.Single(Server.RequestsTo("/LiveTv/Channels")).Query("Limit"));
        Assert.Contains("00000000000000000000000000000001", programs.Query("channelIds"));
    }

    [Fact]
    public void Changing_the_day_asks_for_that_day_s_programmes()
    {
        Get<DateTimeText>().Clock = static () => new DateTimeOffset(2024, 5, 17, 14, 40, 0, TimeSpan.Zero);
        Server.OnGet("/LiveTv/GuideInfo", """{"StartDate":"2024-05-17T00:00:00.000Z","EndDate":"2024-05-19T00:00:00.000Z"}""");
        Server.OnGet("/LiveTv/Channels", """{"Items":[{"Id":"00000000-0000-0000-0000-000000000001","Name":"One","Type":"TvChannel"}],"TotalRecordCount":1}""");
        Server.OnGet("/LiveTv/Programs", """{"Items":[]}""");

        var cut = Render<LiveTvGuideView>(parameters => parameters.Add(view => view.Content, Tab(LibraryTab.Guide)));
        cut.WaitForElement(".guide-date-tab-button");
        var tabs = cut.FindAll(".guide-date-tab-button");
        Assert.True(tabs.Count >= 2);

        tabs[1].Click();
        cut.WaitForAssertion(() => Assert.Contains("emby-tab-button-active", cut.FindAll(".guide-date-tab-button")[1].ClassName ?? string.Empty));
        var last = Server.RequestsTo("/LiveTv/Programs").Last();
        Assert.StartsWith("2024-05-18", last.Query("MinEndDate"));
    }

    [Fact]
    public async Task A_programme_context_command_is_owned_by_live_tv_only_for_programmes()
    {
        var handler = Get<LiveTvCommandHandler>();
        var movie = TestItems.Item(TestItems.Movie(1, "Movie"));
        var program = TestItems.Item(new { Id = TestItems.Id(2), Type = "Program" });

        Assert.False(handler.CanHandle(ItemCommand.Record, movie));
        Assert.True(handler.CanHandle(ItemCommand.Record, program));
        Assert.False(handler.CanHandle(ItemCommand.ProgramDialog, movie));
        Assert.True(handler.CanHandle(ItemCommand.ProgramDialog, program));
    }

    /// <summary>Renders the page as the router does, with the location it is shown at.</summary>
    private IRenderedComponent<Pages.LiveTvPage> RenderAt(string url)
    {
        var host = Render<CascadingValue<AppLocation>>(cascade => cascade
            .Add(value => value.Value, AppLocation.Parse(url))
            .AddChildContent<Pages.LiveTvPage>());
        return host.FindComponent<Pages.LiveTvPage>();
    }

    private static LibraryTabContent Tab(string viewType) =>
        LiveTvRoutes.LiveTv.Views.First(view => view.Content.ViewType == viewType).Content;

    private static string Items(params (string Name, string Type)[] items) =>
        "{\"Items\":[" + string.Join(',', items.Select((item, index) =>
            $"{{\"Id\":\"{TestItems.Id(index + 1)}\",\"Name\":\"{item.Name}\",\"Type\":\"{item.Type}\"}}")) + "]}";
}
