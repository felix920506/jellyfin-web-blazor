using Jellyfin.Web.Components;
using Jellyfin.Web.Pages.Playback;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Tests.PlaybackExtras;

internal sealed class FakeLyricsUi : IPlaybackLyricsUi
{
    public List<(int Index, bool Smooth)> Scrolls { get; } = [];
    public Lyrics? Page { get; private set; }
    public bool Detached { get; private set; }

    public Task AttachAsync<T>(ElementReference container, DotNetObjectReference<T> target) where T : class
    {
        Page = target.Value as Lyrics;
        return Task.CompletedTask;
    }

    public Task ScrollAsync(ElementReference container, int index, bool smooth)
    {
        Scrolls.Add((index, smooth));
        return Task.CompletedTask;
    }

    public Task DetachAsync(ElementReference container)
    {
        Detached = true;
        return Task.CompletedTask;
    }
}

public class LyricsTests : BunitContext, IAsyncLifetime
{
    private readonly FakeLyricsUi _ui = new();
    private readonly TestApp _app = new();
    private readonly PlaybackFakes _fakes = new();
    private FakeHttp Server => _app.Server;
    private FakePlayerInterop Player => _fakes.Player;
    private PlaybackManager Manager => Services.GetRequiredService<PlaybackManager>();

    public async Task InitializeAsync()
    {
        _app.Configure(Services);
        _fakes.AddTo(Services);
        Services.AddSingleton<IPlaybackLyricsUi>(_ui);
        JSInterop.Mode = JSRuntimeMode.Loose;
        await _app.LoadAssetsAsync(Services);
        _app.SignIn(services: Services);
        PlaybackServer.FixBitrate(_app);
        PlaybackServer.ScriptMedia(Server);
    }

    private Task PlaySongsAsync() => Manager.PlayAsync(new PlayOptions { Items = PlaybackServer.Songs() });
    private Task PlayMovieAsync() => Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId] });
    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();

    private void Synced(string id = PlaybackServer.SongId) => Server.OnGet($"/Audio/{id}/Lyrics", new
    {
        Lyrics = new[]
        {
            new { Text = "<First & line>", Start = 10_000_000L },
            new { Text = "Second line", Start = 50_000_000L },
            new { Text = "Third line", Start = 100_000_000L },
        },
    });

    [Fact]
    public void Timeline_uses_ticks_last_matching_timestamp_and_no_match_before_start()
    {
        PlaybackLyricLine[] lines = [new("metadata", null), new("one", 10), new("two", 10), new("three", 30)];
        Assert.Equal(-1, PlaybackLyrics.CurrentIndex(lines, 0));
        Assert.Equal(2, PlaybackLyrics.CurrentIndex(lines, 10));
        Assert.Equal(2, PlaybackLyrics.CurrentIndex(lines, 29));
        Assert.Equal(3, PlaybackLyrics.CurrentIndex(lines, 30));
        Assert.Equal(-1, PlaybackLyrics.CurrentIndex([], long.MaxValue));
    }

    [Fact]
    public async Task Synced_lines_update_scroll_and_seek_resumes_paused_playback()
    {
        Synced();
        await PlaySongsAsync();
        var cut = Render<Lyrics>();
        cut.WaitForAssertion(() => Assert.Equal(3, cut.FindAll(".lyricsLine").Count));
        Assert.Contains("&lt;First &amp; line&gt;", cut.Markup);
        Assert.All(cut.FindAll(".lyricsLine"), line => Assert.Contains("futureLyric", line.ClassList));
        Player.Tick("audio", 1);
        cut.WaitForAssertion(() => Assert.Contains((0, false), _ui.Scrolls));
        Assert.DoesNotContain("futureLyric", cut.Find("#lyricPosition0").ClassList);
        Player.Tick("audio", 6);
        cut.WaitForAssertion(() => Assert.Contains((1, true), _ui.Scrolls));
        Assert.Contains("pastLyric", cut.Find("#lyricPosition0").ClassList);
        Assert.Contains("futureLyric", cut.Find("#lyricPosition2").ClassList);

        _ui.Page!.OnManualScroll();
        var scrolls = _ui.Scrolls.Count;
        Player.Tick("audio", 11);
        cut.WaitForAssertion(() => Assert.Contains("pastLyric", cut.Find("#lyricPosition1").ClassList));
        Assert.Equal(scrolls, _ui.Scrolls.Count);

        await Manager.PauseAsync();
        cut.Find("#lyricPosition1").Click();
        cut.WaitForAssertion(() => Assert.False(Manager.IsPaused));
        Assert.Equal(50_000_000, Manager.CurrentTimeTicks);
        Assert.Contains("seek audio 5", Player.Calls);
        cut.WaitForAssertion(() => Assert.True(_ui.Scrolls.Count > scrolls));
        await cut.Instance.DisposeAsync();
        Assert.True(_ui.Detached);
    }

    [Fact]
    public async Task Plain_lyrics_are_not_seekable_and_empty_lyrics_show_the_original_message()
    {
        Server.OnGet($"/Audio/{PlaybackServer.SongId}/Lyrics", new { Lyrics = new[] { new { Text = "Plain words" } } });
        Server.OnGet($"/Audio/{PlaybackServer.SecondSongId}/Lyrics", new { Lyrics = Array.Empty<object>() });
        await PlaySongsAsync();
        var cut = Render<Lyrics>();
        Assert.Equal("Plain words", cut.Find(".lyricsLine").TextContent);
        Assert.Empty(cut.FindAll(".dynamicLyric"));
        var calls = Player.Calls.Count;
        cut.Find(".lyricsLine").Click();
        Assert.Equal(calls, Player.Calls.Count);
        await Manager.NextTrackAsync();
        cut.WaitForAssertion(() => Assert.NotEmpty(cut.FindAll(".lyricsContainer h1")));
        Assert.Empty(cut.FindAll(".lyricsLine"));
    }

    [Fact]
    public async Task Queue_and_now_playing_buttons_follow_the_original_visibility_rules()
    {
        var song = PlaybackServer.Song();
        song.HasLyrics = true;
        await Manager.PlayAsync(new PlayOptions { Items = [song] });
        // Playback reloads the item; mark that full server item as containing lyrics.
        Manager.CurrentItem!.HasLyrics = true;
        var bar = Render<NowPlayingBar>();
        Assert.DoesNotContain("hide", bar.Find(".openLyricsButton").ClassList);
        var queue = Render<Queue>();
        Assert.DoesNotContain("hide", queue.Find(".btnLyrics").ClassList);
        await PlayMovieAsync();
        queue.WaitForAssertion(() => Assert.Contains("hide", queue.Find(".btnLyrics").ClassList));
    }
}
