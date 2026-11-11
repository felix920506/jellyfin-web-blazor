using Jellyfin.Web.Pages.User;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Web.Tests.User;

/// <summary>What the user settings pages share: the edited user and the settings object.</summary>
public class UserPagesTests : AppTestContext
{
    private JellyfinApi Api => Services.GetRequiredService<JellyfinApi>();

    [Fact]
    public void Without_a_parameter_the_signed_in_user_is_edited()
    {
        SignIn();

        Assert.Equal(TestApp.UserId, UserPages.ResolveUserId(Api, null));
        Assert.Equal(TestApp.UserId, UserPages.ResolveUserId(Api, string.Empty));
    }

    [Theory]
    [InlineData("11111111222233334444555555555555")]
    [InlineData("11111111-2222-3333-4444-555555555555")]
    public void The_user_id_parameter_names_the_edited_user(string parameter)
    {
        SignIn();

        Assert.Equal(SettingsTestExtensions.OtherUserId, UserPages.ResolveUserId(Api, parameter));
    }

    [Fact]
    public void An_unreadable_parameter_falls_back_to_the_signed_in_user()
    {
        SignIn();

        Assert.Equal(TestApp.UserId, UserPages.ResolveUserId(Api, "not-an-id"));
    }

    [Fact]
    public void Signed_out_there_is_no_user()
    {
        Assert.Null(UserPages.ResolveUserId(Api, null));
    }

    [Fact]
    public void The_signed_in_user_edits_the_apps_own_settings()
    {
        SignIn();
        var current = Services.GetRequiredService<UserSettings>();
        var appSettings = Services.GetRequiredService<AppSettings>();

        Assert.Same(current, UserPages.SettingsFor(TestApp.UserId, Api, current, appSettings));
    }

    [Fact]
    public void Another_user_gets_separate_settings()
    {
        SignIn(admin: true);
        var current = Services.GetRequiredService<UserSettings>();
        var appSettings = Services.GetRequiredService<AppSettings>();

        var other = UserPages.SettingsFor(SettingsTestExtensions.OtherUserId, Api, current, appSettings);

        Assert.NotSame(current, other);
    }

    [Fact]
    public void Ids_are_written_without_dashes()
    {
        Assert.Equal("11111111222233334444555555555555", UserPages.IdText(SettingsTestExtensions.OtherUserId));
        Assert.Equal(string.Empty, UserPages.IdText(null));
    }

    [Fact]
    public async Task The_user_is_read_and_the_configuration_posted_for_the_given_user()
    {
        SignIn(admin: true);
        var id = SettingsTestExtensions.OtherUserId;
        this.ScriptUser(id, new { Id = id, Name = "guest", Configuration = new { PlayDefaultAudioTrack = true } });
        App.Server.OnPost("/Users/Configuration");

        var user = await UserPages.GetUserAsync(Api, id);
        Assert.NotNull(user);
        Assert.Equal("guest", user.Name);
        Assert.NotNull(user.Configuration);
        user.Configuration.HidePlayedInLatest = true;
        await UserPages.UpdateConfigurationAsync(Api, id, user.Configuration);

        var request = this.PostedConfiguration();
        Assert.NotNull(request);
        Assert.True(request.QueryIs("userId", id));
        Assert.Contains("\"HidePlayedInLatest\":true", request.Body, StringComparison.Ordinal);
        Assert.Contains("\"PlayDefaultAudioTrack\":true", request.Body, StringComparison.Ordinal);
    }
}
