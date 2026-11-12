using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

/// <summary>Data sent to the DOM player modules. It deliberately contains no SDK objects.</summary>
public sealed record PlayerItemData(
    string? Id,
    string Name,
    string MediaType,
    string Url,
    string? ImageUrl = null,
    string? DownloadUrl = null,
    string? Path = null);

public sealed record PlayerOpenRequest(
    string Kind,
    IReadOnlyList<PlayerItemData> Items,
    int StartIndex,
    long StartPositionTicks,
    bool Fullscreen,
    int SlideshowIntervalSeconds = 5);

public interface IPlayersInterop
{
    event Action<string, string, PlayerSnapshot>? Event;

    Task<PlayerSnapshot> OpenAsync(PlayerOpenRequest request);
    Task StopAsync(string kind, bool destroy);
    void Pause(string kind);
    void Unpause(string kind);
    void Next(string kind);
    void Previous(string kind);
    void SetCurrentTime(string kind, double seconds);
    void ToggleFullscreen(string kind);
}
