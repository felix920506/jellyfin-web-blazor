using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>Pure display rules used by the live-TV video OSD.</summary>
public class LiveTvOsdTests
{
    [Fact]
    public void A_channel_displays_its_current_program_and_uses_wall_clock_progress()
    {
        var start = DateTimeOffset.FromUnixTimeMilliseconds(1_749_999_000_000);
        var end = start.AddMinutes(60);
        var programme = new BaseItemDto
        {
            Type = BaseItemDto_Type.Program,
            Name = "News",
            StartDate = start,
            EndDate = end,
        };
        var channel = new BaseItemDto
        {
            Type = BaseItemDto_Type.TvChannel,
            Name = "Channel 1",
            CurrentProgram = programme,
        };

        Assert.Same(programme, PlaybackText.DisplayItem(channel));
        Assert.True(PlaybackText.UsesLiveTvProgress(channel));
    }

    [Fact]
    public void A_channel_without_a_current_program_uses_normal_media_progress()
    {
        var channel = new BaseItemDto { Type = BaseItemDto_Type.TvChannel, Name = "Channel 1" };

        Assert.Same(channel, PlaybackText.DisplayItem(channel));
        Assert.False(PlaybackText.UsesLiveTvProgress(channel));
    }
}
