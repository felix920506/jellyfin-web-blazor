using System.Globalization;
using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>An extra button at the end of a list row; a click raises the list's custom action with <see cref="Id"/>.</summary>
public sealed record ListRightButton(string Id, string Title, string Icon);

/// <summary>The options of <c>listview.js</c> (<c>getListViewHtml</c>), with the original defaults.</summary>
public sealed record ListViewOptions
{
    /// <summary>The <see cref="ItemAction"/> of a click on a row; "link" when null.</summary>
    public string? Action { get; init; }

    /// <summary>"large" for the big image style.</summary>
    public string? ImageSize { get; init; }
    public bool EnableOverview { get; init; }

    /// <summary>Media info at the side of the row (default) or as a line of the body.</summary>
    public bool EnableSideMediaInfo { get; init; } = true;

    /// <summary>Group the rows under headers (see <see cref="Index"/> and <see cref="SortBy"/>).</summary>
    public bool ShowIndex { get; init; }

    /// <summary>"disc" groups by disc number; otherwise the group comes from <see cref="SortBy"/>.</summary>
    public string? Index { get; init; }
    public string? SortBy { get; init; }
    public bool Border { get; init; }
    public bool Highlight { get; init; } = true;
    public bool DragHandle { get; init; }

    /// <summary>False hides the image.</summary>
    public bool Image { get; init; } = true;

    /// <summary>"channel" shows the channel's image instead of the item's.</summary>
    public string? ImageSource { get; init; }
    public bool ImagePlayButton { get; init; }
    public bool DisableIndicators { get; init; }
    public bool ShowIndexNumberLeft { get; init; }
    public bool ShowProgramDateTime { get; init; }
    public bool ShowProgramTime { get; init; }
    public bool ShowChannel { get; init; }
    public bool ShowParentTitle { get; init; }
    public bool ParentTitleWithTitle { get; init; }
    public bool? IncludeParentInfoInTitle { get; init; }
    public bool ShowIndexNumber { get; init; }

    /// <summary>Null: album artist of albums only; true: also the artists of songs; false: none.</summary>
    public bool? Artist { get; init; }
    public bool MediaInfo { get; init; } = true;
    public bool RecordButton { get; init; }
    public bool AddToListButton { get; init; }
    public bool InfoButton { get; init; }
    public IReadOnlyList<ListRightButton>? RightButtons { get; init; }
    public bool EnableUserDataButtons { get; init; } = true;
    public bool EnablePlayedButton { get; init; } = true;
    public bool EnableRatingButton { get; init; } = true;
    public bool MoreButton { get; init; } = true;
    public string? CollectionId { get; init; }
    public string? PlaylistId { get; init; }
    public string? DefaultCardImageIcon { get; init; }
}

/// <summary>Everything <c>ListView.razor</c> needs to draw one row.</summary>
public sealed class ListItemModel
{
    public required BaseItemDto Item { get; init; }

    /// <summary>The group header that starts before this row; null when the row continues a group.</summary>
    public string? GroupTitle { get; init; }
    public required string CssClass { get; init; }
    public required string Action { get; init; }
    public required string ImageAction { get; init; }
    public required IReadOnlyDictionary<string, object> Attributes { get; init; }

    /// <summary>Null when the image is hidden.</summary>
    public string? ImageClass { get; init; }
    public string? ImageUrl { get; init; }
    public string? DefaultIcon { get; init; }
    public string? DefaultText { get; init; }
    public int? MediaSourceCount { get; init; }
    public string PlayedIndicatorHtml { get; init; } = string.Empty;
    public string ProgressHtml { get; init; } = string.Empty;
    public required string BodyClass { get; init; }

    /// <summary>The text lines; empty ones are skipped when drawing but keep their position.</summary>
    public required IReadOnlyList<string> TextLines { get; init; }
    public string? BodyMediaInfoHtml { get; init; }
    public string? SideMediaInfoHtml { get; init; }
    public string TimerIndicatorHtml { get; init; } = string.Empty;
    public bool PlayedButton { get; init; }
    public bool RatingButton { get; init; }
    public string? Overview { get; init; }
}

/// <summary>Port of <c>components/listview/listview.js</c>: turns items into list row models.</summary>
[AppService]
public sealed class ListViewBuilder(Globalize globalize, CardImages images, CardBuilder cards, MediaInfo mediaInfo, DateTimeText dateTime)
{
    private static readonly object ProgramDateTimeOptions = new { weekday = "long", month = "short", day = "numeric", hour = "numeric", minute = "2-digit" };

    private static readonly MediaInfoOptions BodyMediaInfo = new() { EpisodeTitle = false, OriginalAirDate = false, Subtitles = false };

    private static readonly MediaInfoOptions SideMediaInfo = new()
    {
        Year = false,
        Container = false,
        EpisodeTitle = false,
        CriticRating = false,
        OfficialRating = false,
        EndsAt = false
    };

    /// <summary>Builds the rows of a list (<c>getListViewHtml</c>).</summary>
    public IReadOnlyList<ListItemModel> Build(IReadOnlyList<BaseItemDto> items, ListViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var rows = new List<ListItemModel>(items.Count);
        var groupTitle = string.Empty;
        for (var i = 0; i < items.Count; i++)
        {
            string? header = null;
            if (options.ShowIndex)
            {
                var title = GetIndex(items[i], options);
                if (title != groupTitle)
                {
                    header = title;
                    groupTitle = title;
                }
            }

            rows.Add(BuildRow(items[i], options, header));
        }

        return rows;
    }

    /// <summary>The group a row belongs to (<c>getIndex</c>): disc number, first letter or rating.</summary>
    public string GetIndex(BaseItemDto item, ListViewOptions options)
    {
        if (options.Index == "disc")
        {
            return item.ParentIndexNumber is { } disc ? globalize.Translate("ValueDiscNumber", disc) : string.Empty;
        }

        var sortBy = (options.SortBy ?? string.Empty).ToLowerInvariant();
        if (sortBy.StartsWith("sortname", StringComparison.Ordinal))
        {
            if (item.TypeName() == "Episode")
            {
                return string.Empty;
            }

            return FirstLetter(FirstNonEmpty(item.SortName, item.Name, "?"));
        }

        if (sortBy.StartsWith("officialrating", StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(item.OfficialRating) ? globalize.Translate("Unrated") : item.OfficialRating;
        }

        if (sortBy.StartsWith("communityrating", StringComparison.Ordinal))
        {
            return item.CommunityRating is { } rating
                ? Math.Floor(rating).ToString(CultureInfo.InvariantCulture)
                : globalize.Translate("Unrated");
        }

        if (sortBy.StartsWith("criticrating", StringComparison.Ordinal))
        {
            return item.CriticRating is { } rating
                ? Math.Floor(rating).ToString(CultureInfo.InvariantCulture)
                : globalize.Translate("Unrated");
        }

        if (sortBy.StartsWith("albumartist", StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(item.AlbumArtist) ? string.Empty : FirstLetter(item.AlbumArtist);
        }

        return string.Empty;
    }

    /// <summary>The text lines of a row, in order; lines that turn out empty stay in the list.</summary>
    public IReadOnlyList<string> GetTextLines(BaseItemDto item, ListViewOptions options)
    {
        var lines = new List<string>();
        var type = item.TypeName();

        if (options.ShowProgramDateTime && item.StartDate is { } start)
        {
            lines.Add(globalize.FormatDate(start, ProgramDateTimeOptions));
        }

        if (options.ShowProgramTime && item.StartDate is { } startTime)
        {
            lines.Add(dateTime.GetDisplayTime(startTime));
        }

        if (options.ShowChannel && !string.IsNullOrEmpty(item.ChannelName))
        {
            lines.Add(item.ChannelName);
        }

        string? parentTitle = null;
        if (options.ShowParentTitle)
        {
            if (type == "Episode")
            {
                parentTitle = item.SeriesName;
            }
            else if (item.IsSeries == true || (!string.IsNullOrEmpty(item.EpisodeTitle) && !string.IsNullOrEmpty(item.Name)))
            {
                parentTitle = item.Name;
            }
        }

        var displayName = ItemHelper.GetDisplayName(item, globalize, includeParentInfo: options.IncludeParentInfoInTitle);
        if (options.ShowIndexNumber && item.IndexNumber is { } indexNumber)
        {
            displayName = indexNumber.ToString(CultureInfo.InvariantCulture) + ". " + displayName;
        }

        if (options.ShowParentTitle && options.ParentTitleWithTitle)
        {
            if (!string.IsNullOrEmpty(displayName))
            {
                parentTitle = (string.IsNullOrEmpty(parentTitle) ? string.Empty : parentTitle + " - ") + displayName;
            }

            lines.Add(parentTitle ?? string.Empty);
        }
        else if (options.ShowParentTitle)
        {
            lines.Add(parentTitle ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(displayName) && !options.ParentTitleWithTitle)
        {
            lines.Add(displayName);
        }

        if (item.IsFolder == true)
        {
            if (options.Artist != false && !string.IsNullOrEmpty(item.AlbumArtist) && type == "MusicAlbum")
            {
                lines.Add(item.AlbumArtist);
            }
        }
        else if (options.Artist == true && item.ArtistItems is { } artists && type != "MusicAlbum")
        {
            lines.Add(string.Join(", ", artists.Select(artist => artist.Name)));
        }

        if (type == "TvChannel" && item.CurrentProgram is { } program)
        {
            lines.Add(ItemHelper.GetDisplayName(program, globalize));
        }

        return lines;
    }

    /// <summary>The image of a row: the item's own primary image, else its album's, series' or parent's.</summary>
    public string? GetImageUrl(BaseItemDto item, int size, bool channel = false)
    {
        if (channel)
        {
            return item.ChannelId is null
                ? null
                : images.GetScaledImageUrl(item.ChannelId.IdText(), "Primary", item.ChannelPrimaryImageTag, fillWidth: size, fillHeight: size);
        }

        string? tag = null;
        string? itemId = null;
        if (item.ImageTag("Primary") is { Length: > 0 } primary)
        {
            tag = primary;
            itemId = item.IdText();
        }
        else if (item.AlbumId is not null && !string.IsNullOrEmpty(item.AlbumPrimaryImageTag))
        {
            tag = item.AlbumPrimaryImageTag;
            itemId = item.AlbumId.IdText();
        }
        else if (item.SeriesId is not null && !string.IsNullOrEmpty(item.SeriesPrimaryImageTag))
        {
            tag = item.SeriesPrimaryImageTag;
            itemId = item.SeriesId.IdText();
        }
        else if (!string.IsNullOrEmpty(item.ParentPrimaryImageTag))
        {
            tag = item.ParentPrimaryImageTag;
            itemId = item.ParentPrimaryImageItemId.IdText();
        }

        return string.IsNullOrEmpty(itemId) ? null : images.GetScaledImageUrl(itemId, "Primary", tag, fillWidth: size, fillHeight: size);
    }

    private static string FirstLetter(string text)
    {
        var letter = text[..1].ToUpperInvariant();
        return letter[0] is < 'A' or > 'Z' ? "#" : letter;
    }

    private static string FirstNonEmpty(params string?[] values) => values.First(value => !string.IsNullOrEmpty(value))!;

    private ListItemModel BuildRow(BaseItemDto item, ListViewOptions options, string? groupTitle)
    {
        var type = item.TypeName();
        var action = string.IsNullOrEmpty(options.Action) ? ItemAction.Link : options.Action;
        var isLarge = options.ImageSize == "large";

        var cssClass = "listItem";
        if (options.Border || options.Highlight)
        {
            cssClass += " listItem-border";
        }

        if (isLarge)
        {
            cssClass += " listItem-largeImage";
        }

        if (options.EnableOverview)
        {
            cssClass += " listItem-withContentWrapper";
        }

        var attributes = new Dictionary<string, object>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(item.PlaylistItemId))
        {
            attributes["data-playlistitemid"] = item.PlaylistItemId;
        }

        attributes["data-action"] = action;
        foreach (var (name, value) in ItemActions.GetShortcutAttributes(item))
        {
            attributes[name] = value;
        }

        if (item.UserData?.PlaybackPositionTicks is long ticks && ticks > 0)
        {
            attributes["data-positionticks"] = ticks.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrEmpty(options.CollectionId))
        {
            attributes["data-collectionid"] = options.CollectionId;
        }

        if (!string.IsNullOrEmpty(options.PlaylistId))
        {
            attributes["data-playlistid"] = options.PlaylistId;
        }

        string? imageClass = null;
        string? imageUrl = null;
        string? defaultIcon = null;
        string? defaultText = null;
        if (options.Image)
        {
            var channel = options.ImageSource == "channel";
            imageUrl = GetImageUrl(item, isLarge ? 500 : 80, channel);
            imageClass = isLarge ? "listItemImage listItemImage-large" : "listItemImage";
            if (channel)
            {
                imageClass += " listItemImage-channel";
            }

            imageClass += " itemAction";
            if (imageUrl is null)
            {
                imageClass += $" cardImageContainer defaultCardBackground defaultCardBackground{CardBuilder.GetDefaultColorIndex(item.Name)}";
                (defaultIcon, defaultText) = cards.GetDefaultText(item, options.DefaultCardImageIcon);
            }
        }

        var overview = options.EnableOverview && !string.IsNullOrWhiteSpace(item.Overview) ? item.Overview : null;
        var userDataButtons = options.EnableUserDataButtons;

        return new ListItemModel
        {
            Item = item,
            GroupTitle = groupTitle,
            CssClass = cssClass,
            Action = action,
            ImageAction = options.ImagePlayButton ? ItemAction.Link : action,
            Attributes = attributes,
            ImageClass = imageClass,
            ImageUrl = imageUrl,
            DefaultIcon = defaultIcon,
            DefaultText = defaultText,
            MediaSourceCount = options.Image && item.MediaSourceCount > 1 && !options.DisableIndicators ? item.MediaSourceCount : null,
            PlayedIndicatorHtml = options.Image ? Indicators.GetPlayedIndicatorHtml(item) : string.Empty,
            ProgressHtml = options.Image ? Indicators.GetProgressBarHtml(item, dateTime.Now, "listItemProgressBar") : string.Empty,
            BodyClass = options.Image ? "listItemBody itemAction" : "listItemBody itemAction listItemBody-noleftpadding",
            TextLines = GetTextLines(item, options),
            BodyMediaInfoHtml = options.MediaInfo && !options.EnableSideMediaInfo ? mediaInfo.GetPrimaryHtml(item, BodyMediaInfo) : null,
            SideMediaInfoHtml = options.MediaInfo && options.EnableSideMediaInfo ? mediaInfo.GetPrimaryHtml(item, SideMediaInfo) : null,
            TimerIndicatorHtml = !options.RecordButton && type is "Timer" or "Program"
                ? Indicators.GetTimerIndicator(item).Replace("indicatorIcon", "indicatorIcon listItemAside", StringComparison.Ordinal)
                : string.Empty,
            PlayedButton = userDataButtons && options.EnablePlayedButton && ItemHelper.CanMarkPlayed(item),
            RatingButton = userDataButtons && options.EnableRatingButton && ItemHelper.CanRate(item),
            Overview = overview
        };
    }
}
