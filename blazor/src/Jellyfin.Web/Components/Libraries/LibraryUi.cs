using Jellyfin.Web.Services;

namespace Jellyfin.Web.Components;

/// <summary>A content type offered when creating a library; <see cref="Message"/> is trusted markup.</summary>
public sealed record CollectionTypeOption(string Name, string Value, string? Message = null);

/// <summary>A sortable (and optionally selectable) plugin of the library options editor.</summary>
public sealed class LibraryPluginRow(string name, bool isChecked = false)
{
    public string Name { get; } = name;

    public bool Checked { get; set; } = isChecked;
}

/// <summary>Helpers shared by the library pages (startup wizard, dashboard libraries).</summary>
public static class LibraryUi
{
    /// <summary>Port of getCollectionTypeOptions (apps/wizard/controllers/library.js).</summary>
    public static IReadOnlyList<CollectionTypeOption> GetCollectionTypeOptions(Globalize globalize) =>
    [
        new(string.Empty, string.Empty),
        new(globalize["Movies"], "movies", GetLink(globalize, "MovieLibraryHelp", "https://jellyfin.org/docs/general/server/media/movies")),
        new(globalize["TabMusic"], "music", GetLink(globalize, "MusicLibraryHelp", "https://jellyfin.org/docs/general/server/media/music")),
        new(globalize["Shows"], "tvshows", GetLink(globalize, "TvLibraryHelp", "https://jellyfin.org/docs/general/server/media/shows")),
        new(globalize["Books"], "books", GetLink(globalize, "BookLibraryHelp", "https://jellyfin.org/docs/general/server/media/books")),
        new(globalize["HomeVideosPhotos"], "homevideos"),
        new(globalize["MusicVideos"], "musicvideos"),
        new(globalize["MixedMoviesShows"], "mixed", globalize["MessageUnsetContentHelp"])
    ];

    /// <summary>Port of utils/image.ts getLibraryIcon.</summary>
    public static string GetLibraryIcon(string? collectionType) => collectionType switch
    {
        "movies" => "movie",
        "music" => "music_note",
        "homevideos" or "photos" => "photo",
        "livetv" => "live_tv",
        "tvshows" => "tv",
        "trailers" => "theaters",
        "musicvideos" => "music_video",
        "books" => "book",
        "boxsets" => "video_library",
        "playlists" => "queue",
        "channels" => "videocam",
        null => "ondemand_video",
        _ => "folder"
    };

    /// <summary>
    /// Port of getOrderedPlugins: sorts by position in the configured order; plugins that are
    /// not configured come first, in their original order.
    /// </summary>
    public static List<T> OrderPlugins<T>(IEnumerable<T>? plugins, IReadOnlyList<string>? configuredOrder, Func<T, string?> name)
    {
        var order = configuredOrder ?? [];
        return (plugins ?? []).OrderBy(p => IndexOf(order, name(p))).ToList();
    }

    private static int IndexOf(IReadOnlyList<string> list, string? value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetLink(Globalize globalize, string key, string url) =>
        globalize.Translate(
            key,
            $"<a class=\"button-link emby-button\" href=\"{url}\" target=\"_blank\" rel=\"noopener noreferrer\" data-autohide=\"true\">",
            "</a>");
}
