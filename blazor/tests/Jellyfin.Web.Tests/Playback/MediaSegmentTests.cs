using System.Text.Json.Nodes;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>Server media segments and the user's skip policy.</summary>
public class MediaSegmentTests
{
    [Fact]
    public async Task Segments_are_loaded_from_the_server_and_filtered_by_action()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var itemId = Guid.Parse(PlaybackServer.MovieId);
        harness.Server.OnGet($"/MediaSegments/{itemId:N}", new JsonObject
        {
            ["Items"] = new JsonArray(
                new JsonObject { ["Type"] = "Intro", ["StartTicks"] = 100, ["EndTicks"] = 200 },
                new JsonObject { ["Type"] = "Preview", ["StartTicks"] = 300, ["EndTicks"] = 400 },
                new JsonObject { ["Type"] = "Outro", ["StartTicks"] = 500, ["EndTicks"] = 600 }),
        });

        var service = harness.Get<MediaSegmentService>();
        var segments = await service.LoadAsync(itemId);

        Assert.Equal(["Intro", "Preview", "Outro"], segments.Select(segment => segment.Type));
        var segmentRequest = harness.Server.RequestsTo($"/MediaSegments/{itemId:N}").Single();
        Assert.Equal(["Intro", "Preview", "Recap", "Commercial", "Outro"],
            System.Web.HttpUtility.ParseQueryString(segmentRequest.Uri.Query).GetValues("IncludeSegmentTypes") ?? []);
        Assert.Equal("Intro", service.Current(segments, 150)?.Type);
        Assert.Null(service.Current(segments, 250));
        Assert.Null(service.Current(segments, 350));

        harness.Get<Jellyfin.Web.Services.UserSettings>().Set("segmentTypeAction__Preview", "AskToSkip", enableOnServer: false);
        Assert.Equal("Preview", service.Current(segments, 350)?.Type);
    }

    [Fact]
    public async Task Skip_policy_defaults_and_stored_values_are_distinct()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var service = harness.Get<MediaSegmentService>();
        var segments = new[]
        {
            new PlaybackMediaSegment("Intro", 0, 10),
            new PlaybackMediaSegment("Commercial", 20, 30),
        };

        Assert.Equal("AskToSkip", service.Action("Intro"));
        Assert.Equal("None", service.Action("Commercial"));
        harness.Get<Jellyfin.Web.Services.UserSettings>().Set("segmentTypeAction__Commercial", "Skip", enableOnServer: false);
        Assert.Equal("Skip", service.Action("Commercial"));
        Assert.Equal("Commercial", service.Current(segments, 25)?.Type);
    }
}
