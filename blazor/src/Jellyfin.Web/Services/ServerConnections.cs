using System.Text.Json;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;

namespace Jellyfin.Web.Services;

/// <summary>Outcome of a connection attempt. Names match the original client's ConnectionState.</summary>
public enum ConnectionState
{
    SignedIn,
    ServerMismatch,
    ServerSignIn,
    ServerSelection,
    ServerUpdateNeeded,
    Unavailable
}

public sealed record ConnectResult(ConnectionState State, ServerInfo? Server = null, PublicServerInfo? SystemInfo = null);

/// <summary>
/// Finds, connects to and signs in to servers. Port of lib/jellyfin-apiclient
/// (ServerConnections + connectionManager), reduced to one active server at a time.
/// </summary>
public sealed class ServerConnections(
    JellyfinApi api,
    CredentialStore credentials,
    AppSettings appSettings,
    WebConfig config,
    IBrowser browser)
{
    /// <summary>Oldest server version this client talks to.</summary>
    public static readonly Version MinimumServerVersion = new(10, 9, 0);

    /// <summary>Raised after sign-in (or a restored session), once the user is loaded.</summary>
    public event Func<UserDto, Task>? LocalUserSignedIn;

    /// <summary>Raised after sign-out.</summary>
    public event Func<Task>? LocalUserSignedOut;

    /// <summary>Raised after every successful connection to a server.</summary>
    public event Action<ConnectResult>? Connected;

    /// <summary>True once the first automatic connection of this page load has run.</summary>
    public bool FirstConnectionDone { get; set; }

    /// <summary>The saved entry of the server the API is currently bound to.</summary>
    public ServerInfo? CurrentServer { get; private set; }

    /// <summary>Saved servers, most recently used first.</summary>
    public IReadOnlyList<ServerInfo> GetAvailableServers()
    {
        credentials.Servers.Sort((a, b) => (b.DateLastAccessed ?? 0).CompareTo(a.DateLastAccessed ?? 0));
        return credentials.Servers.ToList();
    }

    public ServerInfo? GetLastUsedServer() => GetAvailableServers().FirstOrDefault();

    public ServerInfo? GetServer(string? id) => string.IsNullOrEmpty(id)
        ? null
        : credentials.Servers.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Startup: bind the API to the last used server, or to the server hosting this page.</summary>
    public async Task InitializeAsync()
    {
        var url = GetLastUsedServer()?.Address;
        if (string.IsNullOrEmpty(url))
        {
            url = await DiscoverServerAddressAsync();
        }

        if (!string.IsNullOrEmpty(url))
        {
            InitApiClient(url);
        }
    }

    /// <summary>
    /// Port of serverAddress() in utils/dashboard.js: the servers from config.json, or the server
    /// this page is hosted on. Prefers a server whose startup wizard is not complete.
    /// </summary>
    public async Task<string?> DiscoverServerAddressAsync()
    {
        var candidates = config.Data.Servers.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.TrimEnd('/')).ToList();
        if (candidates.Count == 0)
        {
            var environment = browser.Environment;
            if (environment.Protocol == "file:")
            {
                return null;
            }

            var href = environment.Href;
            var hashIndex = href.IndexOf('#');
            if (hashIndex >= 0)
            {
                href = href[..hashIndex];
            }

            var webIndex = href.LastIndexOf("/web", StringComparison.OrdinalIgnoreCase);
            candidates.Add(webIndex >= 0 ? href[..webIndex] : environment.Origin);
        }

        var results = await Task.WhenAll(candidates.Select(async url => (Url: url, Info: await api.GetPublicInfoAsync(url, TimeSpan.FromSeconds(10)))));
        var answered = results.Where(r => r.Info is not null).ToList();
        return answered.FirstOrDefault(r => r.Info!.StartupWizardCompleted != true).Url ?? answered.FirstOrDefault().Url;
    }

    /// <summary>Binds the API to a server address, creating a saved entry when none matches.</summary>
    public void InitApiClient(string url)
    {
        url = url.TrimEnd('/');
        var server = credentials.Servers.FirstOrDefault(s => SameAddress(s.ManualAddress, url) || SameAddress(s.LocalAddress, url) || SameAddress(s.RemoteAddress, url));
        var isNew = server is null;
        server ??= new ServerInfo();
        server.DateLastAccessed = Now();
        server.LastConnectionMode = ConnectionMode.Manual;
        server.ManualAddress = url;
        server.ManualAddressOnly = true;

        if (isNew)
        {
            credentials.Replace([server]);
        }
        else
        {
            credentials.Save();
        }

        CurrentServer = server;
        api.SetServer(url, server.Id, server.Name);
    }

    /// <summary>Connects to the most recently used saved server.</summary>
    public async Task<ConnectResult> ConnectAsync(bool? enableAutoLogin = null)
    {
        var first = GetLastUsedServer();
        credentials.Save();
        return first is null
            ? new ConnectResult(ConnectionState.ServerSelection)
            : await ConnectToServerAsync(first, enableAutoLogin);
    }

    /// <summary>Connects to a saved server, restoring its session when a valid token is stored.</summary>
    public async Task<ConnectResult> ConnectToServerAsync(ServerInfo server, bool? enableAutoLogin = null)
    {
        var autoLogin = enableAutoLogin ?? appSettings.EnableAutoLogin;
        var reached = await TryReconnectAsync(server);
        if (reached is null)
        {
            return new ConnectResult(ConnectionState.Unavailable, server);
        }

        var (url, mode, info) = reached.Value;
        if (Version.TryParse(info.Version, out var version) && version < MinimumServerVersion)
        {
            return new ConnectResult(ConnectionState.ServerUpdateNeeded, server, info);
        }

        if (!string.IsNullOrEmpty(server.Id) && !string.Equals(server.Id, info.Id, StringComparison.OrdinalIgnoreCase))
        {
            return new ConnectResult(ConnectionState.ServerMismatch, server, info);
        }

        UserDto? user = null;
        if (!autoLogin)
        {
            server.UserId = null;
            server.AccessToken = null;
        }
        else if (!string.IsNullOrEmpty(server.AccessToken))
        {
            user = await ValidateAuthenticationAsync(server, url);
        }

        server.Name = info.ServerName ?? server.Name;
        server.Id = info.Id ?? server.Id;
        if (!string.IsNullOrEmpty(info.LocalAddress))
        {
            server.LocalAddress = info.LocalAddress;
        }

        server.LastConnectionMode = mode;
        server.DateLastAccessed = Now();
        server = credentials.AddOrUpdate(server);
        credentials.Save();

        CurrentServer = server;
        api.SetServer(url, server.Id, server.Name);
        api.SetAuthentication(server.AccessToken, server.UserId, user);

        var signedIn = !string.IsNullOrEmpty(server.AccessToken) && user is not null;
        var result = new ConnectResult(signedIn ? ConnectionState.SignedIn : ConnectionState.ServerSignIn, server, info);

        if (signedIn)
        {
            await ReportCapabilitiesAsync();
            await RaiseAsync(LocalUserSignedIn, h => h(user!));
        }

        Connected?.Invoke(result);
        return result;
    }

    /// <summary>Connects to an address typed by the user. Tries https, then http, when no scheme is given.</summary>
    public async Task<ConnectResult> ConnectToAddressAsync(string address, bool? enableAutoLogin = null)
    {
        address = NormalizeAddress(address);
        if (address.Length == 0)
        {
            return new ConnectResult(ConnectionState.Unavailable);
        }

        var candidates = address.Contains("://", StringComparison.Ordinal)
            ? new[] { address }
            : ["https://" + address, "http://" + address];

        foreach (var url in candidates)
        {
            var info = await api.GetPublicInfoAsync(url);
            if (info is null)
            {
                continue;
            }

            var server = GetServer(info.Id) ?? new ServerInfo { Id = info.Id };
            server.ManualAddress = url;
            server.LastConnectionMode = ConnectionMode.Manual;
            server.ManualAddressOnly ??= true;
            return await ConnectToServerAsync(server, enableAutoLogin);
        }

        return new ConnectResult(ConnectionState.Unavailable);
    }

    /// <summary>Completes sign-in with the result of any of the authenticate endpoints.</summary>
    public async Task OnAuthenticatedAsync(AuthenticationResult result)
    {
        var server = CurrentServer ?? new ServerInfo { ManualAddress = api.ServerAddress, LastConnectionMode = ConnectionMode.Manual, ManualAddressOnly = true };
        server.Id = result.ServerId ?? server.Id;
        server.UserId = result.User?.Id?.ToString("N");
        server.AccessToken = result.AccessToken;
        server.DateLastAccessed = Now();
        server = credentials.AddOrUpdate(server);
        credentials.Save();

        CurrentServer = server;
        api.SetServer(api.ServerAddress, server.Id, server.Name ?? result.User?.ServerName);
        api.SetAuthentication(server.AccessToken, server.UserId, result.User);
        await ReportCapabilitiesAsync();

        if (result.User is not null)
        {
            await RaiseAsync(LocalUserSignedIn, h => h(result.User));
        }
    }

    /// <summary>Signs out of the current server and forgets its token.</summary>
    public async Task LogoutAsync()
    {
        if (api.IsLoggedIn)
        {
            try
            {
                await api.Client.Sessions.Logout.PostAsync();
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[ServerConnections] logout request failed: {ex.Message}");
            }
        }

        foreach (var server in credentials.Servers.Where(s => s.UserLinkType != "Guest"))
        {
            server.UserId = null;
            server.AccessToken = null;
            server.ExchangeToken = null;
        }

        credentials.Save();
        api.SetAuthentication(null, null);
        await RaiseAsync(LocalUserSignedOut, h => h());
    }

    public void DeleteServer(string? id)
    {
        credentials.Servers.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        credentials.Save();
        if (CurrentServer is not null && string.Equals(CurrentServer.Id, id, StringComparison.OrdinalIgnoreCase))
        {
            CurrentServer = null;
        }
    }

    /// <summary>"Force connect" after a server mismatch: adopt the id now answering at the address.</summary>
    public async Task UpdateSavedServerIdAsync(ServerInfo server)
    {
        var info = await api.GetPublicInfoAsync(server.Address);
        if (info?.Id is null)
        {
            return;
        }

        server.Id = info.Id;
        server.Name = info.ServerName ?? server.Name;
        server.AccessToken = null;
        server.UserId = null;
        credentials.Save();
    }

    /// <summary>Tells the server what this client can play and which remote commands it accepts.</summary>
    public async Task ReportCapabilitiesAsync()
    {
        try
        {
            await api.Client.Sessions.Capabilities.Full.PostAsync(new ClientCapabilitiesDto
            {
                PlayableMediaTypes = [MediaType.Audio, MediaType.Video],
                SupportsMediaControl = true,
                SupportsPersistentIdentifier = false,
                SupportedCommands =
                [
                    GeneralCommandType.MoveUp, GeneralCommandType.MoveDown, GeneralCommandType.MoveLeft, GeneralCommandType.MoveRight,
                    GeneralCommandType.PageUp, GeneralCommandType.PageDown, GeneralCommandType.PreviousLetter, GeneralCommandType.NextLetter,
                    GeneralCommandType.ToggleOsd, GeneralCommandType.ToggleContextMenu, GeneralCommandType.Select, GeneralCommandType.Back,
                    GeneralCommandType.SendKey, GeneralCommandType.SendString, GeneralCommandType.GoHome, GeneralCommandType.GoToSettings,
                    GeneralCommandType.VolumeUp, GeneralCommandType.VolumeDown, GeneralCommandType.Mute, GeneralCommandType.Unmute,
                    GeneralCommandType.ToggleMute, GeneralCommandType.SetVolume, GeneralCommandType.SetAudioStreamIndex,
                    GeneralCommandType.SetSubtitleStreamIndex, GeneralCommandType.DisplayContent, GeneralCommandType.GoToSearch,
                    GeneralCommandType.DisplayMessage, GeneralCommandType.SetRepeatMode, GeneralCommandType.SetShuffleQueue,
                    GeneralCommandType.ChannelUp, GeneralCommandType.ChannelDown, GeneralCommandType.PlayMediaSource, GeneralCommandType.PlayTrailers
                ]
            });
        }
        catch (Exception ex)
        {
            AppLog.Warn($"[ServerConnections] reporting capabilities failed: {ex.Message}");
        }
    }

    private static async Task RaiseAsync<THandler>(THandler? handlers, Func<THandler, Task> invoke)
        where THandler : Delegate
    {
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<THandler>())
        {
            try
            {
                await invoke(handler);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"[ServerConnections] event handler failed: {ex}");
            }
        }
    }

    internal static string NormalizeAddress(string address)
    {
        address = address.Trim().TrimEnd('/');
        if (address.StartsWith("http:", StringComparison.OrdinalIgnoreCase))
        {
            address = "http:" + address[5..];
        }
        else if (address.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
        {
            address = "https:" + address[6..];
        }

        return address;
    }

    // Races the server's addresses (staggered by 100 ms each) and returns the first that answers.
    private async Task<(string Url, int Mode, PublicServerInfo Info)?> TryReconnectAsync(ServerInfo server)
    {
        var manualOnly = server.ManualAddressOnly == true;
        var candidates = new List<(string Url, int Mode, int DelayMs)>();
        if (!manualOnly && !string.IsNullOrEmpty(server.LocalAddress))
        {
            candidates.Add((server.LocalAddress, ConnectionMode.Local, 0));
        }

        if (!string.IsNullOrEmpty(server.ManualAddress))
        {
            candidates.Add((server.ManualAddress, ConnectionMode.Manual, 100));
        }

        if (!manualOnly && !string.IsNullOrEmpty(server.RemoteAddress))
        {
            candidates.Add((server.RemoteAddress, ConnectionMode.Remote, 200));
        }

        if (candidates.Count == 0 && !string.IsNullOrEmpty(server.Address))
        {
            candidates.Add((server.Address, server.LastConnectionMode ?? ConnectionMode.Manual, 0));
        }

        var pending = candidates
            .DistinctBy(c => c.Url.TrimEnd('/'), StringComparer.OrdinalIgnoreCase)
            .Select(async c =>
            {
                if (c.DelayMs > 0 && candidates.Count > 1)
                {
                    await Task.Delay(c.DelayMs);
                }

                var info = await api.GetPublicInfoAsync(c.Url);
                return (Url: c.Url.TrimEnd('/'), c.Mode, Info: info);
            })
            .ToList();

        while (pending.Count > 0)
        {
            var finished = await Task.WhenAny(pending);
            pending.Remove(finished);
            var (url, mode, info) = await finished;
            if (info is not null)
            {
                return (url, mode, info);
            }
        }

        return null;
    }

    // Checks a stored token; clears it from the saved entry when the server rejects it.
    private async Task<UserDto?> ValidateAuthenticationAsync(ServerInfo server, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + "/Users/Me");
            request.Headers.TryAddWithoutValidation("Authorization", api.AuthorizationHeader(server.AccessToken));
            using var response = await api.Http.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var user = KiotaJson.Parse(await response.Content.ReadAsStringAsync(), UserDto.CreateFromDiscriminatorValue);
                if (user is not null)
                {
                    return user;
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            AppLog.Warn($"[ServerConnections] validating the saved session failed: {ex.Message}");
        }

        server.UserId = null;
        server.AccessToken = null;
        return null;
    }

    private static bool SameAddress(string? a, string b) => !string.IsNullOrEmpty(a) && string.Equals(a.TrimEnd('/'), b, StringComparison.OrdinalIgnoreCase);

    private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}
