using Jellyfin.Web.Services;

namespace Jellyfin.Web.Tests.Library;

/// <summary>The server requests of the library views, per tab, filter and sort order.</summary>
public class LibraryQueryBuilderTests
{
    private const string UserId = "user1";
    private const string ParentId = "library1";

    private static LibraryTabContent Tab(LibraryRouteConfig route, int index) => route.ViewAt(index).Content;

    private static Dictionary<string, string?> Query(LibraryRequest request) =>
        request.Query.Where(entry => entry.Value is not null).ToDictionary(entry => entry.Key, entry => Text(entry.Value));

    private static string? Text(object? value) => value switch
    {
        null => null,
        string text => text,
        bool flag => flag ? "true" : "false",
        IEnumerable<string> list => string.Join(',', list),
        IEnumerable<int> list => string.Join(',', list),
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
    };

    private static LibraryRequest Build(LibraryTabContent content, LibraryViewSettings? settings = null, int pageSize = 100) =>
        LibraryQueryBuilder.Build(new LibraryQueryContext(content, ParentId, settings ?? LibraryViewSettings.Defaults(content.ViewType), pageSize, UserId));

    [Fact]
    public void The_movies_tab_lists_movies_of_the_library_recursively()
    {
        var request = Build(Tab(LibraryRoutes.Movies, 0));

        Assert.Equal("Items", request.Path);
        var query = Query(request);
        Assert.Equal(UserId, query["userId"]);
        Assert.Equal(ParentId, query["parentId"]);
        Assert.Equal("true", query["recursive"]);
        Assert.Equal("1", query["imageTypeLimit"]);
        Assert.Equal("Primary,Backdrop", query["enableImageTypes"]);
        Assert.Equal("MediaSourceCount,PrimaryImageAspectRatio", query["fields"]);
        Assert.Equal("100", query["limit"]);
        Assert.Equal("0", query["startIndex"]);
        Assert.Equal("SortName", query["sortBy"]);
        Assert.Equal("Ascending", query["sortOrder"]);
        Assert.Equal("Movie", query["includeItemTypes"]);
        Assert.False(query.ContainsKey("isFavorite"));
        Assert.False(query.ContainsKey("isMissing"));
    }

    [Fact]
    public void A_page_size_of_zero_asks_for_everything()
    {
        var query = Query(Build(Tab(LibraryRoutes.Movies, 0), pageSize: 0));

        Assert.False(query.ContainsKey("limit"));
    }

    [Fact]
    public void The_favorites_tab_asks_for_favorites()
    {
        var query = Query(Build(Tab(LibraryRoutes.Movies, 2)));

        Assert.Equal("true", query["isFavorite"]);
        Assert.Equal("Movie", query["includeItemTypes"]);
    }

    [Fact]
    public void The_folders_tab_is_not_recursive()
    {
        var query = Query(Build(Tab(LibraryRoutes.Books, 0)));

        Assert.Equal("false", query["recursive"]);
        Assert.Equal("Folder,AudioBook,Book", query["includeItemTypes"]);
    }

    [Fact]
    public void The_episodes_tab_sorts_by_series_and_hides_missing_episodes()
    {
        var query = Query(Build(Tab(LibraryRoutes.TvShows, 5)));

        Assert.Equal("SeriesSortName", query["sortBy"]);
        Assert.Equal("false", query["isMissing"]);
        Assert.Equal("Episode", query["includeItemTypes"]);
    }

    [Theory]
    [InlineData(2, "AlbumArtist")]
    [InlineData(3, "Artist")]
    public void The_artist_tabs_list_persons(int tab, string personType)
    {
        var request = Build(Tab(LibraryRoutes.Music, tab));

        Assert.Equal("Persons", request.Path);
        var query = Query(request);
        Assert.Equal(personType, query["personTypes"]);
        Assert.False(query.ContainsKey("sortBy"));
        Assert.False(query.ContainsKey("recursive"));
    }

    [Fact]
    public void The_people_tab_lists_the_video_person_kinds_unless_some_are_deselected()
    {
        var content = Tab(LibraryRoutes.Movies, 7);

        var all = Query(Build(content));
        var some = Query(Build(content, LibraryViewSettings.Defaults(content.ViewType) with
        {
            Filters = new LibraryFilters { PersonKinds = ["Actor", "Director"], Status = ["IsFavorite"] }
        }));

        Assert.Equal("Actor,Director,Writer,Producer,GuestStar,Unknown", all["personTypes"]);
        Assert.Equal("PrimaryImageAspectRatio", all["fields"]);
        Assert.Equal("Actor,Director", some["personTypes"]);
        Assert.Equal("IsFavorite", some["filters"]);
    }

    [Fact]
    public void The_authors_tab_lists_authors()
    {
        var request = Build(Tab(LibraryRoutes.Books, 2));

        Assert.Equal("Persons", request.Path);
        Assert.Equal("Author", Query(request)["personTypes"]);
    }

    [Fact]
    public void The_studios_tab_lists_studios_with_thumbs()
    {
        var content = Tab(LibraryRoutes.Movies, 5);

        var request = Build(content, LibraryViewSettings.Defaults(content.ViewType) with
        {
            Filters = new LibraryFilters { Status = ["IsFavorite"] }
        });

        Assert.Equal("Studios", request.Path);
        var query = Query(request);
        Assert.Equal("DateCreated,PrimaryImageAspectRatio", query["fields"]);
        Assert.Equal("Thumb", query["enableImageTypes"]);
        Assert.Equal("true", query["isFavorite"]);
        Assert.Equal("Movie", query["includeItemTypes"]);
    }

    [Theory]
    [InlineData("#", "nameLessThan", "A")]
    [InlineData("T", "nameStartsWith", "T")]
    public void The_alphabet_picker_narrows_the_names(string letter, string key, string value)
    {
        var content = Tab(LibraryRoutes.Movies, 0);

        var query = Query(Build(content, LibraryViewSettings.Defaults(content.ViewType) with { Alphabet = letter, StartIndex = 50 }));

        Assert.Equal(value, query[key]);
        Assert.Equal("50", query["startIndex"]);
    }

    [Fact]
    public void Sort_order_and_image_type_reach_the_request()
    {
        var content = Tab(LibraryRoutes.Movies, 0);

        var query = Query(Build(content, LibraryViewSettings.Defaults(content.ViewType) with
        {
            SortBy = ["CommunityRating", "SortName"],
            SortOrder = "Descending",
            ImageType = "Thumb"
        }));

        Assert.Equal("CommunityRating,SortName", query["sortBy"]);
        Assert.Equal("Descending", query["sortOrder"]);
        Assert.Equal("Thumb,Backdrop", query["enableImageTypes"]);
        Assert.Equal("MediaSourceCount", query["fields"]);
    }

    [Fact]
    public void Filters_become_query_parameters()
    {
        var filters = new LibraryFilters
        {
            Features = ["HasSubtitles", "HasTrailer", "HasSpecialFeature", "HasThemeSong", "HasThemeVideo"],
            Status = ["IsUnplayed", "IsFavorite"],
            VideoBasicFilter = ["IsHD", "Is4K", "Is3D"],
            VideoTypes = ["Dvd", "BluRay"],
            SeriesStatus = ["Ended"],
            Genres = ["Action", "Sci-Fi, Fantasy"],
            OfficialRatings = ["PG"],
            Tags = ["one", "two"],
            StudioIds = ["s1", "s2"],
            Years = [2019, 2021],
            AudioLanguages = ["eng"],
            SubtitleLanguages = ["ger", "fre"]
        };

        var query = LibraryQueryBuilder.FiltersQuery(LibraryTab.Movies, filters).ToDictionary(entry => entry.Key, entry => Text(entry.Value));

        Assert.Equal("true", query["hasSubtitles"]);
        Assert.Equal("true", query["hasTrailer"]);
        Assert.Equal("true", query["hasSpecialFeature"]);
        Assert.Equal("true", query["hasThemeSong"]);
        Assert.Equal("true", query["hasThemeVideo"]);
        Assert.Equal("true", query["isHd"]);
        Assert.Equal("true", query["is4K"]);
        Assert.Equal("true", query["is3D"]);
        Assert.Equal("Dvd,BluRay", query["videoTypes"]);
        Assert.Equal("Ended", query["seriesStatus"]);
        Assert.Equal("IsUnplayed,IsFavorite", query["filters"]);
        Assert.Equal("Action|Sci-Fi, Fantasy", query["genres"]);
        Assert.Equal("PG", query["officialRatings"]);
        Assert.Equal("one|two", query["tags"]);
        Assert.Equal("s1|s2", query["studioIds"]);
        Assert.Equal("2019,2021", query["years"]);
        Assert.Equal("eng", query["audioLanguages"]);
        Assert.Equal("ger,fre", query["subtitleLanguages"]);
        Assert.False(query.ContainsKey("isMissing"));
    }

    [Fact]
    public void Standard_definition_wins_over_high_definition()
    {
        var query = LibraryQueryBuilder.FiltersQuery(LibraryTab.Movies, new LibraryFilters { VideoBasicFilter = ["IsSD", "IsHD"] });

        Assert.Equal(("isHd", (object?)false), Assert.Single(query));
    }

    [Theory]
    [InlineData("IsMissing", "isMissing=true,isUnaired=false")]
    [InlineData("IsUnaired", "isUnaired=true")]
    [InlineData("IsMissing,IsUnaired", "isMissing=true")]
    [InlineData("ParentIndexNumber", "parentIndexNumber=0,isMissing=false")]
    public void The_episode_filters_follow_the_original_rules(string selected, string expected)
    {
        var query = LibraryQueryBuilder.FiltersQuery(LibraryTab.Episodes, new LibraryFilters { EpisodeFilter = selected.Split(',') });

        Assert.Equal(expected, string.Join(',', query.Select(entry => entry.Key + "=" + Text(entry.Value))));
    }

    [Fact]
    public void A_custom_request_replaces_the_built_one()
    {
        var content = new LibraryTabContent
        {
            ViewType = LibraryTab.Channels,
            Request = context => new LibraryRequest("LiveTv/Channels", [("userId", context.UserId), ("limit", context.PageSize)])
        };

        var request = Build(content, pageSize: 7);

        Assert.Equal("LiveTv/Channels", request.Path);
        Assert.Equal("7", Query(request)["limit"]);
    }

    [Fact]
    public void Play_options_carry_the_filters_and_the_sort_order()
    {
        var settings = LibraryViewSettings.Defaults(LibraryTab.Movies) with
        {
            SortBy = ["DateCreated", "SortName"],
            SortOrder = "Descending",
            Filters = new LibraryFilters { Status = ["IsUnplayed"], Genres = ["Action"], Years = [2019] }
        };

        var play = LibraryQueryBuilder.PlayQueryOptions(LibraryTab.Movies, ParentId, settings, shuffle: false);
        var shuffle = LibraryQueryBuilder.PlayQueryOptions(LibraryTab.Movies, null, settings, shuffle: true);
        var sort = LibraryQueryBuilder.SortQueryOptions(settings);

        Assert.Equal(ParentId, play["ParentId"]);
        Assert.Equal("IsUnplayed", play["Filters"]);
        Assert.Equal("Action", play["Genres"]);
        Assert.Equal("2019", play["Years"]);
        Assert.Equal("DateCreated,SortName", play["SortBy"]);
        Assert.Equal("Descending", play["SortOrder"]);
        Assert.False(shuffle.ContainsKey("ParentId"));
        Assert.Equal("Random", shuffle["SortBy"]);
        Assert.False(shuffle.ContainsKey("SortOrder"));
        Assert.Equal(2, sort.Count);
        Assert.Equal("DateCreated,SortName", sort["SortBy"]);
    }

    [Fact]
    public void Each_view_has_its_sort_options()
    {
        Assert.Equal(
            new[]
            {
                "Name", "OptionRandom", "OptionCommunityRating", "OptionCriticRating", "OptionDateAdded", "OptionDatePlayed",
                "OptionParentalRating", "OptionPlayCount", "OptionReleaseDate", "Runtime"
            },
            LibraryQueryBuilder.SortOptions(LibraryTab.Movies).Select(option => option.Label));
        Assert.Equal(new[] { "SeriesSortName" }, LibraryQueryBuilder.SortOptions(LibraryTab.Episodes)[0].Value);
        Assert.Equal(new[] { "Name" }, LibraryQueryBuilder.SortOptions(LibraryTab.Songs)[0].Value);
        Assert.Equal(new[] { "SeriesDatePlayed", "SortName" }, LibraryQueryBuilder.SortOptions(LibraryTab.Series)[5].Value);
        Assert.Equal(3, LibraryQueryBuilder.SortOptions(LibraryTab.Photos).Count);
        Assert.Empty(LibraryQueryBuilder.SortOptions(LibraryTab.Studios));
        Assert.Empty(LibraryQueryBuilder.SortOptions(LibraryTab.Persons));
    }

    [Fact]
    public void The_image_types_are_hidden_for_views_with_fixed_images()
    {
        Assert.Equal(new[] { "Primary", "Banner", "Disc", "Logo", "Thumb" }, LibraryQueryBuilder.ImageTypes(LibraryTab.Movies));
        Assert.Empty(LibraryQueryBuilder.ImageTypes(LibraryTab.Episodes));
        Assert.Empty(LibraryQueryBuilder.ImageTypes(LibraryTab.Albums));
    }

    [Fact]
    public void The_filter_menu_of_movies_has_the_original_groups_in_order()
    {
        var values = new LibraryFilterValues
        {
            Genres = ["Action", "Drama"],
            OfficialRatings = ["PG"],
            Tags = ["tag"],
            Years = [2008, 2019],
            Studios = [("s1", "Studio One")],
            AudioLanguages = [("eng", "English")],
            SubtitleLanguages = [("ger", "German")]
        };

        var sections = LibraryQueryBuilder.FilterSections(LibraryTab.Movies, values);

        Assert.Equal(
            new[]
            {
                "filtersStatus", "filtersFeatures", "filtersVideoTypes", "filtersGenres", "filtersOfficialRatings", "filtersTags",
                "filtersYears", "filtersStudios", "filtersAudioLanguages", "filtersSubtitleLanguages"
            },
            sections.Select(section => section.Id));
        Assert.Equal(new[] { "Played", "Unplayed", "Favorite", "ContinueWatching" }, sections[0].Options.Select(option => option.Label));
        Assert.Equal(new[] { "IsSD", "IsHD", "Is4K", "Is3D", "Dvd", "BluRay", "Iso" }, sections[2].Options.Select(option => option.Value));
        Assert.Equal(new LibraryFilterOption("StudioIds", "s1", "Studio One", false), Assert.Single(sections[7].Options));
        Assert.Equal(new[] { "2008", "2019" }, sections[6].Options.Select(option => option.Value));
    }

    [Fact]
    public void The_filter_menu_depends_on_the_view()
    {
        string[] Ids(string viewType) => LibraryQueryBuilder.FilterSections(viewType, LibraryFilterValues.Empty).Select(section => section.Id).ToArray();

        Assert.Equal(new[] { "filtersStatus", "filtersSeriesStatus", "filtersFeatures", "filtersVideoTypes" }, Ids(LibraryTab.Series));
        Assert.Equal(new[] { "filtersStatus", "filtersEpisodesStatus", "filtersFeatures", "filtersVideoTypes" }, Ids(LibraryTab.Episodes));
        Assert.Equal(new[] { "filtersStatus", "filtersPersonKinds" }, Ids(LibraryTab.Persons));
        Assert.Equal(new[] { "filtersStatus" }, Ids(LibraryTab.Albums));

        // Views without played state offer the favorite filter only.
        var albums = LibraryQueryBuilder.FilterSections(LibraryTab.Albums, LibraryFilterValues.Empty)[0];
        Assert.Equal(new[] { "Favorite" }, albums.Options.Select(option => option.Label));
        var books = LibraryQueryBuilder.FilterSections(LibraryTab.Books, LibraryFilterValues.Empty)[0];
        Assert.Equal("ContinueReading", books.Options[^1].Label);
    }

    [Fact]
    public void Played_and_unplayed_exclude_each_other()
    {
        var played = new LibraryFilterOption("Status", "IsPlayed", "Played");
        var unplayed = new LibraryFilterOption("Status", "IsUnplayed", "Unplayed");
        var favorite = new LibraryFilterOption("Status", "IsFavorite", "Favorite");

        var filters = LibraryQueryBuilder.Toggle(null, played, true);
        filters = LibraryQueryBuilder.Toggle(filters, favorite, true);
        filters = LibraryQueryBuilder.Toggle(filters, unplayed, true);

        Assert.Equal(new[] { "IsFavorite", "IsUnplayed" }, filters!.Status);
        Assert.True(LibraryQueryBuilder.IsSelected(filters, unplayed));
        Assert.False(LibraryQueryBuilder.IsSelected(filters, played));

        filters = LibraryQueryBuilder.Toggle(filters, unplayed, false);
        filters = LibraryQueryBuilder.Toggle(filters, favorite, false);

        Assert.Null(filters);
    }

    [Fact]
    public void Person_kinds_are_all_selected_until_one_is_deselected()
    {
        var actor = new LibraryFilterOption("PersonKinds", "Actor", "Actor");
        var writer = new LibraryFilterOption("PersonKinds", "Writer", "Writer");

        Assert.True(LibraryQueryBuilder.IsSelected(null, actor));

        var filters = LibraryQueryBuilder.Toggle(null, actor, false);

        Assert.Equal(new[] { "Director", "Writer", "Producer", "GuestStar", "Unknown" }, filters!.PersonKinds);
        Assert.False(LibraryQueryBuilder.IsSelected(filters, actor));
        Assert.True(LibraryQueryBuilder.IsSelected(filters, writer));

        // Selecting the last missing kind is the default again, which is not stored.
        Assert.Null(LibraryQueryBuilder.Toggle(filters, actor, true));
    }
}
