using System.Net;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>Starting, changing and ending playback against the scripted server and the fake elements.</summary>
public class PlaybackFlowTests
{
    [Fact]
    public async Task Playing_a_movie_by_id_negotiates_and_direct_plays_it()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        PlaybackServer.Script(harness.Server);

        // The item as the server sends it: ids without dashes.
        harness.Server.OnGet($"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}", _ => new JsonObject
        {
            ["Name"] = "Test Movie",
            ["ServerId"] = TestApp.ServerId,
            ["Id"] = PlaybackServer.MovieId,
            ["Type"] = "Movie",
            ["MediaType"] = "Video",
            ["RunTimeTicks"] = PlaybackServer.MovieTicks,
            ["IsFolder"] = false,
            ["UserData"] = new JsonObject { ["PlaybackPositionTicks"] = 0, ["Played"] = false },
        });
        harness.Server.OnPost($"/Items/{PlaybackServer.MovieId}/PlaybackInfo", _ => FakeHttp.Json(PlaybackServer.PlaybackInfo(PlaybackServer.MediaSource())));

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId] });

        Assert.True(harness.Manager.IsPlaying, string.Join("\n", harness.Server.Requests.Select(r => r.Method + " " + r.Uri)));
        Assert.Equal("video", harness.Player.Loads.Single().Kind);
        var stream = harness.Manager.CurrentStreamInfo!;
        Assert.Equal(PlayMethods.DirectPlay, stream.PlayMethod);
        Assert.Equal("Test Movie", stream.Item.Name);
    }

    [Fact]
    public async Task The_start_asks_the_server_how_to_play_with_the_saved_bitrate_and_the_profile()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.PlayMovieAsync();

        var body = PlaybackServer.PlaybackInfoRequests(harness.Server).Single();
        Assert.Equal(PlaybackServer.UserId, (string?)body["UserId"]);
        Assert.Equal(0, (long?)body["StartTimeTicks"] ?? 0);
        Assert.True((bool?)body["IsPlayback"]);
        Assert.True((bool?)body["AutoOpenLiveStream"]);
        Assert.Equal(PlaybackServer.Bitrate, (int?)body["MaxStreamingBitrate"]);
        Assert.NotEmpty(body["DeviceProfile"]!["DirectPlayProfiles"]!.AsArray());

        // The bitrate is the saved one: nothing was measured.
        Assert.Empty(harness.Server.RequestsTo("/Playback/BitrateTest"));

        // A fresh start in full screen asks for the cinema mode intros (the server has none).
        Assert.Single(harness.Server.RequestsTo("/Intros"));

        var load = harness.Player.Loads.Single().Request;
        Assert.Equal(
            $"{TestApp.ServerUrl}/Videos/{PlaybackServer.MovieId}/stream.mkv?Static=true&mediaSourceId={PlaybackServer.MovieId}&deviceId={harness.Get<JellyfinApi>().DeviceId}&ApiKey=test-token&Tag=etag-1111",
            load.Url);
        Assert.Equal(0, load.StartSeconds);
        Assert.False(load.UseHlsJs);
        Assert.True(load.Fullscreen);
    }

    [Fact]
    public async Task The_start_is_reported_with_the_state_and_the_queue()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.PlayMovieAsync();
        await PlaybackHarness.Eventually(() => harness.StartReports.Count == 1, "the start report");

        var report = harness.StartReports.Single();
        Assert.Equal(100, (int?)report["VolumeLevel"]);
        Assert.False((bool?)report["IsMuted"]);
        Assert.False((bool?)report["IsPaused"]);
        Assert.Equal("RepeatNone", (string?)report["RepeatMode"]);
        Assert.Equal("Sorted", (string?)report["ShuffleMode"]);
        Assert.Equal("Default", (string?)report["PlaybackOrder"]);
        Assert.Equal(PlaybackServer.Bitrate, (int?)report["MaxStreamingBitrate"]);
        Assert.Equal(0, (long?)report["PositionTicks"]);
        Assert.Equal(harness.Timers.NowMs * 10000, (long?)report["PlaybackStartTimeTicks"]);
        Assert.Equal(1, (double?)report["PlaybackRate"]);
        Assert.Equal(2, (int?)report["AudioStreamIndex"]);
        Assert.Equal("DirectPlay", (string?)report["PlayMethod"]);
        Assert.Equal("session-1", (string?)report["PlaySessionId"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)report["MediaSourceId"]);
        Assert.True((bool?)report["CanSeek"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)report["ItemId"]);
        Assert.Null(report["EventName"]);

        var queued = Assert.Single(report["NowPlayingQueue"]!.AsArray())!;
        Assert.Equal(PlaybackServer.MovieId, (string?)queued["Id"]);
        Assert.Equal(harness.Manager.CurrentPlaylistItemId, (string?)queued["PlaylistItemId"]);
        Assert.NotNull(harness.Manager.CurrentPlaylistItemId);

        // The progress reports of the original: every ten seconds.
        Assert.Equal(new[] { 10000 }, harness.Timers.Intervals);
    }

    [Fact]
    public async Task Resuming_starts_the_stream_at_the_position_and_skips_the_intros()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.PlayMovieAsync(startPositionTicks: 300_000_000);
        await PlaybackHarness.Eventually(() => harness.StartReports.Count == 1, "the start report");

        Assert.Equal(300_000_000, (long?)PlaybackServer.PlaybackInfoRequests(harness.Server).Single()["StartTimeTicks"]);
        Assert.Equal(30, harness.Player.Loads.Single().Request.StartSeconds);
        Assert.Equal(300_000_000, (long?)harness.StartReports.Single()["PositionTicks"]);
        Assert.Equal(300_000_000, harness.Manager.CurrentTimeTicks);
        Assert.Empty(harness.Server.RequestsTo("/Intros"));
    }

    [Fact]
    public async Task An_error_of_the_element_falls_back_to_a_transcode_at_the_same_position()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 20);

        harness.Player.Emit("video", "error", errorType: MediaErrors.MediaDecodeError);
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 2, "the second load");

        var retry = PlaybackServer.PlaybackInfoRequests(harness.Server)[1];
        Assert.False((bool?)retry["EnableDirectPlay"]);
        Assert.False((bool?)retry["EnableDirectStream"]);
        Assert.False((bool?)retry["AllowVideoStreamCopy"]);
        Assert.Equal(200_000_000, (long?)retry["StartTimeTicks"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)retry["MediaSourceId"]);

        var load = harness.Player.LastLoad;
        Assert.Equal(TestApp.ServerUrl + PlaybackServer.TranscodeUrl, load.Url);
        Assert.Equal("application/x-mpegURL", load.MimeType);
        Assert.True(load.UseHlsJs);
        Assert.Equal(20, load.StartSeconds);

        await PlaybackHarness.Eventually(() => harness.Manager.PlayMethod == PlayMethods.Transcode, "the new stream");
        Assert.Equal("transcode-1", harness.Manager.PlaySessionId);
        Assert.True(harness.Manager.IsPlaying);
        Assert.Equal(0, harness.DialogCount);
        Assert.Empty(harness.StopReports);
    }

    [Fact]
    public async Task A_source_the_element_refuses_at_the_start_is_retried_as_a_transcode()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Player.LoadResult = (_, request) => request.Url.Contains("master.m3u8", StringComparison.Ordinal)
            ? new PlayerLoadResult { Ok = true }
            : new PlayerLoadResult { Ok = false, ErrorName = "NotSupportedError" };

        await harness.PlayMovieAsync();

        // The original waits a moment before it handles the error.
        Assert.Single(harness.Player.Loads);
        Assert.Equal(new long[] { 100 }, harness.Timers.PendingDelays);
        await harness.Timers.AdvanceAsync(100);
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 2, "the second load");

        Assert.False((bool?)PlaybackServer.PlaybackInfoRequests(harness.Server)[1]["EnableDirectPlay"]);
        await PlaybackHarness.Eventually(() => harness.Manager.PlayMethod == PlayMethods.Transcode, "the new stream");
        Assert.Equal(0, harness.DialogCount);
    }

    [Fact]
    public async Task An_error_with_nothing_left_to_try_ends_playback_with_its_message()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var source = PlaybackServer.MediaSource();
        source["SupportsTranscoding"] = false;
        PlaybackServer.ScriptPlaybackInfo(harness.Server, direct: source);
        var events = new List<string>();
        harness.Manager.Event += events.Add;
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 5);

        harness.Player.Emit("video", "error", errorType: MediaErrors.MediaDecodeError);
        await PlaybackHarness.Eventually(() => harness.DialogCount == 1, "the error dialog");

        Assert.Contains(PlaybackEvents.PlaybackError, events);
        Assert.Contains(PlaybackEvents.PlaybackStop, events);
        Assert.Single(harness.Player.Loads);
        Assert.Null(harness.Manager.CurrentPlayer);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Empty(harness.Manager.GetPlaylist());
        Assert.Empty(harness.Timers.Intervals);
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");
        Assert.Equal(50_000_000, (long?)harness.StopReports.Single()["PositionTicks"]);
    }

    [Fact]
    public async Task A_server_without_a_compatible_stream_shows_one_message_and_plays_nothing()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnPost(
            $"/Items/{PlaybackServer.MovieId}/PlaybackInfo",
            _ => FakeHttp.Json(new JsonObject { ["MediaSources"] = new JsonArray(), ["ErrorCode"] = "NoCompatibleStream" }));

        await harness.PlayMovieAsync();

        Assert.Equal(1, harness.DialogCount);
        Assert.Empty(harness.Player.Loads);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Empty(harness.StartReports);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_failing_negotiation_shows_one_message_and_plays_nothing(HttpStatusCode status)
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnPost($"/Items/{PlaybackServer.MovieId}/PlaybackInfo", _ => FakeHttp.Status(status));

        await harness.PlayMovieAsync();

        Assert.Equal(1, harness.DialogCount);
        Assert.Empty(harness.Player.Loads);
        Assert.False(harness.Manager.IsPlaying);
    }

    [Fact]
    public async Task An_item_that_does_not_exist_shows_one_message()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.Manager.PlayAsync(new PlayOptions { Ids = ["99999999999999999999999999999999"] });

        Assert.Equal(1, harness.DialogCount);
        Assert.Empty(harness.Player.Loads);
    }

    [Fact]
    public async Task Stopping_reports_the_position_and_releases_the_player_and_the_queue()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var stops = new List<PlaybackStoppedInfo>();
        harness.Manager.PlaybackStopped += stops.Add;
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 12);

        await harness.Manager.StopAsync();
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");

        var report = harness.StopReports.Single();
        Assert.Equal(120_000_000, (long?)report["PositionTicks"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)report["ItemId"]);
        Assert.Equal(PlaybackServer.MovieId, (string?)report["MediaSourceId"]);
        Assert.Single(report["NowPlayingQueue"]!.AsArray());

        Assert.Contains("stop video destroy=True fade=False", harness.Player.Calls);
        Assert.Contains("destroy video", harness.Player.Calls);
        Assert.Null(harness.Manager.CurrentPlayer);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Null(harness.Manager.CurrentItem);
        Assert.Empty(harness.Manager.GetPlaylist());
        Assert.Empty(harness.Timers.Intervals);
        Assert.Equal(0, harness.DialogCount);

        var stop = Assert.Single(stops);
        Assert.Null(stop.NextItem);
        Assert.Equal(120_000_000, stop.State.PositionTicks);
    }

    [Fact]
    public async Task Songs_are_played_through_the_universal_url_without_a_negotiation()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.PlaySongsAsync();
        await PlaybackHarness.Eventually(() => harness.StartReports.Count == 1, "the start report");

        Assert.DoesNotContain(harness.Server.Requests, r => r.Path.EndsWith("/PlaybackInfo", StringComparison.Ordinal));
        var (kind, load) = harness.Player.Loads.Single();
        Assert.Equal("audio", kind);
        Assert.StartsWith($"{TestApp.ServerUrl}/Audio/{PlaybackServer.SongId}/universal?", load.Url, StringComparison.Ordinal);
        Assert.Contains("MaxStreamingBitrate=8000000", load.Url, StringComparison.Ordinal);
        Assert.Contains($"PlaySessionId={harness.Manager.PlaySessionId}", load.Url, StringComparison.Ordinal);
        Assert.False(load.Fullscreen);

        Assert.True(harness.Manager.IsPlayingAudio);
        Assert.False(harness.Manager.IsPlayingVideo);
        Assert.Equal(new[] { "Opening", "Middle", "Closing" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal(0, harness.Manager.CurrentPlaylistIndex);

        var report = harness.StartReports.Single();
        Assert.Equal(PlaybackServer.SongId, (string?)report["ItemId"]);
        Assert.Equal(3, report["NowPlayingQueue"]!.AsArray().Count);
    }

    [Fact]
    public async Task The_end_of_a_song_plays_the_next_one_and_the_end_of_the_last_releases_the_player()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var stops = new List<PlaybackStoppedInfo>();
        harness.Manager.PlaybackStopped += stops.Add;
        await harness.PlaySongsAsync();
        harness.Player.Tick("audio", 30);

        harness.Player.End("audio");
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 2 && harness.StartReports.Count == 2, "the second song");

        Assert.Equal("Middle", harness.Manager.CurrentItem?.Name);
        Assert.Equal(1, harness.Manager.CurrentPlaylistIndex);
        Assert.Contains($"/Audio/{PlaybackServer.SecondSongId}/universal?", harness.Player.LastLoad.Url, StringComparison.Ordinal);

        // The element is kept for the next song; the stop of the first is reported once.
        Assert.Contains("stop audio destroy=False fade=True", harness.Player.Calls);
        Assert.DoesNotContain("destroy audio", harness.Player.Calls);
        var firstStop = Assert.Single(harness.StopReports);
        Assert.Equal(PlaybackServer.SongId, (string?)firstStop["ItemId"]);
        Assert.Equal(300_000_000, (long?)firstStop["PositionTicks"]);
        Assert.Equal(PlaybackServer.SecondSongId, (string?)harness.StartReports[1]["ItemId"]);
        Assert.Equal("Middle", Assert.Single(stops).NextItem?.Name);
        Assert.Equal(new[] { 10000 }, harness.Timers.Intervals);

        harness.Player.End("audio");
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 3 && harness.StartReports.Count == 3, "the third song");
        Assert.Equal("Closing", harness.Manager.CurrentItem?.Name);

        harness.Player.End("audio");
        await PlaybackHarness.Eventually(() => harness.Manager.CurrentPlayer is null && harness.StopReports.Count == 3, "the end of the queue");

        Assert.Equal(3, harness.Player.Loads.Count);
        Assert.Contains("destroy audio", harness.Player.Calls);
        Assert.Empty(harness.Manager.GetPlaylist());
        Assert.Empty(harness.Timers.Intervals);
        Assert.Null(stops[^1].NextItem);
    }

    [Fact]
    public async Task Repeating_one_song_plays_it_again_at_its_end()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlaySongsAsync();
        harness.Manager.SetRepeatMode(PlayQueue.RepeatOne);

        harness.Player.End("audio");
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 2, "the song again");

        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);
        Assert.Equal(0, harness.Manager.CurrentPlaylistIndex);
    }

    [Fact]
    public async Task The_next_video_waits_for_the_viewer_unless_auto_play_is_on()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var stops = new List<PlaybackStoppedInfo>();
        harness.Manager.PlaybackStopped += stops.Add;
        await harness.PlayMoviesAsync();
        Assert.Equal(new[] { "Test Movie", "Second Movie" }, harness.Manager.GetPlaylist().Select(i => i.Name));

        harness.Player.End("video");
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");

        // The original leaves the decision to the "up next" UI: the stop names the next item.
        var stop = Assert.Single(stops);
        Assert.Equal("Second Movie", stop.NextItem?.Name);
        Assert.Equal("Video", stop.NextMediaType);
        Assert.Single(harness.Player.Loads);
        Assert.DoesNotContain("destroy video", harness.Player.Calls);

        await harness.Manager.NextTrackAsync();

        Assert.Equal(2, harness.Player.Loads.Count);
        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
        Assert.Equal(1, harness.Manager.CurrentPlaylistIndex);
        Assert.Single(harness.StopReports);
    }

    [Fact]
    public async Task The_next_video_starts_by_itself_when_the_user_enabled_auto_play()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync(user => user.Configuration!.EnableNextEpisodeAutoPlay = true);
        await harness.PlayMoviesAsync();

        harness.Player.End("video");
        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 2 && harness.StartReports.Count == 2, "the second movie");

        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
        Assert.Contains($"/Videos/{PlaybackServer.SecondMovieId}/stream.mkv", harness.Player.LastLoad.Url, StringComparison.Ordinal);
        Assert.Equal(PlaybackServer.SecondMovieId, (string?)harness.StartReports[1]["ItemId"]);
    }

    [Fact]
    public async Task Playing_something_else_stops_and_reports_what_was_playing()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlayMovieAsync();
        harness.Player.Tick("video", 8);

        await harness.PlaySongsAsync();
        await PlaybackHarness.Eventually(() => harness.StartReports.Count == 2 && harness.StopReports.Count == 1, "the reports");

        // Another player takes over: the video element is released.
        Assert.Contains("stop video destroy=True fade=False", harness.Player.Calls);
        Assert.Equal(PlaybackServer.MovieId, (string?)harness.StopReports.Single()["ItemId"]);
        Assert.Equal(80_000_000, (long?)harness.StopReports.Single()["PositionTicks"]);
        Assert.True(harness.Manager.IsPlayingAudio);
        Assert.Equal(3, harness.Manager.GetPlaylist().Count);
    }

    [Fact]
    public async Task Previous_track_goes_back_one_item_and_starts_it_from_the_beginning()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        await harness.PlaySongsAsync();
        await harness.Manager.NextTrackAsync();
        Assert.Equal("Middle", harness.Manager.CurrentItem?.Name);

        await harness.Manager.PreviousTrackAsync();

        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);
        Assert.Equal(0, harness.Manager.CurrentPlaylistIndex);
        Assert.Equal(0, harness.Player.LastLoad.StartSeconds);

        // There is nothing before the first item.
        await harness.Manager.PreviousTrackAsync();
        Assert.Equal(3, harness.Player.Loads.Count);
    }

    [Fact]
    public async Task Signing_out_stops_playback_and_forgets_the_queue()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnPost("/Sessions/Logout");
        await harness.PlayMoviesAsync();

        await harness.Get<ServerConnections>().LogoutAsync();

        Assert.Contains("stop video destroy=True fade=False", harness.Player.Calls);
        Assert.Null(harness.Manager.CurrentPlayer);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Empty(harness.Manager.GetPlaylist());
        Assert.Empty(harness.Timers.Intervals);
    }

    [Fact]
    public async Task Without_a_player_for_the_media_type_a_message_is_shown()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var book = PlaybackServer.Movie("33333333333333333333333333333333", "An Unsupported Book");
        book.Type = BaseItemDto_Type.Book;
        book.MediaType = BaseItemDto_MediaType.Book;
        book.Path = "/books/unsupported.mobi";
        PlaybackServer.Script(harness.Server, book);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [book] });

        Assert.Equal(1, harness.DialogCount);
        Assert.Empty(harness.Player.Loads);
        Assert.False(harness.Manager.IsPlaying);
    }

    [Fact]
    public async Task Play_buttons_are_offered_for_what_a_local_player_or_an_expansion_can_play()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var manager = harness.Manager;

        Assert.True(manager.CanPlay(PlaybackServer.Movie()));
        Assert.True(manager.CanPlay(PlaybackServer.Song()));
        Assert.True(manager.CanPlay(new BaseItemDto { Type = BaseItemDto_Type.Series }));
        Assert.True(manager.CanPlay(new BaseItemDto { Type = BaseItemDto_Type.MusicAlbum }));
        Assert.True(manager.CanPlay(new BaseItemDto { Type = BaseItemDto_Type.Photo, MediaType = BaseItemDto_MediaType.Photo, Path = "/photo.jpg" }));
        var missing = PlaybackServer.Movie();
        missing.LocationType = BaseItemDto_LocationType.Virtual;
        Assert.False(manager.CanPlay(missing));

        // Queueing needs a player that plays the media type.
        Assert.False(manager.CanQueue(PlaybackServer.Song()));
        await harness.PlaySongsAsync();
        Assert.True(manager.CanQueue(PlaybackServer.Song()));
        Assert.True(manager.CanQueue(new BaseItemDto { Type = BaseItemDto_Type.MusicAlbum }));
        Assert.False(manager.CanQueue(PlaybackServer.Movie()));
    }
}
