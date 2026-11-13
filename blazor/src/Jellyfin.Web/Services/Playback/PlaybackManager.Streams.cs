using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services.Playback;

/// <summary>
/// The media source that played before the next item starts (the original's getPreviousSource()):
/// used to keep the version and the audio/subtitle choice across episodes.
/// </summary>
internal sealed record PreviousSource(MediaSourceInfo Info, int? AudioStreamIndex, int? SubtitleStreamIndex, bool HasAlternateVersions);

/// <summary>The options object the original passes to getPlaybackInfo().</summary>
internal sealed class PlaybackInfoOptions
{
    public int? MaxBitrate { get; set; }
    public long? StartPosition { get; set; }
    public bool IsPlayback { get; set; }
    public int? AudioStreamIndex { get; set; }
    public int? SubtitleStreamIndex { get; set; }
    public int? SecondarySubtitleStreamIndex { get; set; }
    public bool? EnableDirectPlay { get; set; }
    public bool? EnableDirectStream { get; set; }
    public bool? AllowVideoStreamCopy { get; set; }
    public bool? AllowAudioStreamCopy { get; set; }
}

/// <summary>The params object of changeStream().</summary>
internal sealed class StreamChangeParams
{
    public int? AudioStreamIndex { get; init; }
    public int? SubtitleStreamIndex { get; init; }
    public int? MaxStreamingBitrate { get; init; }
    public bool? EnableDirectPlay { get; init; }
    public bool? EnableDirectStream { get; init; }
    public bool? AllowVideoStreamCopy { get; init; }
    public bool? AllowAudioStreamCopy { get; init; }
}

/// <summary>What getPlaybackInfo() returns: the sources plus the fields of the server's answer.</summary>
internal sealed class PlaybackInfoResult
{
    public List<PlaybackSource> MediaSources { get; init; } = [];
    public string? PlaySessionId { get; init; }
    public string? ErrorCode { get; init; }
}

/// <summary>How to play: PlaybackInfo negotiation, the stream decision and stream changes.</summary>
public sealed partial class PlaybackManager
{
    private const int DefaultMaxBitrate = 1500000;
    private const int InNetworkAudioBitrate = 150000000;

    /// <summary>Port of utils/mediaSource.ts isHls().</summary>
    public static bool IsHls(MediaSourceInfo? mediaSource) =>
        string.Equals(mediaSource?.TranscodingSubProtocol.ToApiString(), "hls", StringComparison.OrdinalIgnoreCase)
        || string.Equals(mediaSource?.Container, "hls", StringComparison.OrdinalIgnoreCase);

    /// <summary>Port of utils/container.ts includesAny(): a leading '-' inverts the list.</summary>
    public static bool IncludesAny(string? list, string? search)
    {
        if (string.IsNullOrEmpty(list))
        {
            return true;
        }

        var inverse = list.StartsWith('-');
        var values = (inverse ? list[1..] : list).Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 0)
        {
            return true;
        }

        var searched = (search ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries);
        return searched.Any(values.Contains) ? !inverse : inverse;
    }

    /// <summary>Port of appSettings.enableAutomaticBitrateDetection() (getter).</summary>
    public bool EnableAutomaticBitrateDetection(bool? isInNetwork, string? mediaType)
    {
        if (isInNetwork == true && mediaType == "Audio")
        {
            return true;
        }

        return AppSettings.ToBoolean(_appSettings.Get($"enableautobitratebitrate-{mediaType}-{JsBool(isInNetwork)}"), true);
    }

    /// <summary>Port of appSettings.enableAutomaticBitrateDetection() (setter).</summary>
    public void SetEnableAutomaticBitrateDetection(bool? isInNetwork, string? mediaType, bool value)
    {
        if (isInNetwork == true && mediaType == "Audio")
        {
            value = true;
        }

        _appSettings.Set($"enableautobitratebitrate-{mediaType}-{JsBool(isInNetwork)}", value);
    }

    /// <summary>Port of appSettings.maxStreamingBitrate() (getter).</summary>
    public int MaxStreamingBitrateSetting(bool? isInNetwork, string? mediaType)
    {
        if (isInNetwork == true && mediaType == "Audio")
        {
            // Unlimited bitrate for audio inside the network.
            return InNetworkAudioBitrate;
        }

        var saved = _appSettings.Get($"maxbitrate-{mediaType}-{JsBool(isInNetwork)}");
        return int.TryParse(saved, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value != 0
            ? value
            : DefaultMaxBitrate;
    }

    /// <summary>Port of appSettings.maxStreamingBitrate() (setter).</summary>
    public void SetMaxStreamingBitrateSetting(bool? isInNetwork, string? mediaType, int value)
    {
        if (isInNetwork == true && mediaType == "Audio")
        {
            // Not saved: the value is fixed for audio inside the network.
            return;
        }

        _appSettings.Set($"maxbitrate-{mediaType}-{JsBool(isInNetwork)}", value);
    }

    /// <summary>The max bitrate in effect for the current stream.</summary>
    public int GetMaxStreamingBitrate()
    {
        if (CurrentPlayer is not { } player)
        {
            return GetSavedMaxStreamingBitrate(null);
        }

        var data = Data(player);
        return data.MaxStreamingBitrate is > 0 ? data.MaxStreamingBitrate.Value : GetSavedMaxStreamingBitrate(data.StreamInfo?.MediaType);
    }

    /// <summary>Whether the bitrate of the current stream's media type is detected automatically.</summary>
    public bool EnableAutomaticBitrateDetection() =>
        EnableAutomaticBitrateDetection(_bitrateDetector.SavedEndpointInfo?.IsInNetwork, CurrentStreamInfo?.MediaType);

    /// <summary>Port of setMaxStreamingBitrate(): saves the choice and restarts the stream with it.</summary>
    public async Task SetMaxStreamingBitrateAsync(bool enableAutomaticBitrateDetection, int? maxBitrate = null)
    {
        if (CurrentPlayer is not { } player || Data(player).StreamInfo is not { } streamInfo)
        {
            return;
        }

        var mediaType = streamInfo.MediaType;
        EndpointInfo endpoint;
        try
        {
            endpoint = await _bitrateDetector.GetEndpointInfoAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppLog.Warn($"[playback] endpoint info failed: {ex.Message}");
            return;
        }

        int bitrate;
        if (enableAutomaticBitrateDetection)
        {
            SetEnableAutomaticBitrateDetection(endpoint.IsInNetwork, mediaType, true);
            bitrate = await _bitrateDetector.DetectBitrateAsync(force: true);
        }
        else
        {
            SetEnableAutomaticBitrateDetection(endpoint.IsInNetwork, mediaType, false);
            bitrate = maxBitrate ?? GetSavedMaxStreamingBitrate(mediaType);
        }

        SetMaxStreamingBitrateSetting(endpoint.IsInNetwork, mediaType, bitrate);
        await ChangeStreamAsync(player, GetCurrentTicks(player), new StreamChangeParams { MaxStreamingBitrate = bitrate });
    }

    /// <summary>Port of canPlayerSeek().</summary>
    public bool CanSeek(IMediaPlayer player)
    {
        var streamInfo = Data(player).StreamInfo;
        if ((streamInfo?.Url ?? string.Empty).Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (player.Seekable is { } seekable)
        {
            return seekable;
        }

        if (streamInfo?.PlayMethod == PlayMethods.Transcode)
        {
            return false;
        }

        return player.DurationMs is > 0;
    }

    /// <summary>Port of getCurrentTicks(): the position in the item, transcoding offset included.</summary>
    public long GetCurrentTicks(IMediaPlayer player)
    {
        var ticks = (long)Math.Floor(10000 * (player.CurrentTimeMs ?? 0));
        return ticks + (Data(player).StreamInfo?.TranscodingOffsetTicks ?? 0);
    }

    /// <summary>Port of duration(): the run time in ticks, or null when unknown.</summary>
    public long? GetDurationTicks(IMediaPlayer player)
    {
        if (Data(player).StreamInfo?.MediaSource?.RunTimeTicks is { } runTime && runTime > 0)
        {
            return runTime;
        }

        return player.DurationMs is { } duration && duration > 0 ? (long)(duration * 10000) : null;
    }

    /// <summary>Port of getMatchingMediaSource(): the version with the name of the previous one.</summary>
    internal static MediaSourceInfo? GetMatchingMediaSource(IReadOnlyList<MediaSourceInfo>? mediaSources, PreviousSource? prevSource)
    {
        var versionName = prevSource?.Info.Name;
        if (string.IsNullOrEmpty(versionName) || prevSource is not { HasAlternateVersions: true } || mediaSources is null || mediaSources.Count < 2)
        {
            return null;
        }

        return mediaSources.FirstOrDefault(m => m.Name == versionName);
    }

    /// <summary>Port of autoSetNextTracks(): picks the tracks that match the previous item's choice.</summary>
    internal static (int? AudioStreamIndex, int? SubtitleStreamIndex) AutoSetNextTracks(
        PreviousSource? prevSource,
        IReadOnlyList<MediaStream>? mediaStreams,
        bool audio,
        bool subtitle)
    {
        int? audioIndex = null;
        int? subtitleIndex = null;
        if (prevSource is null || mediaStreams is null)
        {
            return (audioIndex, subtitleIndex);
        }

        if (audio && prevSource.AudioStreamIndex is { } prevAudio)
        {
            audioIndex = RankStreamType(prevAudio, prevSource, mediaStreams, "Audio");
        }

        if (subtitle && prevSource.SubtitleStreamIndex is { } prevSubtitle)
        {
            subtitleIndex = RankStreamType(prevSubtitle, prevSource, mediaStreams, "Subtitle");
        }

        return (audioIndex, subtitleIndex);
    }

    /// <summary>Port of getOptimalMediaSource(); the sources carry their enableDirectPlay already.</summary>
    internal static PlaybackSource? GetOptimalMediaSource(BaseItemDto item, IReadOnlyList<PlaybackSource> versions)
    {
        if (versions.Count == 0)
        {
            return null;
        }

        var itemId = IdOf(item);
        var ownSource = versions.FirstOrDefault(v => v.Info.Id is { } id && NormalizeId(id) == itemId);
        if (ownSource is not null
            && (ownSource.EnableDirectPlay || ownSource.Info.SupportsDirectStream == true || ownSource.Info.SupportsTranscoding == true))
        {
            return ownSource;
        }

        return versions.FirstOrDefault(v => v.EnableDirectPlay)
            ?? versions.FirstOrDefault(v => v.Info.SupportsDirectStream == true)
            ?? versions.FirstOrDefault(v => v.Info.SupportsTranscoding == true)
            ?? versions[0];
    }

    /// <summary>Port of getMimeType().</summary>
    internal static string GetMimeType(string type, string? container)
    {
        container = (container ?? string.Empty).ToLowerInvariant();
        if (type == "audio")
        {
            switch (container)
            {
                case "opus": return "audio/ogg";
                case "webma": return "audio/webm";
                case "m4a": return "audio/mp4";
            }
        }
        else if (type == "video")
        {
            switch (container)
            {
                case "mkv": return "video/x-matroska";
                case "m4v": return "video/mp4";
                case "mov": return "video/quicktime";
                case "mpg": return "video/mpeg";
                case "flv": return "video/x-flv";
            }
        }

        return type + "/" + container;
    }

    /// <summary>Port of getAudioStreamUrlFromDeviceProfile(): the universal audio endpoint.</summary>
    internal string GetAudioStreamUrl(BaseItemDto item, JsonObject deviceProfile, int? maxBitrate, long? startPosition)
    {
        var transcodingProfile = Profiles(deviceProfile, "TranscodingProfiles")
            .FirstOrDefault(p => (string?)p["Type"] == "Audio" && (string?)p["Context"] == "Streaming");

        var directPlayContainers = string.Join(',', Profiles(deviceProfile, "DirectPlayProfiles")
            .Where(p => (string?)p["Type"] == "Audio")
            .Select(p =>
            {
                var audioCodec = (string?)p["AudioCodec"];
                return string.IsNullOrEmpty(audioCodec) ? (string?)p["Container"] : $"{(string?)p["Container"]}|{audioCodec}";
            }));

        string? maxAudioSampleRate = null;
        string? maxAudioBitDepth = null;
        string? maxAudioBitrate = null;
        foreach (var codecProfile in Profiles(deviceProfile, "CodecProfiles").Where(p => (string?)p["Type"] == "Audio"))
        {
            foreach (var condition in Profiles(codecProfile, "Conditions").Where(c => (string?)c["Condition"] == "LessThanEqual"))
            {
                var value = condition["Value"]?.ToString();
                switch ((string?)condition["Property"])
                {
                    case "AudioBitDepth":
                        maxAudioBitDepth = value;
                        break;
                    case "AudioSampleRate":
                        maxAudioSampleRate = value;
                        break;
                    case "AudioBitrate":
                        maxAudioBitrate = value;
                        break;
                }
            }
        }

        return _api.GetUrl(
            $"Audio/{IdOf(item)}/universal",
            ("UserId", _api.UserIdText),
            ("DeviceId", _api.DeviceId),
            ("MaxStreamingBitrate", string.IsNullOrEmpty(maxAudioBitrate) ? maxBitrate?.ToString(CultureInfo.InvariantCulture) : maxAudioBitrate),
            ("Container", directPlayContainers),
            ("TranscodingContainer", (string?)transcodingProfile?["Container"]),
            ("TranscodingProtocol", (string?)transcodingProfile?["Protocol"]),
            ("AudioCodec", (string?)transcodingProfile?["AudioCodec"]),
            ("MaxAudioSampleRate", maxAudioSampleRate),
            ("MaxAudioBitDepth", maxAudioBitDepth),
            ("ApiKey", _api.AccessToken),
            ("PlaySessionId", (++_startingPlaySession).ToString(CultureInfo.InvariantCulture)),
            ("StartTimeTicks", (startPosition ?? 0).ToString(CultureInfo.InvariantCulture)),
            ("EnableRedirection", true),
            ("EnableRemoteMedia", false),
            ("EnableAudioVbrEncoding", transcodingProfile?["EnableAudioVbrEncoding"]?.ToString().ToLowerInvariant()));
    }

    /// <summary>The body of POST Items/{id}/PlaybackInfo, keys in the original's order.</summary>
    internal JsonObject BuildPlaybackInfoBody(JsonObject deviceProfile, string? mediaSourceId, string? liveStreamId, PlaybackInfoOptions options)
    {
        var body = new JsonObject
        {
            ["UserId"] = _api.UserIdText,
            ["StartTimeTicks"] = options.StartPosition ?? 0,
            ["IsPlayback"] = options.IsPlayback,
            ["AutoOpenLiveStream"] = options.IsPlayback,
        };

        if (options.AudioStreamIndex is { } audio)
        {
            body["AudioStreamIndex"] = audio;
        }

        if (options.SubtitleStreamIndex is { } subtitle)
        {
            body["SubtitleStreamIndex"] = subtitle;
        }

        if (options.SecondarySubtitleStreamIndex is { } secondarySubtitle)
        {
            body["SecondarySubtitleStreamIndex"] = secondarySubtitle;
        }

        if (options.EnableDirectPlay is { } enableDirectPlay)
        {
            body["EnableDirectPlay"] = enableDirectPlay;
        }

        if (options.EnableDirectStream is { } enableDirectStream)
        {
            body["EnableDirectStream"] = enableDirectStream;
        }

        if (options.AllowVideoStreamCopy is { } allowVideoStreamCopy)
        {
            body["AllowVideoStreamCopy"] = allowVideoStreamCopy;
        }

        if (options.AllowAudioStreamCopy is { } allowAudioStreamCopy)
        {
            body["AllowAudioStreamCopy"] = allowAudioStreamCopy;
        }

        if (!string.IsNullOrEmpty(mediaSourceId))
        {
            body["MediaSourceId"] = mediaSourceId;
        }

        if (!string.IsNullOrEmpty(liveStreamId))
        {
            body["LiveStreamId"] = liveStreamId;
        }

        if (options.MaxBitrate is { } maxBitrate && maxBitrate > 0)
        {
            body["MaxStreamingBitrate"] = maxBitrate;
        }

        body["AlwaysBurnInSubtitleWhenTranscoding"] = _appSettings.GetBool("alwaysBurnInSubtitleWhenTranscoding", false);
        body["DeviceProfile"] = deviceProfile.DeepClone();
        return body;
    }

    /// <summary>Port of createStreamInfo(): decides the play method and builds the stream URL.</summary>
    internal StreamInfo CreateStreamInfo(string? type, BaseItemDto item, PlaybackSource source, long? startPosition)
    {
        var mediaSource = source.Info;
        string? mediaUrl = null;
        string? contentType = null;
        long transcodingOffsetTicks = 0;
        var playMethod = PlayMethods.Transcode;
        var container = (mediaSource.Container ?? string.Empty).ToLowerInvariant();

        if (type is "Video" or "Audio")
        {
            contentType = GetMimeType(type.ToLowerInvariant(), container);

            if (source.EnableDirectPlay)
            {
                mediaUrl = mediaSource.Path;
                playMethod = PlayMethods.DirectPlay;
            }
            else if (!string.IsNullOrEmpty(source.StreamUrl))
            {
                mediaUrl = source.StreamUrl;
            }
            else if (mediaSource.SupportsDirectPlay == true || mediaSource.SupportsDirectStream == true)
            {
                var prefix = type == "Video" ? "Videos" : "Audio";
                mediaUrl = _api.GetUrl(
                    $"{prefix}/{IdOf(item)}/stream.{container}",
                    ("Static", true),
                    ("mediaSourceId", mediaSource.Id),
                    ("deviceId", _api.DeviceId),
                    ("ApiKey", _api.AccessToken),
                    ("Tag", mediaSource.ETag),
                    ("LiveStreamId", mediaSource.LiveStreamId));
                playMethod = mediaSource.SupportsDirectPlay == true ? PlayMethods.DirectPlay : PlayMethods.DirectStream;
            }
            else if (mediaSource.SupportsTranscoding == true && !string.IsNullOrEmpty(mediaSource.TranscodingUrl))
            {
                mediaUrl = _api.GetUrl(mediaSource.TranscodingUrl);

                if (string.Equals(mediaSource.TranscodingSubProtocol.ToApiString(), "hls", StringComparison.OrdinalIgnoreCase))
                {
                    contentType = "application/x-mpegURL";
                }
                else
                {
                    contentType = GetMimeType(type.ToLowerInvariant(), mediaSource.TranscodingContainer);

                    if (!mediaUrl.Contains("copytimestamps=true", StringComparison.OrdinalIgnoreCase))
                    {
                        transcodingOffsetTicks = startPosition ?? 0;
                    }
                }
            }
        }
        else
        {
            // All other media types (the original also handles photos and books here).
            mediaUrl = mediaSource.Path;
            playMethod = PlayMethods.DirectPlay;
        }

        // Fallback (used for offline items in the original).
        if (string.IsNullOrEmpty(mediaUrl) && mediaSource.SupportsDirectPlay == true)
        {
            mediaUrl = mediaSource.Path;
            playMethod = PlayMethods.DirectPlay;
        }

        return new StreamInfo
        {
            Url = mediaUrl,
            MimeType = contentType,
            TranscodingOffsetTicks = transcodingOffsetTicks,
            PlayMethod = playMethod,
            PlayerStartPositionTicks = startPosition,
            Item = item,
            Source = source,
            TextTracks = GetTextTracks(mediaSource),
            MediaType = type,
            LiveStreamId = mediaSource.LiveStreamId,
            PlaySessionId = GetParam(mediaUrl),
            Title = item.Name,
        };
    }

    /// <summary>Port of playInternal(): detects the bitrate and starts playing one item.</summary>
    internal async Task PlayInternalAsync(BaseItemDto item, ItemPlayOptions playOptions, Action onPlaybackStarted, PreviousSource? prevSource)
    {
        if (item.IsPlaceHolder == true)
        {
            _loading.Hide();
            ShowPlaybackInfoErrorMessage("PlaybackErrorPlaceHolder");
            return;
        }

        // Normalize defaults to simplify checks throughout the process.
        playOptions.IsFirstItem = playOptions.IsFirstItem || prevSource is null;

        try
        {
            await _interceptors.BeforePlayAsync(item, playOptions);
        }
        catch (PlaybackInterceptException exception)
        {
            OnPlaybackRejection(exception, playOptions.Fullscreen);
            return;
        }

        if (playOptions.Fullscreen)
        {
            _loading.Show();
        }

        var mediaType = item.MediaType.ToApiString();
        try
        {
            var bitrate = IsServerItem(item)
                ? await DetectPlaybackBitrateAsync(mediaType)
                : GetSavedMaxStreamingBitrate(mediaType);
            await PlayAfterBitrateDetectAsync(bitrate, item, playOptions, onPlaybackStarted, prevSource);
        }
        catch (Exception ex)
        {
            // Everything that goes wrong while starting ends in the same place, as in the original.
            OnPlaybackRejection(ex, playOptions.Fullscreen);
        }
        finally
        {
            if (playOptions.Fullscreen)
            {
                _loading.Hide();
            }
        }
    }

    /// <summary>
    /// Port of changeStream(): seeks in the player when that is possible, otherwise negotiates a new
    /// stream that starts at <paramref name="ticks"/> (or uses other tracks, bitrate, play method).
    /// </summary>
    internal async Task ChangeStreamAsync(IMediaPlayer player, long ticks, StreamChangeParams? parameters = null)
    {
        if (CanSeek(player) && parameters is null)
        {
            player.SetCurrentTime(ticks / 10000);
            return;
        }

        parameters ??= new StreamChangeParams();
        var data = Data(player);
        if (data.StreamInfo is not { Source: { } currentSource } current)
        {
            return;
        }

        var liveStreamId = current.LiveStreamId;
        var playSessionId = current.PlaySessionId;
        var currentItem = current.Item;

        try
        {
            var deviceProfile = await player.GetDeviceProfileAsync(currentItem, isRetry: parameters.EnableDirectPlay == false);
            var audioStreamIndex = parameters.AudioStreamIndex ?? data.AudioStreamIndex;
            var subtitleStreamIndex = parameters.SubtitleStreamIndex ?? data.SubtitleStreamIndex;
            var maxBitrate = parameters.MaxStreamingBitrate is > 0 ? parameters.MaxStreamingBitrate.Value : GetMaxStreamingBitrate();
            var currentPlayOptions = GetItemPlayOptions(currentItem);

            var result = await GetPlaybackInfoAsync(player, currentItem, deviceProfile, currentSource.Info.Id, liveStreamId, new PlaybackInfoOptions
            {
                MaxBitrate = maxBitrate,
                StartPosition = ticks,
                IsPlayback = true,
                AudioStreamIndex = audioStreamIndex,
                SubtitleStreamIndex = subtitleStreamIndex,
                SecondarySubtitleStreamIndex = data.SecondarySubtitleStreamIndex,
                EnableDirectPlay = parameters.EnableDirectPlay,
                EnableDirectStream = parameters.EnableDirectStream,
                AllowVideoStreamCopy = parameters.AllowVideoStreamCopy,
                AllowAudioStreamCopy = parameters.AllowAudioStreamCopy,
            });

            if (!ValidatePlaybackInfoResult(result) || result.MediaSources.Count == 0)
            {
                return;
            }

            var source = result.MediaSources[0];
            source.PlaySessionId = result.PlaySessionId ?? source.PlaySessionId;
            source.HasAlternateVersions = currentSource.HasAlternateVersions;
            source.AlbumNormalizationGain ??= currentSource.AlbumNormalizationGain;

            var streamInfo = CreateStreamInfo(currentItem.MediaType.ToApiString(), currentItem, source, ticks);
            if (string.IsNullOrEmpty(streamInfo.PlaySessionId))
            {
                streamInfo.PlaySessionId = source.PlaySessionId;
            }
            streamInfo.Fullscreen = currentPlayOptions.Fullscreen;
            streamInfo.AspectRatio = current.AspectRatio;
            streamInfo.ResetSubtitleOffset = false;
            streamInfo.MaxStreamingBitrate = maxBitrate;
            streamInfo.PlaybackStartTimeTicks = current.PlaybackStartTimeTicks;

            if (string.IsNullOrEmpty(streamInfo.Url))
            {
                CancelPlayback();
                ShowPlaybackInfoErrorMessage("PlaybackError." + MediaErrors.NoMediaError);
                return;
            }

            data.SubtitleStreamIndex = subtitleStreamIndex;
            data.AudioStreamIndex = audioStreamIndex;
            data.MaxStreamingBitrate = maxBitrate;

            await ChangeStreamToUrlAsync(player, playSessionId, streamInfo);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppLog.Warn($"[playback] changing the stream failed: {ex.Message}");
        }
    }

    /// <summary>Port of onPlaybackRejection(): cancels and tells the user why nothing plays.</summary>
    private void OnPlaybackRejection(Exception exception, bool fullscreen)
    {
        CancelPlayback();
        if (fullscreen)
        {
            _loading.Hide();
        }

        if (exception is PlaybackInterceptException { SuppressError: true })
        {
            return;
        }

        if (exception is PlaybackInterceptException { Reported: true })
        {
            // The specific message has been shown already. The original shows "ErrorDefault" on top
            // of it; the port leaves it at the one that says what is wrong.
            return;
        }

        if (exception is PlaybackRejectedException { Reported: true })
        {
            // A negotiation helper has already shown the server's specific error.
            return;
        }

        var message = "ErrorDefault";
        if (ApiErrors.StatusOf(exception) is { } status)
        {
            if ((int)status >= 500)
            {
                message = "PlaybackError." + MediaErrors.ServerError;
            }
            else if ((int)status >= 400)
            {
                message = "PlaybackError." + MediaErrors.NoMediaError;
            }
        }

        AppLog.Warn(exception is PlaybackRejectedException or HttpRequestException
            ? $"[playback] starting playback failed: {exception.Message}"
            : $"[playback] starting playback failed: {exception}");

        ShowPlaybackInfoErrorMessage(message);
    }

    /// <summary>Port of cancelPlayback().</summary>
    private void CancelPlayback()
    {
        if (CurrentPlayer is { IsLocalPlayer: true } player)
        {
            StopProgressTimer();
            Data(player).StreamInfo = null;
            player.Destroy();
            SetCurrentPlayer(null);
        }

        Raise(PlaybackEvents.PlaybackCancelled);
    }

    private int GetSavedMaxStreamingBitrate(string? mediaType) =>
        MaxStreamingBitrateSetting(_bitrateDetector.SavedEndpointInfo?.IsInNetwork, mediaType);

    private async Task<int> DetectPlaybackBitrateAsync(string? mediaType)
    {
        try
        {
            var endpoint = await _bitrateDetector.GetEndpointInfoAsync();
            if (mediaType is "Video" or "Audio" && EnableAutomaticBitrateDetection(endpoint.IsInNetwork, mediaType))
            {
                var bitrate = await _bitrateDetector.DetectBitrateAsync();
                SetMaxStreamingBitrateSetting(endpoint.IsInNetwork, mediaType, bitrate);
                return bitrate;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException)
        {
            AppLog.Warn($"[playback] bitrate detection failed: {ex.Message}");
        }

        return GetSavedMaxStreamingBitrate(mediaType);
    }

    /// <summary>Port of playAfterBitrateDetect().</summary>
    private async Task PlayAfterBitrateDetectAsync(int maxBitrate, BaseItemDto item, ItemPlayOptions playOptions, Action onPlaybackStarted, PreviousSource? prevSource)
    {
        var startPosition = playOptions.StartPositionTicks;
        var player = GetPlayer(item);
        var activePlayer = CurrentPlayer;

        if (activePlayer is not null)
        {
            // Changing items: the stop of the active player must not start the next item of the queue.
            _playNextAfterEnded = false;
            await OnPlaybackChangingAsync(activePlayer, player, item);
        }

        if (player is null)
        {
            CancelPlayback();
            _loading.Hide();
            AppLog.Warn($"[playback] no player found for the requested media: {item.Name}");
            ShowPlaybackInfoErrorMessage("ErrorPlayerNotFound");
            return;
        }

        if (!IsServerItem(item) || item.MediaType is BaseItemDto_MediaType.Photo or BaseItemDto_MediaType.Book)
        {
            if (player is not IPlaylistMediaPlayer managed)
            {
                throw new PlaybackRejectedException(reported: false, $"'{item.Name}' is not supported by the selected player");
            }

            var items = _pendingOtherItems ?? [item];
            var managedStream = await managed.PlayItemsAsync(items, playOptions);
            var managedData = Data(player);
            managedData.IsChangingStream = false;
            managedData.StreamInfo = managedStream;
            _loading.Hide();
            onPlaybackStarted();
            OnPlaybackStarted(player, playOptions, managedStream, mediaSource: null);
            return;
        }

        var mediaSourceId = playOptions.MediaSourceId;
        var isLiveTv = item.Type.ToApiString() is "TvChannel" or "LiveTvChannel";
        var sourceItem = isLiveTv ? null : await GetItemAsync(string.IsNullOrEmpty(mediaSourceId) ? IdOf(item) : mediaSourceId);
        var deviceProfile = await player.GetDeviceProfileAsync(item);
        var configuration = _api.User?.Configuration;

        // Keep playing the version (e.g. "1080p") the previous item played.
        var versionSource = string.IsNullOrEmpty(mediaSourceId) ? GetMatchingMediaSource(sourceItem?.MediaSources, prevSource) : null;
        if (versionSource is not null)
        {
            mediaSourceId = versionSource.Id;
        }

        IReadOnlyList<MediaStream> mediaStreams = versionSource?.MediaStreams ?? sourceItem?.MediaStreams ?? [];

        var options = new PlaybackInfoOptions
        {
            MaxBitrate = maxBitrate,
            StartPosition = startPosition,
            AudioStreamIndex = playOptions.AudioStreamIndex,
            SubtitleStreamIndex = playOptions.SubtitleStreamIndex,
            SecondarySubtitleStreamIndex = playOptions.SecondarySubtitleStreamIndex,
        };

        var (autoAudio, autoSubtitle) = AutoSetNextTracks(
            prevSource,
            mediaStreams,
            configuration?.RememberAudioSelections == true,
            configuration?.RememberSubtitleSelections == true);
        if (autoAudio is not null)
        {
            options.AudioStreamIndex = autoAudio;
        }

        if (autoSubtitle is not null)
        {
            options.SubtitleStreamIndex = autoSubtitle;
        }

        if ((autoAudio is not null || autoSubtitle is not null) && string.IsNullOrEmpty(mediaSourceId))
        {
            mediaSourceId = IdOf(item);
        }

        var source = await GetPlaybackMediaSourceAsync(player, deviceProfile, item, mediaSourceId, options);

        var playedItem = await GetItemOfMediaSourceAsync(item, source, sourceItem);
        var streamInfo = CreateStreamInfo(item.MediaType.ToApiString(), playedItem, source, startPosition);
        if (string.IsNullOrEmpty(streamInfo.PlaySessionId))
        {
            streamInfo.PlaySessionId = source.PlaySessionId;
        }
        streamInfo.AspectRatio = playOptions.AspectRatio;
        streamInfo.Fullscreen = playOptions.Fullscreen;
        streamInfo.MaxStreamingBitrate = maxBitrate;
        streamInfo.SecondarySubtitleStreamIndex = playOptions.SecondarySubtitleStreamIndex;

        var data = Data(player);
        data.IsChangingStream = false;
        data.MaxStreamingBitrate = maxBitrate;
        data.StreamInfo = streamInfo;

        try
        {
            await player.PlayAsync(streamInfo);
        }
        catch (PlayerException ex)
        {
            // The player could not play the source: report the start anyway so the error handling
            // (which may fall back to transcoding) finds the state it expects.
            onPlaybackStarted();
            OnPlaybackStarted(player, playOptions, streamInfo, source);
            _ = ReportStartErrorAsync(player, ex, streamInfo);
            return;
        }

        _loading.Hide();
        onPlaybackStarted();
        OnPlaybackStarted(player, playOptions, streamInfo, source);
    }

    private async Task ReportStartErrorAsync(IMediaPlayer player, PlayerException exception, StreamInfo streamInfo)
    {
        try
        {
            await _timers.Delay(100);
            await OnPlaybackErrorAsync(player, GetMediaError(exception), streamInfo);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[playback] handling a playback error failed: {ex}");
        }
    }

    private static string GetMediaError(PlayerException exception) =>
        exception.ErrorType
        ?? (exception.ErrorName == "NotSupportedError" ? MediaErrors.MediaNotSupported : MediaErrors.PlayerError);

    /// <summary>Port of getPlaybackInfo().</summary>
    private async Task<PlaybackInfoResult> GetPlaybackInfoAsync(
        IMediaPlayer player,
        BaseItemDto item,
        JsonObject deviceProfile,
        string? mediaSourceId,
        string? liveStreamId,
        PlaybackInfoOptions options)
    {
        if (item.MediaType.ToApiString() == "Audio" && player.IsLocalPlayer)
        {
            // Audio needs no negotiation: the universal endpoint decides on the server.
            var info = new MediaSourceInfo
            {
                Id = IdOf(item),
                MediaStreams = [],
                RunTimeTicks = item.RunTimeTicks,
            };
            return new PlaybackInfoResult
            {
                MediaSources =
                [
                    new PlaybackSource(info) { StreamUrl = GetAudioStreamUrl(item, deviceProfile, options.MaxBitrate, options.StartPosition) },
                ],
            };
        }

        var body = BuildPlaybackInfoBody(deviceProfile, mediaSourceId, liveStreamId, options);
        using var response = await _api.SendAsync(HttpMethod.Post, $"Items/{IdOf(item)}/PlaybackInfo", body);
        response.EnsureSuccessStatusCode();
        var result = KiotaJson.Parse(await response.Content.ReadAsStringAsync(), PlaybackInfoResponse.CreateFromDiscriminatorValue);
        var sources = (result?.MediaSources ?? []).Select(s => new PlaybackSource(s)
        {
            PlaySessionId = result?.PlaySessionId,
        }).ToList();
        return new PlaybackInfoResult
        {
            MediaSources = sources,
            PlaySessionId = result?.PlaySessionId,
            ErrorCode = result?.ErrorCode.ToApiString(),
        };
    }

    /// <summary>Port of validatePlaybackInfoResult().</summary>
    private bool ValidatePlaybackInfoResult(PlaybackInfoResult result)
    {
        if (string.IsNullOrEmpty(result.ErrorCode))
        {
            return true;
        }

        // The "NoCompatibleStream" message keeps its old key.
        ShowPlaybackInfoErrorMessage(result.ErrorCode == "NoCompatibleStream"
            ? "PlaybackErrorNoCompatibleStream"
            : "PlaybackError." + result.ErrorCode);
        return false;
    }

    /// <summary>Port of getPlaybackMediaSource().</summary>
    private async Task<PlaybackSource> GetPlaybackMediaSourceAsync(
        IMediaPlayer player,
        JsonObject deviceProfile,
        BaseItemDto item,
        string? mediaSourceId,
        PlaybackInfoOptions options)
    {
        options.IsPlayback = true;
        var result = await GetPlaybackInfoAsync(player, item, deviceProfile, mediaSourceId, liveStreamId: null, options);
        if (!ValidatePlaybackInfoResult(result))
        {
            throw new PlaybackRejectedException(reported: true);
        }

        foreach (var candidate in result.MediaSources)
        {
            candidate.EnableDirectPlay = await SupportsDirectPlayAsync(candidate.Info);
        }

        var source = GetOptimalMediaSource(item, result.MediaSources);
        if (source is null)
        {
            ShowPlaybackInfoErrorMessage("PlaybackError." + MediaErrors.NoMediaError);
            throw new PlaybackRejectedException(reported: true);
        }

        var itemId = IdOf(item);
        source.HasAlternateVersions = result.MediaSources.Count > 1
            || item.MediaSources is { Count: > 1 }
            || (!string.IsNullOrEmpty(mediaSourceId) && NormalizeId(mediaSourceId) != itemId);

        if (source.Info.RequiresOpening == true && string.IsNullOrEmpty(source.Info.LiveStreamId))
        {
            options.AudioStreamIndex = null;
            options.SubtitleStreamIndex = null;
            var opened = await OpenLiveStreamAsync(result.PlaySessionId, deviceProfile, item, source.Info, options);
            return new PlaybackSource(opened)
            {
                EnableDirectPlay = await SupportsDirectPlayAsync(opened),
                HasAlternateVersions = source.HasAlternateVersions,
            };
        }

        if (item.AlbumId is not null)
        {
            try
            {
                source.AlbumNormalizationGain = (await GetItemAsync(IdOf(item.AlbumId)))?.NormalizationGain;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                // The gain only matters for volume normalization; the original fails playback here.
                AppLog.Warn($"[playback] album of {itemId} not available: {ex.Message}");
            }
        }

        return source;
    }

    /// <summary>Port of getLiveStream(): POST LiveStreams/Open.</summary>
    private async Task<MediaSourceInfo> OpenLiveStreamAsync(
        string? playSessionId,
        JsonObject deviceProfile,
        BaseItemDto item,
        MediaSourceInfo mediaSource,
        PlaybackInfoOptions options)
    {
        var url = _api.GetUrl(
            "LiveStreams/Open",
            ("UserId", _api.UserIdText),
            ("StartTimeTicks", (options.StartPosition ?? 0).ToString(CultureInfo.InvariantCulture)),
            ("ItemId", IdOf(item)),
            ("PlaySessionId", playSessionId),
            ("MaxStreamingBitrate", options.MaxBitrate is { } maxBitrate && maxBitrate > 0 ? maxBitrate.ToString(CultureInfo.InvariantCulture) : null),
            ("AudioStreamIndex", options.AudioStreamIndex?.ToString(CultureInfo.InvariantCulture)),
            ("SubtitleStreamIndex", options.SubtitleStreamIndex?.ToString(CultureInfo.InvariantCulture)));
        var body = new JsonObject
        {
            ["DeviceProfile"] = deviceProfile.DeepClone(),
            ["OpenToken"] = mediaSource.OpenToken,
        };

        using var response = await _api.SendAsync(HttpMethod.Post, url, body);
        response.EnsureSuccessStatusCode();
        var result = KiotaJson.Parse(await response.Content.ReadAsStringAsync(), LiveStreamResponse.CreateFromDiscriminatorValue);
        return result?.MediaSource ?? throw new PlaybackRejectedException(reported: false, "the server opened no live stream");
    }

    /// <summary>Port of supportsDirectPlay(): may the player be given the source's own (http) path?</summary>
    private async Task<bool> SupportsDirectPlayAsync(MediaSourceInfo mediaSource)
    {
        // Folder rips (BluRay, DVD) are played from their path as well.
        var isFolderRip = mediaSource.VideoType.ToApiString() is "BluRay" or "Dvd" or "HdDvd";
        if (mediaSource.SupportsDirectPlay != true && !isFolderRip)
        {
            return false;
        }

        // The original also tests RequiredHttpHeaders.length, which is always "no headers" because the
        // field is an object. File paths are only reachable from native shells, never from a browser.
        if (mediaSource.Protocol.ToApiString() != "Http")
        {
            return false;
        }

        if (mediaSource.SupportsDirectStream != true && mediaSource.SupportsTranscoding != true)
        {
            // If this is the only way it can be played, then allow it.
            return true;
        }

        return await IsHostReachableAsync(mediaSource);
    }

    private async Task<bool> IsHostReachableAsync(MediaSourceInfo mediaSource)
    {
        if (mediaSource.IsRemote == true)
        {
            return true;
        }

        var endpoint = await _bitrateDetector.GetEndpointInfoAsync();
        if (endpoint.IsInNetwork != true)
        {
            // The media server cannot be assumed to be on the same network as the media.
            return false;
        }

        if (endpoint.IsLocal != true)
        {
            var path = (mediaSource.Path ?? string.Empty).ToLowerInvariant();
            if (path.Contains("localhost", StringComparison.Ordinal) || path.Contains("127.0.0.1", StringComparison.Ordinal))
            {
                // This will only work if the app is on the same machine as the server.
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Port of getItemOfMediaSource(): when another version than the item itself plays, the played
    /// item carries that version's chapters and trickplay data.
    /// </summary>
    private async Task<BaseItemDto> GetItemOfMediaSourceAsync(BaseItemDto item, PlaybackSource source, BaseItemDto? sourceItem)
    {
        var sourceId = source.Info.Id is { } id ? NormalizeId(id) : null;
        if (!source.HasAlternateVersions || sourceId is null || sourceId == IdOf(item))
        {
            return item;
        }

        var versionItem = sourceItem is not null && IdOf(sourceItem) == sourceId ? sourceItem : null;
        if (versionItem is null)
        {
            try
            {
                versionItem = await GetItemAsync(sourceId);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                versionItem = null;
            }
        }

        if (versionItem is null)
        {
            return item;
        }

        var copy = KiotaJson.Clone(item, BaseItemDto.CreateFromDiscriminatorValue);
        if (copy is null)
        {
            return item;
        }

        copy.Chapters = versionItem.Chapters;
        var trickplay = new Dictionary<string, object>();
        foreach (var data in new[] { item.Trickplay?.AdditionalData, versionItem.Trickplay?.AdditionalData })
        {
            foreach (var pair in data ?? new Dictionary<string, object>())
            {
                trickplay[pair.Key] = pair.Value;
            }
        }

        copy.Trickplay = new BaseItemDto_Trickplay { AdditionalData = trickplay };

        // The play options travel with the object the queue holds; the copy needs them too.
        if (_itemOptions.TryGetValue(item, out var options))
        {
            _itemOptions.AddOrUpdate(copy, options);
        }

        return copy;
    }

    /// <summary>Port of getTextTracks(): the subtitle streams the server delivers as files.</summary>
    private List<TextTrackInfo> GetTextTracks(MediaSourceInfo mediaSource)
    {
        var tracks = new List<TextTrackInfo>();
        foreach (var stream in mediaSource.MediaStreams ?? [])
        {
            if (stream.Type.ToApiString() != "Subtitle" || stream.DeliveryMethod.ToApiString() != "External" || string.IsNullOrEmpty(stream.DeliveryUrl))
            {
                continue;
            }

            tracks.Add(new TextTrackInfo(
                stream.IsExternalUrl == true ? stream.DeliveryUrl : _api.GetUrl(stream.DeliveryUrl),
                string.IsNullOrEmpty(stream.Language) ? "und" : stream.Language,
                stream.Index == mediaSource.DefaultSubtitleStreamIndex,
                stream.Index ?? 0,
                stream.Codec));
        }

        return tracks;
    }

    /// <summary>Port of changeStreamToUrl() and setSrcIntoPlayer().</summary>
    private async Task ChangeStreamToUrlAsync(IMediaPlayer player, string? playSessionId, StreamInfo streamInfo)
    {
        var data = Data(player);
        data.IsChangingStream = true;

        var stopEncodings = data.StreamInfo is not null && !string.IsNullOrEmpty(playSessionId);
        if (stopEncodings)
        {
            await StopActiveEncodingsAsync(playSessionId!);
        }

        data.StreamInfo = streamInfo;
        PlayerException? error = null;
        try
        {
            await player.PlayAsync(streamInfo);
        }
        catch (PlayerException ex)
        {
            error = ex;
        }

        data.IsChangingStream = false;
        if (error is null)
        {
            streamInfo.Started = true;
            streamInfo.Ended = false;
            SendProgressUpdate(player, PlaybackEvents.TimeUpdate);
            Raise(PlaybackEvents.MediaStreamsChange);
        }
        else
        {
            await OnPlaybackErrorAsync(player, GetMediaError(error), streamInfo);
        }

        if (stopEncodings)
        {
            await StopActiveEncodingsAsync(playSessionId!);
        }
    }

    /// <summary>The api client's stopActiveEncodings(): DELETE Videos/ActiveEncodings.</summary>
    private async Task StopActiveEncodingsAsync(string playSessionId)
    {
        try
        {
            using var response = await _api.SendAsync(
                HttpMethod.Delete,
                _api.GetUrl("Videos/ActiveEncodings", ("deviceId", _api.DeviceId), ("PlaySessionId", playSessionId)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            AppLog.Warn($"[playback] stopping the encoding failed: {ex.Message}");
        }
    }

    /// <summary>Port of getPreviousSource(): the current source with the tracks in use.</summary>
    private PreviousSource? GetPreviousSource(IMediaPlayer player)
    {
        var data = Data(player);
        return data.StreamInfo?.Source is { } source
            ? new PreviousSource(source.Info, data.AudioStreamIndex, data.SubtitleStreamIndex, source.HasAlternateVersions)
            : null;
    }

    /// <summary>Port of rankStreamType(): the stream of the new item most like the previous one.</summary>
    private static int? RankStreamType(int prevIndex, PreviousSource prevSource, IReadOnlyList<MediaStream> mediaStreams, string streamType)
    {
        if (prevIndex == -1)
        {
            // Subtitles were off: keep them off.
            return streamType == "Subtitle" ? -1 : null;
        }

        var prevStreams = prevSource.Info.MediaStreams;
        if (prevStreams is null || prevIndex < 0 || prevIndex >= prevStreams.Count)
        {
            return null;
        }

        var prevStream = prevStreams[prevIndex];
        var prevRelIndex = 0;
        foreach (var stream in prevStreams)
        {
            if (stream.Type.ToApiString() != streamType)
            {
                continue;
            }

            if (stream.Index == prevIndex)
            {
                break;
            }

            prevRelIndex += 1;
        }

        int? bestStreamIndex = null;
        var bestStreamScore = 0;
        var newRelIndex = 0;
        foreach (var stream in mediaStreams)
        {
            if (stream.Type.ToApiString() != streamType)
            {
                continue;
            }

            var score = 0;
            if (prevStream.Codec == stream.Codec)
            {
                score += 1;
            }

            if (prevRelIndex == newRelIndex)
            {
                score += 1;
            }

            if (!string.IsNullOrEmpty(prevStream.DisplayTitle) && prevStream.DisplayTitle == stream.DisplayTitle)
            {
                score += 2;
            }

            if (!string.IsNullOrEmpty(prevStream.Language) && prevStream.Language != "und" && prevStream.Language == stream.Language)
            {
                score += 2;
            }

            if (score > bestStreamScore && score >= 3)
            {
                bestStreamScore = score;
                bestStreamIndex = stream.Index;
            }

            newRelIndex += 1;
        }

        return bestStreamIndex;
    }

    private static IEnumerable<JsonObject> Profiles(JsonObject parent, string name) =>
        parent[name] is JsonArray array ? array.OfType<JsonObject>() : [];

    /// <summary>Port of getParam('playSessionId', url): empty when the URL has none.</summary>
    private static string GetParam(string? url)
    {
        var match = PlaySessionIdRegex().Match(url ?? string.Empty);
        return match.Success ? Uri.UnescapeDataString(match.Groups[1].Value.Replace('+', ' ')) : string.Empty;
    }

    private static string JsBool(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "undefined",
    };

    [GeneratedRegex("[?&]playSessionId=([^&#]*)", RegexOptions.IgnoreCase)]
    private static partial Regex PlaySessionIdRegex();

}
