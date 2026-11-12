using System.Text.Json;
using Jellyfin.Web.Services;
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
}
