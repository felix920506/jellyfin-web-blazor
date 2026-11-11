using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Web.Services;

/// <summary>
/// The server's websocket: push messages (UserDataChanged, LibraryChanged, Sessions, Play,
/// Playstate, GeneralCommand, SyncPlay…) and keep-alives. Connects while a user is signed in
/// and reconnects after failures.
/// </summary>
public sealed class ServerEvents(JellyfinApi api) : IAsyncDisposable
{
    private readonly Dictionary<string, List<Action<JsonElement>>> _handlers = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _socket;
    private CancellationTokenSource? _run;

    /// <summary>Raised when the socket opens or closes.</summary>
    public event Action? ConnectionChanged;

    public bool IsConnected => _socket?.State == WebSocketState.Open;

    /// <summary>
    /// Listens for a message type, e.g. "UserDataChanged". The handler receives the message's
    /// <c>Data</c> element (cloned, safe to keep). Dispose the result to stop listening.
    /// </summary>
    public IDisposable Subscribe(string messageType, Action<JsonElement> handler)
    {
        if (!_handlers.TryGetValue(messageType, out var list))
        {
            _handlers[messageType] = list = [];
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    /// <summary>Listens for several message types with one handler receiving (type, data).</summary>
    public IDisposable Subscribe(IEnumerable<string> messageTypes, Action<string, JsonElement> handler)
    {
        var subscriptions = messageTypes.Select(type => Subscribe(type, data => handler(type, data))).ToList();
        return new Subscription(() => subscriptions.ForEach(s => s.Dispose()));
    }

    /// <summary>Sends a message, e.g. <c>SendAsync("SessionsStart", "0,1500")</c>. No-op while disconnected.</summary>
    public async Task SendAsync(string messageType, object? data = null)
    {
        var socket = _socket;
        if (socket?.State != WebSocketState.Open)
        {
            return;
        }

        var payload = data is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { MessageType = messageType })
            : JsonSerializer.SerializeToUtf8Bytes(new { MessageType = messageType, Data = data });

        await _sendLock.WaitAsync();
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            // The receive loop notices the broken socket and reconnects.
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>Opens the socket for the signed-in user (restarting it if already open).</summary>
    public void Start()
    {
        Stop();
        if (!api.IsLoggedIn || string.IsNullOrEmpty(api.ServerAddress))
        {
            return;
        }

        var run = _run = new CancellationTokenSource();
        _ = RunAsync(run.Token);
    }

    public void Stop()
    {
        _run?.Cancel();
        _run = null;
        var socket = _socket;
        _socket = null;
        if (socket is not null)
        {
            socket.Abort();
            socket.Dispose();
            ConnectionChanged?.Invoke();
        }
    }

    public ValueTask DisposeAsync()
    {
        Stop();
        return ValueTask.CompletedTask;
    }

    /// <summary>Delivers a message to the subscribers. Also used by tests.</summary>
    internal void Dispatch(string messageType, JsonElement data)
    {
        if (!_handlers.TryGetValue(messageType, out var list))
        {
            return;
        }

        foreach (var handler in list.ToArray())
        {
            try
            {
                handler(data);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[ServerEvents] handler for {messageType} failed: {ex}");
            }
        }
    }

    private async Task RunAsync(CancellationToken cancellation)
    {
        var retryDelay = TimeSpan.FromSeconds(1);
        while (!cancellation.IsCancellationRequested)
        {
            var socket = new ClientWebSocket();
            try
            {
                var address = api.ServerAddress!;
                var wsAddress = (address.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws") + address[address.IndexOf("://", StringComparison.Ordinal)..];
                var uri = new Uri($"{wsAddress}/socket?ApiKey={Uri.EscapeDataString(api.AccessToken ?? string.Empty)}&deviceId={Uri.EscapeDataString(api.DeviceId)}");
                await socket.ConnectAsync(uri, cancellation);
                _socket = socket;
                retryDelay = TimeSpan.FromSeconds(1);
                ConnectionChanged?.Invoke();
                await ReceiveAsync(socket, cancellation);
            }
            catch (OperationCanceledException)
            {
                // Stopped.
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[ServerEvents] websocket error: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_socket, socket))
                {
                    _socket = null;
                    ConnectionChanged?.Invoke();
                }

                socket.Dispose();
            }

            try
            {
                await Task.Delay(retryDelay, cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            retryDelay = TimeSpan.FromSeconds(Math.Min(retryDelay.TotalSeconds * 2, 30));
        }
    }

    private async Task ReceiveAsync(ClientWebSocket socket, CancellationToken cancellation)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        using var keepAlive = new CancellationTokenSource();
        try
        {
            while (socket.State == WebSocketState.Open && !cancellation.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cancellation);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
                message.SetLength(0);
                HandleMessage(text, keepAlive.Token);
            }
        }
        finally
        {
            keepAlive.Cancel();
        }
    }

    private void HandleMessage(string text, CancellationToken keepAliveToken)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (!root.TryGetProperty("MessageType", out var typeElement) || typeElement.GetString() is not { } type)
            {
                return;
            }

            var data = root.TryGetProperty("Data", out var dataElement) ? dataElement.Clone() : default;
            if (type == "ForceKeepAlive")
            {
                var seconds = data.ValueKind == JsonValueKind.Number ? data.GetInt32() : 60;
                _ = KeepAliveAsync(TimeSpan.FromSeconds(Math.Max(1, seconds / 2.0)), keepAliveToken);
                return;
            }

            if (type != "KeepAlive")
            {
                Dispatch(type, data);
            }
        }
        catch (JsonException ex)
        {
            AppLog.Warn($"[ServerEvents] bad message: {ex.Message}");
        }
    }

    private async Task KeepAliveAsync(TimeSpan interval, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await SendAsync("KeepAlive");
                await Task.Delay(interval, cancellation);
            }
        }
        catch (OperationCanceledException)
        {
            // Socket closed.
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}
