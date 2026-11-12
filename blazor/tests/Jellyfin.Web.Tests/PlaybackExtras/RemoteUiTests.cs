using Jellyfin.Web.Components;
using Jellyfin.Web.Pages.Playback;
using Jellyfin.Web.Tests.Playback;
using MudBlazor;

namespace Jellyfin.Web.Tests.PlaybackExtras;

public class RemoteUiTests : PlaybackTestContext
{
    [Fact]
    public async Task The_toolbar_lists_targets_and_disconnects_the_selected_target()
    {
        RemoteSessions.Script(Server, RemoteSessions.Session());
        var popovers = Render<MudPopoverProvider>();
        var cut = Render<RemotePlayButton>();
        cut.Find(".jf-remote-play-button").Click();
        popovers.WaitForAssertion(() => Assert.Single(popovers.FindAll(".jf-remote-play-target")));
        Assert.Contains("Living Room TV", popovers.Find(".jf-remote-play-target").TextContent);
        popovers.Find(".jf-remote-play-target").Click();
        cut.WaitForAssertion(() => Assert.Contains("Living Room TV", cut.Find(".jf-remote-play-button-active").TextContent));
        cut.Find(".jf-remote-play-button-active").Click();
        popovers.WaitForAssertion(() => Assert.Single(popovers.FindAll(".jf-remote-play-disconnect")));
        popovers.Find(".jf-remote-play-disconnect").Click();
        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(".jf-remote-play-button-active")));
        Assert.Null(Manager.CurrentTarget);
    }

    [Fact]
    public async Task The_remote_queue_exposes_navigation_message_text_and_transport()
    {
        RemoteSessions.Script(Server, RemoteSessions.Session());
        await RemoteSessions.ConnectAsync(Manager);
        RemoteSessions.Dispatch(Services, RemoteSessions.Playing(PlaybackServer.Songs()[0]));
        var cut = Render<Queue>();
        Assert.DoesNotContain("hide", cut.Find(".remoteControlSection").ClassList);
        Assert.False(cut.Find(".btnArrowUp").HasAttribute("disabled"));
        Assert.False(cut.Find(".btnContextMenu").HasAttribute("disabled"));
        cut.Find(".btnArrowUp").Click();
        cut.Find(".btnPlayPause").Click();
        cut.Find("#txtMessageTitle").Input("Hello");
        cut.Find("#txtMessageText").Input("Dinner is ready");
        cut.Find(".sendMessageForm").Submit();
        cut.Find("#txtTypeText").Input("Search text");
        cut.Find(".typeTextForm").Submit();
        cut.WaitForAssertion(() => Assert.Contains("SendString {\"String\":\"Search text\"}", RemoteSessions.Commands(Server)));
        Assert.Contains("MoveUp", RemoteSessions.Commands(Server));
        Assert.Contains("DisplayMessage {\"Header\":\"Hello\",\"Text\":\"Dinner is ready\"}", RemoteSessions.Commands(Server));
        Assert.Contains("PlayPause", RemoteSessions.PlayStateCommands(Server));
        Assert.Equal(string.Empty, cut.Find("#txtTypeText").GetAttribute("value"));
        Assert.Equal(string.Empty, cut.Find("#txtMessageTitle").GetAttribute("value"));
        Assert.Equal("Dinner is ready", cut.Find("#txtMessageText").GetAttribute("value"));
    }

    [Fact]
    public async Task Local_playback_never_exposes_remote_navigation_or_message_sections()
    {
        await PlaySongsAsync();
        var cut = Render<Queue>();
        foreach (var selector in new[] { ".remoteControlSection", ".navigationSection", ".sendMessageSection", ".sendTextSection" })
        {
            Assert.Contains("hide", cut.Find(selector).ClassList);
        }
    }
}
