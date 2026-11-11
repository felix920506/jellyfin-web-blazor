using System.Text.Json;

namespace Jellyfin.Web.Services.Playback;

/// <summary>Network information of the client as the server sees it (System/Endpoint).</summary>
public sealed record EndpointInfo(bool? IsInNetwork = null, bool? IsLocal = null);

/// <summary>
/// Measures the download speed to choose a streaming bitrate. Port of
/// apps/legacy/features/playback/utils/bitrateTest.ts, with the same test sizes, thresholds and cache.
/// </summary>
[AppService]
public sealed class BitrateDetector(JellyfinApi api, IPlaybackTimers timers)
{
    public const int MaxBitrate = 2147483647;
    public const int LanBitrate = 140000000;
    private const int TimeoutMs = 5000;
    private const int CacheMs = 3600000;

    private static readonly (int Bytes, int Threshold)[] Tests =
    [
        (500000, 500000),
        (1000000, 20000000),
        (3000000, 50000000),
    ];

    private int? _lastDetectedBitrate;
    private long _lastDetectedBitrateTime;
    private Task<int>? _pending;
    private EndpointInfo? _endpointInfo;

    /// <summary>The endpoint info fetched last, or null (the original's getSavedEndpointInfo()).</summary>
    public EndpointInfo? SavedEndpointInfo => _endpointInfo;

    /// <summary>Forgets everything known about the connection (sign-out, server change).</summary>
    public void Reset()
    {
        _lastDetectedBitrate = null;
        _lastDetectedBitrateTime = 0;
        _pending = null;
        _endpointInfo = null;
    }

    /// <summary>GET System/Endpoint, cached for the session as the api client does.</summary>
    public Task<EndpointInfo> GetEndpointInfoAsync()
    {
        if (_endpointInfo is not null)
        {
            return Task.FromResult(_endpointInfo);
        }

        return FetchEndpointInfoAsync();
    }

    public Task<int> DetectBitrateAsync(bool force = false)
    {
        if (!force && _lastDetectedBitrate is { } cached && timers.NowMs - _lastDetectedBitrateTime <= CacheMs)
        {
            return Task.FromResult(cached);
        }

        if (_pending is { IsCompleted: false })
        {
            return _pending;
        }

        return _pending = DetectAsync();
    }

    private async Task<int> DetectAsync()
    {
        EndpointInfo info;
        try
        {
            info = await GetEndpointInfoAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            info = new EndpointInfo();
        }

        return await DetectInternalAsync(0, null, info);
    }

    private async Task<int> DetectInternalAsync(int index, int? currentBitrate, EndpointInfo info)
    {
        if (index >= Tests.Length)
        {
            return Normalize(currentBitrate, info);
        }

        var test = Tests[index];
        int bitrate;
        try
        {
            bitrate = await GetDownloadSpeedAsync(test.Bytes);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return Normalize(currentBitrate ?? 0, info);
        }

        return bitrate < test.Threshold
            ? Normalize(bitrate, info)
            : await DetectInternalAsync(index + 1, bitrate, info);
    }

    private async Task<int> GetDownloadSpeedAsync(int bytes)
    {
        using var cancellation = new CancellationTokenSource(TimeoutMs);
        using var request = new HttpRequestMessage(HttpMethod.Get, api.GetUrl("Playback/BitrateTest", ("Size", bytes)));
        request.Headers.TryAddWithoutValidation("Cache-Control", "no-cache, no-store");
        request.Headers.TryAddWithoutValidation("Authorization", api.AuthorizationHeader());
        using var response = await api.Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
        response.EnsureSuccessStatusCode();

        // The time is measured from the headers to the end of the body, as the original does.
        var start = timers.NowMs;
        var body = await response.Content.ReadAsByteArrayAsync(cancellation.Token);
        var seconds = Math.Max(timers.NowMs - start, 1) / 1000d;
        return (int)Math.Min(Math.Round(body.Length / seconds * 8), MaxBitrate);
    }

    private int Normalize(int? bitrate, EndpointInfo info)
    {
        if (bitrate is null or 0)
        {
            if (_lastDetectedBitrate is { } last)
            {
                return last;
            }

            bitrate = 0;
        }

        // PORT-TODO(playback): the original also clamps to navigator.connection.downlinkMax where the
        // browser exposes it (Network Information API on some mobile browsers).
        var result = (int)Math.Min(Math.Round(bitrate.Value * 0.7), MaxBitrate);
        if (info.IsInNetwork == true)
        {
            result = Math.Max(result, LanBitrate);
        }

        _lastDetectedBitrate = result;
        _lastDetectedBitrateTime = timers.NowMs;
        return result;
    }

    private async Task<EndpointInfo> FetchEndpointInfoAsync()
    {
        using var response = await api.SendAsync(HttpMethod.Get, "System/Endpoint");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        _endpointInfo = new EndpointInfo(ReadBool(root, "IsInNetwork"), ReadBool(root, "IsLocal"));
        return _endpointInfo;
    }

    private static bool? ReadBool(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
