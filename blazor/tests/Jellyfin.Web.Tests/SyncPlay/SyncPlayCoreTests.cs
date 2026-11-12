using System.Text.Json;
using System.Text.Json.Nodes;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.SyncPlay;

public sealed class SyncPlayCoreTests
{
    [Fact]
    public void MeasurementUsesMinimumRoundTripSample()
    {
        var first = new SyncPlayTimeMeasurement(1000, 1100, 1200, 1400);
        var second = new SyncPlayTimeMeasurement(2000, 2070, 2170, 2240);

        Assert.Equal(-50d, first.OffsetMilliseconds);
        Assert.Equal(300, first.RoundTripMilliseconds);
        Assert.Equal(0d, second.OffsetMilliseconds);
        Assert.Equal(140, second.RoundTripMilliseconds);
    }

    [Fact]
    public async Task TimeSyncServerKeepsTheLowestLatencyMeasurement()
    {
        var app = new TestApp();
        app.SignIn();
        var timers = new FakePlaybackTimers { NowMs = 10_000 };
        var server = new TimeSyncServer(app.Get<JellyfinApi>(), timers);
        app.Server.OnGet("/GetUtcTime", new { RequestReceptionTime = "1970-01-01T00:00:00.020Z", ResponseTransmissionTime = "1970-01-01T00:00:00.030Z" });

        await server.UpdateAsync();

        Assert.True(server.IsReady);
        Assert.Equal(-5, server.PingMilliseconds);
        Assert.Equal(-9975, server.OffsetMilliseconds);
    }

    [Fact]
    public async Task TimeSyncServerChoosesTheLowestLatencyFromSeveralSamples()
    {
        var app = new TestApp();
        app.SignIn();
        var timers = new FakePlaybackTimers { NowMs = 10_000 };
        var sample = 0;
        app.Server.On(HttpMethod.Get, "/GetUtcTime", _ =>
        {
            sample++;
            return sample switch
            {
                1 => Response(timers, 10_040, 10_080, 10_300),
                2 => Response(timers, 10_390, 10_400, 10_400),
                _ => Response(timers, 10_450, 10_490, 10_600)
            };
        });

        var server = new TimeSyncServer(app.Get<JellyfinApi>(), timers);
        await server.UpdateAsync();
        await server.UpdateAsync();
        await server.UpdateAsync();

        Assert.Equal(45, server.OffsetMilliseconds);
        Assert.Equal(45, server.PingMilliseconds);

        static HttpResponseMessage Response(FakePlaybackTimers timers, long received, long sent, long clientReceived)
        {
            timers.NowMs = clientReceived;
            return FakeHttp.Json(new { RequestReceptionTime = received, ResponseTransmissionTime = sent });
        }
    }

    [Fact]
    public async Task GroupUpdateEnablesAndCommandDrivesLocalPlaybackWithoutEcho()
    {
        var app = new TestApp();
        app.SignIn();
        var playback = app.Get<Jellyfin.Web.Services.Playback.PlaybackManager>();
        var service = app.Get<SyncPlayService>();
        await service.StartAsync();

        var group = JsonDocument.Parse("""
            {"Type":"GroupJoined","Data":{"GroupId":"group-1","GroupName":"Movie night","LastUpdatedAt":"2025-01-01T00:00:00Z","Participants":["tester"]}}
            """).RootElement.Clone();
        await service.ProcessGroupUpdateAsync(group);

        Assert.True(service.IsEnabled);
        Assert.Equal("group-1", service.Group!.GroupId);

        var queue = JsonDocument.Parse("""
            {"Type":"PlayQueue","Data":{"LastUpdate":"2025-01-01T00:00:01Z","Playlist":[{"ItemId":"item-1","PlaylistItemId":"playlist-1"}],"PlayingItemIndex":0,"StartPositionTicks":0}}
            """).RootElement.Clone();
        await service.ProcessGroupUpdateAsync(queue);
        Assert.Equal("playlist-1", service.Queue!.CurrentPlaylistItemId);

        var command = JsonDocument.Parse("""
            {"Command":"Pause","When":"2025-01-01T00:00:02Z","EmittedAt":"2025-01-01T00:00:02Z","PositionTicks":12300000,"PlaylistItemId":"playlist-1"}
            """).RootElement.Clone();
        await service.ProcessCommandAsync(command);

        Assert.False(playback.IsPlaying);
        Assert.Empty(app.Server.RequestsTo("/SyncPlay/Pause"));
    }

    [Fact]
    public async Task GroupUpdatesTrackMembershipStateAndDisableReasons()
    {
        var app = new TestApp();
        app.SignIn();
        var service = app.Get<SyncPlayService>();
        var joinedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
        await service.ProcessGroupUpdateAsync(Message($"{{\"Type\":\"GroupJoined\",\"Data\":{{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"{joinedAt:O}\",\"Participants\":[\"tester\"]}}}}"));
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"UserJoined\",\"Data\":\"guest\"}"));
        Assert.Contains("guest", service.Group!.Participants);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"UserLeft\",\"Data\":\"guest\"}"));
        Assert.DoesNotContain("guest", service.Group!.Participants);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"StateUpdate\",\"Data\":{\"State\":\"Waiting\",\"Reason\":\"Buffering\"}}"));
        Assert.Equal("Waiting", service.LastStateUpdate!.State);
        Assert.Equal("Buffering", service.LastStateUpdate.Reason);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"GroupUpdate\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"New name\",\"LastUpdatedAt\":\"2025-01-01T00:00:01Z\",\"Participants\":[\"tester\"]}}"));
        Assert.Equal("New name", service.Group!.GroupName);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"GroupLeft\",\"Data\":null}"));
        Assert.False(service.IsEnabled);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"GroupJoined\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"2025-01-01T00:00:00Z\",\"Participants\":[\"tester\"]}}"));
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"NotInGroup\",\"Data\":null}"));
        Assert.False(service.IsEnabled);
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"GroupDoesNotExist\",\"Data\":null}"));
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public async Task QueueReasonsArePreservedAndQueueModesApplyLocally()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Timers.NowMs = DateTimeOffset.Parse("2025-01-01T00:00:00Z").ToUnixTimeMilliseconds();
        var service = harness.Get<SyncPlayService>();
        var group = Message("{\"Type\":\"GroupJoined\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"2025-01-01T00:00:00Z\",\"Participants\":[\"tester\"]}}");
        await service.ProcessGroupUpdateAsync(group);
        foreach (var (reason, index, repeat, shuffle) in new[]
        {
            ("NewPlaylist", 0, "RepeatNone", "Sorted"),
            ("SetCurrentItem", 1, "RepeatNone", "Sorted"),
            ("MoveItem", 0, "RepeatNone", "Sorted"),
            ("Queue", 0, "RepeatNone", "Sorted"),
            ("RemoveItems", 0, "RepeatNone", "Sorted"),
            ("RepeatMode", 0, "RepeatAll", "Sorted"),
            ("ShuffleMode", 0, "RepeatAll", "Shuffle")
        })
        {
            var seconds = Array.IndexOf(new[] { "NewPlaylist", "SetCurrentItem", "MoveItem", "Queue", "RemoveItems", "RepeatMode", "ShuffleMode" }, reason) + 1;
            var json = $"{{\"Type\":\"PlayQueue\",\"Data\":{{\"LastUpdate\":\"2025-01-01T00:00:{seconds:00}Z\",\"Reason\":\"{reason}\",\"Playlist\":[{{\"ItemId\":\"{PlaybackServer.MovieId}\",\"PlaylistItemId\":\"p1\"}},{{\"ItemId\":\"{PlaybackServer.SecondMovieId}\",\"PlaylistItemId\":\"p2\"}}],\"PlayingItemIndex\":{index},\"StartPositionTicks\":0,\"RepeatMode\":\"{repeat}\",\"ShuffleMode\":\"{shuffle}\"}}}}";
            await service.ProcessGroupUpdateAsync(Message(json));
            Assert.Equal(reason, service.Queue!.Reason);
        }

        Assert.Equal(PlayQueue.RepeatAll, harness.Manager.RepeatMode);
        Assert.Equal(PlayQueue.Shuffled, harness.Manager.ShuffleMode);
    }

    [Fact]
    public async Task CommandsAreScheduledInLocalTimeAndSuppressTheirEchoes()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Timers.NowMs = DateTimeOffset.Parse("2025-01-01T00:00:00Z").ToUnixTimeMilliseconds();
        var service = harness.Get<SyncPlayService>();
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"GroupJoined\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"2025-01-01T00:00:00Z\",\"Participants\":[\"tester\"]}}"));
        await service.ProcessGroupUpdateAsync(Message("{\"Type\":\"PlayQueue\",\"Data\":{\"LastUpdate\":\"2025-01-01T00:00:01Z\",\"Reason\":\"NewPlaylist\",\"Playlist\":[{\"ItemId\":\"" + PlaybackServer.MovieId + "\",\"PlaylistItemId\":\"p1\"}],\"PlayingItemIndex\":0,\"StartPositionTicks\":0}}"));
        await harness.Manager.PauseAsync();

        var at = DateTimeOffset.FromUnixTimeMilliseconds(harness.Timers.NowMs + 2000);
        var command = $"{{\"Command\":\"Unpause\",\"When\":\"{at:O}\",\"EmittedAt\":\"{at:O}\",\"PositionTicks\":0,\"PlaylistItemId\":\"p1\"}}";
        var pending = service.ProcessCommandAsync(Message(command));
        Assert.Contains(2000, harness.Timers.PendingDelays);
        await harness.Timers.AdvanceAsync(1999);
        Assert.True(harness.Manager.IsPaused);
        await harness.Timers.AdvanceAsync(1);
        await pending;
        Assert.False(harness.Manager.IsPaused);
        Assert.Empty(harness.Server.RequestsTo("/SyncPlay/Unpause"));

        var pauseAt = DateTimeOffset.FromUnixTimeMilliseconds(harness.Timers.NowMs + 1000);
        var pause = service.ProcessCommandAsync(Message($"{{\"Command\":\"Pause\",\"When\":\"{pauseAt:O}\",\"EmittedAt\":\"{pauseAt:O}\",\"PositionTicks\":12300000,\"PlaylistItemId\":\"p1\"}}"));
        await harness.Timers.AdvanceAsync(1000);
        await pause;
        Assert.True(harness.Manager.IsPaused);
        Assert.Equal(12300000, harness.Manager.CurrentTimeTicks);

        static JsonElement Message(string json) => JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task ReadyAndBufferingReportsContainClockPositionAndQueueIdentity()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Timers.NowMs = DateTimeOffset.Parse("2025-01-01T00:00:00Z").ToUnixTimeMilliseconds();
        var service = harness.Get<SyncPlayService>();
        await service.ProcessGroupUpdateAsync(JsonDocument.Parse("{\"Type\":\"GroupJoined\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"2025-01-01T00:00:00Z\",\"Participants\":[\"tester\"]}}").RootElement.Clone());
        await service.ProcessGroupUpdateAsync(JsonDocument.Parse("{\"Type\":\"PlayQueue\",\"Data\":{\"LastUpdate\":\"2025-01-01T00:00:01Z\",\"Reason\":\"NewPlaylist\",\"Playlist\":[{\"ItemId\":\"" + PlaybackServer.MovieId + "\",\"PlaylistItemId\":\"p1\"}],\"PlayingItemIndex\":0,\"StartPositionTicks\":0}}").RootElement.Clone());
        harness.Player.Emit("video", "waiting");
        harness.Player.Emit("video", "playing");
        await PlaybackHarness.Eventually(() => harness.Server.RequestsTo("/SyncPlay/Buffering").Count > 0 && harness.Server.RequestsTo("/SyncPlay/Ready").Count > 0);
        Assert.Equal("p1", harness.Server.RequestsTo("/SyncPlay/Buffering").Last().Json<JsonObject>()!["PlaylistItemId"]!.GetValue<string>());
        Assert.False(service.IsBuffering);
    }

    [Fact]
    public async Task DriftCorrectionUsesSpeedThenSkipAtConfiguredThresholds()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Timers.NowMs = DateTimeOffset.Parse("2025-01-01T00:00:00Z").ToUnixTimeMilliseconds();
        var service = harness.Get<SyncPlayService>();
        await service.ProcessGroupUpdateAsync(JsonDocument.Parse("{\"Type\":\"GroupJoined\",\"Data\":{\"GroupId\":\"g\",\"GroupName\":\"Night\",\"LastUpdatedAt\":\"2025-01-01T00:00:00Z\",\"Participants\":[\"tester\"]}}").RootElement.Clone());
        await service.ProcessGroupUpdateAsync(JsonDocument.Parse("{\"Type\":\"PlayQueue\",\"Data\":{\"LastUpdate\":\"2025-01-01T00:00:01Z\",\"Reason\":\"NewPlaylist\",\"Playlist\":[{\"ItemId\":\"" + PlaybackServer.MovieId + "\",\"PlaylistItemId\":\"p1\"}],\"PlayingItemIndex\":0,\"StartPositionTicks\":0}}").RootElement.Clone());
        service.SaveSettings(new SyncPlaySettings { EnableSyncCorrection = true, MinDelaySpeedToSync = 60, MaxDelaySpeedToSync = 3000, SpeedToSyncDuration = 1000, MinDelaySkipToSync = 400, UseSpeedToSync = true, UseSkipToSync = true });
        var when = DateTimeOffset.FromUnixTimeMilliseconds(harness.Timers.NowMs);
        await service.ProcessCommandAsync(JsonDocument.Parse($"{{\"Command\":\"Unpause\",\"When\":\"{when:O}\",\"EmittedAt\":\"{when:O}\",\"PositionTicks\":1000000000,\"PlaylistItemId\":\"p1\"}}").RootElement.Clone());
        harness.Player.Tick("video", 99);
        Assert.StartsWith("SpeedToSync", service.SyncMethod);
        await harness.Timers.AdvanceAsync(1000);
        harness.Player.Tick("video", 90);
        Assert.Equal("SkipToSync", service.SyncMethod);
    }

    [Fact]
    public void SettingsArePersistedAndLoadedWithInvariantValues()
    {
        var app = new TestApp();
        app.SignIn();
        var service = app.Get<SyncPlayService>();
        service.SaveSettings(new SyncPlaySettings { ExtraTimeOffset = 12.5, EnableSyncCorrection = true, MinDelaySpeedToSync = 75, MaxDelaySpeedToSync = 2500, SpeedToSyncDuration = 1200, MinDelaySkipToSync = 500, UseSpeedToSync = false, UseSkipToSync = true });
        Assert.Equal("12.5", app.Get<AppSettings>().Get("extraTimeOffset", "syncPlay"));
        Assert.True(service.Settings.EnableSyncCorrection);
        Assert.False(service.Settings.UseSpeedToSync);
        Assert.Equal(75, service.Settings.MinDelaySpeedToSync);
        Assert.Equal("false", app.Get<AppSettings>().Get("useSpeedToSync", "syncPlay"));
        Assert.Equal("true", app.Get<AppSettings>().Get("useSkipToSync", "syncPlay"));
    }

    private static JsonElement Message(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
