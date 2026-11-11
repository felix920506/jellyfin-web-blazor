using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>Port of <c>components/itemHelper.js</c>: display names and what may be done with an item.</summary>
public static class ItemHelper
{
    /// <summary>
    /// The title shown for an item: episode numbering ("S1:E2 - Name"), special episodes, channel
    /// numbers and programme episode titles, as <c>itemHelper.getDisplayName</c> builds it.
    /// </summary>
    /// <param name="includeParentInfo">false drops the season part ("2. Name").</param>
    /// <param name="includeIndexNumber">false drops the numbering entirely.</param>
    public static string GetDisplayName(BaseItemDto item, Globalize globalize, bool? includeParentInfo = null, bool? includeIndexNumber = null)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.TypeName() == "Timer")
        {
            item = item.Extras()?.ProgramInfo ?? item;
        }

        var type = item.TypeName();
        var name = (type is "Program" or "Recording" && (item.IsSeries == true || !string.IsNullOrEmpty(item.EpisodeTitle))
            ? item.EpisodeTitle
            : item.Name) ?? string.Empty;

        if (type == "TvChannel")
        {
            return string.IsNullOrEmpty(item.ChannelNumber) ? name : item.ChannelNumber + " " + name;
        }

        if (type == "Episode" && item.ParentIndexNumber == 0)
        {
            name = globalize.Translate("ValueSpecialEpisodeName", name);
        }
        else if (type is "Episode" or "Program" or "Recording"
                 && item.IndexNumber is { } index && item.ParentIndexNumber is { } parentIndex
                 && includeIndexNumber != false)
        {
            var displayIndex = Invariant(index);
            string number;
            var separator = " - ";
            if (includeParentInfo != false)
            {
                number = "S" + Invariant(parentIndex) + ":E" + displayIndex;
            }
            else
            {
                number = displayIndex;
                separator = ". ";
            }

            if (item.IndexNumberEnd is { } end)
            {
                number += "-" + Invariant(end);
            }

            name = string.IsNullOrEmpty(name) ? number : number + separator + name;
        }

        return name;
    }

    public static bool IsLocalItem(BaseItemDto item) =>
        // Local (downloaded) items carry ids starting with "local"; the SDK's Guid ids never do.
        false;

    public static bool SupportsAddingToCollection(BaseItemDto item)
    {
        var type = item.TypeName();
        if (type is "Genre" or "MusicGenre" or "Studio" or "UserView" or "CollectionFolder" or "Audio" or "Program" or "Timer" or "SeriesTimer")
        {
            return false;
        }

        if (IsIncompleteRecording(item))
        {
            return false;
        }

        return item.CollectionTypeName() is null && item.MediaTypeName() != "Photo" && !IsLocalItem(item);
    }

    public static bool SupportsAddingToPlaylist(BaseItemDto item)
    {
        var type = item.TypeName();
        if (type is "Program" or "TvChannel" or "Timer" or "SeriesTimer")
        {
            return false;
        }

        if (item.MediaTypeName() == "Photo" || IsIncompleteRecording(item) || IsLocalItem(item))
        {
            return false;
        }

        if (item.CollectionTypeName() == "livetv")
        {
            return false;
        }

        return item.MediaTypeName() is not null || item.IsFolder == true || type is "Genre" or "MusicGenre" or "MusicArtist";
    }

    public static bool CanEdit(UserDto? user, BaseItemDto item)
    {
        var type = item.TypeName();
        if (type is "UserRootFolder" or "UserView" or "Program" or "Timer" or "SeriesTimer")
        {
            return false;
        }

        if (IsIncompleteRecording(item) || IsLocalItem(item))
        {
            return false;
        }

        return user?.Policy?.IsAdministrator == true;
    }

    public static bool CanIdentify(UserDto? user, BaseItemDto item) =>
        item.TypeName() is "Movie" or "Trailer" or "Series" or "BoxSet" or "Person" or "Book" or "MusicAlbum" or "MusicArtist" or "MusicVideo"
        && user?.Policy?.IsAdministrator == true
        && !IsLocalItem(item);

    public static bool CanEditImages(UserDto? user, BaseItemDto item)
    {
        var type = item.TypeName();
        if (item.MediaTypeName() == "Photo")
        {
            return false;
        }

        if (type == "UserView")
        {
            return user?.Policy?.IsAdministrator == true;
        }

        if (IsIncompleteRecording(item))
        {
            return false;
        }

        return type is not ("Timer" or "SeriesTimer") && CanEdit(user, item) && !IsLocalItem(item);
    }

    public static bool CanEditSubtitles(UserDto? user, BaseItemDto item)
    {
        var type = item.TypeName();
        if (item.MediaTypeName() != "Video" || IsIncompleteRecording(item))
        {
            return false;
        }

        if (type is "TvChannel" or "Program" or "Timer" or "SeriesTimer" or "UserRootFolder" or "UserView")
        {
            return false;
        }

        if (IsLocalItem(item) || item.LocationType == BaseItemDto_LocationType.Virtual)
        {
            return false;
        }

        return user?.Policy?.EnableSubtitleManagement == true || user?.Policy?.IsAdministrator == true;
    }

    public static bool CanEditLyrics(UserDto? user, BaseItemDto item) =>
        item.MediaTypeName() == "Audio" && !IsLocalItem(item) && user?.Policy?.IsAdministrator == true;

    /// <param name="sharingSupported">Whether the host can share links (<c>appHost.supports(Sharing)</c>).</param>
    public static bool CanShare(UserDto? user, BaseItemDto item, bool sharingSupported)
    {
        var type = item.TypeName();
        if (type is "Program" or "TvChannel" or "Timer" or "SeriesTimer")
        {
            return false;
        }

        if (IsIncompleteRecording(item) || IsLocalItem(item))
        {
            return false;
        }

        return user?.Policy?.EnablePublicSharing == true && sharingSupported;
    }

    public static bool EnableDateAddedDisplay(BaseItemDto item) =>
        item.IsFolder != true && item.MediaTypeName() is not null && item.TypeName() is not ("Program" or "TvChannel" or "Trailer");

    public static bool CanMarkPlayed(BaseItemDto item)
    {
        var type = item.TypeName();
        if (type == "Program")
        {
            return false;
        }

        var mediaType = item.MediaTypeName();
        if (mediaType == "Video" && type != "TvChannel")
        {
            return true;
        }

        if (mediaType == "Audio" && type == "AudioBook")
        {
            return true;
        }

        return type is "Series" or "Season" or "BoxSet" or "Folder" || mediaType == "Book";
    }

    public static bool CanRate(BaseItemDto item) =>
        item.TypeName() is not ("Program" or "Timer" or "SeriesTimer" or "CollectionFolder" or "UserView" or "Channel")
        && item.UserData is not null;

    public static bool CanRefreshMetadata(BaseItemDto item, UserDto? user)
    {
        if (user?.Policy?.IsAdministrator != true)
        {
            return false;
        }

        var type = item.TypeName();
        return item.CollectionTypeName() != "livetv"
               && type is not ("Timer" or "SeriesTimer" or "Program" or "TvChannel")
               && !IsIncompleteRecording(item)
               && !IsLocalItem(item);
    }

    private static bool IsIncompleteRecording(BaseItemDto item) =>
        item.TypeName() == "Recording" && item.Status != "Completed";

    private static string Invariant(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>Port of <c>utils/image.ts</c>: the Material icon that stands in for a missing image.</summary>
public static class ItemIcons
{
    /// <summary>The icon of a library by collection type (<c>getLibraryIcon</c>).</summary>
    public static string ForLibrary(string? collectionType) => collectionType switch
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

    /// <summary>The icon of an item type, or <paramref name="defaultIcon"/> (<c>getItemTypeIcon</c>).</summary>
    public static string? ForItemType(string? type, string? defaultIcon = null) => type switch
    {
        "MusicAlbum" => "album",
        "MusicArtist" or "Person" => "person",
        "Audio" => "audiotrack",
        "Movie" => "movie",
        "Episode" or "Series" => "tv",
        "Program" => "live_tv",
        "Book" => "book",
        "Folder" => "folder",
        "BoxSet" => "video_library",
        "Playlist" => "queue",
        "Photo" => "photo",
        "PhotoAlbum" => "photo_album",
        _ => defaultIcon
    };
}
