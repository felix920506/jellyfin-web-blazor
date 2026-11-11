using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>How a saved server was last reached.</summary>
public static class ConnectionMode
{
    public const int Local = 0;
    public const int Remote = 1;
    public const int Manual = 2;
}

/// <summary>
/// A saved server. The JSON shape is the one written by the original client
/// (jellyfin-apiclient "Credentials"), so both clients can share a browser profile.
/// </summary>
public sealed class ServerInfo
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? ManualAddress { get; set; }
    public string? LocalAddress { get; set; }
    public string? RemoteAddress { get; set; }
    public int? LastConnectionMode { get; set; }
    public long? DateLastAccessed { get; set; }
    public string? UserId { get; set; }
    public string? AccessToken { get; set; }

    [JsonPropertyName("manualAddressOnly")]
    public bool? ManualAddressOnly { get; set; }

    public string? UserLinkType { get; set; }
    public string? ExchangeToken { get; set; }

    /// <summary>Unknown properties are preserved when the credentials are written back.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Port of getServerAddress(): the address for the last connection mode.</summary>
    [JsonIgnore]
    public string? Address => LastConnectionMode switch
    {
        ConnectionMode.Local when !string.IsNullOrEmpty(LocalAddress) => LocalAddress,
        ConnectionMode.Manual when !string.IsNullOrEmpty(ManualAddress) => ManualAddress,
        ConnectionMode.Remote when !string.IsNullOrEmpty(RemoteAddress) => RemoteAddress,
        _ => FirstNonEmpty(ManualAddress, LocalAddress, RemoteAddress)
    };

    private static string? FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v));
}

/// <summary>Saved servers and tokens in localStorage under "jellyfin_credentials".</summary>
public sealed class CredentialStore(IBrowser browser)
{
    public const string StorageKey = "jellyfin_credentials";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private List<ServerInfo>? _servers;

    /// <summary>The live list. Call <see cref="Save"/> after changing an entry.</summary>
    public List<ServerInfo> Servers => _servers ??= Load();

    public void Save() => browser.StorageSet(StorageKey, JsonSerializer.Serialize(new Document { Servers = Servers }, JsonOptions));

    public void Replace(IEnumerable<ServerInfo> servers)
    {
        _servers = servers.ToList();
        Save();
    }

    /// <summary>Adds the server, or merges it into the saved entry with the same id.</summary>
    public ServerInfo AddOrUpdate(ServerInfo server)
    {
        var existing = string.IsNullOrEmpty(server.Id)
            ? null
            : Servers.FirstOrDefault(s => string.Equals(s.Id, server.Id, StringComparison.OrdinalIgnoreCase));

        if (existing is null)
        {
            Servers.Add(server);
            return server;
        }

        if (ReferenceEquals(existing, server))
        {
            return existing;
        }

        existing.DateLastAccessed = Math.Max(existing.DateLastAccessed ?? 0, server.DateLastAccessed ?? 0);
        existing.Name = server.Name ?? existing.Name;
        existing.ManualAddress = server.ManualAddress ?? existing.ManualAddress;
        existing.LocalAddress = server.LocalAddress ?? existing.LocalAddress;
        existing.RemoteAddress = server.RemoteAddress ?? existing.RemoteAddress;
        existing.LastConnectionMode = server.LastConnectionMode ?? existing.LastConnectionMode;
        existing.ManualAddressOnly = server.ManualAddressOnly ?? existing.ManualAddressOnly;
        if (!string.IsNullOrEmpty(server.AccessToken))
        {
            existing.AccessToken = server.AccessToken;
            existing.UserId = server.UserId;
        }

        return existing;
    }

    private List<ServerInfo> Load()
    {
        var json = browser.StorageGet(StorageKey);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Document>(json, JsonOptions)?.Servers ?? [];
        }
        catch (JsonException ex)
        {
            AppLog.Warn($"[CredentialStore] ignoring unreadable credentials: {ex.Message}");
            return [];
        }
    }

    private sealed class Document
    {
        public List<ServerInfo> Servers { get; set; } = [];
    }
}
