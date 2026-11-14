using System.Text.Json.Nodes;
using Jellyfin.Web.Components;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Jellyfin.Web.Tests.PlaybackExtras;

/// <summary>
/// The Chromecast player against a fake Cast SDK: when the target exists, pairing, the messages the
/// commands become (with the connection the receiver needs) and the receiver's state mirrored into
/// the manager.
/// </summary>
public class CastPlayerTests
{
    private const string ReceiverId = "F007D354";

    /// <summary>A signed-in app whose Cast SDK is available, initialised for the user's receiver application.</summary>
    private static async Task<PlaybackHarness> WithCastAsync(bool receivers = true, Action<TestApp>? prepare = null)
    {
        var harness = await PlaybackHarness.WithMediaAsync(prepare: prepare);
        ScriptUser(harness.Server);
        harness.Fakes.Cast.SdkAvailable = true;
        _ = harness.Manager;
        await harness.Get<CastPlayer>().LoadAsync();
        harness.Fakes.Cast.ReportReceivers(receivers);
        return harness;
    }

    private static void ScriptUser(FakeHttp server, string? receiverId = ReceiverId) =>
        server.OnGet($"/Users/{PlaybackServer.UserId}", new JsonObject
        {
            ["Id"] = PlaybackServer.UserId,
            ["Name"] = "tester",
            ["Configuration"] = new JsonObject { ["CastReceiverId"] = receiverId },
        }.ToJsonString());

    private static async Task<PlaybackHarness> ConnectedAsync(Action<TestApp>? prepare = null)
    {
        var harness = await WithCastAsync(prepare: prepare);
        var target = Assert.Single(await harness.Manager.GetTargetsAsync(), t => t.PlayerName == CastPlayer.PlayerName);
        await harness.Manager.TrySetActivePlayerAsync(target.PlayerName, target);
        Assert.IsType<CastPlayer>(harness.Manager.CurrentPlayer);
        harness.Fakes.Cast.Messages.Clear();
        return harness;
    }

    private static List<string> Events(PlaybackManager manager)
    {
        var names = new List<string>();
        manager.Event += names.Add;
        return names;
    }

    /// <summary>What the receiver (jellyfin-chromecast) sends as the data of its state messages.</summary>
    private static JsonObject ReceiverState(
        long positionTicks = 0,
        bool paused = false,
        int volume = 70,
        bool muted = false,
        string repeatMode = "RepeatNone",
        string? itemId = PlaybackServer.MovieId) => new()
    {
        ["ItemId"] = itemId,
        ["PlayState"] = new JsonObject
        {
            ["PositionTicks"] = positionTicks,
            ["IsPaused"] = paused,
            ["IsMuted"] = muted,
            ["VolumeLevel"] = volume,
            ["RepeatMode"] = repeatMode,
            ["CanSeek"] = true,
            ["AudioStreamIndex"] = 2,
            ["SubtitleStreamIndex"] = -1,
            ["PlayMethod"] = "DirectStream",
            ["MediaSourceId"] = PlaybackServer.MovieId,
        },
        ["NowPlayingItem"] = new JsonObject
        {
            ["Id"] = itemId,
            ["Name"] = "Test Movie",
            ["Type"] = "Movie",
            ["MediaType"] = "Video",
            ["RunTimeTicks"] = PlaybackServer.MovieTicks,
            ["PrimaryImageTag"] = "primary-tag",
            ["BackdropImageTag"] = "backdrop-tag",
            ["BackdropItemId"] = itemId,
            ["MediaStreams"] = new JsonArray(
                new JsonObject { ["Index"] = 2, ["Type"] = "Audio", ["DisplayTitle"] = "English" },
                new JsonObject { ["Index"] = 3, ["Type"] = "Audio", ["DisplayTitle"] = "German" },
                new JsonObject { ["Index"] = 0, ["Type"] = "Subtitle", ["DisplayTitle"] = "English - SUBRIP" }),
        },
    };

    // --- availability -----------------------------------------------------------------------------------

    [Fact]
    public async Task Without_the_sdk_there_is_no_cast_target()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        ScriptUser(harness.Server);
        var cast = harness.Get<CastPlayer>();

        await cast.LoadAsync();
        harness.Fakes.Cast.ReportReceivers(true);

        Assert.False(cast.IsAvailable);
        Assert.Empty(harness.Fakes.Cast.Initialized);
        Assert.Empty(harness.Server.RequestsTo($"/Users/{PlaybackServer.UserId}"));
        Assert.DoesNotContain(await harness.Manager.GetTargetsAsync(), t => t.PlayerName == CastPlayer.PlayerName);
    }

    [Fact]
    public async Task The_sdk_is_only_loaded_in_chromium_browsers()
    {
        await using var firefox = await PlaybackHarness.CreateAsync(
            app => app.Browser.Environment = app.Browser.Environment with { UserAgent = FakeCapabilityProbe.FirefoxUserAgent });
        await firefox.Get<CastPlayer>().StartAsync();
        Assert.Equal(0, firefox.Fakes.Cast.Loads);

        await using var chrome = await PlaybackHarness.CreateAsync();
        await chrome.Get<CastPlayer>().StartAsync();
        Assert.Equal(1, chrome.Fakes.Cast.Loads);
    }

    [Fact]
    public async Task The_api_is_initialised_with_the_users_receiver_application_also_after_each_sign_in()
    {
        await using var harness = await WithCastAsync(receivers: false);
        var cast = harness.Get<CastPlayer>();

        Assert.True(cast.IsAvailable);
        Assert.True(cast.IsInitialized);
        Assert.Equal([ReceiverId], harness.Fakes.Cast.Initialized);
        Assert.Empty(await cast.GetTargetsAsync());

        // Signing in (again) initialises again, like the original's localusersignedin handler.
        ScriptUser(harness.Server, "ABCDEF12");
        harness.Server.OnPost("/Sessions/Capabilities/Full");
        await harness.Get<ServerConnections>().OnAuthenticatedAsync(new Jellyfin.Sdk.Generated.Models.AuthenticationResult
        {
            User = new Jellyfin.Sdk.Generated.Models.UserDto { Id = TestApp.UserId, Name = "tester", ServerId = TestApp.ServerId },
            AccessToken = "test-token",
            ServerId = TestApp.ServerId,
        });
        Assert.Equal([ReceiverId, "ABCDEF12"], harness.Fakes.Cast.Initialized);
    }

    [Fact]
    public async Task Without_a_receiver_application_the_api_is_not_initialised()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        ScriptUser(harness.Server, receiverId: null);
        harness.Fakes.Cast.SdkAvailable = true;
        var cast = harness.Get<CastPlayer>();

        await cast.LoadAsync();

        Assert.True(cast.IsAvailable);
        Assert.False(cast.IsInitialized);
        Assert.Empty(harness.Fakes.Cast.Initialized);
    }

    [Fact]
    public async Task Available_receivers_add_the_cast_target()
    {
        await using var harness = await WithCastAsync();

        var target = Assert.Single(await harness.Manager.GetTargetsAsync());
        Assert.Equal(CastPlayer.PlayerName, target.Name);
        Assert.Equal(CastPlayer.PlayerName, target.Id);
        Assert.Equal(CastPlayer.PlayerName, target.PlayerName);
        Assert.Equal(CastPlayer.PlayerName, target.AppName);
        Assert.Equal("cast", target.DeviceType);
        Assert.False(target.IsLocalPlayer);
        Assert.Equal(["Audio", "Video"], target.PlayableMediaTypes);
        Assert.Equal(
            ["VolumeUp", "VolumeDown", "Mute", "Unmute", "ToggleMute", "SetVolume", "SetAudioStreamIndex", "SetSubtitleStreamIndex", "DisplayContent", "SetRepeatMode"],
            target.SupportedCommands);

        harness.Fakes.Cast.ReportReceivers(false);
        Assert.Empty(await harness.Manager.GetTargetsAsync());
    }

    // --- pairing -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Pairing_opens_a_session_and_asks_the_receiver_to_identify()
    {
        await using var harness = await WithCastAsync();
        var manager = harness.Manager;
        var events = Events(manager);
        var target = Assert.Single(await manager.GetTargetsAsync());

        await manager.TrySetActivePlayerAsync(target.PlayerName, target);

        Assert.Equal(1, harness.Fakes.Cast.SessionRequests);
        Assert.IsType<CastPlayer>(manager.CurrentPlayer);
        Assert.Equal(CastPlayer.DeviceStates.Active, harness.Get<CastPlayer>().DeviceState);
        var info = manager.GetPlayerInfo();
        Assert.NotNull(info);
        Assert.False(info.IsLocalPlayer);
        Assert.Equal(CastPlayer.PlayerName, info.Name);
        Assert.Equal(CastPlayer.SupportedCommands, manager.GetSupportedCommands());
        Assert.Equal([PlaybackEvents.PlayerChange], events);

        // The receiver's name is known once connected.
        Assert.Equal("Living Room Chromecast", Assert.Single(await manager.GetTargetsAsync()).DeviceName);

        var identify = Assert.Single(harness.Fakes.Cast.Messages);
        Assert.Equal(
            ["options", "command", "userId", "deviceId", "accessToken", "serverAddress", "serverId", "serverVersion", "receiverName"],
            identify.Select(p => p.Key));
        Assert.Equal("Identify", identify["command"]!.GetValue<string>());
        Assert.Empty(identify["options"]!.AsObject());
        Assert.Equal(PlaybackServer.UserId, identify["userId"]!.GetValue<string>());
        Assert.Equal(harness.Get<JellyfinApi>().DeviceId, identify["deviceId"]!.GetValue<string>());
        Assert.Equal("test-token", identify["accessToken"]!.GetValue<string>());
        Assert.Equal(TestApp.ServerUrl, identify["serverAddress"]!.GetValue<string>());
        Assert.Equal(TestApp.ServerId, identify["serverId"]!.GetValue<string>());
        Assert.Equal("10.11.0", identify["serverVersion"]!.GetValue<string>());
        Assert.Equal("Living Room Chromecast", identify["receiverName"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_cancelled_device_picker_keeps_playback_here()
    {
        await using var harness = await WithCastAsync();
        harness.Fakes.Cast.SessionToGrant = null;
        var target = Assert.Single(await harness.Manager.GetTargetsAsync());

        await harness.Manager.TrySetActivePlayerAsync(target.PlayerName, target);

        Assert.Null(harness.Manager.CurrentPlayer);
        Assert.Equal(CastPlayer.DeviceStates.Error, harness.Get<CastPlayer>().DeviceState);
        Assert.Empty(harness.Fakes.Cast.Messages);
    }

    [Fact]
    public async Task Pairing_needs_an_initialised_api()
    {
        await using var harness = await WithCastAsync();
        harness.Fakes.Cast.InitializeResult = false;
        await harness.Get<CastPlayer>().InitializeChromecastAsync();
        var target = harness.Get<CastPlayer>().GetCurrentTargetInfo();

        await harness.Manager.TrySetActivePlayerAsync(target.PlayerName, target);

        Assert.Equal(0, harness.Fakes.Cast.SessionRequests);
        Assert.Null(harness.Manager.CurrentPlayer);
    }

    [Fact]
    public async Task A_joined_session_makes_the_cast_player_active_and_an_ended_one_returns_here()
    {
        await using var harness = await WithCastAsync();
        var manager = harness.Manager;
        var events = Events(manager);

        // sessionListener: a session that survived a page reload.
        harness.Fakes.Cast.JoinSession(FakeCastInterop.Receiver("Kitchen"));

        Assert.IsType<CastPlayer>(manager.CurrentPlayer);
        Assert.Equal("Kitchen", manager.GetPlayerInfo()?.DeviceName);
        Assert.Equal(0, harness.Fakes.Cast.SessionRequests);
        Assert.Equal(["Identify"], harness.Fakes.Cast.Commands);
        Assert.Equal([PlaybackEvents.PlayerChange], events);

        harness.Fakes.Cast.EndSession();

        Assert.Null(manager.CurrentPlayer);
        Assert.Equal(CastPlayer.DeviceStates.Idle, harness.Get<CastPlayer>().DeviceState);
        Assert.Equal([PlaybackEvents.PlayerChange, PlaybackEvents.PlayerChange], events);
    }

    // --- commands ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Playing_loads_the_items_and_sends_them_as_stubs_with_the_playback_settings()
    {
        await using var harness = await ConnectedAsync();
        harness.Get<AppSettings>().Set("chromecastBitrate1", "8000000");
        harness.Get<AppSettings>().Set("subtitleburnin", "onlyimageformats");

        await harness.Manager.PlayAsync(new PlayOptions
        {
            Ids = [PlaybackServer.SecondMovieId, PlaybackServer.MovieId],
            StartPositionTicks = 50_000_000,
            AudioStreamIndex = 3,
            SubtitleStreamIndex = 0,
            MediaSourceId = PlaybackServer.SecondMovieId,
        });

        // The items come from /Items with the original's limits; nothing is played here.
        var request = Assert.Single(harness.Server.RequestsTo("/Items"));
        Assert.Equal("100", request.Query("limit"));
        Assert.Equal("Virtual", request.Query("excludeLocationTypes"));
        Assert.Empty(harness.Player.Loads);
        Assert.Empty(harness.Server.RequestsTo("/PlaybackInfo"));

        var message = harness.Fakes.Cast.LastMessage("PlayNow");
        var options = message["options"]!.AsObject();
        Assert.Equal(50_000_000, options["startPositionTicks"]!.GetValue<long>());
        Assert.Equal(3, options["audioStreamIndex"]!.GetValue<int>());
        Assert.Equal(0, options["subtitleStreamIndex"]!.GetValue<int>());
        Assert.Equal(PlaybackServer.SecondMovieId, options["mediaSourceId"]!.GetValue<string>());
        Assert.Equal(TestApp.ServerId, options["serverId"]!.GetValue<string>());

        // Sorted like the requested ids, reduced to the fields the receiver needs.
        var items = options["items"]!.AsArray().Select(i => i!.AsObject()).ToList();
        Assert.Equal([PlaybackServer.SecondMovieId, PlaybackServer.MovieId], items.Select(i => i["Id"]!.GetValue<string>()));
        Assert.Equal(["Id", "ServerId", "Name", "Type", "MediaType", "IsFolder"], items[0].Select(p => p.Key));
        Assert.Equal("Second Movie", items[0]["Name"]!.GetValue<string>());
        Assert.Equal("Movie", items[0]["Type"]!.GetValue<string>());
        Assert.Equal("Video", items[0]["MediaType"]!.GetValue<string>());

        Assert.Equal(8_000_000, message["maxBitrate"]!.GetValue<int>());
        Assert.Equal("onlyimageformats", message["subtitleBurnIn"]!.GetValue<string>());
        Assert.Equal(-3, message["subtitleAppearance"]!["verticalPosition"]!.GetValue<int>());
        Assert.Equal("contain", message["subtitleAppearance"]!["aspectMode"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_single_id_is_loaded_as_one_item()
    {
        await using var harness = await ConnectedAsync();

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId] });

        Assert.Single(harness.Server.RequestsTo($"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}"));
        var items = harness.Fakes.Cast.LastMessage("PlayNow")["options"]!["items"]!.AsArray();
        Assert.Equal(PlaybackServer.MovieId, Assert.Single(items)!["Id"]!.GetValue<string>());

        // Messages without items carry no playback settings.
        await harness.Manager.PauseAsync();
        Assert.Null(harness.Fakes.Cast.LastMessage("Pause")["subtitleAppearance"]);
        Assert.NotNull(harness.Fakes.Cast.LastMessage("PlayNow")["subtitleAppearance"]);
    }

    [Fact]
    public async Task Queueing_shuffle_and_instant_mix_are_their_own_commands()
    {
        await using var harness = await ConnectedAsync();
        var manager = harness.Manager;
        var song = PlaybackServer.Songs()[0];

        await manager.QueueAsync(new PlayOptions { Items = [song] });
        await manager.QueueNextAsync(new PlayOptions { Items = [song] });
        await manager.ShuffleAsync(song);
        await manager.InstantMixAsync(song);

        Assert.Equal(["PlayLast", "PlayNext", "Shuffle", "InstantMix"], harness.Fakes.Cast.Commands);
        Assert.All(harness.Fakes.Cast.Messages, m =>
            Assert.Equal(PlaybackServer.SongId, m["options"]!["items"]![0]!["Id"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Transport_tracks_mute_and_repeat_are_messages()
    {
        await using var harness = await ConnectedAsync();
        var manager = harness.Manager;
        harness.Fakes.Cast.Receive("playbackstart", ReceiverState());
        harness.Fakes.Cast.Messages.Clear();

        await manager.PauseAsync();
        await manager.UnpauseAsync();
        await manager.PlayPauseAsync();
        await manager.SeekAsync(150_000_000);
        await manager.NextTrackAsync();
        await manager.PreviousTrackAsync();
        await manager.SetAudioStreamIndexAsync(3);
        await manager.SetSubtitleStreamIndexAsync(-1);
        manager.SetMute(true);
        manager.SetMute(false);
        manager.ToggleMute();
        manager.SetRepeatMode("RepeatAll");
        manager.SetQueueShuffleMode(PlayQueue.Shuffled);
        await manager.StopAsync();

        Assert.Equal(
            ["Pause", "Unpause", "PlayPause", "Seek", "NextTrack", "PreviousTrack", "SetAudioStreamIndex", "SetSubtitleStreamIndex",
                "Mute", "Unmute", "ToggleMute", "SetRepeatMode", "Stop"],
            harness.Fakes.Cast.Commands);
        var messages = harness.Fakes.Cast.Messages;
        Assert.Equal(15, messages[3]["options"]!["position"]!.GetValue<double>());
        Assert.Equal(3, messages[6]["options"]!["index"]!.GetValue<int>());
        Assert.Equal(-1, messages[7]["options"]!["index"]!.GetValue<int>());
        Assert.Equal("RepeatAll", messages[11]["options"]!["RepeatMode"]!.GetValue<string>());
        Assert.All(messages, m => Assert.Equal("Living Room Chromecast", m["receiverName"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Volume_is_the_receivers_own_volume()
    {
        await using var harness = await ConnectedAsync();
        var manager = harness.Manager;

        manager.SetVolume(30);
        manager.VolumeUp();
        harness.Fakes.Cast.UpdateSession(FakeCastInterop.Receiver(volume: 0.02));
        manager.VolumeDown();
        await manager.SendCommandAsync("SetVolume", CommandArguments.Of(("Volume", 120)));

        Assert.Equal([0.3, 0.45, 0, 1], harness.Fakes.Cast.VolumeLevels.Select(v => Math.Round(v, 2)));
        Assert.Empty(harness.Fakes.Cast.Messages);
    }

    [Fact]
    public async Task Display_mirroring_sends_display_content_and_other_general_commands_are_dropped()
    {
        await using var harness = await ConnectedAsync();

        await harness.Manager.DisplayContentAsync("Test Movie", PlaybackServer.MovieId, "Movie");
        await harness.Manager.SendCommandAsync("DisplayMessage", CommandArguments.Of(("Text", "Hello")));

        var message = Assert.Single(harness.Fakes.Cast.Messages);
        Assert.Equal("DisplayContent", message["command"]!.GetValue<string>());
        Assert.Equal(
            """{"ItemName":"Test Movie","ItemId":"11111111111111111111111111111111","ItemType":"Movie"}""",
            message["options"]!.ToJsonString());
    }

    [Fact]
    public async Task Ending_the_session_stops_and_then_ends_the_receiver_application()
    {
        await using var harness = await ConnectedAsync();
        var ending = harness.Manager.EndSessionAsync();

        Assert.Equal(["Stop"], harness.Fakes.Cast.Commands);
        Assert.Equal(0, harness.Fakes.Cast.SessionStops);
        await harness.Timers.AdvanceAsync(1000);
        await ending;
        Assert.Equal(1, harness.Fakes.Cast.SessionStops);
    }

    [Fact]
    public async Task A_localhost_server_is_sent_as_its_local_address()
    {
        await using var harness = await ConnectedAsync();
        harness.Get<JellyfinApi>().SetServer("http://localhost:8096", TestApp.ServerId, "Test Server");
        harness.Server.OnGet("/System/Info/Public", new { Id = TestApp.ServerId, Version = "10.11.1", LocalAddress = "http://192.168.1.20:8096" });

        await harness.Manager.PauseAsync();

        var message = harness.Fakes.Cast.LastMessage("Pause");
        Assert.Equal("http://192.168.1.20:8096", message["serverAddress"]!.GetValue<string>());
        Assert.Equal("10.11.1", message["serverVersion"]!.GetValue<string>());
    }

    // --- receiver state ----------------------------------------------------------------------------------

    [Fact]
    public async Task The_receivers_state_is_mirrored_into_the_manager()
    {
        await using var harness = await ConnectedAsync();
        var manager = harness.Manager;
        var events = Events(manager);
        var cast = harness.Fakes.Cast;

        cast.Receive("playbackstart", ReceiverState(positionTicks: 10_000_000));

        Assert.Equal([PlaybackEvents.StateChange, PlaybackEvents.PlaybackStart], events);
        Assert.True(manager.IsPlaying);
        Assert.True(manager.IsPlayingVideo);
        Assert.False(manager.IsPlayingAudio);
        Assert.Equal("Test Movie", manager.CurrentItem?.Name);
        Assert.Equal("primary-tag", manager.CurrentItem?.ImageTags?.AdditionalData["Primary"]);
        Assert.Equal(["backdrop-tag"], manager.CurrentItem?.BackdropImageTags);
        Assert.Equal(10_000_000, manager.CurrentTimeTicks);
        Assert.Equal(PlaybackServer.MovieTicks, manager.DurationTicks);
        Assert.Equal(70, manager.Volume);
        Assert.False(manager.IsPaused);
        Assert.Equal(2, manager.AudioStreamIndex);
        Assert.Equal(["English", "German"], manager.AudioTracks().Select(t => t.DisplayTitle));
        Assert.Equal(["English - SUBRIP"], manager.SubtitleTracks().Select(t => t.DisplayTitle));
        var state = manager.GetPlayerState();
        Assert.NotNull(state);
        Assert.Equal("DirectStream", state.PlayMethod);
        Assert.Equal(PlaybackServer.MovieId, state.MediaSourceId);

        events.Clear();
        cast.Receive("playbackprogress", ReceiverState(positionTicks: 20_000_000));
        cast.Receive("timeupdate", ReceiverState(positionTicks: 30_000_000));
        cast.Receive("pause", ReceiverState(positionTicks: 30_000_000, paused: true));
        Assert.True(manager.IsPaused);
        cast.Receive("unpause", ReceiverState(positionTicks: 30_000_000));
        cast.Receive("volumechange", ReceiverState(positionTicks: 30_000_000, volume: 40, muted: true));
        Assert.Equal(40, manager.Volume);
        Assert.True(manager.IsMuted);
        cast.Receive("repeatmodechange", ReceiverState(positionTicks: 30_000_000, volume: 40, muted: true, repeatMode: "RepeatOne"));
        cast.Receive("playstatechange", ReceiverState(positionTicks: 30_000_000, volume: 40, muted: true, repeatMode: "RepeatOne", paused: true));

        // Events without an item are skipped.
        cast.Receive("timeupdate", ReceiverState(positionTicks: 99_000_000, itemId: null));
        cast.Receive("timeupdate");

        Assert.Equal(
            [PlaybackEvents.TimeUpdate, PlaybackEvents.TimeUpdate, PlaybackEvents.Pause, PlaybackEvents.Unpause,
                PlaybackEvents.VolumeChange, PlaybackEvents.RepeatModeChange, PlaybackEvents.Pause],
            events);
        Assert.Equal(30_000_000, manager.CurrentTimeTicks);
        Assert.Equal(40, manager.Volume);
        Assert.True(manager.IsMuted);
        Assert.Equal("RepeatOne", manager.RepeatMode);
        Assert.True(manager.IsPaused);

        events.Clear();
        cast.Receive("playbackstop", ReceiverState(positionTicks: 30_000_000, volume: 40, muted: true));

        Assert.Equal([PlaybackEvents.PlaybackStop], events);
        Assert.False(manager.IsPlaying);
        Assert.Null(manager.CurrentItem);

        // The volume of the receiver is kept.
        Assert.Equal(40, manager.Volume);
        Assert.True(manager.IsMuted);
    }

    [Fact]
    public async Task A_stop_from_here_tells_the_stop_event_that_nothing_follows()
    {
        await using var harness = await ConnectedAsync();
        var cast = harness.Get<CastPlayer>();
        var seen = new List<string>();
        harness.Manager.Event += name =>
        {
            if (name == PlaybackEvents.PlaybackStop)
            {
                seen.Add(cast.LastPlayerData!.ContainsKey("nextItem") && cast.LastPlayerData["NextMediaType"] is null ? "no next" : "next may follow");
            }
        };
        var withNext = ReceiverState();
        withNext["NextMediaType"] = "Video";

        harness.Fakes.Cast.Receive("playbackstart", ReceiverState());
        harness.Fakes.Cast.Receive("playbackstop", ReceiverState());
        harness.Fakes.Cast.Receive("playbackstart", ReceiverState());
        await harness.Manager.StopAsync();
        harness.Fakes.Cast.Receive("playbackstop", withNext);

        Assert.Equal(["next may follow", "no next"], seen);
        Assert.Null(cast.LastPlayerData!["NowPlayingItem"]);
    }

    [Fact]
    public async Task Receiver_errors_are_shown_after_a_moment()
    {
        await using var harness = await ConnectedAsync();

        harness.Fakes.Cast.Receive("playbackerror", JsonValue.Create(string.Empty));
        Assert.Equal(0, harness.DialogCount);
        await harness.Timers.AdvanceAsync(300);
        Assert.Equal(1, harness.DialogCount);

        harness.Fakes.Cast.Receive("connectionerror");
        await harness.Timers.AdvanceAsync(300);
        Assert.Equal(2, harness.DialogCount);
    }

    [Fact]
    public async Task Playing_here_moves_to_the_receiver_when_it_is_chosen()
    {
        await using var harness = await WithCastAsync();
        var manager = harness.Manager;
        await harness.PlayMovieAsync(startPositionTicks: 0);
        harness.Player.Tick("video", 12);

        var target = Assert.Single(await manager.GetTargetsAsync());
        await manager.TrySetActivePlayerAsync(target.PlayerName, target);
        for (var i = 0; i < 100 && !harness.Fakes.Cast.Commands.Contains("PlayNow"); i++)
        {
            await Task.Delay(10);
        }

        var play = harness.Fakes.Cast.LastMessage("PlayNow");
        Assert.Equal(120_000_000, play["options"]!["startPositionTicks"]!.GetValue<long>());
        Assert.Equal(PlaybackServer.MovieId, play["options"]!["items"]![0]!["Id"]!.GetValue<string>());
        Assert.Contains("stop video destroy=True fade=False", harness.Player.Calls);
    }
}

/// <summary>The cast menu of the app bar and the now playing bar with the Cast target.</summary>
public class CastUiTests : PlaybackTestContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private CastPlayer Cast => Services.GetRequiredService<CastPlayer>();

    private async Task MakeCastAvailableAsync(bool receivers)
    {
        Server.OnGet($"/Users/{PlaybackServer.UserId}", new JsonObject
        {
            ["Id"] = PlaybackServer.UserId,
            ["Configuration"] = new JsonObject { ["CastReceiverId"] = "F007D354" },
        }.ToJsonString());
        Fakes.Cast.SdkAvailable = true;
        _ = Manager;
        await Cast.LoadAsync();
        Fakes.Cast.ReportReceivers(receivers);
    }

    [Fact]
    public void Without_the_sdk_the_menu_says_google_cast_is_unsupported()
    {
        Server.OnGet("/Sessions", "[]");
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<RemotePlayButton>();

        cut.Find(".jf-remote-play-button").Click();

        popovers.WaitForAssertion(() => Assert.Contains("Google Cast Unsupported", popovers.Find(".jf-remote-play-nocast").TextContent), Timeout);
        Assert.Empty(popovers.FindAll(".jf-remote-play-target"));
        Assert.Empty(popovers.FindAll(".jf-remote-play-notargets"));
    }

    [Fact]
    public async Task With_receivers_the_menu_lists_the_cast_target_and_selecting_it_connects()
    {
        Server.OnGet("/Sessions", "[]");
        await MakeCastAvailableAsync(receivers: true);
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<RemotePlayButton>();

        cut.Find(".jf-remote-play-button").Click();

        popovers.WaitForAssertion(() => Assert.Single(popovers.FindAll(".jf-remote-play-target")), Timeout);
        Assert.Empty(popovers.FindAll(".jf-remote-play-nocast"));
        var entry = popovers.Find(".jf-remote-play-target");
        Assert.Contains("Google Cast - Google Cast", entry.TextContent);
        Assert.Equal(CastPlayer.PlayerName, entry.GetAttribute("data-id"));

        entry.Click();

        cut.WaitForAssertion(() => Assert.Contains(CastPlayer.PlayerName, cut.Find(".jf-remote-play-button-active").TextContent), Timeout);
        Assert.IsType<CastPlayer>(Manager.CurrentPlayer);
        Assert.Equal(["Identify"], Fakes.Cast.Commands);
    }

    [Fact]
    public async Task Without_receivers_or_sessions_the_menu_says_there_are_no_targets()
    {
        Server.OnGet("/Sessions", "[]");
        await MakeCastAvailableAsync(receivers: false);
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<RemotePlayButton>();

        cut.Find(".jf-remote-play-button").Click();

        popovers.WaitForAssertion(() => Assert.Contains("No cast targets available", popovers.Find(".jf-remote-play-notargets").TextContent), Timeout);
        Assert.Empty(popovers.FindAll(".jf-remote-play-nocast"));
        Assert.Empty(popovers.FindAll(".jf-remote-play-target"));
    }

    [Fact]
    public async Task The_now_playing_bar_shows_what_the_receiver_plays()
    {
        Server.OnGet("/Sessions", "[]");
        await MakeCastAvailableAsync(receivers: true);
        Services.GetRequiredService<AppRouter>().Start();
        var target = Cast.GetCurrentTargetInfo();
        await Manager.TrySetActivePlayerAsync(target.PlayerName, target);
        var cut = Render<NowPlayingBar>();

        Fakes.Cast.Receive("playbackstart", new JsonObject
        {
            ["ItemId"] = PlaybackServer.SongId,
            ["PlayState"] = new JsonObject { ["PositionTicks"] = 70_000_000, ["IsPaused"] = false, ["VolumeLevel"] = 50 },
            ["NowPlayingItem"] = new JsonObject
            {
                ["Id"] = PlaybackServer.SongId,
                ["Name"] = "Opening",
                ["Type"] = "Audio",
                ["MediaType"] = "Audio",
                ["RunTimeTicks"] = PlaybackServer.SongTicks,
            },
        });

        cut.WaitForAssertion(() => Assert.DoesNotContain("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
        Assert.Contains("Opening", cut.Find(".nowPlayingBarText").TextContent);
        Assert.Equal("0:07 / 0:30", cut.Find(".nowPlayingBarCurrentTime").TextContent);

        cut.Find(".playPauseButton").Click();
        Assert.Equal("PlayPause", Fakes.Cast.Commands[^1]);

        Fakes.Cast.Receive("playbackstop", new JsonObject { ["ItemId"] = PlaybackServer.SongId, ["PlayState"] = new JsonObject() });
        cut.WaitForAssertion(() => Assert.Contains("nowPlayingBar-hidden", cut.Find(".nowPlayingBar").ClassList), Timeout);
    }
}
