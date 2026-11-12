using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.LiveTv;

/// <summary>
/// The TV guide's dates, programme markup and channel request (components/guide/guide.js):
/// the day tabs, the half-hour header, the cells and the category filters.
/// </summary>
public class GuideModelTests
{
    private static readonly DateTimeOffset Now = new(2024, 5, 17, 14, 40, 12, TimeSpan.Zero);

    private static GuideClock Clock(int offsetMinutes = 0) => new(_ => offsetMinutes);

    private static GuideInfo Info(string start, string end) =>
        new(DateTimeOffset.Parse(start), DateTimeOffset.Parse(end));

    private static GuideProgram Program(
        string id,
        DateTimeOffset start,
        DateTimeOffset end,
        string channel = "channel1",
        bool kids = false,
        bool sports = false,
        bool news = false,
        bool movie = false,
        bool series = false,
        bool live = false,
        bool premiere = false,
        bool repeat = false,
        bool hd = false,
        string? episode = null,
        string? timer = null,
        string? seriesTimer = null,
        string? status = null) => new()
        {
            Id = id,
            ServerId = "server1",
            ChannelId = channel,
            Name = "Show " + id,
            EpisodeTitle = episode,
            StartText = start.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            EndText = end.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
            Start = start,
            End = end,
            IsKids = kids,
            IsSports = sports,
            IsNews = news,
            IsMovie = movie,
            IsSeries = series,
            IsLive = live,
            IsPremiere = premiere,
            IsRepeat = repeat,
            IsHD = hd,
            TimerId = timer,
            SeriesTimerId = seriesTimer,
            Status = status
        };

    private static GuideOptions Options(params string[] categories) => new()
    {
        Categories = categories,
        ShowEpisodeTitle = true,
        ShowLiveIndicator = true,
        ShowPremiereIndicator = true,
        ShowNewIndicator = true,
        ShowRepeatIndicator = true,
        ShowHdIcon = true,
        ColorCodedBackgrounds = true
    };

    private static GuideLabels Labels => new("Live", "Premiere", "New", "Repeat");

    [Fact]
    public void The_date_tabs_start_at_the_current_hour_and_cover_the_guide_data()
    {
        var range = Clock().DateRange(Info("2024-05-15T00:00:00Z", "2024-05-19T06:00:00Z"), Now, currentDate: null);

        Assert.Equal(3, range.Tabs.Count);
        Assert.Equal(new DateTimeOffset(2024, 5, 17, 14, 0, 0, TimeSpan.Zero), range.Tabs[0].Date);
        Assert.Equal(17, range.Tabs[0].Day);
        Assert.True(range.Tabs[0].Active);
        Assert.Equal(new DateTimeOffset(2024, 5, 18, 0, 0, 0, TimeSpan.Zero), range.Tabs[1].Date);
        Assert.False(range.Tabs[1].Active);
        Assert.Equal(new DateTimeOffset(2024, 5, 19, 0, 0, 0, TimeSpan.Zero), range.Tabs[2].Date);

        // The grid starts at the current hour, rounded down to the half hour; the scroll target is the next half hour.
        Assert.Equal(range.Tabs[0].Date, range.Date);
        Assert.Equal(14 * 3600000d, range.StartTimeOfDayMs);
        Assert.Equal((14 * 3600000d) + (30 * 60000d), range.ScrollToTimeMs);
    }

    [Fact]
    public void The_scroll_fraction_is_the_time_of_day_minus_the_start_of_the_grid()
    {
        var range = Clock().DateRange(Info("2024-05-17T00:00:00Z", "2024-05-18T00:00:00Z"), Now, currentDate: null);

        var fraction = (range.ScrollToTimeMs - range.StartTimeOfDayMs) / GuideClock.MsPerDay;

        Assert.Equal(30d / (24 * 60), fraction);
    }

    [Fact]
    public void The_viewer_s_offset_shifts_the_day_the_tabs_belong_to()
    {
        // 02:40 UTC is 21:40 the day before at UTC-5, so the grid starts at 21:00 local.
        var now = new DateTimeOffset(2024, 5, 17, 2, 40, 0, TimeSpan.Zero);
        var range = Clock(-300).DateRange(Info("2024-05-15T00:00:00Z", "2024-05-18T00:00:00Z"), now, currentDate: null);

        Assert.Equal(new DateTimeOffset(2024, 5, 17, 2, 0, 0, TimeSpan.Zero), range.Tabs[0].Date);
        Assert.Equal(16, range.Tabs[0].Day);
        // 21:40 local: the grid starts at 21:00 and, as at 14:40, the scroll target is the half hour.
        Assert.Equal(21 * 3600000d, range.StartTimeOfDayMs);
        Assert.Equal((21 * 3600000d) + (30 * 60000d), range.ScrollToTimeMs);
    }

    [Fact]
    public void A_selected_day_stays_selected_and_a_short_span_reaches_the_next_midnight()
    {
        var clock = Clock();
        var info = Info("2024-05-17T18:00:00Z", "2024-05-17T20:00:00Z");
        var first = clock.DateRange(info, Now, currentDate: null);
        var next = clock.DateRange(info, Now, first.Tabs[0].Date);

        // The span is shorter than a day, so setDateRange pushes the end to the next midnight and the
        // loop (start <= end) still draws that midnight: today at the current hour, then tomorrow.
        Assert.Equal(2, first.Tabs.Count);
        Assert.Equal(new DateTimeOffset(2024, 5, 18, 0, 0, 0, TimeSpan.Zero), first.Tabs[1].Date);
        Assert.True(next.Tabs[0].Active);
        Assert.False(next.Tabs[1].Active);
        Assert.Equal(first.Tabs[0].Date, next.Date);
    }

    [Fact]
    public void The_header_has_forty_eight_half_hours_from_the_start_of_the_grid()
    {
        var start = new DateTimeOffset(2024, 5, 17, 14, 0, 0, TimeSpan.Zero);

        var slots = GuideClock.Timeslots(start);

        Assert.Equal(48, slots.Count);
        Assert.Equal(start, slots[0]);
        Assert.Equal(start.AddMinutes(30), slots[1]);
        Assert.Equal(start.AddHours(23.5), slots[^1]);
    }

    [Fact]
    public void A_cell_is_positioned_across_the_day_and_the_current_one_is_active()
    {
        var grid = new DateTimeOffset(2024, 5, 17, 14, 0, 0, TimeSpan.Zero);
        var current = Program("now", Now.AddMinutes(-20), Now.AddMinutes(40));
        var later = Program("later", grid.AddHours(2), grid.AddHours(3));

        var html = GuideHtml.ChannelPrograms([current, later], grid, Options(), Now, Labels);

        Assert.Contains("data-action=\"programdialog\"", html);
        Assert.Contains("data-id=\"now\"", html);
        Assert.Contains("programCell-active", html);
        Assert.Contains("class=\"programCell itemAction\"", html.Split("programCell-active")[1]);
        Assert.Contains("is=\"emby-programcell\"", html);
        Assert.Contains("Show now", html);
        Assert.Contains("Show later", html);
    }

    [Fact]
    public void A_programme_shorter_than_two_percent_of_the_day_does_not_slide()
    {
        var grid = new DateTimeOffset(2024, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var shortOne = Program("brief", grid.AddHours(1), grid.AddMinutes(70));

        var html = GuideHtml.ChannelPrograms([shortOne], grid, Options(), Now, Labels);

        Assert.DoesNotContain("is=\"emby-programcell\"", html);
        Assert.Contains("data-id=\"brief\"", html);
    }

    [Fact]
    public void Indicators_and_the_timer_icon_follow_the_programme()
    {
        var grid = new DateTimeOffset(2024, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var live = Program("live", grid.AddHours(1), grid.AddHours(2), live: true, hd: true, episode: "Pilot", seriesTimer: "series1", status: "Cancelled");

        var html = GuideHtml.ChannelPrograms([live], grid, Options(), Now, Labels);

        Assert.Contains("liveTvProgram", html);
        Assert.Contains(">Live<", html);
        Assert.Contains("programSecondaryTitle", html);
        Assert.Contains("Pilot", html);
        Assert.Contains("guide-programTextIcon\">HD", html);
        Assert.Contains("seriesTimerIcon-inactive", html);
        Assert.Contains("data-seriestimerid=\"series1\"", html);
    }

    [Fact]
    public void A_single_timer_draws_the_manual_record_icon()
    {
        var grid = new DateTimeOffset(2024, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var recorded = Program("rec", grid.AddHours(1), grid.AddHours(2), timer: "timer1");

        var html = GuideHtml.ChannelPrograms([recorded], grid, Options(), Now, Labels);

        Assert.Contains("timerIcon fiber_manual_record", html);
        Assert.Contains("data-timerid=\"timer1\"", html);
        Assert.DoesNotContain("seriesTimerIcon", html);
    }

    [Theory]
    [InlineData("kids", true, false, false, false, "programCell-kids")]
    [InlineData("sports", false, true, false, false, "programCell-sports")]
    [InlineData("news", false, false, true, false, "programCell-news")]
    [InlineData("movies", false, false, false, true, "programCell-movie")]
    public void A_category_colours_its_programmes_and_hides_the_others(string category, bool kids, bool sports, bool news, bool movie, string accent)
    {
        var grid = new DateTimeOffset(2024, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var matching = Program("match", grid.AddHours(1), grid.AddHours(2), kids: kids, sports: sports, news: news, movie: movie);
        var other = Program("other", grid.AddHours(3), grid.AddHours(4), series: true);

        var html = GuideHtml.ChannelPrograms([matching, other], grid, Options(category), Now, Labels);

        Assert.Contains(accent, html);
        Assert.Contains("Show match", html);
        Assert.DoesNotContain("Show other", html);
    }

    [Fact]
    public void Programmes_outside_the_day_are_dropped()
    {
        var grid = new DateTimeOffset(2024, 5, 17, 0, 0, 0, TimeSpan.Zero);
        var before = Program("before", grid.AddHours(-3), grid.AddHours(-1));
        var after = Program("after", grid.AddDays(1).AddHours(1), grid.AddDays(1).AddHours(2));

        var html = GuideHtml.ChannelPrograms([before, after], grid, Options(), Now, Labels);

        Assert.Empty(html);
    }

    [Fact]
    public void The_programme_list_keeps_the_server_s_order_and_compacts_ids()
    {
        const string json = """
            {"Items":[
              {"Id":"00000000-0000-0000-0000-000000000001","ChannelId":"00000000-0000-0000-0000-00000000000a","Name":"One","StartDate":"2024-05-17T14:00:00.000Z","EndDate":"2024-05-17T15:00:00.000Z","IsHD":true},
              {"Id":"not-a-guid","ChannelId":"channel","Name":"Two","StartDate":"not-a-date","EndDate":"2024-05-17T16:00:00.000Z"},
              {"Id":"00000000-0000-0000-0000-000000000002","ChannelId":"00000000-0000-0000-0000-00000000000a","Name":"Three","StartDate":"2024-05-17T15:00:00.000Z","EndDate":"2024-05-17T16:00:00.000Z"}
            ]}
            """;

        var programs = GuideProgram.ParseList(json);

        Assert.Equal(2, programs.Count);
        Assert.Equal("00000000000000000000000000000001", programs[0].Id);
        Assert.Equal("0000000000000000000000000000000a", programs[0].ChannelId);
        Assert.True(programs[0].IsHD);
        Assert.Equal("00000000000000000000000000000002", programs[1].Id);
    }

    [Fact]
    public void No_category_asks_for_every_channel()
    {
        var query = new GuideOptions().ChannelQuery(0, 500);

        Assert.Equal(0, query.StartIndex);
        Assert.Equal(500, query.Limit);
        Assert.True(query.EnableFavoriteSorting);
        Assert.Null(query.IsMovie);
        Assert.Null(query.IsSports);
        Assert.Null(query.IsNews);
        Assert.Null(query.IsKids);
        Assert.Null(query.IsSeries);
        Assert.Null(query.SortBy);
    }

    [Fact]
    public void Picked_categories_become_the_channel_filter_and_the_order_is_sent()
    {
        var query = new GuideOptions { Categories = ["movies", "series", "all"], ChannelOrder = "DatePlayed", FavoriteChannelsAtTop = false }
            .ChannelQuery(500, 500);

        Assert.Equal(500, query.StartIndex);
        Assert.False(query.EnableFavoriteSorting);
        Assert.True(query.IsMovie);
        Assert.True(query.IsSeries);
        Assert.Null(query.IsSports);
        Assert.Null(query.IsNews);
        Assert.Null(query.IsKids);
        Assert.Equal("DatePlayed", query.SortBy);
        Assert.Equal("Descending", query.SortOrder);
    }

    [Fact]
    public void Four_categories_count_as_everything_except_the_series_flag()
    {
        var query = new GuideOptions { Categories = ["movies", "sports", "kids", "news", "all"] }.ChannelQuery(0, 500);

        Assert.Null(query.IsMovie);
        Assert.Null(query.IsSports);
        Assert.Null(query.IsNews);
        Assert.Null(query.IsKids);
        Assert.Null(query.IsSeries);
    }

    [Theory]
    [InlineData(1920, false, true, true)]
    [InlineData(400, false, false, false)]
    public void Indicators_need_a_wide_enough_window_and_follow_the_stored_settings(int width, bool live, bool hd, bool premiere)
    {
        var app = new TestApp();
        app.SignIn();
        var settings = app.Get<UserSettings>();
        settings.Set("guide-indicator-hd", "true");
        settings.Set("guide-indicator-live", "false");
        settings.Set("guide-colorcodedbackgrounds", "true");
        settings.Set("livetv-favoritechannelsattop", "false");
        settings.Set("livetv-channelorder", "DatePlayed");

        var options = GuideOptions.From(settings, ["news"], width);

        Assert.Equal(hd, options.ShowHdIcon);
        Assert.Equal(live, options.ShowLiveIndicator);
        Assert.Equal(premiere, options.ShowPremiereIndicator);
        Assert.Equal(width >= GuideOptions.MinIndicatorWidth, options.ShowNewIndicator);
        Assert.False(options.ShowRepeatIndicator);
        Assert.True(options.ColorCodedBackgrounds);
        Assert.False(options.FavoriteChannelsAtTop);
        Assert.Equal("DatePlayed", options.ChannelOrder);
        Assert.Equal(["news"], options.Categories);
    }
}
