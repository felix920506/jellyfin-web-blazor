using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

[AppService(typeof(IMediaPlayer))]
public sealed class PhotoPlayer(IPlayersInterop interop, JellyfinApi api, UserSettings settings)
    : ManagedMediaPlayer("photo", interop, api), IItemMediaPlayer
{
    public override string Name => "Photo Player";
    public override string Id => "photoplayer";
    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "photo", StringComparison.OrdinalIgnoreCase);
    public override bool CanPlayItem(BaseItemDto item) => item.MediaType == BaseItemDto_MediaType.Photo
        && (item.Id is not null || !string.IsNullOrWhiteSpace(item.Path));
    protected override int SlideshowIntervalSeconds => Math.Max(1, settings.GetInt("slideshowInterval", 5, false));
    protected override string? UrlFor(BaseItemDto item)
    {
        var id = item.Id?.ToString("N");
        return id is null ? item.Path : Api.GetUrl($"Items/{id}/Images/Primary", ("ApiKey", Api.AccessToken));
    }

    protected override PlayerItemData ToPlayerItem(BaseItemDto item)
    {
        var data = base.ToPlayerItem(item);
        return data with { Url = UrlFor(item) ?? data.Url, ImageUrl = UrlFor(item), DownloadUrl = item.Id is { } id ? Api.GetUrl($"Items/{id:N}/Download", ("ApiKey", Api.AccessToken)) : null };
    }
}
