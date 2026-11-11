using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Components.Shell;

/// <summary>
/// Material icon font names in the form MudBlazor's <c>Icon</c> parameters accept. The app ships
/// the same icon font as the original client, so any of its ligature names work.
/// </summary>
public static class MdIcon
{
    /// <summary>The value for a MudBlazor <c>Icon</c> parameter, e.g. <c>MdIcon.Of("home")</c>.</summary>
    public static string Of(string name) => "material-icons " + name;

    /// <summary>The icon name for a library. Port of apps/modern/components/LibraryIcon.tsx.</summary>
    public static string ForLibrary(BaseItemDto_CollectionType? collectionType) => collectionType switch
    {
        BaseItemDto_CollectionType.Movies => "movie",
        BaseItemDto_CollectionType.Music => "music_note",
        BaseItemDto_CollectionType.Homevideos or BaseItemDto_CollectionType.Photos => "photo",
        BaseItemDto_CollectionType.Livetv => "live_tv",
        BaseItemDto_CollectionType.Tvshows => "tv",
        BaseItemDto_CollectionType.Trailers => "local_movies",
        BaseItemDto_CollectionType.Musicvideos => "music_video",
        BaseItemDto_CollectionType.Books => "book",
        BaseItemDto_CollectionType.Boxsets => "collections",
        BaseItemDto_CollectionType.Playlists => "queue",
        _ => "folder"
    };
}
