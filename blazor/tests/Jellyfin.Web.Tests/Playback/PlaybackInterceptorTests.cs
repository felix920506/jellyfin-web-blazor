using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.Playback;

public sealed class PlaybackInterceptorTests
{
    [Fact]
    public async Task Playback_access_is_rejected_without_a_dialog_when_not_fullscreen()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync(user: user => user.Policy!.EnableMediaPlayback = false);
        var item = PlaybackServer.Movie();
        var options = new ItemPlayOptions { Fullscreen = false, IsFirstItem = true };

        await Assert.ThrowsAsync<PlaybackInterceptException>(() => harness.Get<PlaybackInterceptors>().BeforePlayAsync(item, options));

        Assert.Empty(harness.Get<AppDialogs>().Dialogs);
    }

    [Fact]
    public async Task Fullscreen_playback_access_shows_the_restriction_message()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync(user: user => user.Policy!.EnableMediaPlayback = false);
        var pending = harness.Get<PlaybackInterceptors>().BeforePlayAsync(PlaybackServer.Movie(), new ItemPlayOptions { IsFirstItem = true });
        var dialogs = harness.Get<AppDialogs>();

        Assert.Single(dialogs.Dialogs);
        dialogs.Dialogs[0].Close("ok");
        await Assert.ThrowsAsync<PlaybackInterceptException>(() => pending);
        Assert.Empty(dialogs.Dialogs);
    }

    [Fact]
    public async Task Experimental_video_warning_is_shown_once_per_iso_week()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var item = PlaybackServer.Movie();
        item.VideoType = (BaseItemDto_VideoType)Enum.Parse(typeof(BaseItemDto_VideoType), "Iso");
        var interceptor = harness.Get<PlaybackInterceptors>();
        var dialogs = harness.Get<AppDialogs>();

        var first = interceptor.BeforePlayAsync(item, new ItemPlayOptions { IsFirstItem = true });
        Assert.Single(dialogs.Dialogs);
        dialogs.Dialogs[0].Close("ok");
        await first;

        await interceptor.BeforePlayAsync(item, new ItemPlayOptions { IsFirstItem = true });
        Assert.Empty(dialogs.Dialogs);
    }

    [Fact]
    public async Task Still_watching_short_threshold_prompts_after_two_episodes()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var settings = harness.Get<UserSettings>();
        settings.Set("stillWatchingPrompt", "short", enableOnServer: false);
        var interceptor = harness.Get<PlaybackInterceptors>();
        var dialogs = harness.Get<AppDialogs>();

        await interceptor.BeforePlayAsync(PlaybackServer.Episode("33333333333333333333333333333333", "One", 1), new ItemPlayOptions { IsFirstItem = true });
        await interceptor.BeforePlayAsync(PlaybackServer.Episode("44444444444444444444444444444444", "Two", 2), new ItemPlayOptions());
        harness.Timers.NowMs += (long)TimeSpan.FromHours(1).TotalMilliseconds;
        var pending = interceptor.BeforePlayAsync(PlaybackServer.Episode("55555555555555555555555555555555", "Three", 3), new ItemPlayOptions());

        Assert.Single(dialogs.Dialogs);
        dialogs.Dialogs[0].Close("ok");
        await pending;
    }

    [Fact]
    public async Task Still_watching_can_be_disabled()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Get<UserSettings>().Set("stillWatchingPrompt", "disabled", enableOnServer: false);
        var interceptor = harness.Get<PlaybackInterceptors>();

        await interceptor.BeforePlayAsync(PlaybackServer.Episode("66666666666666666666666666666666", "One", 1), new ItemPlayOptions { IsFirstItem = true });
        harness.Timers.NowMs += (long)TimeSpan.FromHours(10).TotalMilliseconds;
        await interceptor.BeforePlayAsync(PlaybackServer.Episode("77777777777777777777777777777777", "Two", 2), new ItemPlayOptions());

        Assert.Empty(harness.Get<AppDialogs>().Dialogs);
    }

    [Fact]
    public async Task Playback_manager_resolves_the_interceptor_service()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        Assert.Same(harness.Get<PlaybackInterceptors>(), harness.Services.GetRequiredService<PlaybackInterceptors>());
        Assert.NotNull(harness.Manager);
    }
}
