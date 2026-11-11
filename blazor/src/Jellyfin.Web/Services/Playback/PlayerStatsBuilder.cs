using System.Globalization;
using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>A block of the stats overlay: an optional header and its lines.</summary>
public sealed record PlayerStatCategory(string? Name, IReadOnlyList<PlayerStat> Stats, string? SubText = null);

/// <summary>
/// Port of the data side of components/playerstats/playerstats.js and of
/// components/playback/playMethodHelper.ts: what the "Playback info" overlay shows.
/// </summary>
[AppService]
public sealed class PlayerStatsBuilder(PlaybackManager playback, JellyfinApi api, Globalize globalize, IPlaybackTimers timers)
{
    /// <summary>The server is asked for this device's session at most this often.</summary>
    public const int SessionIntervalMs = 10000;

    private SessionInfoDto? _lastSession;
    private long _lastSessionTime = long.MinValue;

    /// <summary>Forgets the cached session (the overlay was closed or the player changed).</summary>
    public void Reset()
    {
        _lastSession = null;
        _lastSessionTime = long.MinValue;
    }

    /// <summary>Port of getStats(): the categories for the current player.</summary>
    public async Task<IReadOnlyList<PlayerStatCategory>> GetStatsAsync()
    {
        if (playback.CurrentPlayer is not { } player)
        {
            return [];
        }

        var playerStatsTask = player.GetStatsAsync();
        var session = await GetSessionAsync();
        var playerStats = await playerStatsTask;

        var displayPlayMethod = GetDisplayPlayMethod(session);
        var localizedDisplayMethod = displayPlayMethod switch
        {
            "DirectPlay" => globalize["DirectPlaying"],
            "Remux" => globalize["Remuxing"],
            "DirectStream" => globalize["DirectStreaming"],
            "Transcode" => globalize["Transcoding"],
            _ => displayPlayMethod,
        };

        var categories = new List<PlayerStatCategory>
        {
            new(globalize["LabelPlaybackInfo"], [new PlayerStat(globalize["LabelPlayer"], $"{player.Name}  ({localizedDisplayMethod})")]),
        };
        categories.AddRange(PlayerCategories(playerStats));

        if (session.TranscodingInfo is { } transcodingInfo)
        {
            var name = displayPlayMethod switch
            {
                "Remux" => globalize["LabelRemuxingInfo"],
                "DirectStream" => globalize["LabelDirectStreamingInfo"],
                _ => globalize["LabelTranscodingInfo"],
            };
            categories.Add(new PlayerStatCategory(name, TranscodingStats(transcodingInfo, displayPlayMethod)));
        }

        categories.Add(new PlayerStatCategory(globalize["LabelOriginalMediaInfo"], MediaSourceStats()));

        // PORT-TODO(playback): the SyncPlay category ("LabelSyncPlayInfo") belongs to the SyncPlay workstream.
        return categories;
    }

    /// <summary>Port of playMethodHelper.getDisplayPlayMethod().</summary>
    public static string? GetDisplayPlayMethod(SessionInfoDto session)
    {
        if (session.NowPlayingItem is null)
        {
            return null;
        }

        if (session.TranscodingInfo is { } info)
        {
            if ((info.IsVideoDirect == true || string.IsNullOrEmpty(info.VideoCodec)) && info.IsAudioDirect == true)
            {
                return "Remux";
            }

            if (info.IsVideoDirect == true)
            {
                return "DirectStream";
            }
        }

        return session.PlayState?.PlayMethod.ToApiString() switch
        {
            "Transcode" => "Transcode",
            "DirectStream" or "DirectPlay" => "DirectPlay",
            _ => null,
        };
    }

    /// <summary>Port of getSession(): this device's session as the server sees it.</summary>
    private async Task<SessionInfoDto> GetSessionAsync()
    {
        if (_lastSession is not null && timers.NowMs - _lastSessionTime < SessionIntervalMs)
        {
            return _lastSession;
        }

        try
        {
            using var response = await api.SendAsync(HttpMethod.Get, api.GetUrl("Sessions", ("deviceId", api.DeviceId)));
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            _lastSession = KiotaJson.ParseList(document.RootElement, SessionInfoDto.CreateFromDiscriminatorValue).FirstOrDefault() ?? new SessionInfoDto();
            _lastSessionTime = timers.NowMs;
            return _lastSession;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return new SessionInfoDto();
        }
    }

    /// <summary>The categories the player reports itself (getStats() of the HTML players).</summary>
    private List<PlayerStatCategory> PlayerCategories(PlayerStats? stats)
    {
        var categories = new List<PlayerStatCategory>();
        if (stats is null)
        {
            return categories;
        }

        var mediaInfos = new List<string> { stats.Hls ? "HLS" : "Video" };
        if (!string.IsNullOrEmpty(stats.Protocol))
        {
            mediaInfos.Add($"({stats.Protocol})");
        }

        categories.Add(new PlayerStatCategory(null, [new PlayerStat(globalize["LabelStreamType"], string.Join("  ", mediaInfos))]));

        var video = new List<PlayerStat>();
        var viewInfos = new List<string>();
        if (stats.ViewWidth > 0 && stats.ViewHeight > 0)
        {
            viewInfos.Add(Invariant($"{stats.ViewWidth}x{stats.ViewHeight}"));
        }

        if (stats.VideoWidth > 0 && stats.VideoHeight > 0)
        {
            viewInfos.Add(Invariant($"{stats.VideoWidth}x{stats.VideoHeight}"));
        }

        if (viewInfos.Count > 0)
        {
            video.Add(new PlayerStat(globalize["LabelPlayerSizes"], string.Join(" / ", viewInfos)));
        }

        if (stats.DroppedFrames is { } dropped)
        {
            video.Add(new PlayerStat(globalize["LabelPlaybackQuality"], dropped.ToString(CultureInfo.InvariantCulture)));
        }

        categories.Add(new PlayerStatCategory(null, video));

        var audio = new List<PlayerStat>();
        if (!string.IsNullOrEmpty(stats.SinkId))
        {
            audio.Add(new PlayerStat("Sink Id:", stats.SinkId));
        }

        categories.Add(new PlayerStatCategory(null, audio));
        return categories;
    }

    /// <summary>Port of getTranscodingStats().</summary>
    private List<PlayerStat> TranscodingStats(TranscodingInfo info, string? displayPlayMethod)
    {
        var stats = new List<PlayerStat>();
        var targetInfos = new List<string>();
        var transcodeInfos = new List<string>();

        if (!string.IsNullOrEmpty(info.VideoCodec))
        {
            var codec = info.VideoCodec.ToUpperInvariant();
            targetInfos.Add(info.IsVideoDirect == true ? $"{codec} (direct)" : codec);
        }

        if (!string.IsNullOrEmpty(info.AudioCodec))
        {
            var codec = info.AudioCodec.ToUpperInvariant();
            targetInfos.Add(info.IsAudioDirect == true ? $"{codec} (direct)" : codec);
        }

        if (displayPlayMethod == "Transcode")
        {
            if (info.AudioChannels is > 0)
            {
                targetInfos.Add(Invariant($"{info.AudioChannels} Ch"));
            }

            if (info.Bitrate is > 0)
            {
                targetInfos.Add(PlaybackText.DisplayBitrate(info.Bitrate.Value));
            }

            if (info.CompletionPercentage is { } completion && completion != 0)
            {
                transcodeInfos.Add(completion.ToString("0.0", CultureInfo.InvariantCulture) + "%");
            }

            if (info.Framerate is { } framerate && framerate != 0)
            {
                var videoStream = (playback.CurrentMediaSource?.MediaStreams ?? []).FirstOrDefault(s => s.Type.ToApiString() == "Video");
                var fps = Number(framerate) + " fps";
                transcodeInfos.Add(videoStream?.ReferenceFrameRate is { } original && original != 0
                    ? fps + " (" + (framerate / original).ToString("0.00", CultureInfo.InvariantCulture) + "x)"
                    : fps);
            }
        }

        if (targetInfos.Count > 0)
        {
            stats.Add(new PlayerStat(globalize["LabelTargetCodecs"], string.Join(",  ", targetInfos)));
        }

        if (transcodeInfos.Count > 0)
        {
            stats.Add(new PlayerStat(globalize["LabelProgressAndSpeed"], string.Join(" / ", transcodeInfos)));
        }

        var reasons = (info.TranscodeReasons ?? [])
            .Select(r => r.ToApiString())
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => globalize.Translate(r!))
            .ToList();
        if (reasons.Count > 0)
        {
            // One reason per line (the overlay keeps the line breaks).
            stats.Add(new PlayerStat(globalize["LabelReasons"], string.Join('\n', reasons)));
        }

        return stats;
    }

    /// <summary>Port of getMediaSourceStats().</summary>
    private List<PlayerStat> MediaSourceStats()
    {
        var stats = new List<PlayerStat>();
        var mediaSource = playback.CurrentMediaSource;
        if (mediaSource is null)
        {
            return stats;
        }

        var containerInfos = new List<string>();
        if (!string.IsNullOrEmpty(mediaSource.Container))
        {
            containerInfos.Add(mediaSource.Container);
        }

        if (mediaSource.Size is > 0)
        {
            containerInfos.Add(PlaybackText.ReadableSize(mediaSource.Size.Value));
        }

        if (mediaSource.Bitrate is > 0)
        {
            containerInfos.Add(PlaybackText.DisplayBitrate(mediaSource.Bitrate.Value));
        }

        Add(stats, "LabelProfileContainer", containerInfos, ",  ");

        var videoStream = (mediaSource.MediaStreams ?? []).FirstOrDefault(s => s.Type.ToApiString() == "Video");
        var audioStreamIndex = playback.AudioStreamIndex;
        var audioStream = playback.AudioTracks().FirstOrDefault(s => s.Index == audioStreamIndex);

        var videoCodecInfos = new List<string>();
        if (!string.IsNullOrEmpty(videoStream?.Codec))
        {
            videoCodecInfos.Add(videoStream.Codec.ToUpperInvariant());
        }

        if (!string.IsNullOrEmpty(videoStream?.VideoDoViTitle))
        {
            videoCodecInfos.Add(videoStream.VideoDoViTitle);
        }

        Add(stats, "LabelVideoCodec", videoCodecInfos, "  ");

        var videoAttributes = new List<string>();
        if (!string.IsNullOrEmpty(videoStream?.Profile))
        {
            videoAttributes.Add(videoStream.Profile);
        }

        if (videoStream?.Level is > 0)
        {
            videoAttributes.Add("Lv " + Number(videoStream.Level.Value));
        }

        if (videoStream?.BitRate is > 0)
        {
            videoAttributes.Add(PlaybackText.DisplayBitrate(videoStream.BitRate.Value));
        }

        var frameRate = videoStream?.AverageFrameRate is { } average && average != 0 ? average : videoStream?.RealFrameRate;
        if (frameRate is { } fps && fps != 0)
        {
            videoAttributes.Add(fps.ToString("0.00", CultureInfo.InvariantCulture) + " fps");
        }

        Add(stats, "LabelVideoAttributes", videoAttributes, ",  ");

        var videoBitDepthInfos = new List<string>();
        if (videoStream?.BitDepth is > 0)
        {
            videoBitDepthInfos.Add(Invariant($"{videoStream.BitDepth} Bit"));
        }

        if (!string.IsNullOrEmpty(videoStream?.PixelFormat))
        {
            videoBitDepthInfos.Add(videoStream.PixelFormat);
        }

        if (videoStream?.VideoRangeType.ToApiString() is { Length: > 0 } rangeType)
        {
            videoBitDepthInfos.Add(rangeType);
        }

        Add(stats, "LabelVideoBitDepth", videoBitDepthInfos, ",  ");

        var videoColorInfos = new List<string>();
        if (!string.IsNullOrEmpty(videoStream?.ColorSpace))
        {
            videoColorInfos.Add(videoStream.ColorSpace + "(m)");
        }

        if (!string.IsNullOrEmpty(videoStream?.ColorPrimaries))
        {
            videoColorInfos.Add(videoStream.ColorPrimaries + "(p)");
        }

        if (!string.IsNullOrEmpty(videoStream?.ColorTransfer))
        {
            videoColorInfos.Add(videoStream.ColorTransfer + "(t)");
        }

        Add(stats, "LabelVideoColors", videoColorInfos, ",  ");

        var audioCodecInfos = new List<string>();
        if (!string.IsNullOrEmpty(audioStream?.Codec))
        {
            audioCodecInfos.Add(audioStream.Codec.ToUpperInvariant());
        }

        if (!string.IsNullOrEmpty(audioStream?.Profile))
        {
            audioCodecInfos.Add(audioStream.Profile);
        }

        Add(stats, "LabelAudioCodec", audioCodecInfos, "  ");

        var audioAttributes = new List<string>();
        if (audioStream?.Channels is > 0)
        {
            audioAttributes.Add(Invariant($"{audioStream.Channels} Ch"));
        }

        if (audioStream?.BitRate is > 0)
        {
            audioAttributes.Add(PlaybackText.DisplayBitrate(audioStream.BitRate.Value));
        }

        if (audioStream?.SampleRate is > 0)
        {
            audioAttributes.Add(Invariant($"{audioStream.SampleRate} Hz"));
        }

        if (audioStream?.BitDepth is > 0)
        {
            audioAttributes.Add(Invariant($"{audioStream.BitDepth} Bit"));
        }

        Add(stats, "LabelAudioAttributes", audioAttributes, ",  ");
        return stats;
    }

    private void Add(List<PlayerStat> stats, string labelKey, List<string> values, string separator)
    {
        if (values.Count > 0)
        {
            stats.Add(new PlayerStat(globalize[labelKey], string.Join(separator, values)));
        }
    }

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);

    /// <summary>A number as JavaScript prints it (no trailing zeros).</summary>
    private static string Number(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);
}
