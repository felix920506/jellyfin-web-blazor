using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>
/// The media elements of the browser, faked: loads succeed (unless <see cref="LoadResult"/> says
/// otherwise) and every command answers with the event the real element raises for it.
/// </summary>
internal sealed class FakePlayerInterop : IPlayerInterop
{
    private readonly Dictionary<string, PlayerSnapshot> _snapshots = new()
    {
        ["video"] = new PlayerSnapshot { Paused = true },
        ["audio"] = new PlayerSnapshot { Paused = true },
    };

    public event Action<string, string, PlayerSnapshot, string?>? PlayerEvent;

    public event Action<string, double?>? MediaSessionAction;

    /// <summary>Everything that was loaded, in order.</summary>
    public List<(string Kind, PlayerLoadRequest Request)> Loads { get; } = [];

    /// <summary>The commands in order, e.g. "pause video", "stop audio destroy=True fade=True".</summary>
    public List<string> Calls { get; } = [];

    /// <summary>Decides the result of a load; null means every load succeeds.</summary>
    public Func<string, PlayerLoadRequest, PlayerLoadResult>? LoadResult { get; set; }

    /// <summary>Duration the element reports for what it loads.</summary>
    public double DurationSeconds { get; set; } = 60;

    public List<IReadOnlyList<SubtitleCue>?> SubtitleCues { get; } = [];

    public List<IReadOnlyList<SubtitleCue>?> SecondarySubtitleCues { get; } = [];

    public List<(SubtitleRendererRequest? Request, bool Secondary)> SubtitleRenderers { get; } = [];

    public List<MediaSessionInfo?> MediaSessions { get; } = [];

    public PlayerStats? Stats { get; set; } = new() { VideoWidth = 1280, VideoHeight = 720, ViewWidth = 1366, ViewHeight = 768, DroppedFrames = 2 };

    public PlayerSnapshot Snapshot(string kind) => _snapshots[kind];

    public PlayerLoadRequest LastLoad => Loads[^1].Request;

    public Task<PlayerLoadResult> LoadAsync(string kind, PlayerLoadRequest request)
    {
        Loads.Add((kind, request));
        var result = LoadResult?.Invoke(kind, request) ?? new PlayerLoadResult { Ok = true };
        if (result.Ok)
        {
            _snapshots[kind] = _snapshots[kind] with
            {
                CurrentTime = request.TranscodingOffsetSeconds > 0 ? 0 : request.StartSeconds,
                Duration = DurationSeconds,
                Volume = request.Volume ?? _snapshots[kind].Volume,
                Paused = false,
                Seekable = true,
                Buffered = [],
            };
            Emit(kind, "firstplaying");
            Emit(kind, "playing");
        }

        return Task.FromResult(result);
    }

    public Task StopAsync(string kind, bool destroy, bool fade)
    {
        Calls.Add($"stop {kind} destroy={destroy} fade={fade}");
        Emit(kind, "stopped", s => s with { Paused = true });
        return Task.CompletedTask;
    }

    public void Destroy(string kind) => Calls.Add("destroy " + kind);

    public void Pause(string kind)
    {
        Calls.Add("pause " + kind);
        Emit(kind, "pause", s => s with { Paused = true });
    }

    public void Unpause(string kind)
    {
        Calls.Add("unpause " + kind);
        Emit(kind, "unpause", s => s with { Paused = false });
    }

    public void SetCurrentTime(string kind, double seconds)
    {
        Calls.Add(FormattableString.Invariant($"seek {kind} {seconds}"));
        Emit(kind, "timeupdate", s => s with { CurrentTime = seconds });
    }

    public void SetVolume(string kind, double elementVolume)
    {
        Calls.Add(FormattableString.Invariant($"volume {kind} {elementVolume:0.###}"));
        Emit(kind, "volumechange", s => s with { Volume = elementVolume });
    }

    public void SetMute(string kind, bool mute)
    {
        Calls.Add($"mute {kind} {mute}");
        Emit(kind, "volumechange", s => s with { Muted = mute });
    }

    public void SetPlaybackRate(string kind, double rate)
    {
        Calls.Add(FormattableString.Invariant($"rate {kind} {rate}"));
        Emit(kind, "ratechange", s => s with { PlaybackRate = rate });
    }

    public void SetAspectRatio(string value) => Calls.Add("aspect " + value);

    public void SetAudioTrack(int position) => Calls.Add("audiotrack " + position);

    public void SetSubtitleCues(IReadOnlyList<SubtitleCue>? cues)
    {
        Calls.Add("cues " + (cues?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
        SubtitleCues.Add(cues);
    }

    public void SetSecondarySubtitleCues(IReadOnlyList<SubtitleCue>? cues)
    {
        Calls.Add("secondary cues " + (cues?.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"));
        SecondarySubtitleCues.Add(cues);
    }

    public void SetSubtitleRenderer(SubtitleRendererRequest? request, bool secondary)
    {
        Calls.Add($"renderer {(secondary ? "secondary" : "primary")} {request?.Codec ?? "none"}");
        SubtitleRenderers.Add((request, secondary));
    }

    public void SetSubtitleRendererOffset(double seconds, bool secondary) =>
        Calls.Add(FormattableString.Invariant($"renderer offset {(secondary ? "secondary" : "primary")} {seconds}"));

    public void ToggleFullscreen()
    {
        Calls.Add("fullscreen");
        Emit("video", "fullscreenchange", s => s with { Fullscreen = !s.Fullscreen });
    }

    public void SetPictureInPicture(bool enabled)
    {
        Calls.Add("pip " + enabled);
        Emit("video", "pictureinpicturechange", s => s with { PictureInPicture = enabled });
    }

    public void SetAirPlayEnabled(string kind, bool enabled)
    {
        Calls.Add($"airplay {kind} {enabled}");
        Emit(kind, "airplaychange", s => s with { AirPlay = enabled });
    }

    public void NavigatedToOsd() => Calls.Add("osd");

    public Task<PlayerStats?> GetStatsAsync(string kind) => Task.FromResult(Stats);

    public void SetMediaSession(MediaSessionInfo? info) => MediaSessions.Add(info);

    /// <summary>Raises an element event, optionally changing the element's state first.</summary>
    public void Emit(string kind, string name, Func<PlayerSnapshot, PlayerSnapshot>? change = null, string? errorType = null)
    {
        if (change is not null)
        {
            _snapshots[kind] = change(_snapshots[kind]);
        }

        PlayerEvent?.Invoke(kind, name, _snapshots[kind], errorType);
    }

    /// <summary>The element reports a new position ("timeupdate").</summary>
    public void Tick(string kind, double seconds) => Emit(kind, "timeupdate", s => s with { CurrentTime = seconds });

    /// <summary>The element reaches the end of its source.</summary>
    public void End(string kind) => Emit(kind, "stopped", s => s with { Paused = true });

    public void RaiseMediaSessionAction(string action, double? seekTime = null) => MediaSessionAction?.Invoke(action, seekTime);
}

/// <summary>A browser's answers to the capability probe.</summary>
internal sealed class FakeCapabilityProbe : IMediaCapabilityProbe
{
    public const string H264 = "video/mp4; codecs=\"avc1.42E01E, mp4a.40.2\"";
    public const string Vp8 = "video/webm; codecs=\"vp8\"";
    public const string Vp9 = "video/webm; codecs=\"vp9\"";
    public const string H264High = "video/mp4; codecs=\"avc1.640029, mp4a.40.2\"";
    public const string HeAac = "video/mp4; codecs=\"avc1.640029, mp4a.40.5\"";
    public const string NativeHls = "application/vnd.apple.mpegURL";
    public const string SafariUserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.4 Safari/605.1.15";
    public const string FirefoxUserAgent = "Mozilla/5.0 (X11; Linux x86_64; rv:131.0) Gecko/20100101 Firefox/131.0";

    public MediaCapabilities Caps { get; set; } = Chrome;

    public int Probes { get; private set; }

    public MediaCapabilities? Current { get; private set; }

    /// <summary>
    /// A desktop Chrome: H.264/AAC, VP8/VP9/AV1, MP3, Opus, FLAC, Media Source Extensions. The type
    /// strings are the ones the device profile asks for.
    /// </summary>
    public static MediaCapabilities Chrome => new()
    {
        VideoTypes =
        [
            H264, H264High, HeAac,
            "video/mp4; codecs=\"avc1.640029, mp4a.69\"", "video/mp4; codecs=\"avc1.640029, mp4a.6B\"", "video/mp4; codecs=\"avc1.640029, mp3\"",
            Vp8, Vp9, "video/mp4; codecs=\"av01.0.15M.08\"", "video/mp4; codecs=\"av01.0.15M.10\"", "video/x-matroska",
        ],
        AudioTypes = ["audio/ogg; codecs=\"opus\"", "audio/webm", "audio/mp3", "audio/aac", "audio/flac", "audio/wav", "audio/ogg"],
        MediaSource = true,
        TextTracks = true,
        AudioTracks = false,
        SpeakerCount = 2,
        PictureInPicture = true,
        Fullscreen = true,
    };

    /// <summary>A Chromium without the licensed codecs: the open ones only.</summary>
    public static MediaCapabilities OpenCodecsOnly => Chrome with
    {
        VideoTypes = [Vp8, Vp9],
        AudioTypes = ["audio/ogg; codecs=\"opus\"", "audio/webm", "audio/mp3", "audio/flac", "audio/wav", "audio/ogg"],
    };

    /// <summary>A Safari: native HLS, H.264/HEVC, AC-3, audio tracks, no VP9.</summary>
    public static MediaCapabilities Safari => new()
    {
        VideoTypes =
        [
            H264, H264High, HeAac, "video/mp4; codecs=\"hvc1.1.L120\"", "video/mp4; codecs=\"hvc1.1.0.L120\"", NativeHls, "application/x-mpegURL",
            "audio/mp4; codecs=\"ac-3\"", "audio/mp4; codecs=\"ec-3\"", "application/vnd.apple.mpegURL; codecs=\"avc1.42E01E, ac-3\"",
        ],
        AudioTypes = ["audio/mp3", "audio/aac", "audio/flac", "audio/wav"],
        MediaSource = true,
        TextTracks = true,
        AudioTracks = true,
        SpeakerCount = 2,
        PictureInPicture = true,
        Fullscreen = true,
        AirPlay = true,
    };

    public Task<MediaCapabilities> GetAsync()
    {
        Probes++;
        Current = Caps;
        return Task.FromResult(Caps);
    }
}

/// <summary>A clock that only moves when the test says so.</summary>
internal sealed class FakePlaybackTimers : IPlaybackTimers
{
    private readonly object _gate = new();
    private readonly List<Interval> _intervals = [];
    private readonly List<(long Due, TaskCompletionSource Source)> _delays = [];

    public long NowMs { get; set; } = 1_750_000_000_000;

    /// <summary>The periods of the running intervals.</summary>
    public IReadOnlyList<int> Intervals
    {
        get
        {
            lock (_gate)
            {
                return _intervals.Select(i => i.Period).ToList();
            }
        }
    }

    /// <summary>The delays somebody is waiting for, as milliseconds from now.</summary>
    public IReadOnlyList<long> PendingDelays
    {
        get
        {
            lock (_gate)
            {
                return _delays.Select(d => d.Due - NowMs).ToList();
            }
        }
    }

    public IDisposable SetInterval(Func<Task> callback, int milliseconds)
    {
        var interval = new Interval(this, callback, milliseconds) { Next = NowMs + milliseconds };
        lock (_gate)
        {
            _intervals.Add(interval);
        }

        return interval;
    }

    public Task Delay(int milliseconds)
    {
        if (milliseconds <= 0)
        {
            return Task.CompletedTask;
        }

        var source = new TaskCompletionSource();
        lock (_gate)
        {
            _delays.Add((NowMs + milliseconds, source));
        }

        return source.Task;
    }

    /// <summary>Moves the clock, running the delays and intervals that come due, in time order.</summary>
    public async Task AdvanceAsync(long milliseconds)
    {
        var target = NowMs + milliseconds;
        while (true)
        {
            TaskCompletionSource? delay = null;
            Interval? interval = null;
            lock (_gate)
            {
                var nextDelay = _delays.Where(d => d.Due <= target).OrderBy(d => d.Due).Cast<(long Due, TaskCompletionSource Source)?>().FirstOrDefault();
                var nextInterval = _intervals.Where(i => i.Next <= target).OrderBy(i => i.Next).FirstOrDefault();
                if (nextDelay is { } due && (nextInterval is null || due.Due <= nextInterval.Next))
                {
                    _delays.Remove(due);
                    NowMs = Math.Max(NowMs, due.Due);
                    delay = due.Source;
                }
                else if (nextInterval is not null)
                {
                    NowMs = Math.Max(NowMs, nextInterval.Next);
                    nextInterval.Next += nextInterval.Period;
                    interval = nextInterval;
                }
            }

            if (delay is not null)
            {
                delay.SetResult();
            }
            else if (interval is not null)
            {
                await interval.Callback();
            }
            else
            {
                break;
            }
        }

        NowMs = target;
    }

    private sealed class Interval(FakePlaybackTimers owner, Func<Task> callback, int period) : IDisposable
    {
        public Func<Task> Callback { get; } = callback;

        public int Period { get; } = period;

        public long Next { get; set; }

        public void Dispose()
        {
            lock (owner._gate)
            {
                owner._intervals.Remove(this);
            }
        }
    }
}

/// <summary>Keeps the components the OSD's document listeners would call, so tests can call them.</summary>
internal sealed class FakePlaybackUiInterop : IPlaybackUiInterop
{
    public object? Osd { get; private set; }

    public List<object> Sliders { get; } = [];

    public int OsdDetached { get; private set; }

    public Task AttachOsdAsync<T>(DotNetObjectReference<T> target)
        where T : class
    {
        Osd = target.Value;
        return Task.CompletedTask;
    }

    public void DetachOsd()
    {
        Osd = null;
        OsdDetached++;
    }

    public Task AttachSliderAsync<T>(ElementReference input, DotNetObjectReference<T> target)
        where T : class
    {
        Sliders.Add(target.Value);
        return Task.CompletedTask;
    }

    public void DetachSlider(ElementReference input)
    {
    }
}

/// <summary>The fakes every playback test replaces the browser with.</summary>
internal sealed class PlaybackFakes
{
    public FakePlayerInterop Player { get; } = new();

    public FakeCapabilityProbe Probe { get; } = new();

    public FakePlaybackTimers Timers { get; } = new();

    public FakePlaybackUiInterop Ui { get; } = new();

    public void AddTo(IServiceCollection services)
    {
        services.AddSingleton<IPlayerInterop>(Player);
        services.AddSingleton<IMediaCapabilityProbe>(Probe);
        services.AddSingleton<IPlaybackTimers>(Timers);
        services.AddSingleton<IPlaybackUiInterop>(Ui);
    }
}

/// <summary>Media of the scripted server and the answers playback needs from it.</summary>
internal static class PlaybackServer
{
    public const string MovieId = "11111111111111111111111111111111";
    public const string SecondMovieId = "22222222222222222222222222222222";
    public const string SongId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa1";
    public const string SecondSongId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa2";
    public const string ThirdSongId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa3";
    public const long MovieTicks = 600_000_000;
    public const long SongTicks = 300_000_000;

    public static string UserId => TestApp.UserId.ToString("N");

    public static BaseItemDto Movie(string id = MovieId, string name = "Test Movie", long runTimeTicks = MovieTicks) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = BaseItemDto_Type.Movie,
        MediaType = BaseItemDto_MediaType.Video,
        RunTimeTicks = runTimeTicks,
        ProductionYear = 2015,
        UserData = new UserItemDataDto(),
    };

    public static BaseItemDto Song(string id = SongId, string name = "Opening", int index = 1) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = BaseItemDto_Type.Audio,
        MediaType = BaseItemDto_MediaType.Audio,
        RunTimeTicks = SongTicks,
        Album = "First Album",
        IndexNumber = index,
        Artists = ["Test Artist"],
        UserData = new UserItemDataDto(),
    };

    public static BaseItemDto Episode(string id, string name, int index, bool played = false) => new()
    {
        Id = Guid.Parse(id),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = BaseItemDto_Type.Episode,
        MediaType = BaseItemDto_MediaType.Video,
        SeriesId = Guid.Parse("5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e"),
        SeriesName = "Test Show",
        ParentIndexNumber = 1,
        IndexNumber = index,
        RunTimeTicks = MovieTicks,
        UserData = new UserItemDataDto { Played = played },
    };

    /// <summary>A media source as PlaybackInfo returns it: an mkv with H.264, two AAC tracks and an external SRT.</summary>
    public static JsonObject MediaSource(
        string id = MovieId,
        bool directPlay = true,
        bool directStream = true,
        string? transcodingUrl = null,
        string container = "mkv",
        long runTimeTicks = MovieTicks) => new()
    {
        ["Id"] = id,
        ["Protocol"] = "File",
        ["Container"] = container,
        ["RunTimeTicks"] = runTimeTicks,
        ["SupportsDirectPlay"] = directPlay,
        ["SupportsDirectStream"] = directStream,
        ["SupportsTranscoding"] = true,
        ["ETag"] = "etag-" + id[..4],
        ["Bitrate"] = 4_000_000,
        ["DefaultAudioStreamIndex"] = 2,
        ["TranscodingUrl"] = transcodingUrl,
        ["TranscodingSubProtocol"] = transcodingUrl is null ? null : "hls",
        ["TranscodingContainer"] = transcodingUrl is null ? null : "ts",
        ["MediaStreams"] = new JsonArray(
            new JsonObject
            {
                ["Index"] = 0, ["Type"] = "Subtitle", ["Codec"] = "subrip", ["Language"] = "eng", ["IsExternal"] = true,
                ["IsTextSubtitleStream"] = true, ["SupportsExternalStream"] = true, ["DeliveryMethod"] = "External",
                ["DeliveryUrl"] = $"/Videos/{id}/{id}/Subtitles/0/0/Stream.vtt?api_key=test-token", ["DisplayTitle"] = "English - SUBRIP - External",
            },
            new JsonObject { ["Index"] = 1, ["Type"] = "Video", ["Codec"] = "h264", ["Width"] = 1280, ["Height"] = 720, ["BitRate"] = 3_500_000, ["DisplayTitle"] = "720p H264 SDR" },
            new JsonObject { ["Index"] = 2, ["Type"] = "Audio", ["Codec"] = "aac", ["Language"] = "eng", ["Channels"] = 2, ["IsDefault"] = true, ["DisplayTitle"] = "English - AAC - Stereo - Default" },
            new JsonObject { ["Index"] = 3, ["Type"] = "Audio", ["Codec"] = "aac", ["Language"] = "ger", ["Channels"] = 2, ["DisplayTitle"] = "German - AAC - Stereo" }),
    };

    public static JsonObject PlaybackInfo(params JsonObject[] sources) => new()
    {
        ["MediaSources"] = new JsonArray(sources.Select(s => (JsonNode?)s).ToArray()),
        ["PlaySessionId"] = "session-1",
    };

    public const string TranscodeUrl =
        "/videos/11111111-1111-1111-1111-111111111111/master.m3u8?DeviceId=test&MediaSourceId=11111111111111111111111111111111&VideoCodec=h264&AudioCodec=aac&PlaySessionId=transcode-1&TranscodeReasons=ContainerNotSupported";

    /// <summary>
    /// Scripts what every start of playback asks for: the endpoint info, the session reports and the
    /// items (each answers under <c>Users/{user}/Items/{id}</c> and in <c>Items</c> queries by id).
    /// </summary>
    public static void Script(FakeHttp server, params BaseItemDto[] items)
    {
        server.OnGet("/System/Endpoint", new { IsLocal = false, IsInNetwork = false });
        server.OnGet("/Playback/BitrateTest", _ => new byte[0]);
        server.OnPost("/Sessions/Playing");
        server.OnPost("/Sessions/Playing/Ping");
        server.OnPost("/Sessions/Playing/Progress");
        server.OnPost("/Sessions/Playing/Stopped");
        server.OnDelete("/Videos/ActiveEncodings");
        server.OnGet("/MediaSegments/", new JsonObject { ["Items"] = new JsonArray() });
        server.OnGet("/Items", request =>
        {
            var ids = (request.Query("ids") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
            var found = items.Where(i => ids.Contains(i.Id!.Value.ToString("N"))).Select(ToJson).ToArray();
            return new JsonObject { ["Items"] = new JsonArray(found), ["TotalRecordCount"] = found.Length };
        });
        foreach (var item in items)
        {
            server.OnGet($"/Users/{UserId}/Items/{item.Id!.Value:N}", _ => ToJson(item));
        }
    }

    public static JsonNode? ToJson(BaseItemDto item) => JsonNode.Parse(KiotaJson.Serialize(item));

    /// <summary>Makes further items known to the server (a start of playback fetches the item again).</summary>
    public static void ScriptItems(FakeHttp server, params BaseItemDto[] items)
    {
        foreach (var item in items)
        {
            server.OnGet($"/Users/{UserId}/Items/{item.Id!.Value:N}", _ => ToJson(item));
        }
    }

    /// <summary>The bitrate tests play with: saved, with the automatic detection switched off.</summary>
    public const int Bitrate = 8_000_000;

    /// <summary>
    /// Saves <see cref="Bitrate"/> as the limit outside the network and switches the detection off, so
    /// that a start of playback does not measure the (scripted) connection.
    /// </summary>
    public static void FixBitrate(TestApp app)
    {
        foreach (var mediaType in new[] { "Video", "Audio" })
        {
            app.Browser.LocalStorage[$"enableautobitratebitrate-{mediaType}-false"] = "false";
            app.Browser.LocalStorage[$"maxbitrate-{mediaType}-false"] = "8000000";
        }
    }

    /// <summary>
    /// Answers PlaybackInfo for an item: with a source that plays directly, or, once the client asks
    /// for a stream without direct play, with <paramref name="transcoded"/> (an HLS transcode).
    /// </summary>
    public static void ScriptPlaybackInfo(FakeHttp server, string itemId = MovieId, JsonObject? direct = null, JsonObject? transcoded = null)
    {
        server.OnPost($"/Items/{itemId}/PlaybackInfo", request =>
        {
            var body = JsonNode.Parse(request.Body ?? "{}")!.AsObject();
            var wantsTranscode = body["EnableDirectPlay"]?.GetValue<bool>() == false || body["MaxStreamingBitrate"]?.GetValue<int>() < 1_000_000;
            var source = wantsTranscode
                ? transcoded ?? MediaSource(itemId, directPlay: false, directStream: false, transcodingUrl: TranscodeUrl)
                : direct ?? MediaSource(itemId);
            return FakeHttp.Json(PlaybackInfo(source.DeepClone().AsObject()).ToJsonString());
        });
    }

    /// <summary>The three songs of "First Album".</summary>
    public static BaseItemDto[] Songs() => [Song(), Song(SecondSongId, "Middle", 2), Song(ThirdSongId, "Closing", 3)];

    /// <summary>Two movies that negotiate direct play (and an HLS transcode on request) and three songs.</summary>
    public static void ScriptMedia(FakeHttp server)
    {
        Script(server, [Movie(), Movie(SecondMovieId, "Second Movie"), .. Songs()]);
        ScriptPlaybackInfo(server);
        ScriptPlaybackInfo(server, SecondMovieId);
    }

    /// <summary>The bodies of the PlaybackInfo requests for an item, in order.</summary>
    public static List<JsonObject> PlaybackInfoRequests(FakeHttp server, string itemId = MovieId) =>
        server.RequestsTo($"/Items/{itemId}/PlaybackInfo").Select(r => JsonNode.Parse(r.Body ?? "{}")!.AsObject()).ToList();
}

/// <summary>The app's services with the playback fakes, for tests without components.</summary>
internal sealed class PlaybackHarness : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private PlaybackHarness(TestApp app, PlaybackFakes fakes, ServiceProvider provider)
    {
        App = app;
        Fakes = fakes;
        _provider = provider;
    }

    public TestApp App { get; }

    public PlaybackFakes Fakes { get; }

    public FakeHttp Server => App.Server;

    public FakePlayerInterop Player => Fakes.Player;

    public FakePlaybackTimers Timers => Fakes.Timers;

    public IServiceProvider Services => _provider;

    public PlaybackManager Manager => Get<PlaybackManager>();

    public static async Task<PlaybackHarness> CreateAsync(Action<TestApp>? prepare = null, Action<UserDto>? user = null, bool signIn = true)
    {
        var app = new TestApp();
        prepare?.Invoke(app);
        var fakes = new PlaybackFakes();
        var services = new ServiceCollection();
        app.Configure(services);
        fakes.AddTo(services);
        var provider = services.BuildServiceProvider();
        await app.LoadAssetsAsync(provider);
        if (signIn)
        {
            app.SignIn(services: provider, configure: user);
        }

        return new PlaybackHarness(app, fakes, provider);
    }

    /// <summary>A signed-in app with a fixed bitrate whose server has <see cref="PlaybackServer.ScriptMedia"/>.</summary>
    public static async Task<PlaybackHarness> WithMediaAsync(Action<UserDto>? user = null, Action<TestApp>? prepare = null)
    {
        var harness = await CreateAsync(
            app =>
            {
                PlaybackServer.FixBitrate(app);
                prepare?.Invoke(app);
            },
            user);
        PlaybackServer.ScriptMedia(harness.Server);
        return harness;
    }

    /// <summary>Plays the first movie by its id, as a card does.</summary>
    public Task PlayMovieAsync(long? startPositionTicks = null) =>
        Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId], StartPositionTicks = startPositionTicks });

    /// <summary>Plays the two movies as a queue.</summary>
    public Task PlayMoviesAsync() =>
        Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId, PlaybackServer.SecondMovieId] });

    /// <summary>Plays the three songs as a queue.</summary>
    public Task PlaySongsAsync() => Manager.PlayAsync(new PlayOptions { Items = PlaybackServer.Songs() });

    public int DialogCount => Get<AppDialogs>().Dialogs.Count;

    public T Get<T>()
        where T : notnull => _provider.GetRequiredService<T>();

    /// <summary>The JSON bodies posted to a path, in order.</summary>
    public List<JsonObject> Posts(string pathSuffix) =>
        Server.RequestsTo(pathSuffix)
            .Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith(pathSuffix, StringComparison.OrdinalIgnoreCase))
            .Select(r => JsonNode.Parse(r.Body ?? "{}")!.AsObject())
            .ToList();

    public List<JsonObject> PlayingPingReports => Server.Requests
        .Where(r => r.Method == HttpMethod.Post && r.Path.EndsWith("/Sessions/Playing/Ping", StringComparison.OrdinalIgnoreCase))
        .Select(r => JsonNode.Parse(r.Body ?? "{}")!.AsObject())
        .ToList();

    public List<JsonObject> ProgressReports => Posts("/Sessions/Playing/Progress");

    public List<JsonObject> StartReports => Posts("/Sessions/Playing");

    public List<JsonObject> PlayingPings => PlayingPingReports;

    public List<JsonObject> StopReports => Posts("/Sessions/Playing/Stopped");

    /// <summary>Waits for something that happens on a continuation (reports are not awaited by the manager).</summary>
    public static async Task Eventually(Func<bool> condition, string because = "the condition")
    {
        for (var i = 0; i < 400 && !condition(); i++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition(), because + " did not become true");
    }

    public ValueTask DisposeAsync() => _provider.DisposeAsync();
}

/// <summary>Base class of the playback component tests: a bUnit context over the playback fakes.</summary>
public abstract class PlaybackTestContext : BunitContext, IAsyncLifetime
{
    internal TestApp App { get; private set; } = default!;

    internal PlaybackFakes Fakes { get; } = new();

    internal FakeHttp Server => App.Server;

    internal FakePlayerInterop Player => Fakes.Player;

    internal PlaybackManager Manager => Services.GetRequiredService<PlaybackManager>();

    public virtual async Task InitializeAsync()
    {
        App = new TestApp();
        App.Configure(Services);
        Fakes.AddTo(Services);
        JSInterop.Mode = JSRuntimeMode.Loose;
        await App.LoadAssetsAsync(Services);
        App.SignIn(services: Services);
        PlaybackServer.FixBitrate(App);
        PlaybackServer.ScriptMedia(Server);
    }

    internal Task PlayMovieAsync() => Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.MovieId] });

    internal Task PlaySongsAsync() => Manager.PlayAsync(new PlayOptions { Items = PlaybackServer.Songs() });

    async Task IAsyncLifetime.DisposeAsync() => await DisposeAsync();
}
