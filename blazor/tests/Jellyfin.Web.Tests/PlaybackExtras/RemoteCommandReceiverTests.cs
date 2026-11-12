using System.Text.Json.Nodes;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.PlaybackExtras;

/// <summary>
/// This client as the target of a remote control: what the "Play", "Playstate" and
/// "GeneralCommand" messages of the web socket do here.
/// </summary>
public class RemoteCommandReceiverTests
{
    private static RemoteCommandReceiver Receiver(PlaybackHarness harness, FakeRemoteInput? input = null) => new(
        harness.Get<ServerEvents>(),
        harness.Manager,
        harness.Get<AppRouter>(),
        harness.Get<AppDialogs>(),
        harness.Get<ToastService>(),
        harness.Get<JellyfinApi>(),
        input ?? new FakeRemoteInput(),
        harness.Timers);

    private static Task CommandAsync(RemoteCommandReceiver receiver, string name, params (string Name, string Value)[] arguments)
    {
        var message = new JsonObject { ["Name"] = name };
        if (arguments.Length > 0)
        {
            // The server sends the arguments of a command as strings.
            var values = new JsonObject();
            foreach (var (key, value) in arguments)
            {
                values[key] = value;
            }

            message["Arguments"] = values;
        }

        return receiver.OnGeneralCommandAsync(RemoteSessions.Element(message));
    }

    private static Task PlaystateAsync(RemoteCommandReceiver receiver, string command, long? seekPositionTicks = null)
    {
        var message = new JsonObject { ["Command"] = command };
        if (seekPositionTicks is not null)
        {
            message["SeekPositionTicks"] = seekPositionTicks;
        }

        return receiver.OnPlaystateAsync(RemoteSessions.Element(message));
    }

    private static AppRouter StartRouter(PlaybackHarness harness, string at = "/home")
    {
        var router = harness.Get<AppRouter>();
        router.Start();
        router.Navigate(at);
        return router;
    }

    [Fact]
    public async Task A_play_message_starts_playback_here()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        Receiver(harness);

        // Through the web socket, as the server sends it.
        harness.Get<ServerEvents>().Dispatch("Play", RemoteSessions.Element(new JsonObject
        {
            ["ItemIds"] = new JsonArray(PlaybackServer.MovieId, PlaybackServer.SecondMovieId),
            ["PlayCommand"] = "PlayNow",
            ["StartPositionTicks"] = 200_000_000,
            ["StartIndex"] = 1,
        }));

        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 1, "the start of playback");
        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
        Assert.Equal(20, harness.Player.LastLoad.StartSeconds);
        Assert.Equal(["Test Movie", "Second Movie"], harness.Manager.GetPlaylist().Select(i => i.Name));
    }

    [Fact]
    public async Task Play_next_and_play_last_add_to_the_queue()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        await receiver.OnPlayAsync(RemoteSessions.Element(new JsonObject { ["ItemIds"] = new JsonArray(PlaybackServer.SongId), ["PlayCommand"] = "PlayNow" }));
        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);

        await receiver.OnPlayAsync(RemoteSessions.Element(new JsonObject { ["ItemIds"] = new JsonArray(PlaybackServer.ThirdSongId), ["PlayCommand"] = "PlayLast" }));
        await receiver.OnPlayAsync(RemoteSessions.Element(new JsonObject { ["ItemIds"] = new JsonArray(PlaybackServer.SecondSongId), ["PlayCommand"] = "PlayNext" }));

        Assert.Equal(["Opening", "Middle", "Closing"], harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);
        Assert.Single(harness.Player.Loads);
    }

    [Fact]
    public async Task Playstate_messages_control_what_plays_here()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        await harness.PlayMoviesAsync();
        harness.Player.Tick("video", 15);

        await PlaystateAsync(receiver, "Pause");
        Assert.True(harness.Manager.IsPaused);
        await PlaystateAsync(receiver, "Unpause");
        Assert.False(harness.Manager.IsPaused);
        await PlaystateAsync(receiver, "PlayPause");
        Assert.True(harness.Manager.IsPaused);
        await PlaystateAsync(receiver, "PlayPause");
        Assert.False(harness.Manager.IsPaused);

        await PlaystateAsync(receiver, "Seek", 250_000_000);
        Assert.Equal("seek video 25", harness.Player.Calls[^1]);
        await PlaystateAsync(receiver, "FastForward");
        Assert.Equal("seek video 55", harness.Player.Calls[^1]);
        await PlaystateAsync(receiver, "Rewind");
        Assert.Equal("seek video 45", harness.Player.Calls[^1]);

        await PlaystateAsync(receiver, "NextTrack");
        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
        await PlaystateAsync(receiver, "PreviousTrack");
        Assert.Equal("Test Movie", harness.Manager.CurrentItem?.Name);

        await PlaystateAsync(receiver, "Stop");
        Assert.False(harness.Manager.IsPlaying);

        // An unknown command is ignored.
        await PlaystateAsync(receiver, "Dance");
    }

    [Fact]
    public async Task A_second_stop_within_a_second_is_ignored()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        await harness.PlayMovieAsync();

        await PlaystateAsync(receiver, "Stop");
        Assert.False(harness.Manager.IsPlaying);

        // What was started right after it is not stopped by the echo of the first stop.
        await harness.PlayMovieAsync();
        await harness.Timers.AdvanceAsync(400);
        await PlaystateAsync(receiver, "Stop");
        Assert.True(harness.Manager.IsPlaying);

        await harness.Timers.AdvanceAsync(700);
        await PlaystateAsync(receiver, "Stop");
        Assert.False(harness.Manager.IsPlaying);
    }

    [Fact]
    public async Task Volume_and_mode_commands_reach_the_player()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        await harness.PlaySongsAsync();

        await CommandAsync(receiver, "SetVolume", ("Volume", "40"));
        Assert.Equal(40, harness.Manager.Volume);
        await CommandAsync(receiver, "VolumeUp");
        Assert.Equal(42, harness.Manager.Volume);
        await CommandAsync(receiver, "VolumeDown");
        Assert.Equal(40, harness.Manager.Volume);
        await CommandAsync(receiver, "Mute");
        Assert.True(harness.Manager.IsMuted);
        await CommandAsync(receiver, "Unmute");
        Assert.False(harness.Manager.IsMuted);
        await CommandAsync(receiver, "ToggleMute");
        Assert.True(harness.Manager.IsMuted);

        await CommandAsync(receiver, "SetRepeatMode", ("RepeatMode", "RepeatAll"));
        Assert.Equal(PlayQueue.RepeatAll, harness.Manager.RepeatMode);
        await CommandAsync(receiver, "SetShuffleQueue", ("ShuffleMode", "Shuffle"));
        Assert.Equal(PlayQueue.Shuffled, harness.Manager.ShuffleMode);
        await CommandAsync(receiver, "SetShuffleQueue", ("ShuffleMode", "Sorted"));
        Assert.Equal(PlayQueue.Sorted, harness.Manager.ShuffleMode);

        // A command without its argument changes nothing.
        await CommandAsync(receiver, "SetVolume");
        Assert.Equal(40, harness.Manager.Volume);
    }

    [Fact]
    public async Task Track_commands_switch_the_streams_of_the_video()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        await harness.PlayMovieAsync();

        await CommandAsync(receiver, "SetSubtitleStreamIndex", ("Index", "0"));
        Assert.Equal(0, harness.Manager.SubtitleStreamIndex);
        await CommandAsync(receiver, "SetSubtitleStreamIndex", ("Index", "-1"));
        Assert.Equal(-1, harness.Manager.SubtitleStreamIndex);

        await CommandAsync(receiver, "SetAudioStreamIndex", ("Index", "3"));
        Assert.Equal(3, harness.Manager.AudioStreamIndex);

        await CommandAsync(receiver, "ToggleFullscreen");
        Assert.Equal("fullscreen", harness.Player.Calls[^1]);
    }

    [Fact]
    public async Task Navigation_commands_move_the_focus_and_the_page()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var input = new FakeRemoteInput();
        var receiver = Receiver(harness, input);
        StartRouter(harness);

        foreach (var name in new[] { "MoveUp", "MoveDown", "MoveLeft", "MoveRight", "Select" })
        {
            await CommandAsync(receiver, name);
        }

        await CommandAsync(receiver, "SendString", ("String", "blade runner"));
        Assert.Equal(["move up", "move down", "move left", "move right", "select", "text blade runner"], input.Calls);

        await CommandAsync(receiver, "GoToSettings");
        Assert.Equal("/mypreferencesmenu", harness.App.Browser.Hash);
        await CommandAsync(receiver, "GoToSearch");
        Assert.Equal("/search", harness.App.Browser.Hash);
        await CommandAsync(receiver, "Back");
        Assert.Equal("/mypreferencesmenu", harness.App.Browser.Hash);
        await CommandAsync(receiver, "GoHome");
        Assert.Equal("/home", harness.App.Browser.Hash);

        // There is nothing behind the home page.
        await CommandAsync(receiver, "Back");
        Assert.Equal("/home", harness.App.Browser.Hash);

        // Commands the original does nothing for either.
        foreach (var name in new[] { "PageUp", "PageDown", "ChannelUp", "ChannelDown", "ToggleOsd", "ToggleContextMenu", "SendKey" })
        {
            await CommandAsync(receiver, name);
        }

        Assert.Equal("/home", harness.App.Browser.Hash);
        Assert.Equal(6, input.Calls.Count);
    }

    [Fact]
    public async Task Display_content_opens_the_item_unless_a_video_plays_here()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        StartRouter(harness);

        await CommandAsync(receiver, "DisplayContent", ("ItemName", "Test Movie"), ("ItemId", PlaybackServer.MovieId), ("ItemType", "Movie"));
        Assert.Equal($"/details?id={PlaybackServer.MovieId}&serverId={TestApp.ServerId}", harness.App.Browser.Hash);

        // Music does not mind another page ...
        await harness.PlaySongsAsync();
        await CommandAsync(receiver, "DisplayContent", ("ItemId", PlaybackServer.SecondMovieId));
        Assert.Equal($"/details?id={PlaybackServer.SecondMovieId}&serverId={TestApp.ServerId}", harness.App.Browser.Hash);

        // ... a video does: its page is the player.
        await harness.PlayMovieAsync();
        Assert.Equal("/video", harness.App.Browser.Hash);
        await CommandAsync(receiver, "DisplayContent", ("ItemId", PlaybackServer.MovieId));
        Assert.Equal("/video", harness.App.Browser.Hash);
    }

    [Fact]
    public async Task A_message_is_a_toast_with_a_timeout_and_a_dialog_without()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        var toasts = harness.Get<ToastService>();

        await CommandAsync(receiver, "DisplayMessage", ("Header", "Kitchen"), ("Text", "Dinner is ready"), ("TimeoutMs", "5000"));
        Assert.Equal("Dinner is ready", Assert.Single(toasts.Messages).Text);
        Assert.Equal(0, harness.DialogCount);

        // The dialog stays until it is answered; the command does not wait for that here.
        var shown = CommandAsync(receiver, "DisplayMessage", ("Header", "Kitchen"), ("Text", "It gets cold"));
        Assert.Equal(1, harness.DialogCount);
        Assert.Single(toasts.Messages);
        harness.Get<AppDialogs>().CloseAll();
        await shown;
    }

    [Fact]
    public async Task Play_trailers_plays_the_trailers_of_the_item()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var receiver = Receiver(harness);
        var movie = PlaybackServer.Movie();
        movie.LocalTrailerCount = 1;
        PlaybackServer.ScriptItems(harness.Server, movie);
        harness.Server.OnGet(
            $"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}/LocalTrailers",
            _ => new JsonArray(PlaybackServer.ToJson(PlaybackServer.Movie(PlaybackServer.SecondMovieId, "Second Movie"))).ToJsonString());

        await CommandAsync(receiver, "PlayTrailers", ("ItemId", PlaybackServer.MovieId));

        await PlaybackHarness.Eventually(() => harness.Player.Loads.Count == 1, "the trailer");
        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
    }
}
