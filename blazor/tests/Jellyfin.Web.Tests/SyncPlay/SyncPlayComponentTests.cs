using Bunit;
using Jellyfin.Web.Components;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.SyncPlay;

public sealed class SyncPlayComponentTests : AppTestContext
{
    [Fact]
    public async Task SettingsDialogShowsOriginalCorrectionControls()
    {
        SignIn();

        var cut = Render<SyncPlaySettingsDialog>();

        Assert.Contains("chkSyncCorrection", cut.Markup);
        Assert.Contains("chkSpeedToSync", cut.Markup);
        Assert.Contains("txtMinDelaySkipToSync", cut.Markup);
    }
}
