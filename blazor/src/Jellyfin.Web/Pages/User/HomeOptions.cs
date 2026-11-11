using Jellyfin.Web.Services;

namespace Jellyfin.Web.Pages.User;

/// <summary>A landing screen of a library (components/homeScreenSettings getLandingScreenOptions).</summary>
public sealed record LandingOption(string Value, string NameKey, bool IsDefault = false);

/// <summary>The option lists and value rules of the home screen settings (components/homeScreenSettings).</summary>
public static class HomeOptions
{
    public const int ConfigurableSections = 10;

    /// <summary>The home sections of a user who has not chosen any (components/homesections DEFAULT_SECTIONS).</summary>
    public static readonly IReadOnlyList<string> DefaultSections =
    [
        "smalllibrarytiles", "resume", "resumeaudio", "resumebook", "livetv", "nextup", "latestmedia", "none", "none", "none"
    ];

    /// <summary>The section types a home section select offers (value, name key).</summary>
    public static readonly IReadOnlyList<(string Value, string NameKey)> SectionTypes =
    [
        ("smalllibrarytiles", "HeaderMyMedia"),
        ("librarybuttons", "HeaderMyMediaSmall"),
        ("activerecordings", "HeaderActiveRecordings"),
        ("resume", "HeaderContinueWatching"),
        ("resumeaudio", "HeaderContinueListening"),
        ("resumebook", "HeaderContinueReading"),
        ("latestmedia", "HeaderLatestMedia"),
        ("nextup", "NextUp"),
        ("livetv", "LiveTV"),
        ("none", "None")
    ];

    /// <summary>Collection types whose libraries never appear in the latest media sections.</summary>
    public static readonly IReadOnlyList<string> ExcludedFromLatest = ["playlists", "livetv", "boxsets", "channels"];

    /// <summary>Collection types whose libraries have a choice of landing screens.</summary>
    public static readonly IReadOnlyList<string> LandingScreenTypes =
        ["movies", "tvshows", "music", "livetv", "homevideos", "musicvideos", "mixed", "books"];

    public static string GetDefaultSection(int index) =>
        index >= 0 && index < DefaultSections.Count ? DefaultSections[index] : string.Empty;

    /// <summary>
    /// The options of the select for the section at <paramref name="index"/> (0-based): the
    /// default section of that position is offered with an empty value.
    /// </summary>
    public static List<SelectOption> SectionOptions(int index, Globalize globalize)
    {
        var defaultSection = GetDefaultSection(index);
        return SectionTypes
            .Select(type => new SelectOption(type.Value == defaultSection ? string.Empty : type.Value, globalize[type.NameKey]))
            .ToList();
    }

    /// <summary>The select value for a stored section: empty for the default (or nothing, or an unknown type).</summary>
    public static string SectionValue(int index, string? userValue)
    {
        if (string.IsNullOrEmpty(userValue) || userValue == GetDefaultSection(index))
        {
            return string.Empty;
        }

        return SectionTypes.Any(type => type.Value == userValue) ? userValue : string.Empty;
    }

    public static IReadOnlyList<LandingOption> LandingOptions(string? collectionType) => collectionType switch
    {
        "movies" =>
        [
            new("movies", "Movies", true), new("suggestions", "Suggestions"), new("favorites", "Favorites"),
            new("collections", "Collections"), new("genres", "Genres"), new("studios", "Studios"), new("playlists", "Playlists")
        ],
        "tvshows" =>
        [
            new("series", "Shows", true), new("suggestions", "Suggestions"), new("upcoming", "TabUpcoming"), new("genres", "Genres"),
            new("studios", "Studios"), new("episodes", "Episodes"), new("collections", "Collections"), new("playlists", "Playlists")
        ],
        "music" =>
        [
            new("albums", "Albums", true), new("suggestions", "Suggestions"), new("albumartists", "HeaderAlbumArtists"), new("artists", "Artists"),
            new("playlists", "Playlists"), new("songs", "Songs"), new("genres", "Genres"), new("collections", "Collections")
        ],
        "livetv" =>
        [
            new("programs", "Programs", true), new("guide", "Guide"), new("channels", "Channels"), new("recordings", "Recordings"),
            new("schedule", "Schedule"), new("seriestimers", "Series")
        ],
        "homevideos" =>
        [
            new("folders", "Folders", true), new("photos", "Photos"), new("photoalbums", "HeaderPhotoAlbums"), new("videos", "HeaderVideos")
        ],
        "musicvideos" =>
        [
            new("folders", "Folders", true), new("suggestions", "Suggestions"), new("musicvideos", "HeaderVideos"), new("playlists", "Playlists")
        ],
        "mixed" =>
        [
            new("folders", "Folders", true), new("suggestions", "Suggestions"), new("mixed", "HeaderMedia"),
            new("collections", "Collections"), new("playlists", "Playlists")
        ],
        "books" =>
        [
            new("folders", "Folders", true), new("books", "Books"), new("authors", "Authors"), new("suggestions", "Suggestions"),
            new("genres", "Genres"), new("collections", "Collections"), new("favorites", "Favorites")
        ],
        _ => []
    };

    /// <summary>The landing select's options: the default screen is offered with an empty value.</summary>
    public static List<SelectOption> LandingSelectOptions(string? collectionType, Globalize globalize) =>
        LandingOptions(collectionType)
            .Select(option => new SelectOption(option.IsDefault ? string.Empty : option.Value, globalize[option.NameKey]))
            .ToList();

    /// <summary>The landing select's value for a stored screen: empty for the default, nothing or an unknown screen.</summary>
    public static string LandingValue(string? collectionType, string? userValue)
    {
        if (string.IsNullOrEmpty(userValue))
        {
            return string.Empty;
        }

        var option = LandingOptions(collectionType).FirstOrDefault(o => o.Value == userValue);
        return option is null || option.IsDefault ? string.Empty : option.Value;
    }
}
