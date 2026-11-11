using Jellyfin.Web.Pages.User;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.User;

/// <summary>The option lists of the playback settings and the streaming bitrate settings behind the quality selects.</summary>
public class PlaybackOptionsTests : AppTestContext
{
    private AppSettings AppSettings => Services.GetRequiredService<AppSettings>();

    private Globalize Globalize => Services.GetRequiredService<Globalize>();

    [Fact]
    public void Quality_lists_start_with_automatic_and_descend()
    {
        var video = PlaybackOptions.QualityOptions(PlaybackOptions.VideoBitrates, Globalize);
        var audio = PlaybackOptions.QualityOptions(PlaybackOptions.AudioBitrates, Globalize);

        Assert.Equal(new SelectOption(string.Empty, Globalize["Auto"]), video[0]);
        Assert.Equal(new SelectOption("120000000", "120 Mbps"), video[1]);
        Assert.Equal(new SelectOption("420000", "420 kbps"), video[^1]);
        Assert.Equal(15, video.Count);
        Assert.Equal(new SelectOption("2000000", "2 Mbps"), audio[1]);
        Assert.Equal(new SelectOption("64000", "64 kbps"), audio[^1]);
        Assert.Equal(10, audio.Count);

        var videoBitrates = PlaybackOptions.VideoBitrates.Select(b => b.Bitrate).ToList();
        Assert.Equal(videoBitrates.OrderByDescending(b => b), videoBitrates);
        var audioBitrates = PlaybackOptions.AudioBitrates.Select(b => b.Bitrate).ToList();
        Assert.Equal(audioBitrates.OrderByDescending(b => b), audioBitrates);
    }

    [Fact]
    public void Skip_lengths_are_stored_in_milliseconds()
    {
        var options = PlaybackOptions.SkipLengths(Globalize);

        Assert.Equal(["5000", "10000", "15000", "20000", "25000", "30000"], options.Select(option => option.Value));
        Assert.Equal(Globalize.Translate("ValueSeconds", 5), options[0].Name);
        Assert.Contains("30", options[^1].Name, StringComparison.Ordinal);
    }

    [Fact]
    public void Still_watching_options_name_their_episode_count_and_time()
    {
        var options = PlaybackOptions.StillWatching(Globalize);

        Assert.Equal(["disabled", "short", "default", "long", "veryLong"], options.Select(option => option.Value));
        Assert.Equal(Globalize["Disabled"], options[0].Name);
        Assert.Equal(Globalize.Translate("StillWatchingOption", 2, 60), options[1].Name);
        Assert.Equal(Globalize.Translate("StillWatchingOption", 8, 240), options[4].Name);
    }

    [Fact]
    public void Segment_actions_are_translated()
    {
        var options = PlaybackOptions.SegmentActions(Globalize);

        Assert.Equal(["None", "AskToSkip", "Skip"], options.Select(option => option.Value));
        Assert.Equal(
            [Globalize["MediaSegmentAction.None"], Globalize["MediaSegmentAction.AskToSkip"], Globalize["MediaSegmentAction.Skip"]],
            options.Select(option => option.Name));
        Assert.Equal(["Intro", "Preview", "Recap", "Commercial", "Outro"], PlaybackOptions.MediaSegmentTypes);
        Assert.Equal("segmentTypeAction__Intro", PlaybackOptions.SegmentActionId("Intro"));
    }

    [Theory]
    [InlineData("Intro", "AskToSkip")]
    [InlineData("Outro", "AskToSkip")]
    [InlineData("Preview", "None")]
    [InlineData("Recap", "None")]
    [InlineData("Commercial", "None")]
    public void Intros_and_outros_ask_to_skip_by_default(string segmentType, string action)
    {
        SignIn();
        var settings = Services.GetRequiredService<UserSettings>();

        Assert.Equal(action, PlaybackOptions.SegmentAction(settings, segmentType));
    }

    [Fact]
    public async Task A_stored_segment_action_is_used()
    {
        SignIn();
        var settings = Services.GetRequiredService<UserSettings>();
        await settings.SetUserAsync(TestApp.UserId);
        this.StoreUserSetting("segmentTypeAction__Intro", "Skip");
        this.StoreUserSetting("segmentTypeAction__Recap", "AskToSkip");

        Assert.Equal("Skip", PlaybackOptions.SegmentAction(settings, "Intro"));
        Assert.Equal("AskToSkip", PlaybackOptions.SegmentAction(settings, "Recap"));
        Assert.Equal("AskToSkip", PlaybackOptions.SegmentAction(settings, "Outro"));
    }

    [Fact]
    public void Bitrate_settings_use_the_keys_of_the_original()
    {
        Assert.Equal("maxbitrate-Video-true", PlaybackOptions.MaxBitrateKey(true, "Video"));
        Assert.Equal("maxbitrate-Audio-false", PlaybackOptions.MaxBitrateKey(false, "Audio"));
        Assert.Equal("enableautobitratebitrate-Video-false", PlaybackOptions.AutoBitrateKey(false, "Video"));
        Assert.Equal("enableautobitratebitrate-Audio-true", PlaybackOptions.AutoBitrateKey(true, "Audio"));
    }

    [Fact]
    public void Without_settings_the_bitrate_is_detected_automatically()
    {
        Assert.True(PlaybackOptions.EnableAutomaticBitrateDetection(AppSettings, true, "Video"));
        Assert.True(PlaybackOptions.EnableAutomaticBitrateDetection(AppSettings, false, "Video"));
        Assert.True(PlaybackOptions.EnableAutomaticBitrateDetection(AppSettings, false, "Audio"));
        Assert.Equal(1500000, PlaybackOptions.MaxStreamingBitrate(AppSettings, true, "Video"));
        Assert.Equal(1500000, PlaybackOptions.MaxStreamingBitrate(AppSettings, false, "Audio"));
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, true, "Video"));
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, false, "Audio"));
    }

    [Fact]
    public void Music_on_the_home_network_is_never_limited()
    {
        this.StoreAppSetting("maxbitrate-Audio-true", "64000");
        this.StoreAppSetting("enableautobitratebitrate-Audio-true", "false");

        Assert.Equal(150000000, PlaybackOptions.MaxStreamingBitrate(AppSettings, true, "Audio"));
        Assert.True(PlaybackOptions.EnableAutomaticBitrateDetection(AppSettings, true, "Audio"));
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, true, "Audio"));

        PlaybackOptions.SaveBitrateField(AppSettings, "128000", true, "Audio");

        Assert.Equal("64000", this.AppSetting("maxbitrate-Audio-true"));
        Assert.Equal("true", this.AppSetting("enableautobitratebitrate-Audio-true"));
    }

    [Fact]
    public void A_chosen_bitrate_turns_automatic_detection_off()
    {
        PlaybackOptions.SaveBitrateField(AppSettings, "8000000", false, "Video");

        Assert.Equal("8000000", this.AppSetting("maxbitrate-Video-false"));
        Assert.Equal("false", this.AppSetting("enableautobitratebitrate-Video-false"));
        Assert.False(PlaybackOptions.EnableAutomaticBitrateDetection(AppSettings, false, "Video"));
        Assert.Equal(8000000, PlaybackOptions.MaxStreamingBitrate(AppSettings, false, "Video"));
        Assert.Equal("8000000", PlaybackOptions.BitrateFieldValue(AppSettings, false, "Video"));
        // The other network is not affected.
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, true, "Video"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Automatic_keeps_the_bitrate_and_turns_detection_on(string? value)
    {
        PlaybackOptions.SaveBitrateField(AppSettings, "8000000", false, "Video");

        PlaybackOptions.SaveBitrateField(AppSettings, value, false, "Video");

        Assert.Equal("8000000", this.AppSetting("maxbitrate-Video-false"));
        Assert.Equal("true", this.AppSetting("enableautobitratebitrate-Video-false"));
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, false, "Video"));
    }

    [Fact]
    public void A_bitrate_that_is_not_on_offer_selects_nothing()
    {
        this.StoreAppSetting("maxbitrate-Video-true", "7000000");
        this.StoreAppSetting("enableautobitratebitrate-Video-true", "false");
        // Video and music have their own lists.
        this.StoreAppSetting("maxbitrate-Audio-false", "8000000");
        this.StoreAppSetting("enableautobitratebitrate-Audio-false", "false");

        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, true, "Video"));
        Assert.Equal(string.Empty, PlaybackOptions.BitrateFieldValue(AppSettings, false, "Audio"));
        Assert.Equal(7000000, PlaybackOptions.MaxStreamingBitrate(AppSettings, true, "Video"));
    }

    [Fact]
    public void Detection_off_without_a_bitrate_selects_the_fallback_bitrate()
    {
        this.StoreAppSetting("enableautobitratebitrate-Video-false", "false");

        Assert.Equal("1500000", PlaybackOptions.BitrateFieldValue(AppSettings, false, "Video"));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("0", "")]
    [InlineData("abc", "")]
    [InlineData("5000000", "")]
    [InlineData("4000000", "4000000")]
    public void The_cast_bitrate_is_shown_when_it_is_on_offer(string? stored, string expected)
    {
        if (stored is not null)
        {
            this.StoreAppSetting("chromecastBitrate1", stored);
        }

        Assert.Equal(expected, PlaybackOptions.ChromecastFieldValue(AppSettings));
    }
}
