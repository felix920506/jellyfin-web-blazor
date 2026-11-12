using System.Text.Json;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>Estimates server time using Jellyfin's four-timestamp server-time response.</summary>
[AppService]
public sealed class TimeSyncServer(JellyfinApi api, IPlaybackTimers timers)
{
    private const int MaximumSamples = 8;
    private readonly List<SyncPlayTimeMeasurement> _measurements = [];

    public bool IsReady => _measurements.Count > 0;

    public double OffsetMilliseconds => _measurements.Count == 0
        ? 0
        : _measurements.MinBy(m => m.RoundTripMilliseconds)!.OffsetMilliseconds;

    public int PingMilliseconds => _measurements.Count == 0
        ? 0
        : _measurements.MinBy(m => m.RoundTripMilliseconds)!.PingMilliseconds;

    public async Task<bool> UpdateAsync(CancellationToken cancellationToken = default)
    {
        var sent = timers.NowMs;
        using var response = await api.SendAsync(HttpMethod.Get, "GetServerTime", null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var received = timers.NowMs;
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = document.RootElement;
        var requestReceived = ParseTime(root, "RequestReceptionTime");
        var responseSent = ParseTime(root, "ResponseTransmissionTime");
        _measurements.Add(new SyncPlayTimeMeasurement(sent, requestReceived, responseSent, received));
        if (_measurements.Count > MaximumSamples)
        {
            _measurements.RemoveAt(0);
        }

        return true;
    }

    public DateTimeOffset RemoteToLocal(DateTimeOffset remote) => remote.AddMilliseconds(-OffsetMilliseconds);

    public DateTimeOffset LocalToRemote(DateTimeOffset local) => local.AddMilliseconds(OffsetMilliseconds);

    private static long ParseTime(JsonElement root, string property)
    {
        var value = root.GetProperty(property);
        return value.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(value.GetString()!).ToUnixTimeMilliseconds()
            : value.GetInt64();
    }
}
