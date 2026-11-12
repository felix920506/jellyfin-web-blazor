using Jellyfin.Web.Components;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>The six Live TV tabs and the requests their rows make.</summary>
public class LiveTvRoutesTests
{
    private static readonly (int Index, string Label, string View, Type? Component)[] Tabs =
    [
        (0, "Programs", LibraryTab.Programs, typeof(LiveTvSectionsView)),
        (1, "Guide", LibraryTab.Guide, typeof(LiveTvGuideView)),
        (2, "Channels", LibraryTab.Channels, null),
        (3, "Recordings", LibraryTab.Recordings, typeof(LiveTvSectionsView)),
        (4, "Schedule", LibraryTab.Schedule, typeof(LiveTvSectionsView)),
        (5, "Series", LibraryTab.SeriesTimers, typeof(LiveTvSeriesTimersView))
    ];

    [Fact]
    public void The_page_has_the_six_tabs_of_the_original()
    {
        var route = LiveTvRoutes.LiveTv;

        Assert.Equal("/livetv", route.Path);
        Assert.Equal("liveTvPage", route.PageId);
        Assert.Equal("livetv", route.CollectionType);
        Assert.Equal(Tabs.Select(tab => tab.Index), route.Views.Select(view => view.Index));
        Assert.Equal(Tabs.Select(tab => tab.Label), route.Views.Select(view => view.Label));
        Assert.Equal(Tabs.Select(tab => tab.View), route.Views.Select(view => view.Content.ViewType));
        Assert.Equal(Tabs.Select(tab => tab.Component), route.Views.Select(view => view.Content.Component));
        Assert.True(route.Views[0].IsDefault);
    }

    [Fact]
    public void The_channels_tab_has_no_sort_filter_or_alphabet_and_pages_the_channels()
    {
        var content = LiveTvRoutes.LiveTv.ViewAt(2).Content;

        Assert.False(content.IsBtnGridListEnabled);
        Assert.False(content.IsBtnSortEnabled);
        Assert.False(content.IsAlphabetPickerEnabled);
        Assert.NotNull(content.Request);

        var request = content.Request!(new LibraryQueryContext(content, "library", new LibraryViewSettings { StartIndex = 100 }, 50, "user1"));
        var query = Query(request);

        Assert.Equal("LiveTv/Channels", request.Path);
        Assert.Equal("user1", query["userId"]);
        Assert.Equal("100", query["startIndex"]);
        Assert.Equal("50", query["limit"]);
        Assert.Equal("PrimaryImageAspectRatio", query["fields"]);
        Assert.Equal("Primary", query["enableImageTypes"]);
        Assert.False(query.ContainsKey("isFavorite"));
    }

    [Fact]
    public void The_favorite_filter_is_sent_only_when_it_is_on()
    {
        var content = LiveTvRoutes.LiveTv.ViewAt(2).Content;
        var settings = new LibraryViewSettings { Filters = new LibraryFilters { Status = ["IsFavorite"] } };

        var query = Query(content.Request!(new LibraryQueryContext(content, null, settings, 0, "user1")));

        Assert.Equal("true", query["isFavorite"]);
        Assert.False(query.ContainsKey("limit"));
    }

    [Fact]
    public void The_series_tab_turns_off_paging_filtering_and_sorting()
    {
        var content = LiveTvRoutes.LiveTv.ViewAt(5).Content;

        Assert.False(content.IsPaginationEnabled);
        Assert.False(content.IsBtnFilterEnabled);
        Assert.False(content.IsBtnGridListEnabled);
        Assert.False(content.IsBtnSortEnabled);
        Assert.False(content.IsAlphabetPickerEnabled);
    }

    [Fact]
    public void The_programs_view_has_the_six_programme_rows()
    {
        var sections = LiveTvSections.For(LibraryTab.Programs);

        Assert.Equal(
            ["activeprograms", "upcomingepisodes", "upcomingmovies", "upcomingsports", "upcomingkids", "upcomingnews"],
            sections.Select(section => section.Type));
        Assert.Equal("HeaderOnNow", sections[0].Name);
        Assert.Equal("LiveTv/Programs/Recommended", sections[0].Path);
        Assert.Contains(("isAiring", (object?)true), sections[0].Query);
        Assert.Equal(new ListRouteOptions { ItemTypes = "Programs", IsAiring = true }, sections[0].List);
    }

    [Fact]
    public void The_recordings_view_adds_movies_shows_sports_and_kids()
    {
        var sections = new[]
        {
            LiveTvSections.LatestRecordings,
            LiveTvSections.RecordingMovies,
            LiveTvSections.RecordingShows,
            LiveTvSections.RecordingSports,
            LiveTvSections.RecordingKids,
            LiveTvSections.AllRecordings
        };

        Assert.Equal(
            ["latestrecordings", "recordingmovies", "recordingshows", "recordingsports", "recordingkids", "recordingfolders"],
            sections.Select(section => section.Type));
        Assert.Equal("LiveTv/Recordings/Folders", sections[^1].Path);
        Assert.Contains(("isMovie", (object?)true), sections[1].Query);
        Assert.Contains(("isSeries", (object?)true), sections[2].Query);
        Assert.Contains(("isSports", (object?)true), sections[3].Query);
        Assert.Contains(("isKids", (object?)true), sections[4].Query);
    }

    [Fact]
    public void The_schedule_view_lists_the_recordings_in_progress()
    {
        var sections = LiveTvSections.For(LibraryTab.Schedule);

        Assert.Equal(["activerecordings"], sections.Select(section => section.Type));
        Assert.Contains(("isInProgress", (object?)true), sections[0].Query);
        Assert.True(LiveTvSections.HasUpcomingRecordings(LibraryTab.Schedule));
        Assert.False(LiveTvSections.HasUpcomingRecordings(LibraryTab.Programs));
    }

    [Fact]
    public void Timers_group_by_the_local_day_they_start_and_a_timer_without_one_stands_alone()
    {
        var dates = new DateTimeText(new Globalize(new AppAssets(new HttpClient()), new FakeBrowser()), new FakeBrowser());
        var first = TestItems.Item(new { Id = TestItems.Id(1), Name = "Morning", StartDate = "2024-05-17T08:00:00.0000000+00:00" });
        var second = TestItems.Item(new { Id = TestItems.Id(2), Name = "Evening", StartDate = "2024-05-17T20:00:00.0000000+00:00" });
        var next = TestItems.Item(new { Id = TestItems.Id(3), Name = "Tomorrow", StartDate = "2024-05-18T08:00:00.0000000+00:00" });
        var none = TestItems.Item(new { Id = TestItems.Id(4), Name = "Undated" });

        var groups = LiveTvGrouping.GroupTimers([first, second, next, none], dates);

        Assert.Equal(3, groups.Count);
        Assert.Equal(["Morning", "Evening"], groups[0].Items.Select(item => item.Name));
        Assert.Equal(["Tomorrow"], groups[1].Items.Select(item => item.Name));
        Assert.Equal(string.Empty, groups[2].Name);
        Assert.Equal("Undated", groups[2].Items[0].Name);
    }

    private static Dictionary<string, string?> Query(LibraryRequest request) =>
        request.Query.Where(entry => entry.Value is not null).ToDictionary(
            entry => entry.Key,
            entry => entry.Value switch
            {
                bool flag => flag ? "true" : "false",
                _ => Convert.ToString(entry.Value, System.Globalization.CultureInfo.InvariantCulture)
            });
}
