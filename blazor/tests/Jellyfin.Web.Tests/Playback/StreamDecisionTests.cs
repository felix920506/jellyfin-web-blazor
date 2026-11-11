using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>Which source is played, how (direct play, direct stream, transcode) and from which URL.</summary>
public class StreamDecisionTests
{
    private const string Server = "http://jellyfin.test";

    [Fact]
    public async Task A_source_the_server_allows_to_direct_play_is_streamed_statically()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var api = harness.Get<JellyfinApi>();
        var source = new PlaybackSource(Source(PlaybackServer.MediaSource()));

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), source, 50_000_000);

        Assert.Equal(PlayMethods.DirectPlay, stream.PlayMethod);
        Assert.Equal(
            $"{Server}/Videos/{PlaybackServer.MovieId}/stream.mkv?Static=true&mediaSourceId={PlaybackServer.MovieId}"
            + $"&deviceId={Uri.EscapeDataString(api.DeviceId)}&ApiKey={Uri.EscapeDataString(api.AccessToken!)}&Tag=etag-1111",
            stream.Url);
        Assert.Equal("video/x-matroska", stream.MimeType);
        Assert.Equal(0, stream.TranscodingOffsetTicks);
        Assert.Equal(50_000_000, stream.PlayerStartPositionTicks);
        Assert.Equal("Video", stream.MediaType);
        Assert.Equal("Test Movie", stream.Title);
        Assert.Equal(string.Empty, stream.PlaySessionId ?? string.Empty);
    }

    [Fact]
    public async Task A_source_that_only_allows_direct_stream_uses_the_same_url_as_direct_stream()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var source = new PlaybackSource(Source(PlaybackServer.MediaSource(directPlay: false)));

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), source, null);

        Assert.Equal(PlayMethods.DirectStream, stream.PlayMethod);
        Assert.StartsWith($"{Server}/Videos/{PlaybackServer.MovieId}/stream.mkv?Static=true", stream.Url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_that_needs_transcoding_plays_the_servers_hls_url()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var source = new PlaybackSource(Source(PlaybackServer.MediaSource(directPlay: false, directStream: false, transcodingUrl: PlaybackServer.TranscodeUrl)));

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), source, 50_000_000);

        Assert.Equal(PlayMethods.Transcode, stream.PlayMethod);
        Assert.Equal(Server + PlaybackServer.TranscodeUrl, stream.Url);
        Assert.Equal("application/x-mpegURL", stream.MimeType);
        Assert.Equal("transcode-1", stream.PlaySessionId);

        // HLS keeps the item's timeline: no offset to add to the element's time.
        Assert.Equal(0, stream.TranscodingOffsetTicks);
    }

    [Fact]
    public async Task A_progressive_transcode_starts_at_zero_and_remembers_its_offset()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var json = PlaybackServer.MediaSource(directPlay: false, directStream: false, transcodingUrl: "/Videos/1/stream.webm?PlaySessionId=p-1");
        json["TranscodingSubProtocol"] = "http";
        json["TranscodingContainer"] = "webm";
        var copied = PlaybackServer.MediaSource(directPlay: false, directStream: false, transcodingUrl: "/Videos/1/stream.webm?CopyTimestamps=true");
        copied["TranscodingSubProtocol"] = "http";
        copied["TranscodingContainer"] = "webm";

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), new PlaybackSource(Source(json)), 50_000_000);
        var withTimestamps = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), new PlaybackSource(Source(copied)), 50_000_000);

        Assert.Equal("video/webm", stream.MimeType);
        Assert.Equal(50_000_000, stream.TranscodingOffsetTicks);
        Assert.Equal("p-1", stream.PlaySessionId);
        Assert.Equal(0, withTimestamps.TranscodingOffsetTicks);
    }

    [Fact]
    public async Task A_reachable_remote_source_is_played_from_its_own_address()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var json = PlaybackServer.MediaSource();
        json["Path"] = "http://media.example/movie.mkv";
        json["Protocol"] = "Http";
        var source = new PlaybackSource(Source(json)) { EnableDirectPlay = true };

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), source, null);

        Assert.Equal(PlayMethods.DirectPlay, stream.PlayMethod);
        Assert.Equal("http://media.example/movie.mkv", stream.Url);
    }

    [Fact]
    public async Task External_subtitle_streams_become_text_tracks()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var json = PlaybackServer.MediaSource();
        json["DefaultSubtitleStreamIndex"] = 0;

        var stream = harness.Manager.CreateStreamInfo("Video", PlaybackServer.Movie(), new PlaybackSource(Source(json)), null);

        var track = Assert.Single(stream.TextTracks);
        Assert.Equal($"{Server}/Videos/{PlaybackServer.MovieId}/{PlaybackServer.MovieId}/Subtitles/0/0/Stream.vtt?api_key=test-token", track.Url);
        Assert.Equal("eng", track.Language);
        Assert.Equal(0, track.Index);
        Assert.True(track.IsDefault);
    }

    [Fact]
    public async Task The_universal_audio_url_carries_the_profile_of_the_browser()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var api = harness.Get<JellyfinApi>();
        var profile = harness.Get<DeviceProfileBuilder>().GetDeviceProfile(FakeCapabilityProbe.Chrome, PlaybackServer.SongTicks, "Audio");

        var first = new Uri(harness.Manager.GetAudioStreamUrl(PlaybackServer.Song(), profile, 1_000_000, null));
        var second = new Uri(harness.Manager.GetAudioStreamUrl(PlaybackServer.Song(), profile, 1_000_000, 30_000_000));

        Assert.Equal($"/Audio/{PlaybackServer.SongId}/universal", first.AbsolutePath);
        var query = Query(first);
        Assert.Equal(
            new[]
            {
                "UserId", "DeviceId", "MaxStreamingBitrate", "Container", "TranscodingContainer", "TranscodingProtocol", "AudioCodec",
                "ApiKey", "PlaySessionId", "StartTimeTicks", "EnableRedirection", "EnableRemoteMedia", "EnableAudioVbrEncoding",
            },
            query.Select(pair => pair.Key));
        var values = query.ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(PlaybackServer.UserId, values["UserId"]);
        Assert.Equal(api.DeviceId, values["DeviceId"]);
        Assert.Equal("1000000", values["MaxStreamingBitrate"]);
        Assert.Equal("mp4", values["TranscodingContainer"]);
        Assert.Equal("hls", values["TranscodingProtocol"]);
        Assert.Equal("aac", values["AudioCodec"]);
        Assert.Equal(api.AccessToken, values["ApiKey"]);
        Assert.Equal("0", values["StartTimeTicks"]);
        Assert.Equal("true", values["EnableRedirection"]);
        Assert.Equal("false", values["EnableRemoteMedia"]);
        Assert.Equal("true", values["EnableAudioVbrEncoding"]);

        // Containers the element plays, "container|codec" where the profile names a codec.
        var containers = values["Container"].Split(',');
        Assert.Contains("mp3", containers);
        Assert.Contains("flac", containers);
        Assert.Contains("webm|opus", containers);
        Assert.Contains("m4a|aac", containers);
        Assert.Contains("ts|mp3", containers);

        // Each start gets its own play session, counted up from the clock.
        var firstSession = long.Parse(values["PlaySessionId"], System.Globalization.CultureInfo.InvariantCulture);
        var secondValues = Query(second).ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Equal(firstSession + 1, long.Parse(secondValues["PlaySessionId"], System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(firstSession > harness.Timers.NowMs - 1 && firstSession < harness.Timers.NowMs + 1000);
        Assert.Equal("30000000", secondValues["StartTimeTicks"]);
    }

    [Fact]
    public async Task The_playback_info_request_carries_the_choices_and_the_profile()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        var profile = new JsonObject { ["Name"] = "p" };

        var minimal = harness.Manager.BuildPlaybackInfoBody(profile, null, null, new PlaybackInfoOptions { MaxBitrate = 0 });
        var full = harness.Manager.BuildPlaybackInfoBody(
            new JsonObject { ["Name"] = "p" },
            "source-1",
            "live-1",
            new PlaybackInfoOptions
            {
                MaxBitrate = 8_000_000,
                StartPosition = 70_000_000,
                IsPlayback = true,
                AudioStreamIndex = 3,
                SubtitleStreamIndex = -1,
                EnableDirectPlay = false,
                EnableDirectStream = false,
                AllowVideoStreamCopy = false,
                AllowAudioStreamCopy = false,
            });

        Assert.Equal(
            new[] { "UserId", "StartTimeTicks", "IsPlayback", "AutoOpenLiveStream", "AlwaysBurnInSubtitleWhenTranscoding", "DeviceProfile" },
            minimal.Select(pair => pair.Key));
        Assert.Equal(PlaybackServer.UserId, (string?)minimal["UserId"]);
        Assert.Equal(0, (long)minimal["StartTimeTicks"]!);
        Assert.False((bool)minimal["IsPlayback"]!);
        Assert.False((bool)minimal["AutoOpenLiveStream"]!);
        Assert.False((bool)minimal["AlwaysBurnInSubtitleWhenTranscoding"]!);
        Assert.Equal("p", (string?)minimal["DeviceProfile"]!["Name"]);

        Assert.Equal(
            new[]
            {
                "UserId", "StartTimeTicks", "IsPlayback", "AutoOpenLiveStream", "AudioStreamIndex", "SubtitleStreamIndex", "EnableDirectPlay",
                "EnableDirectStream", "AllowVideoStreamCopy", "AllowAudioStreamCopy", "MediaSourceId", "LiveStreamId", "MaxStreamingBitrate",
                "AlwaysBurnInSubtitleWhenTranscoding", "DeviceProfile",
            },
            full.Select(pair => pair.Key));
        Assert.Equal(70_000_000, (long)full["StartTimeTicks"]!);
        Assert.True((bool)full["IsPlayback"]!);
        Assert.True((bool)full["AutoOpenLiveStream"]!);
        Assert.Equal(3, (int)full["AudioStreamIndex"]!);
        Assert.Equal(-1, (int)full["SubtitleStreamIndex"]!);
        Assert.False((bool)full["EnableDirectPlay"]!);
        Assert.False((bool)full["AllowAudioStreamCopy"]!);
        Assert.Equal("source-1", (string?)full["MediaSourceId"]);
        Assert.Equal("live-1", (string?)full["LiveStreamId"]);
        Assert.Equal(8_000_000, (int)full["MaxStreamingBitrate"]!);
    }

    [Fact]
    public async Task The_burn_in_setting_is_sent_with_the_playback_info_request()
    {
        await using var harness = await PlaybackHarness.CreateAsync();
        harness.Get<AppSettings>().Set("alwaysBurnInSubtitleWhenTranscoding", true);

        var body = harness.Manager.BuildPlaybackInfoBody(new JsonObject(), null, null, new PlaybackInfoOptions());

        Assert.True((bool)body["AlwaysBurnInSubtitleWhenTranscoding"]!);
    }

    [Fact]
    public void The_items_own_version_wins_when_it_can_be_played()
    {
        var item = PlaybackServer.Movie();
        var other = new PlaybackSource(Source(PlaybackServer.MediaSource(PlaybackServer.SecondMovieId))) { EnableDirectPlay = true };
        var own = new PlaybackSource(Source(PlaybackServer.MediaSource(directPlay: false, directStream: false)));

        Assert.Same(own, PlaybackManager.GetOptimalMediaSource(item, [other, own]));
        Assert.Null(PlaybackManager.GetOptimalMediaSource(item, []));
    }

    [Fact]
    public void Without_a_playable_own_version_direct_play_beats_direct_stream_beats_transcoding()
    {
        var item = PlaybackServer.Movie("33333333333333333333333333333333");
        var transcode = new PlaybackSource(Source(PlaybackServer.MediaSource("44444444444444444444444444444444", directPlay: false, directStream: false)));
        var directStream = new PlaybackSource(Source(PlaybackServer.MediaSource(PlaybackServer.SecondMovieId, directPlay: false)));
        var directPlay = new PlaybackSource(Source(PlaybackServer.MediaSource())) { EnableDirectPlay = true };

        Assert.Same(directPlay, PlaybackManager.GetOptimalMediaSource(item, [transcode, directStream, directPlay]));
        Assert.Same(directStream, PlaybackManager.GetOptimalMediaSource(item, [transcode, directStream]));
        Assert.Same(transcode, PlaybackManager.GetOptimalMediaSource(item, [transcode]));
    }

    [Fact]
    public void The_version_of_the_previous_item_is_chosen_again_by_name()
    {
        var hd = Source(PlaybackServer.MediaSource());
        hd.Name = "1080p";
        var sd = Source(PlaybackServer.MediaSource(PlaybackServer.SecondMovieId));
        sd.Name = "480p";
        var previous = Source(PlaybackServer.MediaSource("44444444444444444444444444444444"));
        previous.Name = "480p";

        Assert.Same(sd, PlaybackManager.GetMatchingMediaSource([hd, sd], new PreviousSource(previous, 2, null, HasAlternateVersions: true)));
        Assert.Null(PlaybackManager.GetMatchingMediaSource([hd, sd], new PreviousSource(previous, 2, null, HasAlternateVersions: false)));
        Assert.Null(PlaybackManager.GetMatchingMediaSource([sd], new PreviousSource(previous, 2, null, HasAlternateVersions: true)));
        Assert.Null(PlaybackManager.GetMatchingMediaSource([hd, sd], null));
    }

    [Fact]
    public void The_next_item_keeps_the_audio_language_and_the_subtitle_choice()
    {
        var previous = Source(PlaybackServer.MediaSource());
        var next = Source(PlaybackServer.MediaSource(PlaybackServer.SecondMovieId));

        // German (index 3) was chosen and subtitles were off.
        var kept = PlaybackManager.AutoSetNextTracks(new PreviousSource(previous, 3, -1, false), next.MediaStreams, audio: true, subtitle: true);
        var notRemembered = PlaybackManager.AutoSetNextTracks(new PreviousSource(previous, 3, 0, false), next.MediaStreams, audio: false, subtitle: false);
        var subtitleKept = PlaybackManager.AutoSetNextTracks(new PreviousSource(previous, null, 0, false), next.MediaStreams, audio: true, subtitle: true);

        Assert.Equal<(int?, int?)>((3, -1), kept);
        Assert.Equal<(int?, int?)>((null, null), notRemembered);
        Assert.Equal<(int?, int?)>((null, 0), subtitleKept);
        Assert.Equal<(int?, int?)>((null, null), PlaybackManager.AutoSetNextTracks(null, next.MediaStreams, audio: true, subtitle: true));
    }

    [Theory]
    [InlineData("audio", "opus", "audio/ogg")]
    [InlineData("audio", "webma", "audio/webm")]
    [InlineData("audio", "m4a", "audio/mp4")]
    [InlineData("audio", "MP3", "audio/mp3")]
    [InlineData("video", "mkv", "video/x-matroska")]
    [InlineData("video", "m4v", "video/mp4")]
    [InlineData("video", "mov", "video/quicktime")]
    [InlineData("video", "mpg", "video/mpeg")]
    [InlineData("video", "flv", "video/x-flv")]
    [InlineData("video", "webm", "video/webm")]
    public void Containers_map_to_the_mime_types_of_the_original(string type, string container, string expected) =>
        Assert.Equal(expected, PlaybackManager.GetMimeType(type, container));

    [Theory]
    [InlineData(null, "aac", true)]
    [InlineData("", "aac", true)]
    [InlineData("aac,mp3", "mp3", true)]
    [InlineData("aac,mp3", "opus", false)]
    [InlineData("aac,mp3", "opus,aac", true)]
    [InlineData("-aac,mp3", "mp3", false)]
    [InlineData("-aac,mp3", "opus", true)]
    public void Codec_lists_match_like_the_servers(string? list, string search, bool expected) =>
        Assert.Equal(expected, PlaybackManager.IncludesAny(list, search));

    [Fact]
    public void Hls_is_recognised_by_sub_protocol_or_container()
    {
        var hls = Source(PlaybackServer.MediaSource(transcodingUrl: PlaybackServer.TranscodeUrl));
        var container = Source(PlaybackServer.MediaSource(container: "hls"));

        Assert.True(PlaybackManager.IsHls(hls));
        Assert.True(PlaybackManager.IsHls(container));
        Assert.False(PlaybackManager.IsHls(Source(PlaybackServer.MediaSource())));
        Assert.False(PlaybackManager.IsHls(null));
    }

    [Fact]
    public void The_player_can_switch_to_an_audio_track_its_profile_covers()
    {
        var source = Source(PlaybackServer.MediaSource());
        var profile = new JsonObject
        {
            ["DirectPlayProfiles"] = new JsonArray(
                new JsonObject { ["Type"] = "Video", ["Container"] = "mkv", ["VideoCodec"] = "h264", ["AudioCodec"] = "aac,mp3" },
                new JsonObject { ["Type"] = "Audio", ["Container"] = "mkv", ["AudioCodec"] = "dts" }),
        };
        var other = new JsonObject
        {
            ["DirectPlayProfiles"] = new JsonArray(new JsonObject { ["Type"] = "Video", ["Container"] = "mp4", ["AudioCodec"] = "aac" }),
        };

        Assert.True(PlaybackManager.IsAudioStreamSupported(source, 3, profile));
        Assert.False(PlaybackManager.IsAudioStreamSupported(source, 3, other));
        Assert.False(PlaybackManager.IsAudioStreamSupported(source, 1, profile));
        Assert.False(PlaybackManager.IsAudioStreamSupported(source, 9, profile));
    }

    private static MediaSourceInfo Source(JsonObject json) =>
        KiotaJson.Parse(json.ToJsonString(), MediaSourceInfo.CreateFromDiscriminatorValue)!;

    private static List<KeyValuePair<string, string>> Query(Uri uri) =>
        uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Select(pair => new KeyValuePair<string, string>(Uri.UnescapeDataString(pair[0]), Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty)))
            .ToList();
}
