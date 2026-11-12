using System.Text.Json.Nodes;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.PlaybackExtras;

/// <summary>
/// Remote control of another session: the targets, the requests the commands become, the state
/// the "Sessions" messages bring, and what the manager does around a target (transfer of what
/// plays here, returning to the remembered target, display mirroring).
/// </summary>
public class SessionPlayerTests
{
    private static async Task<PlaybackHarness> WithSessionsAsync(bool admin = false, params JsonObject[] sessions)
    {
        var harness = await PlaybackHarness.WithMediaAsync(user => user.Policy!.IsAdministrator = admin);
        RemoteSessions.Script(harness.Server, sessions.Length > 0 ? sessions : [RemoteSessions.Session()]);
        return harness;
    }

    private static List<string> Events(PlaybackManager manager)
    {
        var names = new List<string>();
        manager.Event += name =>
        {
            lock (names)
            {
                names.Add(name);
            }
        };
        return names;
    }

    // --- targets -------------------------------------------------------------------------------------

    [Fact]
    public async Task The_targets_are_the_sessions_the_user_may_control_without_this_device()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var own = RemoteSessions.Session("5e5510000000000000000000000000ff", "This Browser", harness.Get<JellyfinApi>().DeviceId);
        RemoteSessions.Script(
            harness.Server,
            RemoteSessions.Session(RemoteSessions.TvId, "Living Room TV", deviceType: "tv"),
            own,
            RemoteSessions.Session(RemoteSessions.PhoneId, "A Phone", "phone-device", "Jellyfin Android", ["VolumeUp", "DisplayMessage"]));

        var targets = await harness.Manager.GetTargetsAsync();

        var request = Assert.Single(harness.Server.RequestsTo("/Sessions"));
        Assert.Equal(PlaybackServer.UserId, request.Query("ControllableByUserId"));

        // Sorted by name; the session of this device is not a target.
        Assert.Equal(["A Phone", "Living Room TV"], targets.Select(t => t.Name));
        var phone = targets[0];
        Assert.Equal(RemoteSessions.PhoneId, phone.Id);
        Assert.Equal(SessionPlayer.PlayerName, phone.PlayerName);
        Assert.Equal("Jellyfin Android", phone.AppName);
        Assert.Equal("tester", phone.UserName);
        Assert.False(phone.IsLocalPlayer);
        Assert.Equal(["Audio", "Video"], phone.PlayableMediaTypes);
        Assert.Equal(["VolumeUp", "DisplayMessage"], phone.SupportedCommands);
        Assert.Equal("tv", targets[1].DeviceType);
    }

    [Fact]
    public async Task Without_a_session_there_are_no_targets()
    {
        await using var harness = await PlaybackHarness.CreateAsync(signIn: false);
        Assert.Empty(await harness.Manager.GetTargetsAsync());
        Assert.Empty(harness.Server.RequestsTo("/Sessions"));
    }

    [Fact]
    public async Task Choosing_a_target_makes_the_session_player_the_current_player()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        var events = Events(manager);
        Assert.Null(manager.GetPlayerInfo());

        var target = await RemoteSessions.ConnectAsync(manager);

        Assert.IsType<SessionPlayer>(manager.CurrentPlayer);
        Assert.Same(target, manager.CurrentTarget);
        var info = manager.GetPlayerInfo();
        Assert.NotNull(info);
        Assert.False(info.IsLocalPlayer);
        Assert.Equal(SessionPlayer.PlayerName, info.Name);
        Assert.Equal(RemoteSessions.TvId, info.Id);
        Assert.Equal("Living Room TV", info.DeviceName);
        Assert.Equal(RemoteSessions.WebCommands, info.SupportedCommands);
        Assert.Equal(RemoteSessions.WebCommands, manager.GetSupportedCommands());
        Assert.Equal([PlaybackEvents.PlayerChange], events);

        // The watchdog of the "Sessions" subscription runs while the target is active.
        Assert.Contains(5000, harness.Timers.Intervals);

        // Choosing it again changes nothing.
        await manager.TrySetActivePlayerAsync(target.PlayerName, target);
        Assert.Equal([PlaybackEvents.PlayerChange], events);

        manager.SetDefaultPlayerActive();
        Assert.Null(manager.CurrentPlayer);
        Assert.Null(manager.CurrentTarget);
        Assert.Equal([PlaybackEvents.PlayerChange, PlaybackEvents.PlayerChange], events);
        Assert.DoesNotContain(5000, harness.Timers.Intervals);
    }

    // --- commands ------------------------------------------------------------------------------------

    [Fact]
    public async Task Playing_sends_the_ids_to_the_target()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);

        await manager.PlayAsync(new PlayOptions
        {
            Ids = [PlaybackServer.MovieId, PlaybackServer.SecondMovieId],
            StartPositionTicks = 50_000_000,
            StartIndex = 1,
            AudioStreamIndex = 3,
            SubtitleStreamIndex = 0,
            MediaSourceId = PlaybackServer.SecondMovieId,
        });

        var play = Assert.Single(RemoteSessions.Sent(harness.Server));
        Assert.EndsWith($"/Sessions/{RemoteSessions.TvId}/Playing", play.Path);
        Assert.Equal($"{PlaybackServer.MovieId},{PlaybackServer.SecondMovieId}", play.Query("ItemIds"));
        Assert.Equal("PlayNow", play.Query("PlayCommand"));
        Assert.Equal("50000000", play.Query("StartPositionTicks"));
        Assert.Equal("1", play.Query("StartIndex"));
        Assert.Equal("3", play.Query("AudioStreamIndex"));
        Assert.Equal("0", play.Query("SubtitleStreamIndex"));
        Assert.Equal(PlaybackServer.SecondMovieId, play.Query("MediaSourceId"));

        // Nothing plays here.
        Assert.Empty(harness.Player.Loads);
        Assert.Empty(harness.StartReports);
    }

    [Fact]
    public async Task Queueing_shuffling_and_mixes_are_play_commands_of_the_target()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        var songs = PlaybackServer.Songs();

        await manager.PlayAsync(new PlayOptions { Items = [songs[0]] });
        await manager.QueueAsync(new PlayOptions { Items = [songs[1]] });
        await manager.QueueNextAsync(new PlayOptions { Items = [songs[2]] });
        await manager.ShuffleAsync(songs[0]);
        await manager.InstantMixAsync(songs[1]);

        var sent = RemoteSessions.Sent(harness.Server);
        Assert.Equal(["PlayNow", "PlayLast", "PlayNext", "PlayShuffle", "PlayInstantMix"], sent.Select(r => r.Query("PlayCommand")));
        Assert.Equal(
            [PlaybackServer.SongId, PlaybackServer.SecondSongId, PlaybackServer.ThirdSongId, PlaybackServer.SongId, PlaybackServer.SecondSongId],
            sent.Select(r => r.Query("ItemIds")));
        Assert.All(sent, r => Assert.Null(r.Query("StartPositionTicks")));
    }

    [Fact]
    public async Task The_transport_controls_are_play_state_commands_of_the_target()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 100_000_000));

        await manager.PlayPauseAsync();
        await manager.PauseAsync();
        await manager.UnpauseAsync();
        await manager.SeekAsync(300_000_000);
        await manager.NextTrackAsync();
        await manager.PreviousTrackAsync();
        await manager.StopAsync();

        await PlaybackHarness.Eventually(() => RemoteSessions.PlayStateCommands(harness.Server).Count == 7, "the seven commands");
        Assert.Equal(
            ["PlayPause", "Pause", "Unpause", "seek?SeekPositionTicks=300000000", "nextTrack", "previousTrack", "stop"],
            RemoteSessions.PlayStateCommands(harness.Server));

        // The session keeps playing for all the manager knows: its state comes from the server.
        Assert.IsType<SessionPlayer>(manager.CurrentPlayer);
    }

    [Fact]
    public async Task Volume_modes_and_tracks_are_general_commands_of_the_target()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);

        manager.SetVolume(40);
        manager.VolumeUp();
        manager.VolumeDown();
        manager.ToggleMute();
        manager.SetMute(true);
        manager.SetMute(false);
        manager.SetRepeatMode(PlayQueue.RepeatAll);
        manager.SetQueueShuffleMode(PlayQueue.Shuffled);
        await manager.SetAudioStreamIndexAsync(3);
        await manager.SetSubtitleStreamIndexAsync(-1);
        manager.ToggleFullscreen();
        await manager.SendCommandAsync("MoveUp");
        await manager.SendCommandAsync("DisplayMessage", CommandArguments.Of(("Header", "Hello"), ("Text", "Dinner is ready")));
        await manager.DisplayContentAsync("Test Movie", PlaybackServer.MovieId, "Movie");

        await PlaybackHarness.Eventually(() => RemoteSessions.Commands(harness.Server).Count == 14, "the fourteen commands");
        Assert.Equal(
            [
                "SetVolume {\"Volume\":40}",
                "VolumeUp",
                "VolumeDown",
                "ToggleMute",
                "Mute",
                "Unmute",
                "SetRepeatMode {\"RepeatMode\":\"RepeatAll\"}",
                "SetShuffleQueue {\"ShuffleMode\":\"Shuffle\"}",
                "SetAudioStreamIndex {\"Index\":3}",
                "SetSubtitleStreamIndex {\"Index\":-1}",
                "ToggleFullscreen",
                "MoveUp",
                "DisplayMessage {\"Header\":\"Hello\",\"Text\":\"Dinner is ready\"}",
                $"DisplayContent {{\"ItemName\":\"Test Movie\",\"ItemId\":\"{PlaybackServer.MovieId}\",\"ItemType\":\"Movie\"}}",
            ],
            RemoteSessions.Commands(harness.Server));
    }

    [Fact]
    public async Task Commands_that_only_a_remote_target_knows_do_nothing_here()
    {
        await using var harness = await WithSessionsAsync();
        await harness.PlayMovieAsync();
        var requests = harness.Server.Requests.Count;

        await harness.Manager.SendCommandAsync("MoveUp");
        await harness.Manager.DisplayContentAsync("Test Movie", PlaybackServer.MovieId, "Movie");
        await harness.Manager.EndSessionAsync();

        Assert.Equal(requests, harness.Server.Requests.Count);

        // The ones with a meaning for every player work as before.
        await harness.Manager.SendCommandAsync("SetVolume", CommandArguments.Of(("Volume", 30)));
        Assert.Equal(30, harness.Manager.Volume);
        await harness.Manager.SendCommandAsync("ToggleMute");
        Assert.True(harness.Manager.IsMuted);
    }

    // --- state ---------------------------------------------------------------------------------------

    [Fact]
    public async Task The_state_of_the_target_is_what_the_manager_shows()
    {
        await using var harness = await WithSessionsAsync(admin: true);
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        var events = Events(manager);
        Assert.False(manager.IsPlaying);
        Assert.Null(manager.CurrentItem);

        RemoteSessions.Dispatch(
            harness.Services,
            RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 120_000_000, volume: 60, repeatMode: "RepeatAll", playbackOrder: "Shuffle"));

        Assert.Equal([PlaybackEvents.StateChange], events);
        Assert.True(manager.IsPlaying);
        Assert.True(manager.IsPlayingMediaType("Video"));
        Assert.False(manager.IsPlayingMediaType("Audio"));
        Assert.False(manager.IsPlayingLocally);
        Assert.Equal("Test Movie", manager.CurrentItem?.Name);
        Assert.Equal(TestApp.ServerId, manager.CurrentItem?.ServerId);
        Assert.Equal(120_000_000, manager.CurrentTimeTicks);
        Assert.Equal(PlaybackServer.MovieTicks, manager.DurationTicks);
        Assert.True(manager.CanSeek());
        Assert.False(manager.IsPaused);
        Assert.Equal(60, manager.Volume);
        Assert.False(manager.IsMuted);
        Assert.Equal(PlayQueue.RepeatAll, manager.RepeatMode);
        Assert.Equal(PlayQueue.Shuffled, manager.ShuffleMode);
        var state = manager.GetPlayerState();
        Assert.NotNull(state);
        Assert.Equal(120_000_000, state.PositionTicks);
        Assert.Equal("Test Movie", state.NowPlayingItem?.Name);

        // Position, pause, volume and repeat mode have events of their own.
        events.Clear();
        RemoteSessions.Dispatch(
            harness.Services,
            RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 130_000_000, paused: true, volume: 20, muted: true, repeatMode: "RepeatOne", playbackOrder: "Shuffle"));
        Assert.Equal(
            [PlaybackEvents.TimeUpdate, PlaybackEvents.Pause, PlaybackEvents.VolumeChange, PlaybackEvents.RepeatModeChange],
            events.Where(e => e != PlaybackEvents.StateChange));
        Assert.True(manager.IsPaused);
        Assert.Equal(130_000_000, manager.CurrentTimeTicks);
        Assert.Equal(20, manager.Volume);
        Assert.True(manager.IsMuted);
        Assert.Equal(PlayQueue.RepeatOne, manager.RepeatMode);

        // None of this is reported to the server: the target reports its own playback.
        Assert.Empty(harness.StartReports);
        Assert.Empty(harness.ProgressReports);
    }

    [Fact]
    public async Task The_queue_of_the_target_is_loaded_when_it_changes()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        var songs = PlaybackServer.Songs();

        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(songs[1], queue: songs));
        await PlaybackHarness.Eventually(() => manager.GetPlaylist().Count == 3, "the queue");

        Assert.Equal(["Opening", "Middle", "Closing"], manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal(["playlistItem0", "playlistItem1", "playlistItem2"], manager.GetPlaylist().Select(i => i.PlaylistItemId));
        Assert.Equal("playlistItem1", manager.CurrentPlaylistItemId);
        Assert.Equal(1, manager.CurrentPlaylistIndex);
        Assert.Equal("Middle", manager.CurrentItem?.Name);

        // The same queue again (with another position) does not load the items again.
        var loads = QueueLoads(harness);
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(songs[2], positionTicks: 10_000_000, queue: songs));
        await PlaybackHarness.Eventually(() => manager.CurrentPlaylistIndex == 2, "the next song");
        Assert.Equal(loads, QueueLoads(harness));

        // Another queue does.
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(songs[0], queue: [songs[0], songs[2]]));
        await PlaybackHarness.Eventually(() => manager.GetPlaylist().Count == 2, "the shorter queue");
        Assert.Equal(["Opening", "Closing"], manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal(loads + 1, QueueLoads(harness));
    }

    [Fact]
    public async Task Queue_entries_of_the_target_are_chosen_and_moved_by_sending_the_queue_again()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        var songs = PlaybackServer.Songs();
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(songs[0], queue: songs));
        await PlaybackHarness.Eventually(() => manager.GetPlaylist().Count == 3, "the queue");

        await manager.SetCurrentPlaylistItemAsync("playlistItem2");
        var choose = RemoteSessions.Sent(harness.Server)[^1];
        Assert.Equal("PlayNow", choose.Query("PlayCommand"));
        Assert.Equal($"{PlaybackServer.SongId},{PlaybackServer.SecondSongId},{PlaybackServer.ThirdSongId}", choose.Query("ItemIds"));
        Assert.Equal("2", choose.Query("StartIndex"));

        // The last song moves to the front; the playing one (the first) stays the current one.
        var before = RemoteSessions.Sent(harness.Server).Count;
        manager.MovePlaylistItem("playlistItem2", 0);
        await PlaybackHarness.Eventually(() => RemoteSessions.Sent(harness.Server).Count == before + 1, "the reordered queue");
        var move = RemoteSessions.Sent(harness.Server)[^1];
        Assert.Equal($"{PlaybackServer.ThirdSongId},{PlaybackServer.SongId},{PlaybackServer.SecondSongId}", move.Query("ItemIds"));
        Assert.Equal("1", move.Query("StartIndex"));
    }

    [Fact]
    public async Task A_target_that_is_gone_gives_playback_back_to_this_device()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Playing(PlaybackServer.Movie()));
        var events = Events(manager);

        // The other session of the server is still there; the target is not.
        RemoteSessions.Dispatch(harness.Services, RemoteSessions.Session(RemoteSessions.PhoneId, "A Phone", "phone-device"));

        Assert.Null(manager.CurrentPlayer);
        Assert.Null(manager.CurrentTarget);
        Assert.False(manager.IsPlaying);
        Assert.Equal([PlaybackEvents.PlayerChange], events);
    }

    [Theory]
    [InlineData("ServerShuttingDown")]
    [InlineData("ServerRestarting")]
    public async Task A_server_that_goes_away_ends_the_remote_control(string message)
    {
        await using var harness = await WithSessionsAsync();
        harness.Get<RemoteTargetWatcher>();
        await RemoteSessions.ConnectAsync(harness.Manager);

        harness.Get<ServerEvents>().Dispatch(message, RemoteSessions.Element(new JsonObject()));

        Assert.Null(harness.Manager.CurrentPlayer);
    }

    [Fact]
    public async Task What_plays_here_continues_on_the_target_that_is_chosen()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await harness.PlayMoviesAsync();
        harness.Player.Tick("video", 12);

        await RemoteSessions.ConnectAsync(manager);
        await PlaybackHarness.Eventually(() => RemoteSessions.Sent(harness.Server).Count == 1, "the play command");

        // Stopped here (and reported as stopped at the position) ...
        Assert.Contains(harness.Player.Calls, c => c.StartsWith("stop video", StringComparison.Ordinal));
        await PlaybackHarness.Eventually(() => harness.StopReports.Count == 1, "the stop report");
        Assert.Equal(120_000_000, harness.StopReports[0]["PositionTicks"]!.GetValue<long>());

        // ... and started there, with the whole queue.
        var play = RemoteSessions.Sent(harness.Server)[0];
        Assert.Equal($"{PlaybackServer.MovieId},{PlaybackServer.SecondMovieId}", play.Query("ItemIds"));
        Assert.Equal("PlayNow", play.Query("PlayCommand"));
        Assert.Equal("120000000", play.Query("StartPositionTicks"));
        Assert.Equal("0", play.Query("StartIndex"));
        Assert.IsType<SessionPlayer>(manager.CurrentPlayer);

        // The second movie did not start here when the first one was stopped.
        Assert.Single(harness.Player.Loads);
    }

    // --- returning to a target, display mirroring ----------------------------------------------------

    [Fact]
    public async Task The_remembered_target_becomes_active_again_after_signing_in()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        await RemoteSessions.ConnectAsync(manager);
        Assert.False(manager.IsAutoCastEnabled);

        manager.SetAutoCast(true);
        Assert.Equal(RemoteSessions.TvId, harness.App.Browser.LocalStorage["autocastPlayerId"]);
        Assert.True(manager.IsAutoCastEnabled);

        // Without the target the setting reads as off, but the target is kept.
        manager.SetDefaultPlayerActive();
        Assert.False(manager.IsAutoCastEnabled);

        await harness.Get<RemoteTargetWatcher>().RestoreTargetAsync();
        Assert.IsType<SessionPlayer>(manager.CurrentPlayer);
        Assert.Equal(RemoteSessions.TvId, manager.CurrentTarget?.Id);
        Assert.True(manager.IsAutoCastEnabled);

        manager.SetAutoCast(false);
        Assert.False(harness.App.Browser.LocalStorage.ContainsKey("autocastPlayerId"));
        manager.SetDefaultPlayerActive();
        await harness.Get<RemoteTargetWatcher>().RestoreTargetAsync();
        Assert.Null(manager.CurrentPlayer);
    }

    [Fact]
    public async Task A_remembered_target_that_is_not_there_is_ignored()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync(prepare: app => app.Browser.LocalStorage["autocastPlayerId"] = RemoteSessions.PhoneId);
        RemoteSessions.Script(harness.Server, RemoteSessions.Session());

        await harness.Get<RemoteTargetWatcher>().RestoreTargetAsync();

        Assert.Single(harness.Server.RequestsTo("/Sessions"));
        Assert.Null(harness.Manager.CurrentPlayer);
        Assert.Equal(RemoteSessions.PhoneId, harness.App.Browser.LocalStorage["autocastPlayerId"]);
    }

    [Fact]
    public async Task The_item_whose_page_opens_is_shown_on_the_target()
    {
        await using var harness = await WithSessionsAsync();
        var manager = harness.Manager;
        var watcher = harness.Get<RemoteTargetWatcher>();
        var details = AppLocation.Parse($"#/details?id={PlaybackServer.MovieId}&serverId={TestApp.ServerId}");

        // Without a remote target nothing is asked of anybody.
        await watcher.MirrorAsync(details);
        Assert.Empty(harness.Server.RequestsTo($"/Items/{PlaybackServer.MovieId}"));

        await RemoteSessions.ConnectAsync(manager);
        Assert.True(manager.EnableDisplayMirroring);
        await watcher.MirrorAsync(details);
        Assert.Equal(
            [$"DisplayContent {{\"ItemName\":\"Test Movie\",\"ItemId\":\"{PlaybackServer.MovieId}\",\"ItemType\":\"Movie\"}}"],
            RemoteSessions.Commands(harness.Server));

        // Pages without an item are not mirrored.
        await watcher.MirrorAsync(AppLocation.Parse("#/home"));
        await watcher.MirrorAsync(AppLocation.Parse("#/list?parentId=abc&serverId=" + TestApp.ServerId));
        Assert.Single(RemoteSessions.Commands(harness.Server));

        // The switch of the cast menu.
        manager.EnableDisplayMirroring = false;
        await watcher.MirrorAsync(details);
        Assert.Single(RemoteSessions.Commands(harness.Server));
        manager.EnableDisplayMirroring = true;

        // The router is what the watcher listens to.
        var router = harness.Get<AppRouter>();
        router.Start();
        router.ShowItem(PlaybackServer.SecondMovieId, TestApp.ServerId);
        await PlaybackHarness.Eventually(() => RemoteSessions.Commands(harness.Server).Count == 2, "the mirrored page");
        Assert.Contains("Second Movie", RemoteSessions.Commands(harness.Server)[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_target_that_cannot_display_content_is_not_asked_to()
    {
        await using var harness = await WithSessionsAsync(sessions: RemoteSessions.Session(commands: ["VolumeUp", "VolumeDown"]));
        await RemoteSessions.ConnectAsync(harness.Manager);

        await harness.Get<RemoteTargetWatcher>().MirrorAsync(AppLocation.Parse($"#/details?id={PlaybackServer.MovieId}&serverId={TestApp.ServerId}"));

        Assert.Empty(RemoteSessions.Commands(harness.Server));
    }

    // --- the changes a "Sessions" message stands for -------------------------------------------------

    [Fact]
    public void The_first_state_and_a_change_of_transcoding_are_state_changes()
    {
        var session = RemoteSessions.Playing(PlaybackServer.Movie());
        Assert.Equal([PlaybackEvents.StateChange], SessionPlayer.GetChangedEvents(null, session));

        var transcoding = (JsonObject)session.DeepClone();
        transcoding["TranscodingInfo"] = new JsonObject { ["IsVideoDirect"] = false };
        Assert.Equal([PlaybackEvents.StateChange], SessionPlayer.GetChangedEvents(session, transcoding));
        Assert.Equal([PlaybackEvents.StateChange], SessionPlayer.GetChangedEvents(transcoding, session));
    }

    [Fact]
    public void Position_pause_volume_and_repeat_mode_have_events_of_their_own()
    {
        var before = RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10);

        Assert.Empty(SessionPlayer.GetChangedEvents(before, (JsonObject)before.DeepClone()));
        Assert.Equal([PlaybackEvents.TimeUpdate], SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 20)));
        Assert.Equal([PlaybackEvents.Pause], SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10, paused: true)));
        Assert.Equal(
            [PlaybackEvents.Unpause],
            SessionPlayer.GetChangedEvents(RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10, paused: true), before));
        Assert.Equal([PlaybackEvents.VolumeChange], SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10, volume: 50)));
        Assert.Equal([PlaybackEvents.VolumeChange], SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10, muted: true)));
        Assert.Equal(
            [PlaybackEvents.RepeatModeChange],
            SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10, repeatMode: "RepeatAll")));

        // What changes with every message is not a change.
        var later = (JsonObject)before.DeepClone();
        later["LastActivityDate"] = "2026-01-01T10:00:05.0000000Z";
        later["LastPlaybackCheckIn"] = "2026-01-01T10:00:05.0000000Z";
        Assert.Empty(SessionPlayer.GetChangedEvents(before, later));
    }

    [Fact]
    public void Another_item_or_another_track_is_a_state_change()
    {
        var before = RemoteSessions.Playing(PlaybackServer.Movie(), positionTicks: 10);

        Assert.Equal(
            [PlaybackEvents.StateChange],
            SessionPlayer.GetChangedEvents(before, RemoteSessions.Playing(PlaybackServer.Movie(PlaybackServer.SecondMovieId, "Second Movie"), positionTicks: 10)));

        var track = (JsonObject)before.DeepClone();
        track["PlayState"]!["AudioStreamIndex"] = 3;
        Assert.Equal([PlaybackEvents.StateChange], SessionPlayer.GetChangedEvents(before, track));

        var stopped = RemoteSessions.Session();
        Assert.Equal([PlaybackEvents.StateChange], SessionPlayer.GetChangedEvents(before, stopped));
    }

    [Fact]
    public void The_images_of_an_older_server_are_mapped_to_the_fields_of_an_item()
    {
        var session = new JsonObject
        {
            ["NowPlayingItem"] = new JsonObject
            {
                ["Id"] = PlaybackServer.MovieId,
                ["PrimaryImageTag"] = "primary-tag",
                ["BackdropImageTag"] = "backdrop-tag",
                ["BackdropItemId"] = PlaybackServer.MovieId,
            },
        };
        SessionPlayer.NormalizeImages(session, TestApp.ServerId);
        var item = session["NowPlayingItem"]!.AsObject();
        Assert.Equal("primary-tag", item["ImageTags"]!["Primary"]!.GetValue<string>());
        Assert.Equal("backdrop-tag", item["BackdropImageTags"]![0]!.GetValue<string>());
        Assert.Equal(TestApp.ServerId, item["ServerId"]!.GetValue<string>());

        // The backdrop of a parent (an episode shows the one of its series).
        var episode = new JsonObject
        {
            ["NowPlayingItem"] = new JsonObject
            {
                ["Id"] = PlaybackServer.MovieId,
                ["ImageTags"] = new JsonObject { ["Primary"] = "own-tag" },
                ["PrimaryImageTag"] = "older-tag",
                ["BackdropImageTag"] = "series-tag",
                ["BackdropItemId"] = PlaybackServer.SecondMovieId,
            },
        };
        SessionPlayer.NormalizeImages(episode, null);
        item = episode["NowPlayingItem"]!.AsObject();
        Assert.Equal("own-tag", item["ImageTags"]!["Primary"]!.GetValue<string>());
        Assert.Equal("series-tag", item["ParentBackdropImageTags"]![0]!.GetValue<string>());
        Assert.Equal(PlaybackServer.SecondMovieId, item["ParentBackdropItemId"]!.GetValue<string>());
        Assert.Null(item["BackdropImageTags"]);
        Assert.Null(item["ServerId"]);

        // A session that plays nothing is left alone.
        var idle = RemoteSessions.Session();
        SessionPlayer.NormalizeImages(idle, TestApp.ServerId);
        Assert.Null(idle["NowPlayingItem"]);
    }

    /// <summary>The requests that loaded the items of a remote queue.</summary>
    private static int QueueLoads(PlaybackHarness harness)
    {
        lock (harness.Server.Requests)
        {
            return harness.Server.Requests.Count(r => r.Method == HttpMethod.Get && !string.IsNullOrEmpty(r.Query("ids")));
        }
    }
}
