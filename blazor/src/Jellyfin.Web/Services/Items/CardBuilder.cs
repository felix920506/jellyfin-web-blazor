using System.Globalization;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>A piece of a card text line: plain text, or a link to <see cref="Link"/> (a "text action button").</summary>
public sealed record CardTextPart(string Text, BaseItemDto? Link = null, string? Href = null, string? Title = null);

/// <summary>One rendered text line of a card footer; without parts it is a blank spacer line.</summary>
public sealed record CardTextLine(string CssClass, IReadOnlyList<CardTextPart> Parts);

/// <summary>A card footer: inside the image (<c>innerCardFooter</c>) or below it (<c>cardFooter</c>).</summary>
public sealed class CardFooter
{
    /// <summary>The class of the wrapping element; null when the lines are emitted without a wrapper.</summary>
    public string? CssClass { get; init; }
    public string? LogoUrl { get; init; }

    /// <summary>The "more" button of the card layout on mobile.</summary>
    public bool MoreButton { get; init; }
    public IReadOnlyList<CardTextLine> Lines { get; init; } = [];
    public string ProgressHtml { get; init; } = string.Empty;

    /// <summary>The number of versions badge (inner footer only).</summary>
    public int? MediaSourceCount { get; init; }

    public bool IsEmpty => LogoUrl is null && !MoreButton && Lines.Count == 0 && ProgressHtml.Length == 0 && MediaSourceCount is null;
}

/// <summary>Everything <c>Card.razor</c> needs to draw one card (the result of <c>buildCard</c>).</summary>
public sealed class CardModel
{
    public required BaseItemDto Item { get; init; }
    public required int Index { get; init; }

    /// <summary>The <see cref="ItemAction"/> of a click on the card.</summary>
    public required string Action { get; init; }
    public required string Shape { get; init; }
    public required string CssClass { get; init; }
    public required string BoxClass { get; init; }

    /// <summary>Classes of the image container link (cardImageContainer …, without "lazy").</summary>
    public required string ImageContainerClass { get; init; }
    public required string RouteUrl { get; init; }
    public string? ImageUrl { get; init; }
    public string? BlurHash { get; init; }

    /// <summary>The icon behind a loading image (cardPadder).</summary>
    public string? PadderIcon { get; init; }
    public string? PadderText { get; init; }

    /// <summary>What stands in for a missing image: an icon or the item's name.</summary>
    public string? DefaultIcon { get; init; }
    public string? DefaultText { get; init; }
    public string IndicatorsHtml { get; init; } = string.Empty;
    public CardFooter? InnerFooter { get; init; }
    public CardFooter? OuterFooter { get; init; }

    // Mobile overlay buttons.
    public bool CenterPlayButton { get; init; }
    public bool OverlayPlayButton { get; init; }
    public bool OverlayMoreButton { get; init; }

    // Desktop hover menu.
    public bool HoverMenu { get; init; }
    public bool HoverPlayButton { get; init; }
    public bool HoverPlayedButton { get; init; }
    public bool HoverRatingButton { get; init; }

    /// <summary>The card element's data-* attributes, as the original sets them.</summary>
    public required IReadOnlyDictionary<string, object> Attributes { get; init; }
}

/// <summary>Cards under one heading (<c>indexBy</c>); <see cref="Title"/> is null without grouping.</summary>
public sealed record CardSection(string? Title, IReadOnlyList<IReadOnlyList<CardModel>> Columns);

/// <summary>The cards of a list with the options resolved for that list (shape, width, thumb preference).</summary>
public sealed class CardSet
{
    public required CardOptions Options { get; init; }
    public required IReadOnlyList<CardModel> Cards { get; init; }
    public required IReadOnlyList<CardSection> Sections { get; init; }

    /// <summary>True for the "autoVertical" shape: grouped cards wrap inside each section.</summary>
    public bool IsVertical { get; init; }
}

/// <summary>
/// Port of <c>components/cardbuilder/cardBuilder.js</c>: turns items and <see cref="CardOptions"/>
/// into card models. <c>Cards.razor</c> / <c>Card.razor</c> render them with the original markup.
/// </summary>
[AppService]
public sealed class CardBuilder(
    Globalize globalize,
    CardImages images,
    AppRouter router,
    AppHost host,
    IPlaybackManager playback,
    DateTimeText dateTime)
{
    private static readonly object IndexDateOptions = new { weekday = "long", month = "long", day = "numeric" };
    private static readonly object AirDateOptions = new { weekday = "short", month = "short", day = "numeric" };
    private static readonly object PremiereDateOptions = new { year = "numeric", month = "long", day = "numeric" };
    private static readonly IReadOnlyList<CardTextPart> NoParts = [];

    /// <summary>Builds the cards of a list (<c>buildCardsHtml</c>).</summary>
    public CardSet Build(IReadOnlyList<BaseItemDto> items, CardOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var isVertical = options.Shape == CardShape.AutoVertical;
        var resolved = ResolveOptions(items, options);

        var cards = new List<CardModel>(items.Count);
        var sections = new List<CardSection>();
        List<IReadOnlyList<CardModel>> columns = [];
        List<CardModel> column = [];
        string? currentTitle = null;
        var hasIndex = !string.IsNullOrEmpty(resolved.IndexBy);
        var first = true;

        void CloseColumn()
        {
            if (column.Count > 0)
            {
                columns.Add(column);
                column = [];
            }
        }

        void CloseSection()
        {
            CloseColumn();
            if (columns.Count > 0 || !first)
            {
                sections.Add(new CardSection(hasIndex ? currentTitle ?? string.Empty : null, columns));
            }

            columns = [];
        }

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (hasIndex)
            {
                var title = GetIndexValue(item, resolved.IndexBy!);
                if (first || title != currentTitle)
                {
                    if (!first)
                    {
                        CloseSection();
                    }

                    currentTitle = title;
                }
            }

            first = false;
            var card = BuildCard(i, item, resolved);
            cards.Add(card);
            column.Add(card);
            if (resolved.Rows is not > 0 || column.Count >= resolved.Rows)
            {
                CloseColumn();
            }
        }

        if (!first)
        {
            CloseSection();
        }

        return new CardSet { Options = resolved, Cards = cards, Sections = sections, IsVertical = isVertical };
    }

    /// <summary>
    /// Resolves the per-list values the original computes in <c>setCardData</c>: the auto shape,
    /// <c>preferThumb: 'auto'</c>, <c>coverImage</c> for banners and the image width.
    /// </summary>
    public CardOptions ResolveOptions(IReadOnlyList<BaseItemDto> items, CardOptions options)
    {
        var shape = string.IsNullOrEmpty(options.Shape) ? CardShape.Auto : options.Shape;
        var coverImage = options.CoverImage;

        if (shape is CardShape.Auto or CardShape.AutoHome or CardShape.AutoOverflow or CardShape.AutoVertical)
        {
            var overflow = shape == CardShape.AutoOverflow;
            var ratio = CardShape.GetPrimaryImageAspectRatio(items);
            string? chosen = null;
            if (ratio is > 0)
            {
                if (ratio >= 3)
                {
                    chosen = CardShape.Banner;
                    coverImage = true;
                }
                else if (ratio >= 1.33)
                {
                    chosen = CardShape.GetBackdropShape(overflow);
                }
                else if (ratio > 0.8)
                {
                    chosen = CardShape.GetSquareShape(overflow);
                }
                else
                {
                    chosen = CardShape.GetPortraitShape(overflow);
                }
            }

            shape = chosen ?? (string.IsNullOrEmpty(options.DefaultShape) ? CardShape.GetSquareShape(overflow) : options.DefaultShape);
        }

        var preferThumb = options.PreferThumb;
        if (preferThumb.IsAuto)
        {
            preferThumb = shape is CardShape.Backdrop or CardShape.OverflowBackdrop;
        }

        return options with
        {
            Shape = shape,
            CoverImage = coverImage,
            PreferThumb = preferThumb,
            Width = options.Width is > 0 ? options.Width : images.GetImageWidth(shape)
        };
    }

    /// <summary>Builds one card. <paramref name="options"/> must come from <see cref="ResolveOptions"/>.</summary>
    public CardModel BuildCard(int index, BaseItemDto item, CardOptions options)
    {
        var type = item.TypeName();
        var action = ItemAction.Resolve(options.Action, item.IsFolder == true, item.MediaTypeName() == "Photo");

        var shape = options.Shape ?? CardShape.Square;
        if (shape == CardShape.Mixed)
        {
            shape = CardShape.ResolveMixedShape(item.PrimaryImageAspectRatio);
        }

        var image = images.GetCardImage(item, options, shape);
        var imgUrl = image.Url;
        var hasCoverImage = options.CoverImage || image.CoverImage;

        var imageClass = "cardImageContainer";
        if (hasCoverImage)
        {
            imageClass += " coveredImage";
            if (type == "TvChannel")
            {
                imageClass += " coveredImage-contain";
            }
        }

        if (imgUrl is null)
        {
            imageClass += $" defaultCardBackground defaultCardBackground{GetDefaultColorIndex(item.Name)}";
        }

        var progressHtml = Indicators.GetProgressBarHtml(item, dateTime.Now);

        string? logoUrl = null;
        if (options.ShowChannelLogo && !string.IsNullOrEmpty(item.ChannelPrimaryImageTag) && item.ChannelId is not null)
        {
            logoUrl = images.GetScaledImageUrl(item.ChannelId.IdText(), "Primary", item.ChannelPrimaryImageTag, height: 40);
        }
        else if (options.ShowLogo && !string.IsNullOrEmpty(item.ParentLogoImageTag) && item.ParentLogoItemId is not null)
        {
            logoUrl = images.GetScaledImageUrl(item.ParentLogoItemId.IdText(), "Logo", item.ParentLogoImageTag, height: 40);
        }

        CardFooter? innerFooter = null;
        var footerOverlayed = false;
        int? mediaSourceCount = item.MediaSourceCount > 1 && !options.DisableIndicators ? item.MediaSourceCount : null;

        if (options.OverlayText)
        {
            logoUrl = null;
            innerFooter = BuildFooter(
                item, options, progressHtml.Length > 0 ? "innerCardFooter fullInnerCardFooter" : "innerCardFooter", progressHtml,
                image.ForceName, isOuterFooter: false, imgUrl, logoUrl: null, mediaSourceCount);
            footerOverlayed = true;
        }
        else if (progressHtml.Length > 0)
        {
            innerFooter = new CardFooter
            {
                CssClass = "innerCardFooter fullInnerCardFooter innerCardFooterClear",
                ProgressHtml = progressHtml,
                MediaSourceCount = mediaSourceCount
            };
            progressHtml = string.Empty;
        }
        else if (mediaSourceCount is not null)
        {
            innerFooter = new CardFooter { MediaSourceCount = mediaSourceCount };
        }

        CardFooter? outerFooter = null;
        if (!options.OverlayText && !footerOverlayed)
        {
            var footerClass = options.CardLayout ? "cardFooter" : "cardFooter cardFooter-transparent";
            if (logoUrl is not null)
            {
                footerClass += " cardFooter-withlogo";
            }

            if (!options.CardLayout)
            {
                logoUrl = null;
            }

            outerFooter = BuildFooter(item, options, footerClass, progressHtml, image.ForceName, isOuterFooter: true, imgUrl, logoUrl, null);
        }

        if (innerFooter is { IsEmpty: true })
        {
            innerFooter = null;
        }

        if (outerFooter is { IsEmpty: true })
        {
            outerFooter = null;
        }

        var boxClass = "cardBox";
        if (options.CardLayout)
        {
            boxClass += " visualCardBox";
        }

        if (outerFooter is not null && !options.CardLayout)
        {
            boxClass += " cardBox-bottompadded";
        }

        var centerPlay = false;
        var overlayPlay = false;
        var overlayMore = false;
        if (host.IsMobileLayout)
        {
            var overlayPlayButton = options.OverlayPlayButton;
            if (overlayPlayButton is null && !options.OverlayMoreButton && !options.OverlayInfoButton && !options.CardLayout)
            {
                overlayPlayButton = item.MediaTypeName() == "Video";
            }

            centerPlay = options.CenterPlayButton;
            overlayPlay = overlayPlayButton == true
                          && item.IsPlaceHolder != true
                          && (item.LocationType != BaseItemDto_LocationType.Virtual || item.MediaTypeName() is null || type == "Program")
                          && type != "Person";
            overlayMore = options.OverlayMoreButton;
        }

        string? padderIcon = null;
        string? padderText = null;
        if (imgUrl is not null && type != "TvChannel")
        {
            (padderIcon, padderText) = GetDefaultText(item, options.DefaultCardImageIcon ?? "folder");
        }

        var indicatorsHtml = string.Empty;
        if (!options.DisableIndicators)
        {
            if (options.MissingIndicator != false)
            {
                indicatorsHtml += Indicators.GetMissingIndicator(item, dateTime.Now);
            }

            indicatorsHtml += Indicators.GetSyncIndicator(item);
            indicatorsHtml += Indicators.GetTimerIndicator(item);
            indicatorsHtml += Indicators.GetTypeIndicator(item);
            indicatorsHtml += options.ShowGroupCount
                ? Indicators.GetChildCountIndicatorHtml(item, 1)
                : Indicators.GetPlayedIndicatorHtml(item);

            if (type == "CollectionFolder" || item.CollectionTypeName() is not null)
            {
                // PORT-TODO(items): library scan progress (emby-itemrefreshindicator, RefreshProgress
                // websocket messages) is not ported; the hidden placeholder keeps the markup.
                indicatorsHtml += "<div is=\"emby-itemrefreshindicator\" class=\"hide\" data-progress=\"0\"></div>";
            }
        }

        string? defaultIcon = null;
        string? defaultText = null;
        if (imgUrl is null)
        {
            (defaultIcon, defaultText) = GetDefaultText(item, options.DefaultCardImageIcon);
        }

        var cssClass = "card " + shape + "Card";
        if (!string.IsNullOrEmpty(options.CardCssClass))
        {
            cssClass += " " + options.CardCssClass;
        }

        if (!string.IsNullOrEmpty(options.CardClass))
        {
            cssClass += " " + options.CardClass;
        }

        var desktop = host.Layout == LayoutMode.Desktop;
        if (desktop)
        {
            cssClass += " card-hoverable";
        }

        if (options.ShowChildCountIndicator && item.ChildCount is > 0)
        {
            cssClass += " groupedCard";
        }

        if (type is not ("MusicAlbum" or "MusicArtist" or "Audio"))
        {
            cssClass += " card-withuserdata";
        }

        var hoverMenu = desktop && !options.DisableHoverMenu;

        return new CardModel
        {
            Item = item,
            Index = index,
            Action = action,
            Shape = shape,
            CssClass = cssClass,
            BoxClass = boxClass,
            ImageContainerClass = imageClass,
            RouteUrl = router.GetRouteUrl(item),
            ImageUrl = imgUrl,
            BlurHash = imgUrl is null ? null : image.BlurHash,
            PadderIcon = padderIcon,
            PadderText = padderText,
            DefaultIcon = defaultIcon,
            DefaultText = defaultText,
            IndicatorsHtml = indicatorsHtml,
            InnerFooter = innerFooter,
            OuterFooter = outerFooter,
            CenterPlayButton = centerPlay,
            OverlayPlayButton = overlayPlay,
            OverlayMoreButton = overlayMore,
            HoverMenu = hoverMenu,
            HoverPlayButton = hoverMenu && playback.CanPlay(item),
            HoverPlayedButton = hoverMenu && ItemHelper.CanMarkPlayed(item),
            HoverRatingButton = hoverMenu && ItemHelper.CanRate(item),
            Attributes = GetCardAttributes(index, item, options)
        };
    }

    /// <summary>The data-* attributes the original puts on a card element.</summary>
    public static IReadOnlyDictionary<string, object> GetCardAttributes(int index, BaseItemDto item, CardOptions options)
    {
        var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["data-index"] = index.ToString(CultureInfo.InvariantCulture)
        };

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                attributes[name] = value;
            }
        }

        Add("data-timerid", item.TimerId);
        Add("data-seriestimerid", item.SeriesTimerId);
        foreach (var (name, value) in ItemActions.GetShortcutAttributes(item))
        {
            attributes[name] = value;
        }

        Add("data-path", item.Path);
        if (item.UserData?.PlaybackPositionTicks is > 0 and var ticks)
        {
            attributes["data-positionticks"] = ticks.ToString(CultureInfo.InvariantCulture);
        }

        Add("data-collectionid", options.CollectionId);
        Add("data-playlistid", options.PlaylistId);
        Add("data-context", options.Context);
        Add("data-parentid", options.ParentId);
        Add("data-startdate", item.StartDate?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));
        Add("data-enddate", item.EndDate?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ", CultureInfo.InvariantCulture));

        var nameWithPrefix = item.SortName ?? item.Name ?? string.Empty;
        if (nameWithPrefix.Length == 0)
        {
            nameWithPrefix = item.Name ?? string.Empty;
        }

        attributes["data-prefix"] = nameWithPrefix[..Math.Min(3, nameWithPrefix.Length)].ToUpperInvariant();
        return attributes;
    }

    /// <summary>
    /// One of the five default card backgrounds, chosen from the name so that an item keeps its
    /// colour (<c>getDefaultColorIndex</c>). Nameless items get 1 (the original picks at random).
    /// </summary>
    public static int GetDefaultColorIndex(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 1;
        }

        var character = ((int)name[name.Length / 2]).ToString(CultureInfo.InvariantCulture);
        var sum = character.Sum(digit => digit - '0');
        return (sum % 10 % 5) + 1;
    }

    /// <summary>"8:00 PM – 9:00 PM", optionally with the day (<c>getAirTimeText</c>).</summary>
    public string GetAirTimeText(BaseItemDto item, bool showAirDateTime, bool showAirEndTime)
    {
        if (item.StartDate is not { } start)
        {
            return string.Empty;
        }

        var text = string.Empty;
        if (showAirDateTime)
        {
            text += dateTime.ToLocaleDateString(start, AirDateOptions) + " ";
        }

        text += dateTime.GetDisplayTime(start);
        if (item.EndDate is { } end && showAirEndTime)
        {
            text += " - " + dateTime.GetDisplayTime(end);
        }

        return text;
    }

    /// <summary>"3 Movies, 2 Series" for genres, studios, playlists… (<c>getItemCountsHtml</c>).</summary>
    public string GetItemCounts(BaseItemDto item, CardOptions options)
    {
        var counts = new List<string>();
        var type = item.TypeName();

        void Add(int? count, string oneKey, string manyKey)
        {
            if (count is > 0)
            {
                counts.Add(count == 1 ? globalize.Translate(oneKey) : globalize.Translate(manyKey, count));
            }
        }

        if (type == "Playlist")
        {
            var minutes = item.RunTimeTicks is > 0 and var ticks
                ? Math.Max((long)Math.Round(ticks / 600000000d, MidpointRounding.AwayFromZero), 1)
                : 0;
            counts.Add(globalize.Translate("ValueMinutes", minutes));
        }
        else if (type is "Genre" or "Studio")
        {
            Add(item.MovieCount, "ValueOneMovie", "ValueMovieCount");
            Add(item.SeriesCount, "ValueOneSeries", "ValueSeriesCount");
            Add(item.EpisodeCount, "ValueOneEpisode", "ValueEpisodeCount");
        }
        else if (type == "MusicGenre" || options.Context == "MusicArtist")
        {
            Add(item.AlbumCount, "ValueOneAlbum", "ValueAlbumCount");
            Add(item.SongCount, "ValueOneSong", "ValueSongCount");
            Add(item.MusicVideoCount, "ValueOneMusicVideo", "ValueMusicVideoCount");
        }
        else if (type == "Series")
        {
            Add(item.RecursiveItemCount, "ValueOneEpisode", "ValueEpisodeCount");
        }

        return string.Join(", ", counts);
    }

    private string GetIndexValue(BaseItemDto item, string indexBy)
    {
        switch (indexBy)
        {
            case "PremiereDate":
                return item.PremiereDate is { } premiere ? dateTime.ToLocaleDateString(premiere, IndexDateOptions) : string.Empty;
            case "ProductionYear":
                return item.ProductionYear is { } year ? dateTime.FormatPlain(year) : string.Empty;
            case "CommunityRating":
                if (item.CommunityRating is not { } rating || rating == 0)
                {
                    return string.Empty;
                }

                var floor = Math.Floor(rating);
                var value = floor + (rating - floor >= 0.5 ? 0.5 : 0);
                return value.ToString(CultureInfo.InvariantCulture) + "+";
            default:
                return string.Empty;
        }
    }

    public (string? Icon, string? Text) GetDefaultText(BaseItemDto item, string? defaultIcon)
    {
        var type = item.TypeName();
        var icon = type == "CollectionFolder" || item.CollectionTypeName() is not null
            ? ItemIcons.ForLibrary(item.CollectionTypeName())
            : ItemIcons.ForItemType(type, defaultIcon);
        if (!string.IsNullOrEmpty(icon))
        {
            return (icon, null);
        }

        return (null, item.UsesLiveTvNaming() ? item.Name ?? string.Empty : ItemHelper.GetDisplayName(item, globalize));
    }

    private CardFooter BuildFooter(
        BaseItemDto item,
        CardOptions options,
        string footerClass,
        string progressHtml,
        bool forceName,
        bool isOuterFooter,
        string? imgUrl,
        string? logoUrl,
        int? mediaSourceCount)
    {
        item = item.Extras()?.ProgramInfo ?? item;
        var extras = item.Extras();
        var type = item.TypeName();
        var overlayText = options.OverlayText;
        var mobile = host.IsMobileLayout;

        var showTitle = options.ShowTitle.IsAuto || options.ShowTitle.IsTruthy || type is "PhotoAlbum" or "Folder";
        var showOtherText = isOuterFooter ? !overlayText : overlayText;
        var moreButton = isOuterFooter && options.CardLayout && mobile && options.CardFooterAside != "none";
        var cssClass = options.CenterText ? "cardText cardTextCentered" : "cardText";

        // Each entry is one line; an empty entry is a line without text (it only reserves space).
        var lines = new List<IReadOnlyList<CardTextPart>>();
        void Push(string? text) => lines.Add(string.IsNullOrEmpty(text) ? NoParts : [new CardTextPart(text)]);

        var parentTitleUnderneath = type is "MusicAlbum" or "Audio" or "MusicVideo";
        var titleAdded = false;

        if (showOtherText && (options.ShowParentTitle || options.ShowParentTitleOrTitle) && !parentTitleUnderneath)
        {
            if (isOuterFooter && type is "Episode" or "Season" && !string.IsNullOrEmpty(item.SeriesName))
            {
                if (item.SeriesId is not null)
                {
                    lines.Add([TextAction(new BaseItemDto
                    {
                        Id = item.SeriesId,
                        ServerId = item.ServerId,
                        Name = item.SeriesName,
                        Type = BaseItemDto_Type.Series,
                        IsFolder = true
                    })]);
                }
                else
                {
                    Push(item.SeriesName);
                }
            }
            else if (item.UsesLiveTvNaming())
            {
                Push(item.Name);
                if (string.IsNullOrEmpty(item.EpisodeTitle) && item.IndexNumber is null or 0)
                {
                    titleAdded = true;
                }
            }
            else
            {
                var parentTitle = FirstNonEmpty(item.SeriesName, item.Album, item.AlbumArtist);
                if (parentTitle.Length > 0 || showTitle)
                {
                    Push(parentTitle);
                }
            }
        }

        var showMediaTitle = (showTitle && !titleAdded) || (options.ShowParentTitleOrTitle && lines.Count == 0);
        if (!showMediaTitle && !titleAdded && (showTitle || forceName))
        {
            showMediaTitle = true;
        }

        if (showMediaTitle)
        {
            var name = options.ShowTitle.IsAuto && item.IsFolder != true && item.MediaTypeName() == "Photo"
                ? string.Empty
                : ItemHelper.GetDisplayName(item, globalize, includeParentInfo: options.IncludeParentInfoInTitle);
            lines.Add(name.Length == 0 ? NoParts : [TextAction(item, name)]);
        }

        if (showOtherText)
        {
            if (options.ShowParentTitle && parentTitleUnderneath)
            {
                if (isOuterFooter && item.AlbumArtists is { Count: > 0 } artists)
                {
                    var parts = new List<CardTextPart>();
                    foreach (var artist in artists)
                    {
                        if (parts.Count > 0)
                        {
                            parts.Add(new CardTextPart(" / "));
                        }

                        parts.Add(TextAction(new BaseItemDto
                        {
                            Id = artist.Id,
                            ServerId = item.ServerId,
                            Name = artist.Name,
                            Type = BaseItemDto_Type.MusicArtist,
                            IsFolder = true
                        }));
                    }

                    lines.Add(parts);
                }
                else
                {
                    Push(item.UsesLiveTvNaming() ? item.Name : FirstNonEmpty(item.SeriesName, item.Album, item.AlbumArtist));
                }
            }

            if (item.ExtraType is { } extraType && extraType != BaseItemDto_ExtraType.Unknown)
            {
                Push(globalize.Translate(extraType.ToApiString()));
            }

            if (options.ShowItemCounts)
            {
                Push(GetItemCounts(item, options));
            }

            if (options.TextLines is { } textLines)
            {
                foreach (var line in textLines(item))
                {
                    Push(line);
                }
            }

            if (options.ShowSongCount)
            {
                Push(item.SongCount is > 0
                    ? item.SongCount == 1 ? globalize.Translate("ValueOneSong") : globalize.Translate("ValueSongCount", item.SongCount)
                    : string.Empty);
            }

            if (options.ShowPremiereDate)
            {
                Push(item.PremiereDate is { } premiere ? dateTime.ToLocaleDateString(premiere, PremiereDateOptions) : string.Empty);
            }

            if (options.ShowYear || options.ShowSeriesYear)
            {
                var year = item.ProductionYear is { } productionYear ? dateTime.FormatPlain(productionYear) : string.Empty;
                if (type == "Series")
                {
                    if (item.Status == "Continuing")
                    {
                        Push(globalize.Translate("SeriesYearToPresent", year));
                    }
                    else if (item.EndDate is { } endDate && item.ProductionYear is { } startYear)
                    {
                        var endYear = dateTime.LocalYear(endDate);
                        Push(year + (endYear == startYear ? string.Empty : " - " + dateTime.FormatPlain(endYear)));
                    }
                    else
                    {
                        Push(year);
                    }
                }
                else
                {
                    Push(year);
                }
            }

            if (options.ShowRuntime)
            {
                Push(item.RunTimeTicks is > 0 and var ticks ? DateTimeText.GetDisplayRunningTime(ticks) : string.Empty);
            }

            if (options.ShowAirTime)
            {
                Push(GetAirTimeText(item, options.ShowAirDateTime, options.ShowAirEndTime));
            }

            if (options.ShowChannelName)
            {
                if (item.ChannelId is not null)
                {
                    lines.Add([TextAction(
                        new BaseItemDto
                        {
                            Id = item.ChannelId,
                            ServerId = item.ServerId,
                            Name = item.ChannelName,
                            Type = BaseItemDto_Type.TvChannel,
                            MediaType = item.MediaType,
                            IsFolder = false
                        },
                        item.ChannelName ?? string.Empty)]);
                }
                else
                {
                    Push(string.IsNullOrEmpty(item.ChannelName) ? " " : item.ChannelName);
                }
            }

            if (options.ShowCurrentProgram && type == "TvChannel")
            {
                Push(item.CurrentProgram?.Name);
            }

            if (options.ShowCurrentProgramTime && type == "TvChannel")
            {
                Push(item.CurrentProgram is { } program ? GetAirTimeText(program, false, true) : string.Empty);
            }

            if (options.ShowSeriesTimerTime)
            {
                Push(extras?.RecordAnyTime == true
                    ? globalize.Translate("Anytime")
                    : item.StartDate is { } start ? dateTime.GetDisplayTime(start) : string.Empty);
            }

            if (options.ShowSeriesTimerChannel)
            {
                Push(extras?.RecordAnyChannel == true
                    ? globalize.Translate("AllChannels")
                    : string.IsNullOrEmpty(item.ChannelName) ? globalize.Translate("OneChannel") : item.ChannelName);
            }

            if (options.ShowPersonRoleOrType && !string.IsNullOrEmpty(extras?.PersonType))
            {
                if (extras.RoleList is { Count: > 1 } roles)
                {
                    var parts = new List<CardTextPart>();
                    foreach (var role in roles)
                    {
                        if (parts.Count > 0)
                        {
                            parts.Add(new CardTextPart(" / "));
                        }

                        parts.Add(PersonLabel(role.Type, role.Role));
                    }

                    lines.Add(parts);
                }
                else
                {
                    lines.Add([PersonLabel(extras.PersonType, extras.Role)]);
                }
            }
        }

        if ((showTitle || imgUrl is null) && forceName && overlayText && lines.Count == 1)
        {
            lines.Clear();
        }

        if (overlayText && showTitle)
        {
            lines.Clear();
            Push(item.Name);
        }

        var addRightMargin = isOuterFooter && options.CardLayout && !options.CenterText && options.CardFooterAside != "none" && mobile;
        var rendered = GetCardTextLines(lines, cssClass, forceLines: !overlayText, isOuterFooter, addRightMargin, options.Lines);

        var hasContent = logoUrl is not null || moreButton || rendered.Count > 0 || progressHtml.Length > 0;
        var wrapped = hasContent && (!isOuterFooter || logoUrl is not null || options.CardLayout);
        return new CardFooter
        {
            CssClass = wrapped ? footerClass : null,
            LogoUrl = logoUrl,
            MoreButton = moreButton,
            Lines = rendered,
            ProgressHtml = progressHtml,
            MediaSourceCount = mediaSourceCount
        };
    }

    private static List<CardTextLine> GetCardTextLines(
        List<IReadOnlyList<CardTextPart>> lines,
        string cssClass,
        bool forceLines,
        bool isOuterFooter,
        bool addRightMargin,
        int? maxLines)
    {
        var result = new List<CardTextLine>();
        var valid = 0;
        foreach (var parts in lines)
        {
            if (parts.Count == 0)
            {
                continue;
            }

            var currentClass = cssClass;
            if (isOuterFooter)
            {
                currentClass += valid > 0 ? " cardText-secondary" : " cardText-first";
            }

            if (addRightMargin)
            {
                currentClass += " cardText-rightmargin";
            }

            result.Add(new CardTextLine(currentClass, parts));
            valid++;
            if (maxLines is > 0 && valid >= maxLines)
            {
                break;
            }
        }

        if (forceLines)
        {
            var length = maxLines is > 0 ? maxLines.Value : lines.Count;
            while (valid < length)
            {
                result.Add(new CardTextLine(cssClass, NoParts));
                valid++;
            }
        }

        return result;
    }

    private CardTextPart TextAction(BaseItemDto item, string? text = null)
    {
        text ??= ItemHelper.GetDisplayName(item, globalize);
        return new CardTextPart(text, item, router.GetRouteUrl(item), text);
    }

    private CardTextPart PersonLabel(string? type, string? role)
    {
        if (type is "Actor" or "GuestStar" && !string.IsNullOrEmpty(role))
        {
            var roleText = globalize.Translate("PersonRole", role);
            return new CardTextPart(roleText, Title: roleText);
        }

        if (string.IsNullOrEmpty(role) || string.Equals(role, type, StringComparison.OrdinalIgnoreCase))
        {
            return new CardTextPart(globalize.Translate(type ?? string.Empty));
        }

        return new CardTextPart(globalize.Translate(role));
    }

    private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(value => !string.IsNullOrEmpty(value)) ?? string.Empty;
}
