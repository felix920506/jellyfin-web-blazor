using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// A card option that the original reads as <c>true</c>, <c>false</c>, <c>'auto'</c> or leaves
/// undefined (<c>showTitle</c>, <c>preferThumb</c>). Assign a bool or <see cref="Auto"/>.
/// </summary>
public readonly struct CardToggle : IEquatable<CardToggle>
{
    private readonly byte _state;

    private CardToggle(byte state) => _state = state;

    /// <summary>Not given (JavaScript <c>undefined</c>).</summary>
    public static CardToggle Unset => default;

    public static CardToggle Auto => new(3);

    public bool IsSet => _state != 0;

    public bool IsAuto => _state == 3;

    /// <summary>Explicitly <c>false</c> (the original's <c>=== false</c> checks).</summary>
    public bool IsFalse => _state == 1;

    /// <summary>JavaScript truthiness: true or 'auto'.</summary>
    public bool IsTruthy => _state >= 2;

    public static implicit operator CardToggle(bool value) => new(value ? (byte)2 : (byte)1);

    public static bool operator ==(CardToggle left, CardToggle right) => left.Equals(right);

    public static bool operator !=(CardToggle left, CardToggle right) => !left.Equals(right);

    public bool Equals(CardToggle other) => _state == other._state;

    public override bool Equals(object? obj) => obj is CardToggle other && Equals(other);

    public override int GetHashCode() => _state;

    public override string ToString() => _state switch { 1 => "false", 2 => "true", 3 => "auto", _ => "unset" };
}

/// <summary>The card shapes of <c>utils/shape.ts</c>.</summary>
public static class CardShape
{
    public const string Backdrop = "backdrop";
    public const string OverflowBackdrop = "overflowBackdrop";
    public const string SmallBackdrop = "smallBackdrop";
    public const string OverflowSmallBackdrop = "overflowSmallBackdrop";
    public const string Banner = "banner";
    public const string Portrait = "portrait";
    public const string OverflowPortrait = "overflowPortrait";
    public const string Square = "square";
    public const string OverflowSquare = "overflowSquare";
    public const string Auto = "auto";
    public const string AutoHome = "autohome";
    public const string AutoOverflow = "autooverflow";
    public const string AutoVertical = "autoVertical";
    public const string Mixed = "mixed";
    public const string MixedSquare = "mixedSquare";
    public const string MixedBackdrop = "mixedBackdrop";
    public const string MixedPortrait = "mixedPortrait";

    public static string GetSquareShape(bool enableOverflow = true) => enableOverflow ? OverflowSquare : Square;

    public static string GetBackdropShape(bool enableOverflow = true) => enableOverflow ? OverflowBackdrop : Backdrop;

    public static string GetPortraitShape(bool enableOverflow = true) => enableOverflow ? OverflowPortrait : Portrait;

    /// <summary>The width / height ratio a shape is drawn with, or null (<c>getDesiredAspect</c>).</summary>
    public static double? GetDesiredAspect(string? shape)
    {
        if (string.IsNullOrEmpty(shape))
        {
            return null;
        }

        var lower = shape.ToLowerInvariant();
        if (lower.Contains("portrait", StringComparison.Ordinal))
        {
            return 2d / 3d;
        }

        if (lower.Contains("backdrop", StringComparison.Ordinal))
        {
            return 16d / 9d;
        }

        if (lower.Contains("square", StringComparison.Ordinal))
        {
            return 1;
        }

        return lower.Contains("banner", StringComparison.Ordinal) ? 1000d / 185d : null;
    }

    /// <summary>The shape of one card in a "mixed" list (<c>resolveMixedShapeByAspectRatio</c>).</summary>
    public static string ResolveMixedShape(double? primaryImageAspectRatio) => primaryImageAspectRatio switch
    {
        null => MixedSquare,
        >= 1.33 => MixedBackdrop,
        > 0.8 => MixedSquare,
        _ => MixedPortrait
    };

    /// <summary>
    /// The typical image ratio of a list: the median of the items' primary image ratios, snapped to
    /// 2:3, 16:9, 1:1 or 4:3 when close; null when no item has one (<c>getPrimaryImageAspectRatio</c>).
    /// </summary>
    public static double? GetPrimaryImageAspectRatio(IEnumerable<BaseItemDto> items)
    {
        var values = items.Select(item => item.PrimaryImageAspectRatio ?? 0).Where(ratio => ratio != 0).Order().ToList();
        if (values.Count == 0)
        {
            return null;
        }

        var half = values.Count / 2;
        var result = values.Count % 2 == 1 ? values[half] : (values[half - 1] + values[half]) / 2.0;

        const double Aspect2X3 = 2d / 3d;
        if (Math.Abs(Aspect2X3 - result) <= 0.15)
        {
            return Aspect2X3;
        }

        const double Aspect16X9 = 16d / 9d;
        if (Math.Abs(Aspect16X9 - result) <= 0.2)
        {
            return Aspect16X9;
        }

        if (Math.Abs(1 - result) <= 0.15)
        {
            return 1;
        }

        const double Aspect4X3 = 4d / 3d;
        return Math.Abs(Aspect4X3 - result) <= 0.15 ? Aspect4X3 : result;
    }

    /// <summary>
    /// How many cards of a shape fit in a row at a screen width (<c>getPostersPerRow</c>; the TV
    /// layout's values are not ported because the port has no TV layout).
    /// </summary>
    public static double GetPostersPerRow(string? shape, int screenWidth, bool isOrientationLandscape) => shape switch
    {
        Portrait => screenWidth switch
        {
            >= 2200 => 10,
            >= 1920 => 100 / 11.1111111111,
            >= 1600 => 8,
            >= 1400 => 100 / 14.28571428571,
            >= 1200 => 100 / 16.66666667,
            >= 800 => 5,
            >= 700 => 4,
            _ => 100 / 33.33333333
        },
        Square => screenWidth switch
        {
            >= 2200 => 10,
            >= 1920 => 100 / 11.1111111111,
            >= 1600 => 8,
            >= 1400 => 100 / 14.28571428571,
            >= 1200 => 100 / 16.66666667,
            >= 800 => 5,
            >= 700 => 4,
            >= 500 => 100 / 33.33333333,
            _ => 2
        },
        Banner => screenWidth switch
        {
            >= 2200 => 4,
            >= 1200 => 100 / 33.33333333,
            >= 800 => 2,
            _ => 1
        },
        Backdrop => screenWidth switch
        {
            >= 2500 => 6,
            >= 1600 => 5,
            >= 1200 => 4,
            >= 770 => 3,
            >= 420 => 2,
            _ => 1
        },
        SmallBackdrop => screenWidth switch
        {
            >= 1600 => 8,
            >= 1400 => 100 / 14.2857142857,
            >= 1200 => 100 / 16.66666667,
            >= 1000 => 5,
            >= 800 => 4,
            >= 500 => 100 / 33.33333333,
            _ => 2
        },
        OverflowSmallBackdrop when isOrientationLandscape => screenWidth >= 800 ? 100 / 15.5 : 100 / 23.3,
        OverflowSmallBackdrop => screenWidth >= 540 ? 100 / 30 : 100 / 72,
        OverflowPortrait when isOrientationLandscape => screenWidth >= 1700 ? 100 / 11.6 : 100 / 15.5,
        OverflowPortrait => screenWidth switch
        {
            >= 1400 => 100 / 15,
            >= 1200 => 100 / 18,
            >= 760 => 100 / 23,
            >= 400 => 100 / 31.5,
            _ => 100 / 42
        },
        OverflowSquare when isOrientationLandscape => screenWidth >= 1700 ? 100 / 11.6 : 100 / 15.5,
        OverflowSquare => screenWidth switch
        {
            >= 1400 => 100 / 15,
            >= 1200 => 100 / 18,
            >= 760 => 100 / 23,
            >= 540 => 100 / 31.5,
            _ => 100 / 42
        },
        OverflowBackdrop when isOrientationLandscape => screenWidth >= 1700 ? 100 / 18.5 : 100 / 23.3,
        OverflowBackdrop => screenWidth switch
        {
            >= 1800 => 100 / 23.5,
            >= 1400 => 100 / 30,
            >= 760 => 100 / 40,
            >= 640 => 100 / 56,
            _ => 100 / 72
        },
        _ => 4
    };
}

/// <summary>
/// The options of <c>cardBuilder.buildCards</c>, with the original names and meaning. Properties
/// left at their default behave like options the original caller did not pass.
/// </summary>
public sealed record CardOptions
{
    /// <summary>A <see cref="CardShape"/>; null means "auto" (chosen from the items' image ratio).</summary>
    public string? Shape { get; init; }

    /// <summary>The shape an auto shape falls back to when no item has an image ratio.</summary>
    public string? DefaultShape { get; init; }

    /// <summary>Card width in CSS pixels for image sizing; computed from the viewport when null.</summary>
    public int? Width { get; init; }

    // --- image choice ---
    public CardToggle PreferThumb { get; init; }
    public bool PreferBanner { get; init; }
    public bool PreferDisc { get; init; }
    public bool PreferLogo { get; init; }
    public bool PreferParentPoster { get; init; }

    /// <summary>false stops episodes from borrowing the series/parent thumb.</summary>
    public bool? InheritThumb { get; init; }

    /// <summary>Fill the card with the image instead of fitting it.</summary>
    public bool CoverImage { get; init; }
    public bool? Scalable { get; init; }
    public bool? AllowBottomPadding { get; init; }
    public bool ShowDetailsMenu { get; init; }

    /// <summary>Icon shown when an item type has no icon of its own and there is no image.</summary>
    public string? DefaultCardImageIcon { get; init; }

    // --- text ---
    public CardToggle ShowTitle { get; init; }
    public bool ShowParentTitle { get; init; }
    public bool ShowParentTitleOrTitle { get; init; }
    public bool ShowYear { get; init; }
    public bool ShowSeriesYear { get; init; }
    public bool ShowRuntime { get; init; }
    public bool ShowAirTime { get; init; }
    public bool ShowAirDateTime { get; init; }
    public bool ShowAirEndTime { get; init; }
    public bool ShowChannelName { get; init; }
    public bool ShowCurrentProgram { get; init; }
    public bool ShowCurrentProgramTime { get; init; }
    public bool ShowSeriesTimerTime { get; init; }
    public bool ShowSeriesTimerChannel { get; init; }
    public bool ShowPersonRoleOrType { get; init; }
    public bool ShowItemCounts { get; init; }
    public bool ShowSongCount { get; init; }
    public bool ShowPremiereDate { get; init; }
    public bool? IncludeParentInfoInTitle { get; init; }

    /// <summary>Extra text lines per item (<c>options.textLines</c>).</summary>
    public Func<BaseItemDto, IEnumerable<string>>? TextLines { get; init; }

    /// <summary>The number of text lines every card reserves.</summary>
    public int? Lines { get; init; }
    public bool CenterText { get; init; }

    /// <summary>Draw the text over the image instead of below it.</summary>
    public bool OverlayText { get; init; }

    /// <summary>Boxed cards with a solid footer (<c>cardLayout</c>).</summary>
    public bool CardLayout { get; init; }

    /// <summary>"none" removes the footer's more button in the card layout on mobile.</summary>
    public string? CardFooterAside { get; init; }
    public bool ShowChannelLogo { get; init; }
    public bool ShowLogo { get; init; }

    // --- indicators ---
    public bool ShowChildCountIndicator { get; init; }
    public bool ShowGroupCount { get; init; }
    public bool? MissingIndicator { get; init; }
    public bool DisableIndicators { get; init; }

    // --- buttons ---
    public bool? OverlayPlayButton { get; init; }
    public bool OverlayMoreButton { get; init; }
    public bool OverlayInfoButton { get; init; }
    public bool CenterPlayButton { get; init; }
    public bool DisableHoverMenu { get; init; }

    /// <summary>The <see cref="ItemAction"/> a click on the card runs; default "link".</summary>
    public string? Action { get; init; }

    // --- context handed to actions and links ---
    public string? Context { get; init; }
    public string? ParentId { get; init; }
    public string? CollectionId { get; init; }
    public string? PlaylistId { get; init; }

    // --- layout of the list ---
    public string? CardCssClass { get; init; }
    public string? CardClass { get; init; }

    /// <summary>"PremiereDate", "ProductionYear" or "CommunityRating": group the cards under headings.</summary>
    public string? IndexBy { get; init; }
    public string? SectionTitleTagName { get; init; }

    /// <summary>Stack this many cards per column (horizontal lists).</summary>
    public int? Rows { get; init; }
}
