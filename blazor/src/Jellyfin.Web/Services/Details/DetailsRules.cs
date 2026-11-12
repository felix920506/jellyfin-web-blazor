using System.Globalization;

using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>Which of the play buttons of the details page show (<c>reloadPlayButtons</c>).</summary>
public sealed record DetailPlayButtons(bool Play, bool Replay, bool InstantMix, bool Shuffle, bool Resumable)
{
    public static readonly DetailPlayButtons None = new(false, false, false, false, false);

    /// <summary>The translation key of the play button's title.</summary>
    public string PlayTitleKey => Resumable ? "ButtonResume" : "Play";
}

/// <summary>What the children area of the details page shows (<c>setInitialCollapsibleState</c>).</summary>
public enum DetailChildrenKind
{
    /// <summary>Nothing: the item is no folder.</summary>
    None,

    /// <summary>The items of a playlist as a list.</summary>
    Playlist,

    /// <summary>Sections per item type for a person, genre, studio or artist.</summary>
    ItemsByName,

    /// <summary>Sections per item type for the contents of a collection.</summary>
    Collection,

    /// <summary>Seasons (cards), episodes or tracks (list), or the empty "Items" section of other folders.</summary>
    Children
}

/// <summary>
/// The per-item-type rules of the details page, ported from
/// <c>apps/legacy/controllers/itemDetails/index.js</c>. Pure functions, so every rule can be tested
/// without rendering the page.
/// </summary>
public static class DetailsRules
{
    /// <summary>The types whose children are drawn in <c>#listChildrenCollapsible</c> (LIST_VIEW_TYPES).</summary>
    private static readonly HashSet<string> ListViewTypes = ["MusicAlbum", "Playlist", "Season", "Series"];

    private static readonly HashSet<string> SimilarTypes =
        ["Movie", "Trailer", "Series", "Program", "Recording", "MusicAlbum", "MusicArtist", "Playlist", "Audio"];

    private static readonly HashSet<string> MergeablePersonTypes = ["Writer", "Director", "Producer"];

    /// <summary>The library kind links of the page lead to (<c>inferContext</c>).</summary>
    public static string? InferContext(BaseItemDto item) => item.TypeName() switch
    {
        "Movie" or "BoxSet" => "movies",
        "Series" or "Season" or "Episode" => "tvshows",
        "MusicArtist" or "MusicAlbum" or "Audio" or "AudioBook" => "music",
        "Program" => "livetv",
        _ => null
    };

    /// <summary>Whether the version and track selects apply to the item (<c>itemHelper.supportsMediaSourceSelection</c>).</summary>
    public static bool SupportsMediaSourceSelection(BaseItemDto item)
    {
        if (item.MediaTypeName() != "Video" || item.TypeName() == "TvChannel")
        {
            return false;
        }

        if (item.MediaSources is not { Count: > 0 } sources
            || (sources.Count == 1 && sources[0].Type == MediaSourceInfo_Type.Placeholder))
        {
            return false;
        }

        if (item.EnableMediaSourceDisplay is { } enabled)
        {
            return enabled;
        }

        return string.IsNullOrEmpty(item.SourceType) || item.SourceType == "Library";
    }

    /// <summary>The play, replay, instant mix and shuffle buttons (<c>reloadPlayButtons</c>).</summary>
    public static DetailPlayButtons GetPlayButtons(BaseItemDto item, bool canPlay, DateTimeOffset now)
    {
        var type = item.TypeName();
        if (type == "Program")
        {
            // A programme plays (its channel) only while it is on air.
            var onAir = item.StartDate is { } start && item.EndDate is { } end && now >= start && now < end;
            return onAir ? new DetailPlayButtons(true, false, false, false, false) : DetailPlayButtons.None;
        }

        if (!canPlay)
        {
            return DetailPlayButtons.None;
        }

        var music = type is "MusicAlbum" or "MusicGenre" or "MusicArtist";
        var resumable = (item.UserData?.PlaybackPositionTicks ?? 0) > 0;
        return new DetailPlayButtons(
            Play: true,
            Replay: resumable,
            InstantMix: music || type == "Audio",
            Shuffle: item.IsFolder == true || music,
            Resumable: resumable);
    }

    /// <summary>The button row hides only for a programme that cannot be played.</summary>
    public static bool ShowMainButtons(BaseItemDto item, DetailPlayButtons buttons) => item.TypeName() != "Program" || buttons.Play;

    /// <summary>The trailer button (<c>setTrailerButtonVisibility</c>); every local player plays trailers.</summary>
    public static bool ShowTrailerButton(BaseItemDto item) => item.LocalTrailerCount > 0 || item.RemoteTrailers is { Count: > 0 };

    /// <summary>"Split versions": administrators, for an item with grouped versions.</summary>
    public static bool ShowSplitVersions(BaseItemDto item, UserDto? user) =>
        user?.Policy?.IsAdministrator == true
        && item.MediaSources is { } sources && sources.Any(source => source.Type == MediaSourceInfo_Type.Grouping);

    /// <summary>The download button exists for books only; other types download from the menu.</summary>
    public static bool ShowDownload(BaseItemDto item) => item.TypeName() == "Book" && item.CanDownload == true;

    /// <summary>"Stop recording": a recording in progress, for users who manage Live TV.</summary>
    public static bool ShowCancelTimer(BaseItemDto item, UserDto? user) =>
        item.TypeName() == "Recording" && user?.Policy?.EnableLiveTvManagement == true
        && !string.IsNullOrEmpty(item.TimerId) && item.Status == "InProgress";

    /// <summary>"Cancel series": a series timer, for users who manage Live TV.</summary>
    public static bool ShowCancelSeriesTimer(BaseItemDto item, UserDto? user) =>
        item.TypeName() == "SeriesTimer" && user?.Policy?.EnableLiveTvManagement == true;

    /// <summary>The record button and its fields: a programme, for users who manage Live TV.</summary>
    public static bool ShowRecordingFields(BaseItemDto item, UserDto? user) =>
        item.TypeName() == "Program" && user?.Policy?.EnableLiveTvManagement == true;

    /// <summary>The header of the people section (<c>setPeopleHeader</c>).</summary>
    public static string PeopleHeaderKey(BaseItemDto item) =>
        item.MediaTypeName() is "Audio" or "Book" or "Photo" || item.TypeName() == "MusicAlbum" ? "People" : "HeaderCastAndCrew";

    public static DetailChildrenKind GetChildrenKind(BaseItemDto item) => item.TypeName() switch
    {
        "Playlist" => DetailChildrenKind.Playlist,
        "Studio" or "Person" or "Genre" or "MusicGenre" or "MusicArtist" => DetailChildrenKind.ItemsByName,
        "BoxSet" when item.IsFolder == true => DetailChildrenKind.Collection,
        _ => item.IsFolder == true ? DetailChildrenKind.Children : DetailChildrenKind.None
    };

    /// <summary>True when the children are drawn in the primary column, false for the secondary one.</summary>
    public static bool ChildrenInPrimaryColumn(BaseItemDto item) => ListViewTypes.Contains(item.TypeName());

    /// <summary>The translation key of the children section's title.</summary>
    public static string ChildrenTitleKey(BaseItemDto item) => item.TypeName() switch
    {
        "Season" => "Episodes",
        "Series" => "HeaderSeasons",
        "MusicAlbum" => "HeaderTracks",
        _ => "Items"
    };

    /// <summary>Tracks and episodes come without a title above the list.</summary>
    public static bool HideChildrenTitle(BaseItemDto item) => item.TypeName() is "MusicAlbum" or "Season";

    public static bool ShowNextUp(BaseItemDto item) => item.TypeName() == "Series";

    public static bool ShowSeriesSchedule(BaseItemDto item) => item.TypeName() == "Series";

    public static bool ShowSpecials(BaseItemDto item) => item.SpecialFeatureCount > 0;

    public static bool ShowAdditionalParts(BaseItemDto item) => item.PartCount > 1;

    public static bool ShowMusicVideos(BaseItemDto item) => item.TypeName() == "MusicAlbum";

    public static bool ShowSimilar(BaseItemDto item) => SimilarTypes.Contains(item.TypeName());

    public static bool ShowMoreFromSeason(BaseItemDto item) =>
        item.TypeName() == "Episode" && item.SeasonId is not null && item.SeriesId is not null;

    /// <summary>
    /// "More from artist" / "Appears on". The original also asks for it for a song without album
    /// artists and fails there; the port leaves the section out.
    /// </summary>
    public static bool ShowMoreFromArtist(BaseItemDto item) => item.TypeName() switch
    {
        "MusicArtist" => true,
        "Audio" or "MusicAlbum" => item.AlbumArtists is { Count: > 0 },
        _ => false
    };

    public static bool ShowLyrics(BaseItemDto item) => item.TypeName() == "Audio" && item.HasLyrics == true;

    public static bool ShowChannelGuide(BaseItemDto item) => item.TypeName() == "TvChannel";

    /// <summary>The chapters shown as scenes: none when the first one has no image (<c>renderScenes</c>).</summary>
    public static IReadOnlyList<ChapterInfo> GetScenes(BaseItemDto item) =>
        item.Chapters is { Count: > 0 } chapters && !string.IsNullOrEmpty(chapters[0].ImageTag) ? chapters : [];

    /// <summary>
    /// Splits the people of an item into cast and crew, and guest cast. Artists are left out, and a
    /// writer, director or producer credited several times becomes one entry with all the roles.
    /// The people come back as items of type Person, the way the card builder wants them.
    /// </summary>
    public static (IReadOnlyList<BaseItemDto> Cast, IReadOnlyList<BaseItemDto> GuestCast) SplitPeople(BaseItemDto item)
    {
        var cast = new List<BaseItemDto>();
        var guestCast = new List<BaseItemDto>();
        var crewIndex = new Dictionary<Guid, int>();
        var roles = new Dictionary<int, List<ItemPersonRole>>();
        foreach (var person in item.People ?? [])
        {
            var type = person.Type.ToApiString();
            if (type == "GuestStar")
            {
                guestCast.Add(ToItem(person, item.ServerId));
            }
            else if (type is "Artist" or "AlbumArtist")
            {
                // Artists have their own links in the title.
            }
            else if (person.Id is { } id && type is not null && MergeablePersonTypes.Contains(type))
            {
                if (crewIndex.TryGetValue(id, out var index))
                {
                    roles[index].Add(new ItemPersonRole(type, person.Role));
                }
                else
                {
                    crewIndex[id] = cast.Count;
                    var list = new List<ItemPersonRole> { new(type, person.Role) };
                    roles[cast.Count] = list;
                    var entry = ToItem(person, item.ServerId);
                    entry.EnsureExtras().RoleList = list;
                    cast.Add(entry);
                }
            }
            else
            {
                cast.Add(ToItem(person, item.ServerId));
            }
        }

        return (cast, guestCast);
    }

    /// <summary>When a series airs, in the original's (untranslated) words (<c>renderSeriesAirTime</c>).</summary>
    public static string? GetSeriesAirTime(BaseItemDto item)
    {
        if (item.TypeName() != "Series")
        {
            return null;
        }

        var text = string.Empty;
        if (item.AirDays is { Count: > 0 } days)
        {
            text = days.Count == 7 ? "daily" : string.Join(",", days.Select(day => day.ToApiString() + "s"));
        }

        if (!string.IsNullOrEmpty(item.AirTime))
        {
            text += " at " + item.AirTime;
        }

        return text.Length == 0 ? null : (item.Status == "Ended" ? "Aired " : "Airs ") + text;
    }

    /// <summary>Full years between two dates (date-fns <c>intervalToDuration(...).years</c>).</summary>
    public static int YearsBetween(DateTimeOffset start, DateTimeOffset end)
    {
        var years = end.Year - start.Year;
        if (end.Month < start.Month || (end.Month == start.Month && (end.Day < start.Day || (end.Day == start.Day && end.TimeOfDay < start.TimeOfDay))))
        {
            years--;
        }

        return Math.Max(years, 0);
    }

    /// <summary>The collection type sections an item of a collection belongs to, in the original's order.</summary>
    public static readonly IReadOnlyList<(string TitleKey, string? Type, string? MediaType)> CollectionItemTypes =
    [
        ("Movies", "Movie", null),
        ("Series", "Series", null),
        ("Episodes", "Episode", null),
        ("HeaderVideos", null, "Video"),
        ("Albums", "MusicAlbum", null),
        ("Books", "Book", null),
        ("Collections", "BoxSet", null)
    ];

    /// <summary>
    /// Sorts the items of a collection into sections by type (<c>renderCollectionItems</c>): the known
    /// types first, what is left under "other items", and one empty "Items" section for an empty collection.
    /// </summary>
    public static IReadOnlyList<(string TitleKey, string? Type, IReadOnlyList<BaseItemDto> Items)> GroupCollectionItems(IReadOnlyList<BaseItemDto> items)
    {
        var sections = new List<(string, string?, IReadOnlyList<BaseItemDto>)>();
        var rest = items.ToList();
        foreach (var (titleKey, type, mediaType) in CollectionItemTypes)
        {
            var matching = rest
                .Where(item => type is not null ? item.TypeName() == type : item.MediaTypeName() == mediaType)
                .ToList();
            if (matching.Count > 0)
            {
                rest = rest.Except(matching).ToList();
                sections.Add((titleKey, type, matching));
            }
        }

        if (rest.Count > 0)
        {
            sections.Add(("HeaderOtherItems", null, rest));
        }

        if (items.Count == 0)
        {
            sections.Add(("Items", null, []));
        }

        return sections;
    }

    /// <summary>Whether the tracks of an album show their artists: when some track's artists differ from its album artists.</summary>
    public static bool ShowTrackArtists(IReadOnlyList<BaseItemDto> tracks) => tracks.Any(track =>
        !Ids(track.ArtistItems).SetEquals(Ids(track.AlbumArtists)));

    /// <summary>Whether the tracks of an album are grouped by disc.</summary>
    public static bool ShowDiscs(IReadOnlyList<BaseItemDto> tracks) =>
        tracks.Select(track => track.ParentIndexNumber).Distinct().Count() > 1
        || (tracks.Count >= 1 && tracks[0].ParentIndexNumber > 1);

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static HashSet<Guid?> Ids(List<NameGuidPair>? pairs) => (pairs ?? []).Select(pair => pair.Id).ToHashSet();

    private static BaseItemDto ToItem(BaseItemPerson person, string? serverId)
    {
        var item = new BaseItemDto
        {
            Id = person.Id,
            Name = person.Name,
            ServerId = serverId,
            Type = BaseItemDto_Type.Person
        };
        if (!string.IsNullOrEmpty(person.PrimaryImageTag))
        {
            item.ImageTags = new BaseItemDto_ImageTags { AdditionalData = new Dictionary<string, object> { ["Primary"] = person.PrimaryImageTag } };
        }

        var extras = item.EnsureExtras();
        extras.Role = person.Role;
        extras.PersonType = person.Type.ToApiString();
        return item;
    }
}
