using Jellyfin.Web.Services;

namespace Jellyfin.Web.Tests.Screensaver;

public sealed class ScreensaverTests
{
    [Fact]
    public void IdleRequiresBothInputAndFunctionalQuietPeriods()
    {
        var now = new DateTimeOffset(2024, 1, 1, 0, 0, 10, TimeSpan.Zero);
        var minimum = TimeSpan.FromSeconds(5);

        Assert.True(ScreensaverEligibility.IsIdle(now, now.AddSeconds(-5), now.AddSeconds(-5), minimum));
        Assert.False(ScreensaverEligibility.IsIdle(now, now.AddSeconds(-4), now.AddSeconds(-5), minimum));
        Assert.False(ScreensaverEligibility.IsIdle(now, now.AddSeconds(-5), now.AddSeconds(-4), minimum));
    }
}
