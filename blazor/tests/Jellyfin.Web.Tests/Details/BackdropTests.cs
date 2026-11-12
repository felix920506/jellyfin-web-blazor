using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Details;

/// <summary>The page backdrop service (components/backdrop/backdrop.js).</summary>
public class BackdropTests : DetailsTestContext
{
    private Backdrop Backdrops => Get<Backdrop>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    private static BaseItemDto Movie(params string[] backdropTags) => TestItems.Item(new
    {
        Id = TestItems.Id(1),
        Type = "Movie",
        BackdropImageTags = backdropTags
    });

    [Fact]
    public void Nothing_shows_while_the_user_has_backdrops_turned_off()
    {
        Backdrops.SetBackdrops([Movie("b0")]);

        Assert.Empty(BackdropDom.Shown);
        Assert.Empty(Backdrops.Images);
    }

    [Fact]
    public void The_setting_turns_backdrops_on()
    {
        Get<UserSettings>().EnableBackdrops = true;

        Backdrops.SetBackdrops([Movie("b0")]);

        var url = Assert.Single(BackdropDom.Shown);
        Assert.StartsWith($"{TestApp.ServerUrl}/Items/{TestItems.Id(1)}/Images/Backdrop/0?", url);
        Assert.Contains("maxWidth=1920", url);
        Assert.Contains("tag=b0", url);
        Assert.Contains("quality=80", url);
    }

    [Fact]
    public void A_page_can_show_its_backdrop_whatever_the_setting_says()
    {
        Backdrops.SetBackdrops([Movie("b0")], enabled: true);

        Assert.Single(BackdropDom.Shown);
        Assert.Equal(BackdropDom.Shown, Backdrops.Images);
    }

    [Fact]
    public void The_image_is_as_wide_as_the_screen_in_device_pixels()
    {
        App.Browser.Environment = App.Browser.Environment with { ScreenWidth = 400, DevicePixelRatio = 2.5 };

        Assert.Contains("maxWidth=1000", Backdrops.GetBackdropUrl(TestItems.Id(1), "b0", 0));
        Assert.Contains("quality=90", Backdrops.GetImageUrl(TestItems.Id(1), "Primary", "p"));
        Assert.Contains($"/Items/{TestItems.Id(1)}/Images/Primary?", Backdrops.GetImageUrl(TestItems.Id(1), "Primary", "p"));
    }

    [Fact]
    public void An_item_without_backdrops_uses_those_of_its_parent()
    {
        var episode = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Episode",
            ParentBackdropItemId = TestItems.Id(9),
            ParentBackdropImageTags = new[] { "s0", "s1" }
        });

        var urls = Backdrops.GetImageUrls(episode);

        Assert.Equal(2, urls.Count);
        Assert.Contains($"/Items/{TestItems.Id(9)}/Images/Backdrop/0?", urls[0]);
        Assert.Contains("tag=s0", urls[0]);
        Assert.Contains($"/Items/{TestItems.Id(9)}/Images/Backdrop/1?", urls[1]);
        Assert.Empty(Backdrops.GetImageUrls(TestItems.Item(new { Id = TestItems.Id(2), Type = "Person" })));
    }

    [Fact]
    public void Items_without_images_remove_the_backdrop()
    {
        Backdrops.SetBackdrops([Movie("b0")], enabled: true);

        Backdrops.SetBackdrops([Movie()], enabled: true);

        Assert.Null(BackdropDom.Url);
        Assert.Equal(1, BackdropDom.Clears);
        Assert.Empty(Backdrops.Images);
    }

    [Fact]
    public void The_same_images_are_not_shown_again()
    {
        Backdrops.SetBackdrops([Movie("b0")], enabled: true);
        Backdrops.SetBackdrops([Movie("b0")], enabled: true);

        Assert.Single(BackdropDom.Shown);
    }

    [Fact]
    public void Clearing_removes_the_image()
    {
        Backdrops.SetBackdrops([Movie("b0")], enabled: true);

        Backdrops.Clear();

        Assert.Null(BackdropDom.Url);
        Assert.Empty(Backdrops.Images);
    }

    [Fact]
    public async Task Several_images_take_turns()
    {
        var interval = Backdrop.RotationInterval;
        Backdrop.RotationInterval = TimeSpan.FromMilliseconds(20);
        try
        {
            Backdrops.SetBackdrops([Movie("b0", "b1")], enabled: true);
            for (var attempt = 0; attempt < 300 && BackdropDom.Shown.Count < 3; attempt++)
            {
                await Task.Delay(10);
            }

            Backdrops.Clear();
            var shown = BackdropDom.Shown.ToList();
            Assert.True(shown.Count >= 3, "the images did not rotate");
            Assert.Contains("tag=b0", shown[0]);
            Assert.Contains("tag=b1", shown[1]);
            Assert.Contains("tag=b0", shown[2]);

            // Cleared: the rotation stops.
            await Task.Delay(80);
            Assert.Null(BackdropDom.Url);
        }
        finally
        {
            Backdrop.RotationInterval = interval;
        }
    }
}
