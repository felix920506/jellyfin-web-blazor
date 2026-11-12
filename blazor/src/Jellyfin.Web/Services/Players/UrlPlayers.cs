using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

[AppService(typeof(IMediaPlayer))]
public sealed class UrlPlayer(IPlayersInterop interop, JellyfinApi api)
    : ManagedMediaPlayer("url", interop, api), IItemMediaPlayer, IUrlMediaPlayer
{
    public override string Name => "URL Player";
    public override string Id => "urlplayer";
    public override int Priority => 1;
    public override bool CanPlayMediaType(string? mediaType) =>
        mediaType?.Equals("audio", StringComparison.OrdinalIgnoreCase) == true
        || mediaType?.Equals("video", StringComparison.OrdinalIgnoreCase) == true;
    public override bool CanPlayItem(BaseItemDto item) => item.Id is null && CanPlayUrl(item.Path ?? string.Empty);
    public override bool CanPlayUrl(string url) => !url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase)
        && !url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    protected override string? UrlFor(BaseItemDto item) => item.Path;
}

[AppService(typeof(IMediaPlayer))]
public sealed class YoutubePlayer(IPlayersInterop interop, JellyfinApi api)
    : ManagedMediaPlayer("youtube", interop, api), IItemMediaPlayer, IUrlMediaPlayer
{
    public override string Name => "Youtube Player";
    public override string Id => "youtubeplayer";
    public override int Priority => 0;
    public override bool CanPlayMediaType(string? mediaType) =>
        mediaType?.Equals("audio", StringComparison.OrdinalIgnoreCase) == true
        || mediaType?.Equals("video", StringComparison.OrdinalIgnoreCase) == true;
    public override bool CanPlayItem(BaseItemDto item) => item.Id is null && CanPlayUrl(item.Path ?? string.Empty);
    public override bool CanPlayUrl(string url) => url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    protected override string? UrlFor(BaseItemDto item) => item.Path;
}
