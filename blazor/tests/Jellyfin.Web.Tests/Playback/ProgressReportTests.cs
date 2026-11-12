using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>
/// What the server is told while something plays, and when: the rate limits of the original's
/// reportPlaybackProgress(), driven by a clock the tests move.
/// </summary>
public class ProgressReportTests
{
    [Fact]
    public async Task Position_updates_go_out_at_most_every_ten_seconds_with_the_newest_position()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        var timers = harness.Timers;

        // The first update after the start is sent at once.
        harness.Player.Tick("video", 1);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 1, "the first progress report");
        var first = harness.ProgressReports[0];
        Assert.Equal("timeupdate", (string?)first["EventName"]);
        Assert.Equal(10_000_000, (long?)first["PositionTicks"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)first["ItemId"]);
        Assert.False((bool?)first["IsPaused"]);
        Assert.Null(first["NowPlayingQueue"]);

        // The following ones wait for the ten seconds to pass; only the newest is kept.
        timers.NowMs += 1000;
        harness.Player.Tick("video", 2);
        timers.NowMs += 1000;
        harness.Player.Tick("video", 3);
        Assert.Single(harness.ProgressReports);
        Assert.Equal(new long[] { 8000 }, timers.PendingDelays);

        await timers.AdvanceAsync(7999);
        Assert.Single(harness.ProgressReports);

        await timers.AdvanceAsync(1);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the delayed progress report");
        Assert.Equal(30_000_000, (long?)harness.ProgressReports[1]["PositionTicks"]);
    }

    [Fact]
    public async Task A_jump_of_five_seconds_is_a_seek_and_reported_at_once()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 1);
        harness.Timers.NowMs += 1000;

        // One second later the position is 29 seconds further than playing would bring it.
        harness.Player.Tick("video", 31);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the report of the seek");
        Assert.Equal(310_000_000, (long?)harness.ProgressReports[1]["PositionTicks"]);

        // Less than five seconds off is ordinary playback again.
        harness.Timers.NowMs += 1000;
        harness.Player.Tick("video", 36.9);
        Assert.Equal(2, harness.ProgressReports.Count);
        Assert.Equal(new long[] { 9000 }, harness.Timers.PendingDelays);
    }

    [Fact]
    public async Task Every_ten_seconds_the_position_is_reported_even_without_events()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        Assert.Equal(new[] { 10000 }, harness.Timers.Intervals);

        await harness.Timers.AdvanceAsync(10000);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 1, "the first interval report");
        Assert.Equal("timeupdate", (string?)harness.ProgressReports[0]["EventName"]);
        await PlaybackHarness.Eventually(() => harness.PlayingPings.Count == 1, "the first playing ping");

        await harness.Timers.AdvanceAsync(10000);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the second interval report");
        await PlaybackHarness.Eventually(() => harness.PlayingPings.Count == 2, "the second playing ping");
    }

    [Fact]
    public async Task Pause_and_unpause_are_reported_at_once()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 4);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 1, "the first progress report");

        await harness.Manager.PauseAsync();
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the pause report");
        var pause = harness.ProgressReports[1];
        Assert.Equal("pause", (string?)pause["EventName"]);
        Assert.True((bool?)pause["IsPaused"]);
        Assert.Equal(40_000_000, (long?)pause["PositionTicks"]);
        Assert.True(harness.Manager.IsPaused);

        await harness.Manager.PlayPauseAsync();
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 3, "the unpause report");
        var unpause = harness.ProgressReports[2];
        Assert.Equal("unpause", (string?)unpause["EventName"]);
        Assert.False((bool?)unpause["IsPaused"]);
        Assert.False(harness.Manager.IsPaused);
    }

    [Fact]
    public async Task Volume_changes_go_out_at_most_every_three_seconds_and_an_urgent_report_replaces_a_waiting_one()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();

        harness.Manager.SetVolume(50);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 1, "the first volume report");
        Assert.Equal("volumechange", (string?)harness.ProgressReports[0]["EventName"]);
        Assert.Equal(50, (int?)harness.ProgressReports[0]["VolumeLevel"]);

        harness.Manager.SetVolume(40);
        harness.Manager.SetVolume(30);
        Assert.Single(harness.ProgressReports);
        Assert.Equal(new long[] { 3000 }, harness.Timers.PendingDelays);

        await harness.Timers.AdvanceAsync(3000);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the delayed volume report");
        Assert.Equal(30, (int?)harness.ProgressReports[1]["VolumeLevel"]);

        // A waiting volume report is overtaken by the pause, which carries the volume as well.
        harness.Manager.SetMute(true);
        Assert.Equal(2, harness.ProgressReports.Count);
        await harness.Manager.PauseAsync();
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 3, "the pause report");
        Assert.Equal("pause", (string?)harness.ProgressReports[2]["EventName"]);
        Assert.True((bool?)harness.ProgressReports[2]["IsMuted"]);

        await harness.Timers.AdvanceAsync(3000);
        Assert.Equal(3, harness.ProgressReports.Count);
    }

    [Fact]
    public async Task Queue_changes_are_reported_with_the_queue_and_mode_changes_without()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlaySongsAsync();
        var playlist = harness.Manager.GetPlaylist();

        harness.Manager.MovePlaylistItem(playlist[2].PlaylistItemId!, 0);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 1, "the move report");
        var move = harness.ProgressReports[0];
        Assert.Equal("playlistitemmove", (string?)move["EventName"]);
        Assert.Equal(
            new[] { PlaybackServer.ThirdSongId, PlaybackServer.SongId, PlaybackServer.SecondSongId },
            move["NowPlayingQueue"]!.AsArray().Select(entry => (string?)entry!["Id"]));
        Assert.Equal(
            harness.Manager.GetPlaylist().Select(i => i.PlaylistItemId),
            move["NowPlayingQueue"]!.AsArray().Select(entry => (string?)entry!["PlaylistItemId"]));

        harness.Manager.SetRepeatMode(PlayQueue.RepeatAll);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the repeat mode report");
        Assert.Equal("repeatmodechange", (string?)harness.ProgressReports[1]["EventName"]);
        Assert.Equal("RepeatAll", (string?)harness.ProgressReports[1]["RepeatMode"]);
        Assert.Null(harness.ProgressReports[1]["NowPlayingQueue"]);

        harness.Manager.SetQueueShuffleMode(PlayQueue.Shuffled);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 3, "the shuffle mode report");
        Assert.Equal("shufflequeuemodechange", (string?)harness.ProgressReports[2]["EventName"]);
        Assert.Equal("Shuffle", (string?)harness.ProgressReports[2]["ShuffleMode"]);
        Assert.Equal("Shuffle", (string?)harness.ProgressReports[2]["PlaybackOrder"]);

        await harness.Manager.QueueAsync(new PlayOptions { Items = [PlaybackServer.Song("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa4", "Single")] });
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 4, "the add report");
        Assert.Equal("playlistitemadd", (string?)harness.ProgressReports[3]["EventName"]);
        Assert.Equal(4, harness.ProgressReports[3]["NowPlayingQueue"]!.AsArray().Count);
        Assert.Equal("Single", harness.Manager.GetPlaylist()[^1].Name);
    }

    [Fact]
    public async Task Nothing_is_reported_once_playback_has_stopped_and_a_new_start_resets_the_limit()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 1);
        harness.Timers.NowMs += 1000;
        harness.Player.Tick("video", 2);
        Assert.Single(harness.Timers.PendingDelays);

        await harness.Manager.StopAsync();
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");

        // The report that was waiting is dropped with the stop; late events of the element are ignored.
        harness.Player.Tick("video", 3);
        await harness.Timers.AdvanceAsync(20000);
        Assert.Single(harness.ProgressReports);

        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 1);
        await PlaybackHarness.Eventually(() => harness.ProgressReports.Count == 2, "the first report of the new start");
    }

    [Fact]
    public async Task The_reports_reach_the_server_in_the_order_they_were_made()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 59);

        // The end of the media: the element pauses, then ends.
        harness.Player.Emit("video", "pause", s => s with { Paused = true });
        harness.Player.End("video");
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");

        var paths = harness.Server.Requests
            .Where(r => r.Method == HttpMethod.Post && r.Path.Contains("/Sessions/Playing", StringComparison.Ordinal))
            .Select(r => r.Path)
            .ToList();
        Assert.Equal(
            new[] { "/Sessions/Playing", "/Sessions/Playing/Progress", "/Sessions/Playing/Progress", "/Sessions/Playing/Stopped" },
            paths);
    }
}
