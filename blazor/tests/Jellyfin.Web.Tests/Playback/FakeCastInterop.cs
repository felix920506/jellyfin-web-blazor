using System.Text.Json.Nodes;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>
/// The Cast sender SDK as the Chromecast player sees it: records what is sent and lets a test act as
/// the SDK (receivers appear, a session is joined or ends, the receiver sends a message).
/// Unavailable unless <see cref="SdkAvailable"/> is set.
/// </summary>
internal sealed class FakeCastInterop : ICastInterop
{
    public event Action<bool>? ReceiverAvailabilityChanged;

    public event Action<CastSessionInfo>? SessionJoined;

    public event Action<CastSessionInfo>? SessionUpdated;

    public event Action? SessionEnded;

    public event Action<string>? MessageReceived;

    public bool SdkAvailable { get; set; }

    public bool InitializeResult { get; set; } = true;

    /// <summary>What the device picker returns; null stands for a cancelled dialog.</summary>
    public CastSessionInfo? SessionToGrant { get; set; } = Receiver();

    public int Loads { get; private set; }

    public int SessionRequests { get; private set; }

    public int SessionStops { get; private set; }

    public List<string> Initialized { get; } = [];

    /// <summary>The messages sent to the receiver, parsed.</summary>
    public List<JsonObject> Messages { get; } = [];

    public List<double> VolumeLevels { get; } = [];

    /// <summary>The commands of <see cref="Messages"/>, in order.</summary>
    public List<string> Commands => Messages.Select(m => m["command"]!.GetValue<string>()).ToList();

    public static CastSessionInfo Receiver(string name = "Living Room Chromecast", double volume = 0.4) =>
        new() { SessionId = "cast-session-1", ReceiverName = name, VolumeLevel = volume, Muted = false };

    public JsonObject LastMessage(string command) => Messages.Last(m => m["command"]!.GetValue<string>() == command);

    public Task<bool> LoadAsync()
    {
        Loads++;
        return Task.FromResult(SdkAvailable);
    }

    public Task<bool> InitializeAsync(string applicationId)
    {
        Initialized.Add(applicationId);
        return Task.FromResult(InitializeResult);
    }

    public Task<CastSessionInfo?> RequestSessionAsync()
    {
        SessionRequests++;
        return Task.FromResult(SessionToGrant);
    }

    public Task SendMessageAsync(string message)
    {
        Messages.Add(JsonNode.Parse(message)!.AsObject());
        return Task.CompletedTask;
    }

    public Task SetReceiverVolumeLevelAsync(double level)
    {
        VolumeLevels.Add(level);
        return Task.CompletedTask;
    }

    public Task StopSessionAsync()
    {
        SessionStops++;
        return Task.CompletedTask;
    }

    public void ReportReceivers(bool available) => ReceiverAvailabilityChanged?.Invoke(available);

    public void JoinSession(CastSessionInfo session) => SessionJoined?.Invoke(session);

    public void UpdateSession(CastSessionInfo session) => SessionUpdated?.Invoke(session);

    public void EndSession() => SessionEnded?.Invoke();

    /// <summary>A message of the receiver: <c>{ type, data }</c>.</summary>
    public void Receive(string type, JsonNode? data = null) =>
        MessageReceived?.Invoke(new JsonObject { ["type"] = type, ["data"] = data?.DeepClone() }.ToJsonString());
}
