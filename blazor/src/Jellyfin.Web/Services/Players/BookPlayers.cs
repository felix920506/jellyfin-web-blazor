using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;

namespace Jellyfin.Web.Services;

public abstract class BookPlayerBase(string kind, IPlayersInterop interop, JellyfinApi api)
    : ManagedMediaPlayer(kind, interop, api), IItemMediaPlayer
{
    public override bool CanPlayItem(BaseItemDto item) => item.MediaType == BaseItemDto_MediaType.Book && Accepts(item.Path);
    protected abstract bool Accepts(string? path);
    protected override string? UrlFor(BaseItemDto item) => item.Id is { } id
        ? Api.GetUrl($"Items/{id:N}/Download", ("ApiKey", Api.AccessToken))
        : item.Path;
}

[AppService(typeof(IMediaPlayer))]
public sealed class EpubPlayer(IPlayersInterop interop, JellyfinApi api) : BookPlayerBase("epub", interop, api)
{
    public override string Name => "Book Player";
    public override string Id => "bookplayer";
    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "book", StringComparison.OrdinalIgnoreCase);
    protected override bool Accepts(string? path) => path?.EndsWith(".epub", StringComparison.OrdinalIgnoreCase) == true;
}

[AppService(typeof(IMediaPlayer))]
public sealed class PdfPlayer(IPlayersInterop interop, JellyfinApi api) : BookPlayerBase("pdf", interop, api)
{
    public override string Name => "PDF Player";
    public override string Id => "pdfplayer";
    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "book", StringComparison.OrdinalIgnoreCase);
    protected override bool Accepts(string? path) => path?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
}

[AppService(typeof(IMediaPlayer))]
public sealed class ComicsPlayer(IPlayersInterop interop, JellyfinApi api) : BookPlayerBase("comics", interop, api)
{
    public override string Name => "Comics Player";
    public override string Id => "comicsplayer";
    public override bool CanPlayMediaType(string? mediaType) => string.Equals(mediaType, "book", StringComparison.OrdinalIgnoreCase);
    protected override bool Accepts(string? path) => path is not null
        && new[] { ".cbz", ".cbr", ".cbt", ".cb7" }.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
}
