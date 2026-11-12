using System.Globalization;
using System.Text.Json;

namespace Jellyfin.Web.Services;

/// <summary>The progress of a server-side item refresh, as reported by the websocket.</summary>
public readonly record struct RefreshProgressState(double Progress, string? Status);

/// <summary>Shares RefreshProgress websocket state between cards and pages.</summary>
[AppService]
public sealed class RefreshProgressStore : IDisposable
{
    private readonly Dictionary<string, RefreshProgressState> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDisposable _subscription;

    public event Action<string>? Changed;

    public RefreshProgressStore(ServerEvents events)
    {
        _subscription = events.Subscribe("RefreshProgress", OnProgress);
    }

    public RefreshProgressState Get(string? itemId) =>
        itemId is not null && _items.TryGetValue(itemId, out var state) ? state : default;

    public void Dispose() => _subscription.Dispose();

    private void OnProgress(JsonElement data)
    {
        if (!TryString(data, "ItemId", out var itemId) || itemId.Length == 0)
        {
            return;
        }

        var progress = TryDouble(data, "Progress");
        var status = TryString(data, "Status", out var value) ? value : null;
        _items[itemId] = new RefreshProgressState(progress, status);
        Changed?.Invoke(itemId);
    }

    private static bool TryString(JsonElement data, string name, out string value)
    {
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static double TryDouble(JsonElement data, string name)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty(name, out var property))
        {
            return 0;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
        {
            return number;
        }

        return property.ValueKind == JsonValueKind.String
            && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : 0;
    }
}
