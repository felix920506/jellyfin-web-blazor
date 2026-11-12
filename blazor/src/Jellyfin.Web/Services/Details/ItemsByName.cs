using Jellyfin.Sdk.Generated.Models;

namespace Jellyfin.Web.Services;

/// <summary>
/// One section of the page of a person, genre, studio or artist: the items of one type that
/// belong to it. Port of <c>scripts/itemsByName.js</c>.
/// </summary>
public sealed record ItemsByNameSection(string TitleKey, string Type)
{
    /// <summary>
    /// The sections of an item, in the original's order: by the item counts the server reports,
    /// then audio books and books, which the server does not count.
    /// </summary>
    public static IReadOnlyList<ItemsByNameSection> For(BaseItemDto item)
    {
        var sections = new List<ItemsByNameSection>();
        void Add(bool condition, string titleKey, string type)
        {
            if (condition)
            {
                sections.Add(new ItemsByNameSection(titleKey, type));
            }
        }

        Add(item.ArtistCount > 0, "Artists", "MusicArtist");
        Add(item.ProgramCount > 0 && item.TypeName() == "Person", "HeaderUpcomingOnTV", "Program");
        Add(item.MovieCount > 0, "Movies", "Movie");
        Add(item.SeriesCount > 0, "Shows", "Series");
        Add(item.EpisodeCount > 0, "Episodes", "Episode");
        Add(item.TrailerCount > 0, "Trailers", "Trailer");
        Add(item.AlbumCount > 0, "Albums", "MusicAlbum");
        Add(item.MusicVideoCount > 0, "MusicVideos", "MusicVideo");
        Add(true, "HeaderAudioBooks", "Audiobook");
        Add(true, "Books", "Book");
        return sections;
    }

    /// <summary>The number of items a section asks for; a "more" link shows when there are more.</summary>
    public int Limit => Type switch
    {
        "MusicAlbum" => 100,
        "MusicArtist" => 8,
        "Episode" => 6,
        _ => 10
    };

    /// <summary>The query of the section for an item (<c>getQuery</c> with the options of <c>renderSection</c>).</summary>
    public IReadOnlyList<(string Key, object? Value)> GetQuery(BaseItemDto item, bool displayMissingEpisodes)
    {
        var (sortBy, sortOrder) = Type switch
        {
            "Program" => ("StartDate", "Ascending"),
            "Movie" => ("PremiereDate,ProductionYear,SortName", "Descending,Descending,Ascending"),
            "MusicAlbum" => ("PremiereDate,ProductionYear,Sortname", "Descending,Descending,Ascending"),
            "Audiobook" or "Book" => ("ProductionYear,SortName", "Descending,Ascending"),
            _ => ("SortName", "Ascending")
        };

        var query = new List<(string Key, object? Value)>
        {
            ("SortOrder", sortOrder),

            // Artists come from /Persons, which takes no item types.
            ("IncludeItemTypes", Type == "MusicArtist" ? null : Type),
            ("Recursive", true),

            // The original appends the fields to themselves; the server does not mind.
            ("Fields", "ParentId,PrimaryImageAspectRatio,ParentId,PrimaryImageAspectRatio"),
            ("Limit", Limit),
            ("StartIndex", 0),
            ("CollapseBoxSetItems", false),
            ("SortBy", sortBy)
        };

        if (Type == "Episode" && !displayMissingEpisodes)
        {
            query.Add(("isMissing", false));
        }

        // addCurrentItemToQuery
        switch (item.TypeName())
        {
            case "Person":
                query.Add(("PersonIds", item.IdText()));
                break;
            case "Genre" or "MusicGenre":
                query.Add(("Genres", item.Name));
                break;
            case "Studio":
                query.Add(("StudioIds", item.IdText()));
                break;
            case "MusicArtist":
                query.Add((Type == "MusicVideo" ? "ArtistIds" : "AlbumArtistIds", item.IdText()));
                break;
        }

        if (Type == "MusicArtist")
        {
            query.Add(("PersonTypes", "AlbumArtist"));
        }

        return query;
    }

    /// <summary>How the items of the section are drawn.</summary>
    public CardOptions GetCardOptions() => Type switch
    {
        "Program" => new CardOptions
        {
            Shape = CardShape.OverflowBackdrop,
            ShowTitle = true,
            CenterText = true,
            OverlayMoreButton = true,
            PreferThumb = true,
            OverlayText = false,
            ShowAirTime = true,
            ShowAirDateTime = true,
            ShowChannelName = true
        },
        "Movie" or "Audiobook" or "Book" => new CardOptions
        {
            Shape = CardShape.OverflowPortrait,
            ShowTitle = true,
            CenterText = true,
            OverlayMoreButton = true,
            OverlayText = false,
            ShowYear = true
        },
        "MusicVideo" => new CardOptions
        {
            Shape = CardShape.OverflowBackdrop,
            ShowTitle = true,
            CenterText = true,
            OverlayPlayButton = true
        },
        "Trailer" => new CardOptions
        {
            Shape = CardShape.OverflowPortrait,
            ShowTitle = true,
            CenterText = true,
            OverlayPlayButton = true
        },
        "Series" => new CardOptions
        {
            Shape = CardShape.OverflowPortrait,
            ShowTitle = true,
            CenterText = true,
            OverlayMoreButton = true
        },
        "MusicAlbum" => new CardOptions
        {
            Shape = CardShape.OverflowSquare,
            ShowTitle = true,
            ShowYear = true,
            CoverImage = true,
            CenterText = true,
            OverlayPlayButton = true
        },
        "MusicArtist" => new CardOptions
        {
            Shape = CardShape.OverflowSquare,
            ShowTitle = true,
            ShowParentTitle = true,
            CoverImage = true,
            CenterText = true,
            OverlayPlayButton = true
        },
        _ => new CardOptions
        {
            Shape = CardShape.OverflowBackdrop,
            ShowTitle = true,
            ShowParentTitle = true,
            CenterText = true,
            OverlayPlayButton = true
        }
    };

    /// <summary>The list of everything of the section's type that belongs to the item (<c>getMoreItemsHref</c>).</summary>
    public string GetMoreUrl(BaseItemDto item)
    {
        var parameter = item.TypeName() switch
        {
            "Genre" => "genreId",
            "MusicGenre" => "musicGenreId",
            "Studio" => "studioId",
            "MusicArtist" => "artistId",
            "Person" => "personId",
            _ => "parentId"
        };
        return $"#/list?type={Type}&{parameter}={item.IdText()}&serverId={item.ServerId}";
    }
}
