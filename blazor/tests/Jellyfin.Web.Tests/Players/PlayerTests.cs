using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Players;

public sealed class PlayerTests
{
    [Fact]
    public void Players_match_only_their_supported_items_and_urls()
    {
        var app = new TestApp();
        app.SignIn();
        var api = app.Get<JellyfinApi>();
        var settings = app.Get<UserSettings>();
        var interop = new FakePlayersInterop();
        var photo = new PhotoPlayer(interop, api, settings);
        var epub = new EpubPlayer(interop, api);
        var pdf = new PdfPlayer(interop, api);
        var comics = new ComicsPlayer(interop, api);
        var url = new UrlPlayer(interop, api);
        var youtube = new YoutubePlayer(interop, api);

        Assert.True(photo.CanPlayItem(new BaseItemDto { MediaType = BaseItemDto_MediaType.Photo, Path = "/photo.jpg" }));
        Assert.True(epub.CanPlayItem(Book("story.EPUB")));
        Assert.True(pdf.CanPlayItem(Book("manual.PdF")));
        Assert.True(comics.CanPlayItem(Book("issue.CBZ")));
        Assert.False(pdf.CanPlayItem(Book("issue.cbz")));
        Assert.True(youtube.CanPlayUrl("https://youtu.be/abc123"));
        Assert.False(url.CanPlayUrl("https://www.youtube.com/watch?v=abc123"));
        Assert.True(url.CanPlayUrl("https://media.example.test/track.mp3"));
    }

    [Fact]
    public async Task Managed_player_passes_queue_order_start_index_and_position_to_interop()
    {
        var app = new TestApp();
        app.SignIn();
        var interop = new FakePlayersInterop();
        var player = new PhotoPlayer(interop, app.Get<JellyfinApi>(), app.Get<UserSettings>());
        var items = new[]
        {
            new BaseItemDto { MediaType = BaseItemDto_MediaType.Photo, Name = "First", Path = "/first.jpg" },
            new BaseItemDto { MediaType = BaseItemDto_MediaType.Photo, Name = "Second", Path = "/second.jpg" },
        };

        var stream = await player.PlayItemsAsync(items, new ItemPlayOptions { StartIndex = 1, StartPositionTicks = 20_000 });

        var request = Assert.Single(interop.Opens);
        Assert.Equal("photo", request.Kind);
        Assert.Equal(1, request.StartIndex);
        Assert.Equal(20_000, request.StartPositionTicks);
        Assert.Equal("Second", stream.Item.Name);
        Assert.Equal(PlayMethods.DirectPlay, stream.PlayMethod);
        Assert.NotNull(player.CurrentSrc);
    }

    [Fact]
    public async Task Interop_stop_event_clears_the_current_source_and_raises_player_event()
    {
        var app = new TestApp();
        app.SignIn();
        var interop = new FakePlayersInterop();
        var player = new UrlPlayer(interop, app.Get<JellyfinApi>());
        var events = new List<string>();
        player.Event += (_, eventInfo) => events.Add(eventInfo.Name);

        await player.PlayItemsAsync(
            [new BaseItemDto { MediaType = BaseItemDto_MediaType.Video, Path = "https://media.example.test/movie.mp4" }],
            new ItemPlayOptions());
        interop.Raise("url", "stopped", new PlayerSnapshot { Paused = true, CurrentTime = 3 });

        Assert.Null(player.CurrentSrc);
        Assert.Contains("stopped", events);
    }

    private static BaseItemDto Book(string path) => new()
    {
        MediaType = BaseItemDto_MediaType.Book,
        Type = BaseItemDto_Type.Book,
        Path = path,
    };

    private sealed class FakePlayersInterop : IPlayersInterop
    {
        public event Action<string, string, PlayerSnapshot>? Event;
        public List<PlayerOpenRequest> Opens { get; } = [];

        public Task<PlayerSnapshot> OpenAsync(PlayerOpenRequest request)
        {
            Opens.Add(request);
            return Task.FromResult(new PlayerSnapshot { Paused = false, Seekable = true, CurrentTime = request.StartIndex });
        }

        public Task StopAsync(string kind, bool destroy) => Task.CompletedTask;
        public void Pause(string kind) { }
        public void Unpause(string kind) { }
        public void Next(string kind) { }
        public void Previous(string kind) { }
        public void SetCurrentTime(string kind, double seconds) { }
        public void ToggleFullscreen(string kind) { }

        public void Raise(string kind, string name, PlayerSnapshot snapshot) => Event?.Invoke(kind, name, snapshot);
    }
}
