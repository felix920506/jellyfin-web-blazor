using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;
using Jellyfin.Web.Pages.Dashboard;
using Jellyfin.Web.Services;

namespace Jellyfin.Web.Tests.Dashboard;

/// <summary>The dashboard's logic that needs no component: navigation, texts and data shaping.</summary>
public sealed class DashboardLogicTests
{
    private static readonly DateTimeOffset Now = DashboardTestContext.Now;

    // ---- navigation ----

    [Fact]
    public void The_drawer_has_the_sections_of_the_original_in_order() =>
        Assert.Equal(["TabServer", "HeaderDevices", "LiveTV", "TabPlugins", "TabAdvanced"], DashboardNav.Sections.Select(section => section.TitleKey));

    [Fact]
    public void The_drawer_links_to_every_page_of_the_original()
    {
        var paths = DashboardNav.Sections
            .SelectMany(section => section.Links)
            .SelectMany(link => link.Children.Count > 0 ? link.Children : [link])
            .Select(link => link.Path);

        Assert.Equal(
            [
                "/dashboard", "/dashboard/settings", "/dashboard/branding", "/dashboard/users",
                "/dashboard/libraries", "/dashboard/libraries/display", "/dashboard/libraries/metadata", "/dashboard/libraries/nfo",
                "/dashboard/playback/transcoding", "/dashboard/playback/resume", "/dashboard/playback/streaming", "/dashboard/playback/trickplay",
                "/dashboard/devices", "/dashboard/activity",
                "/dashboard/livetv", "/dashboard/livetv/recordings",
                "/dashboard/plugins",
                "/dashboard/networking", "/dashboard/keys", "/dashboard/backups", "/dashboard/logs", "/dashboard/tasks",
            ],
            paths);
    }

    [Fact]
    public void Groups_have_no_page_of_their_own()
    {
        var groups = DashboardNav.Sections.SelectMany(section => section.Links).Where(link => link.Children.Count > 0).ToList();

        Assert.Equal(["HeaderLibraries", "TitlePlayback"], groups.Select(group => group.TextKey));
        Assert.All(groups, group => Assert.Null(group.Path));
    }

    [Theory]
    [InlineData("/dashboard/users", "/dashboard/users", true)]
    [InlineData("/dashboard/users", "/dashboard/users/", true)]
    [InlineData("/dashboard/users", "/Dashboard/Users", true)]
    [InlineData("/dashboard/users", "/dashboard/users/add", false)]
    [InlineData("/dashboard", "/dashboard/settings", false)]
    public void A_link_is_selected_on_its_own_page_only(string link, string path, bool selected) =>
        Assert.Equal(selected, DashboardNav.IsSelected(new DashboardLink("x", "x", link), path));

    [Fact]
    public void The_plugins_link_is_also_selected_on_a_plugin_page()
    {
        var plugins = DashboardNav.Sections.SelectMany(section => section.Links).Single(link => link.Path == "/dashboard/plugins");

        Assert.True(DashboardNav.IsSelected(plugins, "/configurationpage"));
        Assert.True(DashboardNav.IsSelected(plugins, "/dashboard/plugins/repositories"));
        Assert.False(DashboardNav.IsSelected(plugins, "/dashboard/networking"));
    }

    [Fact]
    public void A_group_contains_the_pages_of_its_links()
    {
        var playback = DashboardNav.Sections.SelectMany(section => section.Links).Single(link => link.TextKey == "TitlePlayback");

        Assert.True(DashboardNav.ContainsPath(playback, "/dashboard/playback/resume"));
        Assert.False(DashboardNav.ContainsPath(playback, "/dashboard/libraries"));
    }

    [Theory]
    [InlineData("/dashboard", null)]
    [InlineData("/dashboard/devices", "https://jellyfin.org/docs/general/server/devices")]
    [InlineData("/dashboard/settings", "https://jellyfin.org/docs/general/server/settings")]
    [InlineData("/dashboard/branding", "https://jellyfin.org/docs/general/server/settings")]
    [InlineData("/dashboard/tasks", "https://jellyfin.org/docs/general/server/tasks")]
    [InlineData("/dashboard/users", "https://jellyfin.org/docs/general/server/users/adding-managing-users")]
    [InlineData("/dashboard/users/0f0e0d0c0b0a09080706050403020100/access", "https://jellyfin.org/docs/general/server/users/")]
    [InlineData("/dashboard/users/add", null)]
    [InlineData("/dashboard/livetv/guide", "https://jellyfin.org/docs/general/server/live-tv/setup-guide#adding-guide-data")]
    [InlineData("/dashboard/networking", "https://jellyfin.org/docs/general/networking/")]
    [InlineData("/dashboard/backups", "https://jellyfin.org/docs/general/administration/backup-and-restore/")]
    [InlineData("/dashboard/logs", null)]
    public void Pages_with_documentation_have_a_help_link(string path, string? expected) =>
        Assert.Equal(expected, DashboardNav.HelpUrl(path));

    [Fact]
    public void A_plugin_page_is_addressed_by_its_name() =>
        Assert.Equal("configurationpage?name=My%20Plugin", DashboardNav.PluginPageUrl("My Plugin"));

    [Theory]
    [InlineData("10.11.0.0", "10.11.0")]
    [InlineData("10.11.0.3", "10.11.0.3")]
    [InlineData("10.11.0", "10.11.0")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_version_is_shown_without_an_empty_build_number(string? version, string? expected) =>
        Assert.Equal(expected, DashboardNav.DisplayVersion(version));

    // ---- relative times ----

    [Theory]
    [InlineData(0, 1, "minute")]
    [InlineData(40, 1, "minute")]
    [InlineData(5 * 60, 5, "minute")]
    [InlineData(44 * 60, 44, "minute")]
    [InlineData(45 * 60, 1, "hour")]
    [InlineData(3 * 3600, 3, "hour")]
    [InlineData(23 * 3600, 23, "hour")]
    [InlineData(24 * 3600, 1, "day")]
    [InlineData(29 * 86400, 29, "day")]
    [InlineData(30 * 86400, 1, "month")]
    [InlineData(90 * 86400, 3, "month")]
    [InlineData(365 * 86400, 1, "year")]
    [InlineData(800 * 86400, 2, "year")]
    public void A_time_span_is_told_in_its_largest_fitting_unit(int seconds, int value, string unit) =>
        Assert.Equal((value, unit), RelativeTime.Distance(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void A_negative_time_span_has_the_same_distance() =>
        Assert.Equal((5, "minute"), RelativeTime.Distance(TimeSpan.FromMinutes(-5)));

    [Theory]
    [InlineData(-5, "minute", "5 minutes ago")]
    [InlineData(-1, "hour", "1 hour ago")]
    [InlineData(2, "day", "in 2 days")]
    [InlineData(1, "month", "in 1 month")]
    public void The_fallback_for_relative_times_is_English(double value, string unit, string expected) =>
        Assert.Equal(expected, RelativeTime.EnglishRelative(value, unit));

    [Theory]
    [InlineData(1, "hour", "1 hour")]
    [InlineData(15, "minute", "15 minutes")]
    [InlineData(1.5, "hour", "1.5 hours")]
    public void The_fallback_for_durations_is_English(double value, string unit, string expected) =>
        Assert.Equal(expected, RelativeTime.EnglishDuration(value, unit));

    // ---- storage ----

    [Theory]
    [InlineData(0d, "0.0 Bytes")]
    [InlineData(512d, "512.0 Bytes")]
    [InlineData(1024d, "1.0 KiB")]
    [InlineData(1610612736d, "1.5 GiB")]
    [InlineData(5497558138880d, "5.0 TiB")]
    public void Sizes_are_shown_in_binary_units(double bytes, string expected) =>
        Assert.Equal(expected, StorageText.ReadableSize(bytes));

    [Fact]
    public void The_usage_of_a_folder_is_its_used_part_of_the_drive()
    {
        var usage = StorageText.Usage(new FolderStorageDto { UsedSpace = 1024L * 1024 * 1024, FreeSpace = 3L * 1024 * 1024 * 1024 });

        Assert.Equal(25, usage.Percent);
        Assert.Equal("success", usage.Level);
        Assert.Equal("1.0 GiB / 4.0 GiB", usage.Text);
    }

    [Theory]
    [InlineData(79, "success")]
    [InlineData(80, "warning")]
    [InlineData(89, "warning")]
    [InlineData(90, "error")]
    [InlineData(100, "error")]
    public void A_drive_that_fills_up_is_marked(long used, string level) =>
        Assert.Equal(level, StorageText.Usage(new FolderStorageDto { UsedSpace = used, FreeSpace = 100 - used }).Level);

    [Fact]
    public void An_unknown_usage_shows_question_marks()
    {
        Assert.Equal(new StorageUsage(0, "success", "? / ?"), StorageText.Usage(null));
        Assert.Equal(new StorageUsage(0, "success", "? / ?"), StorageText.Usage(new FolderStorageDto { UsedSpace = -1, FreeSpace = -1 }));
    }

    [Theory]
    [InlineData("Fixed", "StorageType.Fixed", "storage")]
    [InlineData("Network", "StorageType.Network", "lan")]
    [InlineData("Removable", "StorageType.Removable", "usb")]
    [InlineData("Ram", "StorageType.Ram", "memory")]
    [InlineData("CDRom", "StorageType.CDRom", "album")]
    [InlineData("Tape", null, "storage")]
    [InlineData(null, null, "storage")]
    public void Drive_types_have_a_name_and_an_icon(string? type, string? key, string icon)
    {
        Assert.Equal(key, StorageText.TypeKey(type));
        Assert.Equal(icon, StorageText.Icon(type));
    }

    // ---- devices and sessions ----

    [Theory]
    [InlineData("Jellyfin Web", "Chrome", null, "generated/assets/img/devices/chrome.svg")]
    [InlineData("Jellyfin Web", "Firefox Android", null, "generated/assets/img/devices/firefox.svg")]
    [InlineData("Jellyfin Web", "Some Browser", null, "generated/assets/img/devices/html5.svg")]
    [InlineData("Kodi", "Living room", null, "generated/assets/img/devices/kodi.svg")]
    [InlineData("Jellyfin Android", "Pixel", null, "generated/assets/img/devices/android.svg")]
    [InlineData("Finamp", "Phone", "https://example.org/icon.png", "generated/assets/img/devices/finamp.svg")]
    [InlineData("My Client", "Thing", "https://example.org/icon.png", "https://example.org/icon.png")]
    [InlineData("My Client", "Thing", "javascript:alert(1)", "generated/assets/img/devices/other.svg")]
    [InlineData("My Client", "Thing", null, "generated/assets/img/devices/other.svg")]
    public void A_device_shows_the_icon_of_its_app(string app, string device, string? iconUrl, string expected) =>
        Assert.Equal(expected, DeviceIcons.For(app, device, iconUrl));

    [Fact]
    public void A_device_record_uses_the_icon_it_announced()
    {
        Assert.Equal("generated/assets/img/devices/chrome.svg", DeviceIcons.For(new DeviceInfoDto { AppName = "Jellyfin Web", Name = "Chrome" }));
        Assert.Equal("generated/assets/img/devices/roku.svg", DeviceIcons.For(new SessionInfoDto { Client = "Jellyfin Roku", DeviceName = "TV" }));
        Assert.Equal("http://icons.test/a.png", DeviceIcons.For(new DeviceInfoDto { AppName = "A", Name = "B", IconUrl = "http://icons.test/a.png" }));
    }

    [Fact]
    public void Only_recently_active_sessions_with_a_user_or_playback_are_shown()
    {
        var user = Guid.NewGuid();
        var sessions = new[]
        {
            new SessionInfoDto { DeviceId = "recent", UserId = user, LastActivityDate = Now.AddSeconds(-30) },
            new SessionInfoDto { DeviceId = "old", UserId = user, LastActivityDate = Now.AddSeconds(-200) },
            new SessionInfoDto { DeviceId = "anonymous", LastActivityDate = Now },
            new SessionInfoDto { DeviceId = "playing", NowPlayingItem = new BaseItemDto { Name = "Film" }, LastActivityDate = Now },
            new SessionInfoDto { DeviceId = "never", UserId = user },
        };

        Assert.Equal(["recent", "playing"], DashboardSessions.Filter(sessions, Now).Select(session => session.DeviceId));
    }

    [Fact]
    public void An_update_replaces_known_devices_and_appends_new_ones()
    {
        var user = Guid.NewGuid();
        var current = new[]
        {
            new SessionInfoDto { DeviceId = "a", Client = "old" },
            new SessionInfoDto { DeviceId = "b", Client = "kept" },
        };
        var update = new[]
        {
            new SessionInfoDto { DeviceId = "c", Client = "new", UserId = user, LastActivityDate = Now },
            new SessionInfoDto { DeviceId = "a", Client = "replaced", UserId = user, LastActivityDate = Now },
            new SessionInfoDto { DeviceId = "d", Client = "stale", UserId = user, LastActivityDate = Now.AddHours(-1) },
        };

        var merged = DashboardSessions.Merge(current, update, Now);

        Assert.Equal(["a", "b", "c"], merged.Select(session => session.DeviceId));
        Assert.Equal(["replaced", "kept", "new"], merged.Select(session => session.Client));
    }

    [Fact]
    public void The_playback_position_of_a_session_reads_as_times()
    {
        Assert.Equal(("0:00", "0:00"), DashboardSessions.GetNowPlayingTime(new SessionInfoDto()));
        Assert.Equal(
            ("1:05", "1:30:00"),
            DashboardSessions.GetNowPlayingTime(new SessionInfoDto
            {
                NowPlayingItem = new BaseItemDto { RunTimeTicks = 54_000_000_000 },
                PlayState = new PlayerStateInfo { PositionTicks = 650_000_000 },
            }));
    }

    // ---- scheduled tasks ----

    [Fact]
    public void Tasks_are_grouped_by_category_and_sorted_by_name()
    {
        var tasks = new[]
        {
            new TaskInfo { Name = "Scan", Category = "Library" },
            new TaskInfo { Name = "Clean logs", Category = "Maintenance" },
            new TaskInfo { Name = "Extract chapters", Category = "Library" },
            new TaskInfo { Name = "No category" },
        };

        Assert.Equal(["Library", "Maintenance"], TaskTriggerText.Categories(tasks));
        Assert.Equal(["Extract chapters", "Scan"], TaskTriggerText.InCategory(tasks, "Library").Select(task => task.Name));
    }

    [Fact]
    public void The_interval_choices_run_from_a_quarter_of_an_hour_to_a_day()
    {
        Assert.Equal(11, TaskTriggerText.IntervalDurations.Count);
        Assert.Equal(TimeSpan.FromMinutes(15).Ticks, TaskTriggerText.IntervalDurations[0]);
        Assert.Equal(TimeSpan.FromHours(24).Ticks, TaskTriggerText.IntervalDurations[^1]);
    }

    [Fact]
    public void A_daily_trigger_has_a_time_only()
    {
        var trigger = DashboardTriggerDialog.Build(TaskTriggerInfo_Type.DailyTrigger, TaskTriggerInfo_DayOfWeek.Friday, 72_000_000_000, 9_000_000_000, null);

        Assert.Equal(TaskTriggerInfo_Type.DailyTrigger, trigger.Type);
        Assert.Equal(72_000_000_000, trigger.TimeOfDayTicks);
        Assert.Null(trigger.DayOfWeek);
        Assert.Null(trigger.IntervalTicks);
        Assert.Null(trigger.MaxRuntimeTicks);
    }

    [Fact]
    public void A_weekly_trigger_has_a_day_and_a_time()
    {
        var trigger = DashboardTriggerDialog.Build(TaskTriggerInfo_Type.WeeklyTrigger, TaskTriggerInfo_DayOfWeek.Friday, 72_000_000_000, 9_000_000_000, 2);

        Assert.Equal(TaskTriggerInfo_DayOfWeek.Friday, trigger.DayOfWeek);
        Assert.Equal(72_000_000_000, trigger.TimeOfDayTicks);
        Assert.Null(trigger.IntervalTicks);
        Assert.Equal(72_000_000_000, trigger.MaxRuntimeTicks);
    }

    [Fact]
    public void An_interval_trigger_has_an_interval_only()
    {
        var trigger = DashboardTriggerDialog.Build(TaskTriggerInfo_Type.IntervalTrigger, TaskTriggerInfo_DayOfWeek.Friday, 72_000_000_000, 9_000_000_000, 0.5);

        Assert.Equal(9_000_000_000, trigger.IntervalTicks);
        Assert.Null(trigger.TimeOfDayTicks);
        Assert.Null(trigger.DayOfWeek);
        Assert.Equal(18_000_000_000, trigger.MaxRuntimeTicks);
    }

    [Fact]
    public void A_startup_trigger_has_nothing_but_its_type()
    {
        var trigger = DashboardTriggerDialog.Build(TaskTriggerInfo_Type.StartupTrigger, TaskTriggerInfo_DayOfWeek.Friday, 72_000_000_000, 9_000_000_000, null);

        Assert.Equal(TaskTriggerInfo_Type.StartupTrigger, trigger.Type);
        Assert.Null(trigger.TimeOfDayTicks);
        Assert.Null(trigger.DayOfWeek);
        Assert.Null(trigger.IntervalTicks);
    }

    // ---- users ----

    private static ParentalRating Rating(string name, int? score, int? subScore = null) =>
        new() { Name = name, RatingScore = score is null ? null : new ParentalRatingScore { Score = score, SubScore = subScore } };

    [Fact]
    public void Ratings_with_the_same_score_are_one_choice()
    {
        var options = ParentalRatings.Group([Rating("Unrated", null), Rating("G", 0), Rating("TV-G", 0), Rating("PG", 5), Rating("R", 17), Rating("TV-MA", 17, 1)]);

        Assert.Equal(
            [
                new ParentalRatingOption("Unrated", null, null),
                new ParentalRatingOption("G/TV-G", 0, null),
                new ParentalRatingOption("PG", 5, null),
                new ParentalRatingOption("R", 17, null),
                new ParentalRatingOption("TV-MA", 17, 1),
            ],
            options);
    }

    [Theory]
    [InlineData(null, null, -1)]
    [InlineData(5, null, 2)]
    [InlineData(17, 1, 4)]
    [InlineData(17, null, 3)]
    [InlineData(10, null, 2)]
    [InlineData(-1, null, -1)]
    public void The_limit_of_a_user_selects_the_matching_choice(int? rating, int? subRating, int expected)
    {
        var options = ParentalRatings.Group([Rating("Unrated", null), Rating("G", 0), Rating("PG", 5), Rating("R", 17), Rating("TV-MA", 17, 1)]);

        Assert.Equal(expected, ParentalRatings.SelectedIndex(options, rating, subRating));
    }

    [Fact]
    public void An_access_schedule_needs_a_start_before_its_end()
    {
        var schedule = DashboardAccessScheduleDialog.Build("Weekday", "8.5", "17");

        Assert.NotNull(schedule);
        Assert.Equal(AccessSchedule_DayOfWeek.Weekday, schedule.DayOfWeek);
        Assert.Equal(8.5, schedule.StartHour);
        Assert.Equal(17, schedule.EndHour);
        Assert.Null(DashboardAccessScheduleDialog.Build("Monday", "9", "9"));
        Assert.Null(DashboardAccessScheduleDialog.Build("Monday", "10", "9"));
        Assert.Equal(AccessSchedule_DayOfWeek.Sunday, DashboardAccessScheduleDialog.Build("Someday", "0", "24")?.DayOfWeek);
    }

    [Fact]
    public void The_schedule_dialog_offers_every_half_hour_and_the_days_of_the_original()
    {
        Assert.Equal(49, DashboardAccessScheduleDialog.Hours.Length);
        Assert.Equal(0, DashboardAccessScheduleDialog.Hours[0]);
        Assert.Equal(0.5, DashboardAccessScheduleDialog.Hours[1]);
        Assert.Equal(24, DashboardAccessScheduleDialog.Hours[^1]);
        Assert.Equal(10, DashboardAccessScheduleDialog.Days.Length);
        Assert.Equal("OptionWeekends", DashboardAccessScheduleDialog.Days[^1].Key);
    }

    [Fact]
    public void The_user_cache_holds_the_last_user_only()
    {
        var cache = new DashboardUserCache();
        var first = new UserDto { Id = Guid.NewGuid(), Name = "first" };
        var second = new UserDto { Id = Guid.NewGuid(), Name = "second" };

        cache.Set(first);
        Assert.Same(first, cache.Get(first.Id!.Value));
        cache.Set(second);
        Assert.Null(cache.Get(first.Id!.Value));
        cache.Clear();
        Assert.Null(cache.Get(second.Id!.Value));
    }

    // ---- tables ----

    [Theory]
    [InlineData(null, true)]
    [InlineData("  ", true)]
    [InlineData("chrome", true)]
    [InlineData(" WEB ", true)]
    [InlineData("kodi", false)]
    public void A_search_matches_any_of_the_texts(string? search, bool expected) =>
        Assert.Equal(expected, DashboardFormat.Matches(search, "Chrome", null, "Jellyfin Web"));

    [Fact]
    public void A_device_is_called_by_the_name_its_owner_gave_it()
    {
        Assert.Equal("Laptop", DashboardFormat.DeviceName(new DeviceInfoDto { Name = "Chrome", CustomName = "Laptop" }));
        Assert.Equal("Chrome", DashboardFormat.DeviceName(new DeviceInfoDto { Name = "Chrome", CustomName = string.Empty }));
        Assert.Equal("Jellyfin Web 10.11.0", DashboardFormat.AppText(new DeviceInfoDto { AppName = "Jellyfin Web", AppVersion = "10.11.0" }));
        Assert.Equal("Jellyfin Web", DashboardFormat.AppText(new DeviceInfoDto { AppName = "Jellyfin Web" }));
    }

    [Theory]
    [InlineData(null, "All")]
    [InlineData("true", "User")]
    [InlineData("TRUE", "User")]
    [InlineData("1", "User")]
    [InlineData("false", "System")]
    [InlineData("", "System")]
    public void The_activity_view_follows_the_address(string? parameter, string expected) =>
        Assert.Equal(expected, DashboardActivity.ViewOf(parameter));
}
