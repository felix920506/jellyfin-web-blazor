using System.Globalization;
using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Components;

namespace Jellyfin.Web.Services;

/// <summary>What the image options editor shows for one library content type.</summary>
public sealed class ImageOptionsState
{
    /// <summary>The image types of the checkbox list, in the template's order.</summary>
    public List<ImageTypeChoice> Types { get; } = [];

    /// <summary>Whether the type has backdrops, which shows the two backdrop fields.</summary>
    public bool SupportsBackdrops { get; set; }

    public string? MaxBackdrops { get; set; }

    public string? MinBackdropWidth { get; set; }
}

/// <summary>One image type of the image options editor.</summary>
public sealed class ImageTypeChoice(ImageOption_Type type, string labelKey)
{
    public ImageOption_Type Type { get; } = type;

    /// <summary>The translation key of the label.</summary>
    public string LabelKey { get; } = labelKey;

    /// <summary>Whether the content type has images of this type; the others are hidden.</summary>
    public bool Supported { get; set; }

    public bool Checked { get; set; }
}

/// <summary>
/// The logic of the libraries page and its dialogs that does not need a page. Port of
/// features/libraries/utils/collectionTypeOptions.ts, of the texts of features/libraries/components/LibraryCard.tsx,
/// of components/imageOptionsEditor/imageOptionsEditor.js and of the option handling of
/// components/mediaLibraryEditor/mediaLibraryEditor.js.
/// </summary>
public static class DashboardLibraries
{
    // The template's checkboxes: (data-imagetype, label).
    private static readonly (ImageOption_Type Type, string LabelKey)[] ImageTypes =
    [
        (ImageOption_Type.Primary, "Primary"),
        (ImageOption_Type.Art, "Art"),
        (ImageOption_Type.BoxRear, "Back"),
        (ImageOption_Type.Banner, "Banner"),
        (ImageOption_Type.Box, "Box"),
        (ImageOption_Type.Disc, "Disc"),
        (ImageOption_Type.Logo, "Logo"),
        (ImageOption_Type.Menu, "Menu"),
        (ImageOption_Type.Thumb, "Thumb"),
    ];

    /// <summary>
    /// Port of getCollectionTypeOptions of the dashboard: the content types of a new library. Unlike
    /// the wizard's list it has no help texts.
    /// </summary>
    public static IReadOnlyList<CollectionTypeOption> GetCollectionTypeOptions(Globalize globalize) =>
    [
        new(string.Empty, string.Empty),
        new(globalize["Movies"], "movies"),
        new(globalize["TabMusic"], "music"),
        new(globalize["Shows"], "tvshows"),
        new(globalize["Books"], "books"),
        new(globalize["HomeVideosPhotos"], "homevideos"),
        new(globalize["MusicVideos"], "musicvideos"),
        new(globalize["MixedMoviesShows"], "mixed"),
    ];

    /// <summary>The second line of a library card: the name of the content type, "Other" when it has none of the list.</summary>
    public static string TypeName(Globalize globalize, string? collectionType)
    {
        // The original compares loosely, so a library without content type matches nothing and shows "Other".
        var name = GetCollectionTypeOptions(globalize)
            .FirstOrDefault(option => option.Value.Length > 0 && option.Value == collectionType)?.Name;
        return string.IsNullOrEmpty(name) ? globalize["Other"] : name;
    }

    /// <summary>The question asked before a library is removed, with its folders.</summary>
    public static string RemoveConfirmationText(Globalize globalize, VirtualFolderInfo folder) =>
        globalize["MessageAreYouSureYouWishToRemoveMediaFolder"] + "\n\n"
        + globalize["MessageTheFollowingLocationWillBeRemovedFromLibrary"] + "\n\n"
        + string.Join('\n', folder.Locations ?? []);

    /// <summary>The width a card image is asked for: 40% of the screen in device pixels.</summary>
    public static int CardImageWidth(double screenWidth, double devicePixelRatio) =>
        (int)Math.Round(screenWidth * (devicePixelRatio > 0 ? devicePixelRatio : 1) * 0.40, MidpointRounding.AwayFromZero);

    /// <summary>
    /// The folders the library editor lists: the library's path infos, or its locations when it has none.
    /// </summary>
    public static List<string> PathsOf(VirtualFolderInfo library)
    {
        var infos = library.LibraryOptions?.PathInfos;
        var paths = infos is { Count: > 0 }
            ? infos.Select(info => info.Path)
            : library.Locations ?? [];
        return paths.Where(path => !string.IsNullOrEmpty(path)).Select(path => path!).ToList();
    }

    /// <summary>
    /// <c>Object.assign(library.LibraryOptions || {}, getLibraryOptions())</c> of the editor: what the
    /// options editor returns replaces the library's values, and what it does not know (the path
    /// infos, for one) stays.
    /// </summary>
    public static LibraryOptions MergeOptions(LibraryOptions? original, LibraryOptions edited)
    {
        if (original is null)
        {
            return edited;
        }

        var merged = JsonNode.Parse(KiotaJson.Serialize(original)) as JsonObject ?? [];
        var changes = JsonNode.Parse(KiotaJson.Serialize(edited)) as JsonObject ?? [];
        foreach (var (name, value) in changes)
        {
            merged[name] = value?.DeepClone();
        }

        return KiotaJson.Parse(merged.ToJsonString(), LibraryOptions.CreateFromDiscriminatorValue) ?? edited;
    }

    /// <summary>
    /// Whether a content type has image settings worth a "Fetcher settings" button: more than the
    /// primary image.
    /// </summary>
    public static bool HasImageOptions(LibraryTypeOptionsDto? available)
    {
        var supported = available?.SupportedImageTypes ?? [];
        return supported.Count > 1 || (supported.Count == 1 && supported[0] != ImageType.Primary);
    }

    /// <summary>
    /// Port of getImageConfig: the options of an image type: the library's, else the server's
    /// defaults, else "one primary image and nothing else".
    /// </summary>
    public static ImageOption ImageConfigOf(TypeOptions options, LibraryTypeOptionsDto? available, ImageOption_Type type) =>
        options.ImageOptions?.FirstOrDefault(option => option.Type == type)
        ?? available?.DefaultImageOptions?.FirstOrDefault(option => option.Type == type)
        ?? new ImageOption { Type = type, MinWidth = 0, Limit = type == ImageOption_Type.Primary ? 1 : 0 };

    /// <summary>Port of loadValues: what the image options editor shows.</summary>
    public static ImageOptionsState LoadImageOptions(TypeOptions options, LibraryTypeOptionsDto? available)
    {
        var supported = (available?.SupportedImageTypes ?? [])
            .Select(type => type.ToApiString())
            .Where(name => name is not null)
            .ToHashSet(StringComparer.Ordinal);

        var state = new ImageOptionsState { SupportsBackdrops = supported.Contains("Backdrop") };
        foreach (var (type, labelKey) in ImageTypes)
        {
            state.Types.Add(new ImageTypeChoice(type, labelKey)
            {
                Supported = supported.Contains(type.ToApiString()),
                Checked = ImageConfigOf(options, available, type).Limit is not (null or 0),
            });
        }

        var backdrops = ImageConfigOf(options, available, ImageOption_Type.Backdrop);
        state.MaxBackdrops = Number(backdrops.Limit);
        state.MinBackdropWidth = Number(backdrops.MinWidth);
        return state;
    }

    /// <summary>
    /// Port of saveValues: writes what the editor shows into the type's options. Every type of the
    /// list is written, also the ones the content type does not have, followed by the backdrops.
    /// </summary>
    public static void SaveImageOptions(ImageOptionsState state, TypeOptions options)
    {
        var imageOptions = state.Types
            .Select(choice => new ImageOption { Type = choice.Type, Limit = choice.Checked ? 1 : 0, MinWidth = 0 })
            .ToList();
        imageOptions.Add(new ImageOption
        {
            Type = ImageOption_Type.Backdrop,
            Limit = NetworkingLogic.ParseInt(state.MaxBackdrops) ?? 0,
            MinWidth = NetworkingLogic.ParseInt(state.MinBackdropWidth) ?? 0,
        });
        options.ImageOptions = imageOptions;
    }

    /// <summary>
    /// The widths of the trickplay page as the original reads them:
    /// <c>value.replace(' ', '').split(',').map(Number)</c>. An empty part is 0; a part that is not a
    /// number is null (NaN).
    /// </summary>
    public static List<int?> ParseWidthResolutions(string? text)
    {
        var value = text ?? string.Empty;
        var blank = value.IndexOf(' ', StringComparison.Ordinal);
        if (blank >= 0)
        {
            // String.replace with a text only replaces the first match.
            value = value.Remove(blank, 1);
        }

        return value.Split(',')
            .Select(part => part.Trim())
            .Select(part => part.Length == 0
                ? 0
                : int.TryParse(part, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number) ? number : (int?)null)
            .ToList();
    }

    /// <summary>Whether the widths field holds what the original's pattern ([0-9,]*, required) lets through.</summary>
    public static bool IsValidWidthResolutions(string? text) =>
        !string.IsNullOrEmpty(text) && text.All(character => char.IsAsciiDigit(character) || character == ',');

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);
}
