using Jellyfin.Web.Interop;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>The image chosen for a card.</summary>
/// <param name="Url">Absolute image URL, or null when the item has no usable image.</param>
/// <param name="BlurHash">Blurhash of that image, when the server sent one.</param>
/// <param name="ForceName">The image carries no title (thumb/backdrop), so the card must show the name.</param>
/// <param name="CoverImage">The image ratio is close to the card's: fill the card.</param>
public sealed record CardImage(string? Url, string? BlurHash, bool ForceName, bool CoverImage)
{
    public static readonly CardImage None = new(null, null, false, false);
}

/// <summary>
/// Image selection and URLs for items. Port of <c>cardbuilder/utils/url.ts</c> (<c>getCardImageUrl</c>):
/// which of an item's images (own, series, parent, album, channel) fills a card of a given shape.
/// </summary>
[AppService]
public sealed class CardImages(JellyfinApi api, IBrowser browser)
{
    /// <summary>The image for <paramref name="item"/> on a card built with <paramref name="options"/>.</summary>
    /// <param name="shape">The resolved shape (not an auto shape).</param>
    public CardImage GetCardImage(BaseItemDto item, CardOptions options, string? shape)
    {
        item = item.Extras()?.ProgramInfo ?? item;

        var width = options.Width;
        double? height = null;
        var primaryImageAspectRatio = item.PrimaryImageAspectRatio;
        var forceName = false;
        string? imgUrl = null;
        string? imgTag = null;
        var coverImage = false;
        var uiAspect = CardShape.GetDesiredAspect(shape);
        string? imgType = null;
        string? itemId = null;

        var type = item.TypeName();
        var extras = item.Extras();
        var preferThumb = options.PreferThumb.IsTruthy;
        var inheritThumb = options.InheritThumb != false;
        var thumbTag = item.ImageTag("Thumb");
        var primaryTag = item.ImageTag("Primary");
        var backdropTag = item.BackdropImageTags is { Count: > 0 } backdrops ? backdrops[0] : null;
        var parentBackdropTag = item.ParentBackdropImageTags is { Count: > 0 } parentBackdrops ? parentBackdrops[0] : null;
        var skipEpisodeParentPoster = type == "Episode" && uiAspect is > 1;

        void UsePrimaryRatio()
        {
            height = width is > 0 && primaryImageAspectRatio is > 0 ? width.Value / primaryImageAspectRatio.Value : null;
            if (primaryImageAspectRatio is > 0 && uiAspect is { } aspect)
            {
                coverImage = Math.Abs(primaryImageAspectRatio.Value - aspect) / aspect <= 0.2;
            }
        }

        if (preferThumb && thumbTag is not null)
        {
            imgType = "Thumb";
            imgTag = thumbTag;
        }
        else if ((options.PreferBanner || shape == CardShape.Banner) && item.ImageTag("Banner") is { } bannerTag)
        {
            imgType = "Banner";
            imgTag = bannerTag;
        }
        else if (options.PreferDisc && item.ImageTag("Disc") is { } discTag)
        {
            imgType = "Disc";
            imgTag = discTag;
        }
        else if (options.PreferLogo && item.ImageTag("Logo") is { } logoTag)
        {
            imgType = "Logo";
            imgTag = logoTag;
        }
        else if (options.PreferLogo && !string.IsNullOrEmpty(item.ParentLogoImageTag) && item.ParentLogoItemId is not null)
        {
            imgType = "Logo";
            imgTag = item.ParentLogoImageTag;
            itemId = item.ParentLogoItemId.IdText();
        }
        else if (preferThumb && !string.IsNullOrEmpty(item.SeriesThumbImageTag) && inheritThumb)
        {
            imgType = "Thumb";
            imgTag = item.SeriesThumbImageTag;
            itemId = item.SeriesId.IdText();
        }
        else if (preferThumb && item.ParentThumbItemId is not null && inheritThumb && item.MediaTypeName() != "Photo")
        {
            imgType = "Thumb";
            imgTag = item.ParentThumbImageTag;
            itemId = item.ParentThumbItemId.IdText();
        }
        else if (preferThumb && backdropTag is not null)
        {
            imgType = "Backdrop";
            imgTag = backdropTag;
            forceName = true;
        }
        else if (preferThumb && parentBackdropTag is not null && inheritThumb && type == "Episode")
        {
            imgType = "Backdrop";
            imgTag = parentBackdropTag;
            itemId = item.ParentBackdropItemId.IdText();
        }
        else if (options.PreferParentPoster && !preferThumb && type == "Episode"
                 && ((!string.IsNullOrEmpty(item.ParentPrimaryImageTag) && item.ParentPrimaryImageItemId is not null)
                     || (!string.IsNullOrEmpty(item.SeriesPrimaryImageTag) && item.SeriesId is not null)))
        {
            imgType = "Primary";
            if (!string.IsNullOrEmpty(item.ParentPrimaryImageTag) && item.ParentPrimaryImageItemId is not null)
            {
                imgTag = item.ParentPrimaryImageTag;
                itemId = item.ParentPrimaryImageItemId.IdText();
            }
            else
            {
                imgTag = item.SeriesPrimaryImageTag;
                itemId = item.SeriesId.IdText();
            }
        }
        else if (primaryTag is not null && (type != "Episode" || item.ChildCount != 0))
        {
            imgType = "Primary";
            imgTag = primaryTag;
            UsePrimaryRatio();
            if (preferThumb && !options.ShowTitle.IsFalse)
            {
                forceName = true;
            }
        }
        else if (!string.IsNullOrEmpty(item.SeriesPrimaryImageTag) && !skipEpisodeParentPoster)
        {
            imgType = "Primary";
            imgTag = item.SeriesPrimaryImageTag;
            itemId = item.SeriesId.IdText();
        }
        else if (!string.IsNullOrEmpty(extras?.PrimaryImageTag))
        {
            imgType = "Primary";
            imgTag = extras.PrimaryImageTag;
            itemId = extras.PrimaryImageItemId;
            UsePrimaryRatio();
            if (preferThumb && !options.ShowTitle.IsFalse)
            {
                forceName = true;
            }
        }
        else if (!string.IsNullOrEmpty(item.ParentPrimaryImageTag) && !skipEpisodeParentPoster)
        {
            imgType = "Primary";
            imgTag = item.ParentPrimaryImageTag;
            itemId = item.ParentPrimaryImageItemId.IdText();
        }
        else if (item.AlbumId is not null && !string.IsNullOrEmpty(item.AlbumPrimaryImageTag))
        {
            imgType = "Primary";
            imgTag = item.AlbumPrimaryImageTag;
            itemId = item.AlbumId.IdText();
            UsePrimaryRatio();
        }
        else if (type == "Season" && thumbTag is not null)
        {
            imgType = "Thumb";
            imgTag = thumbTag;
        }
        else if (backdropTag is not null)
        {
            imgType = "Backdrop";
            imgTag = backdropTag;
        }
        else if (thumbTag is not null)
        {
            imgType = "Thumb";
            imgTag = thumbTag;
        }
        else if (!string.IsNullOrEmpty(item.SeriesThumbImageTag) && inheritThumb)
        {
            imgType = "Thumb";
            imgTag = item.SeriesThumbImageTag;
            itemId = item.SeriesId.IdText();
        }
        else if (item.ParentThumbItemId is not null && inheritThumb)
        {
            imgType = "Thumb";
            imgTag = item.ParentThumbImageTag;
            itemId = item.ParentThumbItemId.IdText();
        }
        else if (parentBackdropTag is not null && inheritThumb)
        {
            imgType = "Backdrop";
            imgTag = parentBackdropTag;
            itemId = item.ParentBackdropItemId.IdText();
        }

        if (string.IsNullOrEmpty(itemId))
        {
            itemId = item.IdText();
        }

        if (!string.IsNullOrEmpty(imgTag) && imgType is not null && !string.IsNullOrEmpty(itemId))
        {
            if (height is null or 0 && width is > 0 && uiAspect is { } aspect)
            {
                height = width.Value / aspect;
            }

            imgUrl = GetImageUrl(itemId, imgType, imgTag, width, height);
        }

        var blurhash = imgType is not null ? item.BlurHash(imgType, imgTag) : null;
        return new CardImage(imgUrl, blurhash, forceName, coverImage);
    }

    /// <summary>
    /// An item image URL scaled to fill a box of CSS pixels (multiplied by the device pixel ratio),
    /// quality 96, as the cards request it.
    /// </summary>
    public string GetImageUrl(string itemId, string imageType, string? tag, double? width = null, double? height = null)
    {
        var dpr = browser.Environment.DevicePixelRatio;
        return api.GetUrl(
            $"Items/{itemId}/Images/{imageType}",
            ("fillHeight", height is > 0 ? (int)Math.Ceiling(height.Value * dpr) : null),
            ("fillWidth", width is > 0 ? (int)Math.Ceiling(width.Value * dpr) : null),
            ("quality", 96),
            ("tag", tag));
    }

    /// <summary>
    /// An image URL sized in CSS pixels (logos in card footers, list images): the sizes are
    /// multiplied by the device pixel ratio and the default quality is added, as the API
    /// client's <c>getScaledImageUrl</c> does.
    /// </summary>
    public string GetScaledImageUrl(
        string itemId,
        string imageType,
        string? tag,
        int? height = null,
        int? width = null,
        int? maxWidth = null,
        int? fillWidth = null,
        int? fillHeight = null)
    {
        var dpr = browser.Environment.DevicePixelRatio;
        int? Scale(int? value) => value is { } size ? (int)Math.Round(size * dpr, MidpointRounding.AwayFromZero) : null;
        return api.GetUrl(
            $"Items/{itemId}/Images/{imageType}",
            ("height", Scale(height)),
            ("width", Scale(width)),
            ("maxWidth", Scale(maxWidth)),
            ("fillWidth", Scale(fillWidth)),
            ("fillHeight", Scale(fillHeight)),
            ("tag", tag),
            ("quality", string.Equals(imageType, "Backdrop", StringComparison.OrdinalIgnoreCase) ? 80 : 90));
    }

    /// <summary>
    /// The card width in CSS pixels used for image sizing when the caller gave none
    /// (<c>getImageWidth</c>): the viewport divided by the cards per row of the shape.
    /// </summary>
    public int GetImageWidth(string? shape)
    {
        var environment = browser.Environment;
        var screenWidth = browser.ViewportWidth;
        var screenHeight = environment.InnerHeight;

        // A window smaller than the screen may still grow: round down so images are reused.
        if (environment.ScreenWidth - screenWidth > 20)
        {
            screenWidth = screenWidth / 100 * 100;
        }

        var perRow = CardShape.GetPostersPerRow(shape, screenWidth, screenWidth > screenHeight * 1.3);
        return (int)Math.Round(screenWidth / perRow, MidpointRounding.AwayFromZero);
    }
}
