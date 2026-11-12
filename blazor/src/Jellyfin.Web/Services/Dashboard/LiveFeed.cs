using System.Text.Json;

namespace Jellyfin.Web.Services;

/// <summary>
/// A periodic server feed over the web socket: "Sessions", "ScheduledTasksInfo", "ActivityLogEntry".
/// The server only sends these while a client asked for them with "{type}Start" (argument:
/// "initial delay,interval" in milliseconds); disposing sends "{type}Stop". The start message
/// is repeated whenever the socket (re)connects, as a new socket has no subscriptions.
/// </summary>
public sealed class LiveFeed : IDisposable
{
    private readonly ServerEvents _events;
    private readonly string _messageType;
    private readonly string _interval;
    private readonly IDisposable _subscription;
    private bool _disposed;

    public LiveFeed(ServerEvents events, string messageType, string interval, Action<JsonElement> onMessage)
    {
        _events = events;
        _messageType = messageType;
        _interval = interval;
        _subscription = events.Subscribe(messageType, onMessage);
        events.ConnectionChanged += OnConnectionChanged;
        Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _events.ConnectionChanged -= OnConnectionChanged;
        _subscription.Dispose();
        _ = _events.SendAsync(_messageType + "Stop");
    }

    private void OnConnectionChanged()
    {
        if (_events.IsConnected)
        {
            Start();
        }
    }

    private void Start() => _ = _events.SendAsync(_messageType + "Start", _interval);
}
