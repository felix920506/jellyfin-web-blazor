using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Routing;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Items;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Library;

/// <summary>The logic of the legacy list page (controllers/list.js): parameters, requests, options, settings.</summary>
public class ListPageModelTests
{
    private const string UserId = "user1";

    private static readonly ListSort NameSort = new("SortName", "Ascending");

    private readonly TestApp _app = new();

    private static ListParameters Parameters(string url) => ListParameters.From(AppLocation.Parse(url));

    private static BaseItemDto Item(string type, string? collectionType = null, string name = "Item") =>
        TestItems.Item(new { Id = TestItems.Id(1), Name = name, Type = type, CollectionType = collectionType });

    private static Dictionary<string, string?> Query(LibraryRequest request) =>
        request.Query.ToDictionary(entry => entry.Key, entry => entry.Value switch
        {
            string text => text,
            bool flag => flag ? "true" : "false",
            var value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)
        });

    private static LibraryRequest Request(
        ListParameters parameters,
        BaseItemDto? item = null,
        ListSort? sort = null,
        ListFilters? filters = null,
        string? alphabet = null,
        string? sortBy = null,
        int startIndex = 0,
        int? limit = 100) =>
        ListPageModel.Request(
            parameters, item, sort ?? NameSort, filters ?? new ListFilters(), alphabet, ListPageModel.HasAlphaPicker(item), UserId, false, sortBy, startIndex, limit, out _);

    [Fact]
    public void Parameters_come_from_the_url()
    {
        var parameters = Parameters("/list?type=Programs&serverId=s1&parentId=p1&genreId=g1&IsMovie=true&IsKids=false&IsAiring=&tag=fun");

        Assert.Equal("Programs", parameters.Type);
        Assert.Equal("s1", parameters.ServerId);
        Assert.Equal("p1", parameters.ParentId);
        Assert.Equal("g1", parameters.GenreId);
        Assert.Equal("true", parameters.IsMovie);
        Assert.Equal("false", parameters.IsKids);
        Assert.Null(parameters.IsAiring);
        Assert.Equal("fun", parameters.Tag);
        Assert.Null(parameters.StudioId);
    }

    [Theory]
    [InlineData("/list?parentId=p1", "p1")]
    [InlineData("/list?genreId=g1&parentId=p1", "g1")]
    [InlineData("/list?musicGenreId=m1&parentId=p1", "m1")]
    [InlineData("/list?studioId=s1&parentId=p1", "s1")]
    [InlineData("/list?personId=x1&parentId=p1", "x1")]
    [InlineData("/list?type=Movie&parentId=p1", "p1")]
    [InlineData("/list?type=Programs&genreId=g1", null)]
    [InlineData("/list?type=Recordings&parentId=p1", null)]
    [InlineData("/list?type=nextup&parentId=p1", null)]
    [InlineData("/list?type=tag&tag=fun&parentId=p1", null)]
    [InlineData("/list?type=Movie", null)]
    public void The_page_item_follows_the_parameters(string url, string? expected) =>
        Assert.Equal(expected, ListPageModel.ItemId(Parameters(url)));

    [Theory]
    [InlineData("/list", "items")]
    [InlineData("/list?parentId=p1", "items-p1-Folder")]
    [InlineData("/list?type=Movie&parentId=p1", "items-Movie-Folder")]
    [InlineData("/list?type=Movie&genreId=g1&parentId=p1", "items-Movie-Genre-Folder")]
    [InlineData("/list?genreId=g1&parentId=p1", "items-p1-Genre-Folder")]
    [InlineData("/list?musicGenreId=g1&parentId=p1", "items-p1-MusicGenre-Folder")]
    [InlineData("/list?studioId=s1&parentId=p1", "items-p1-Studio-Folder")]
    [InlineData("/list?type=Movie&personId=x1", "items-Movie-Person")]
    [InlineData("/list?type=Movie&IsFavorite=true", "items-Movie-IsFavorite")]
    [InlineData("/list?type=Programs&IsAiring=true&IsMovie=true&IsKids=false&IsSports=false&IsNews=false&IsSeries=false", "items-Programs-IsAiring-IsMovie-IsKids-IsSports-IsNews-IsSeries")]
    [InlineData("/list?type=nextup", "items-nextup")]
    public void Settings_are_kept_per_parameter_combination(string url, string expected) =>
        Assert.Equal(expected, ListPageModel.SettingsKey(Parameters(url)));

    [Fact]
    public void The_default_sort_order_puts_folders_first_without_a_type()
    {
        Assert.Equal("IsFolder,SortName", ListPageModel.DefaultSortBy(Parameters("/list?parentId=p1")));
        Assert.Equal("SortName", ListPageModel.DefaultSortBy(Parameters("/list?type=Movie")));
        Assert.Equal("SeriesSortName,SortName", ListPageModel.DefaultSortBy(Parameters("/list?type=Episode")));
    }

    [Fact]
    public void Sort_options_of_a_folder()
    {
        var options = ListPageModel.SortOptions(Parameters("/list?parentId=p1"));

        Assert.Equal(
            new[] { "Name", "CommunityRating", "CriticRating", "DateAdded", "DatePlayed", "Folders", "ParentalRating", "PlayCount", "ReleaseDate", "Runtime" },
            options.Select(option => option.Name));
        Assert.Equal(
            new[]
            {
                "SortName", "CommunityRating,SortName", "CriticRating,SortName", "DateCreated,SortName", "DatePlayed,SortName", "IsFolder,SortName",
                "OfficialRating,SortName", "PlayCount,SortName", "ProductionYear,PremiereDate,SortName", "Runtime,SortName"
            },
            options.Select(option => option.Value));
    }

    [Fact]
    public void Sort_options_of_typed_lists()
    {
        var episodes = ListPageModel.SortOptions(Parameters("/list?type=Episode"));
        var programs = ListPageModel.SortOptions(Parameters("/list?type=Programs"));

        Assert.Equal("SeriesSortName,SortName", episodes[0].Value);
        Assert.DoesNotContain(episodes, option => option.Name == "Folders");
        Assert.Equal(new[] { "AirDate", "Name", "CommunityRating", "ParentalRating", "ReleaseDate", "Runtime" }, programs.Select(option => option.Name));
        Assert.Equal("StartDate,SortName", programs[0].Value);
    }

    [Fact]
    public void Filters_offered_per_list()
    {
        Assert.Equal(
            new[] { "IsUnplayed", "IsPlayed", "IsFavorite", "IsResumable", "VideoType", "HasSubtitles", "HasTrailer", "HasSpecialFeature", "HasThemeSong", "HasThemeVideo" },
            ListPageModel.VisibleFilters(Parameters("/list?parentId=p1")));
        Assert.DoesNotContain("IsFavorite", ListPageModel.VisibleFilters(Parameters("/list?type=Movie&IsFavorite=true")));
        Assert.Equal(new[] { "Genres" }, ListPageModel.VisibleFilters(Parameters("/list?type=Programs")));
        Assert.Empty(ListPageModel.VisibleFilters(Parameters("/list?type=nextup")));

        Assert.Equal(new[] { "Episode" }, ListPageModel.FilterItemTypes(Parameters("/list?type=nextup")));
        Assert.Equal(new[] { "Program" }, ListPageModel.FilterItemTypes(Parameters("/list?type=Programs")));
        Assert.Empty(ListPageModel.FilterItemTypes(Parameters("/list?type=Movie")));
    }

    [Fact]
    public void View_settings_offered_per_item()
    {
        Assert.Equal(new[] { "showTitle", "imageType", "viewType" }, ListPageModel.VisibleViewSettings(Item("Folder")));
        Assert.Equal(new[] { "showTitle", "imageType", "viewType" }, ListPageModel.VisibleViewSettings(null));
        Assert.Equal(new[] { "showTitle", "viewType" }, ListPageModel.VisibleViewSettings(Item("PhotoAlbum")));
        Assert.Equal(new[] { "showTitle", "viewType" }, ListPageModel.VisibleViewSettings(Item("ChannelFolderItem")));
    }

    [Theory]
    [InlineData("/list?type=Recordings", "Recordings")]
    [InlineData("/list?type=Programs&IsMovie=true", "Movies")]
    [InlineData("/list?type=Programs&IsSports=true", "Sports")]
    [InlineData("/list?type=Programs&IsKids=true", "HeaderForKids")]
    [InlineData("/list?type=Programs&IsAiring=true", "HeaderOnNow")]
    [InlineData("/list?type=Programs&IsSeries=true", "Shows")]
    [InlineData("/list?type=Programs&IsNews=true", "News")]
    [InlineData("/list?type=Programs&IsMovie=false", "Programs")]
    [InlineData("/list?type=nextup", "NextUp")]
    [InlineData("/list?type=favoritemovies", "FavoriteMovies")]
    [InlineData("/list?type=Movie", "Movies")]
    [InlineData("/list?type=Series", "Shows")]
    [InlineData("/list?type=Season", "Seasons")]
    [InlineData("/list?type=Episode", "Episodes")]
    [InlineData("/list?type=MusicArtist", "Artists")]
    [InlineData("/list?type=MusicAlbum", "Albums")]
    [InlineData("/list?type=Audio", "Songs")]
    [InlineData("/list?type=Video", "Videos")]
    public void Titles_without_an_item_are_translated(string url, string key) =>
        Assert.Equal(Title(key, null), ListPageModel.Title(Parameters(url), null));

    [Fact]
    public void The_item_or_the_tag_names_the_page()
    {
        Assert.Equal(Title(null, "Action"), ListPageModel.Title(Parameters("/list?type=Movie&genreId=g1"), Item("Genre", name: "Action")));
        Assert.Equal(Title(null, "fun"), ListPageModel.Title(Parameters("/list?type=tag&tag=fun"), null));
        Assert.Equal(Title("NextUp", null), ListPageModel.Title(Parameters("/list?type=nextup"), Item("Folder")));
    }

    private static (string? Key, string? Text) Title(string? key, string? text) => (key, text);

    [Fact]
    public void Play_buttons_show_for_playable_lists()
    {
        var folder = Parameters("/list?parentId=p1");

        Assert.True(ListPageModel.ShowPlay(folder, Item("Folder")));
        Assert.True(ListPageModel.ShowShuffle(folder, Item("Folder")));
        Assert.True(ListPageModel.ShowPlay(folder, Item("CollectionFolder", "homevideos")));
        Assert.True(ListPageModel.ShowPlay(folder, Item("CollectionFolder", "musicvideos")));
        Assert.False(ListPageModel.ShowPlay(folder, Item("CollectionFolder", "movies")));
        Assert.False(ListPageModel.ShowShuffle(folder, Item("CollectionFolder", "movies")));
        Assert.False(ListPageModel.ShowPlay(folder, Item("UserView")));
        Assert.False(ListPageModel.ShowPlay(folder, Item("Channel")));

        Assert.False(ListPageModel.ShowPlay(Parameters("/list?type=Programs"), null));
        Assert.True(ListPageModel.ShowPlay(Parameters("/list?type=Programs"), Item("MusicGenre")));
        Assert.True(ListPageModel.ShowPlay(Parameters("/list?type=nextup"), null));
        Assert.False(ListPageModel.ShowShuffle(Parameters("/list?type=nextup"), null));
        Assert.True(ListPageModel.ShowPlay(Parameters("/list?type=Movie"), null));
    }

    [Fact]
    public void The_alphabet_picker_needs_an_item_and_a_name_order()
    {
        Assert.False(ListPageModel.HasAlphaPicker(null));
        Assert.False(ListPageModel.HasAlphaPicker(Item("PhotoAlbum")));
        Assert.True(ListPageModel.HasAlphaPicker(Item("Folder")));

        Assert.True(ListPageModel.ShowAlphaPicker(Item("Folder"), new ListSort("IsFolder,SortName", "Ascending")));
        Assert.False(ListPageModel.ShowAlphaPicker(Item("Folder"), new ListSort("Random", "Ascending")));
        Assert.False(ListPageModel.ShowAlphaPicker(null, NameSort));
    }

    [Fact]
    public void A_folder_lists_its_children()
    {
        var parameters = Parameters("/list?parentId=p1");

        var request = ListPageModel.Request(
            parameters, Item("Folder"), new ListSort("IsFolder,SortName", "Descending"), new ListFilters(), null, true, UserId, false, null, 200, 100, out var recursive);

        Assert.Equal("Items", request.Path);
        Assert.False(recursive);
        var query = Query(request);
        Assert.Equal(UserId, query["userId"]);
        Assert.Equal("200", query["StartIndex"]);
        Assert.Equal("100", query["Limit"]);
        Assert.Equal(TestItems.Id(1), query["ParentId"]);
        Assert.Equal("IsFolder,SortName", query["SortBy"]);
        Assert.Equal("Descending", query["SortOrder"]);
        Assert.Equal("PrimaryImageAspectRatio,SortName,Path,ChildCount,MediaSourceCount,PrimaryImageAspectRatio", query["Fields"]);
        Assert.Equal("1", query["ImageTypeLimit"]);
        Assert.False(query.ContainsKey("Recursive"));
        Assert.False(query.ContainsKey("IncludeItemTypes"));
        Assert.False(query.ContainsKey("Filters"));
    }

    [Fact]
    public void A_typed_folder_and_a_shuffled_folder_are_listed_recursively()
    {
        var typed = ListPageModel.Request(
            Parameters("/list?type=Movie&parentId=p1"), Item("Folder"), NameSort, new ListFilters(), null, true, UserId, false, null, 0, 100, out var typedRecursive);
        var shuffled = ListPageModel.Request(
            Parameters("/list?parentId=p1"), Item("Folder"), NameSort, new ListFilters(), null, true, UserId, false, "Random", 0, 300, out var shuffledRecursive);

        Assert.True(typedRecursive);
        Assert.Equal("Movie", Query(typed)["IncludeItemTypes"]);
        Assert.Equal("true", Query(typed)["Recursive"]);

        Assert.True(shuffledRecursive);
        var query = Query(shuffled);
        Assert.Equal("Video,Movie,Series,Music,MusicVideo", query["IncludeItemTypes"]);
        Assert.Equal("true", query["Recursive"]);
        Assert.Equal("Random", query["SortBy"]);
        Assert.False(query.ContainsKey("SortOrder"));
        Assert.Equal("300", query["Limit"]);
    }

    [Fact]
    public void A_type_without_an_item_lists_the_whole_server()
    {
        var request = ListPageModel.Request(
            Parameters("/list?type=Movie&IsFavorite=true&artistId=a1&tag=fun"), null, NameSort, new ListFilters(), "B", false, UserId, false, null, 0, 100, out var recursive);

        Assert.Equal("Items", request.Path);
        Assert.True(recursive);
        var query = Query(request);
        Assert.Equal("Movie", query["IncludeItemTypes"]);
        Assert.Equal("true", query["Recursive"]);
        Assert.Equal("true", query["IsFavorite"]);
        Assert.Equal("a1", query["ArtistIds"]);
        Assert.Equal("fun", query["Tags"]);
        Assert.Equal("PrimaryImageAspectRatio,SortName,Chapters,Trickplay,PrimaryImageAspectRatio", query["Fields"]);

        // Without an item there is no alphabet picker.
        Assert.False(query.ContainsKey("NameStartsWith"));
    }

    [Theory]
    [InlineData("MusicArtist", "Artist")]
    [InlineData("Person", null)]
    public void Artists_and_people_come_from_the_persons_endpoint(string type, string? personTypes)
    {
        var request = Request(Parameters("/list?type=" + type));

        Assert.Equal("Persons", request.Path);
        var query = Query(request);
        Assert.False(query.ContainsKey("IncludeItemTypes"));
        Assert.Equal(personTypes, query.GetValueOrDefault("PersonTypes"));
    }

    [Theory]
    [InlineData("Genre", null, "GenreIds", "Audiobook,Book,BoxSet,Movie,Series,Video")]
    [InlineData("Genre", "movies", "GenreIds", "Movie")]
    [InlineData("Genre", "tvshows", "GenreIds", "Series")]
    [InlineData("MusicGenre", null, "GenreIds", "MusicAlbum")]
    [InlineData("Studio", "tvshows", "StudioIds", "Series")]
    [InlineData("Studio", null, "StudioIds", null)]
    [InlineData("Person", null, "PersonIds", "Movie")]
    public void Genres_studios_and_people_filter_the_library(string itemType, string? collectionType, string idKey, string? itemTypes)
    {
        var request = ListPageModel.Request(
            Parameters("/list?type=Movie&parentId=p1&genreId=x"), Item(itemType, collectionType), NameSort, new ListFilters(), null, true, UserId, false, null, 0, 100, out var recursive);

        Assert.Equal("Items", request.Path);
        Assert.True(recursive);
        var query = Query(request);
        Assert.Equal("p1", query["parentId"]);
        Assert.Equal(TestItems.Id(1), query[idKey]);
        Assert.Equal(itemTypes, query.GetValueOrDefault("IncludeItemTypes"));
        Assert.Equal("true", query["Recursive"]);
        Assert.Equal("PrimaryImageAspectRatio,SortName,PrimaryImageAspectRatio", query["Fields"]);
    }

    [Fact]
    public void Next_up_has_its_own_request()
    {
        var request = ListPageModel.Request(
            Parameters("/list?type=nextup"), null, NameSort, new ListFilters(), null, false, UserId, true, null, 0, 100, out var recursive);

        Assert.Equal("Shows/NextUp", request.Path);
        Assert.False(recursive);
        var query = Query(request);
        Assert.Equal(UserId, query["UserId"]);
        Assert.Equal("100", query["Limit"]);
        Assert.Equal("Primary,Backdrop,Thumb", query["EnableImageTypes"]);
        Assert.Equal("false", query["EnableTotalRecordCount"]);
        Assert.Equal("true", query["EnableRewatching"]);
        Assert.Equal("PrimaryImageAspectRatio,DateCreated,MediaSourceCount,Chapters,Trickplay,PrimaryImageAspectRatio", query["Fields"]);
        Assert.False(query.ContainsKey("StartIndex"));
    }

    [Theory]
    [InlineData("/list?type=Recordings&genreId=g1", "LiveTv/Recordings")]
    [InlineData("/list?type=Programs&IsMovie=true&IsKids=false", "LiveTv/Programs")]
    [InlineData("/list?type=Programs&IsAiring=true", "LiveTv/Programs/Recommended")]
    public void Live_tv_lists_have_their_own_requests(string url, string path)
    {
        var parameters = Parameters(url);

        var request = Request(parameters, startIndex: 100);

        Assert.Equal(path, request.Path);
        var query = Query(request);
        Assert.Equal(UserId, query["UserId"]);
        Assert.Equal("100", query["StartIndex"]);
        Assert.Equal("ChannelInfo,PrimaryImageAspectRatio,PrimaryImageAspectRatio", query["Fields"]);
        if (parameters.Type == "Recordings")
        {
            Assert.Equal("false", query["IsInProgress"]);
            Assert.Equal("g1", query["GenreIds"]);
            Assert.False(query.ContainsKey("HasAired"));
        }
        else
        {
            Assert.Equal("false", query["HasAired"]);
            Assert.Equal(parameters.IsMovie, query.GetValueOrDefault("IsMovie"));
            Assert.Equal(parameters.IsKids, query.GetValueOrDefault("IsKids"));
            Assert.Equal(parameters.IsAiring, query.GetValueOrDefault("IsAiring"));
            Assert.False(query.ContainsKey("IsSports"));
        }
    }

    [Fact]
    public void Saved_filters_narrow_the_request()
    {
        var filters = new ListFilters
        {
            IsPlayed = true,
            IsFavorite = true,
            IsResumable = true,
            VideoTypes = "Dvd,BluRay",
            SeriesStatus = "Ended",
            Is4K = true,
            IsHD = true,
            Is3D = true,
            HasSubtitles = true,
            HasTrailer = true,
            HasSpecialFeature = true,
            HasThemeSong = true,
            HasThemeVideo = true
        };

        var query = Query(Request(Parameters("/list?parentId=p1"), Item("Folder"), filters: filters));

        Assert.Equal("IsPlayed,IsFavorite,IsResumable", query["Filters"]);
        Assert.Equal("Dvd,BluRay", query["VideoTypes"]);
        Assert.Equal("true", query["Is4K"]);
        Assert.Equal("true", query["IsHD"]);
        Assert.Equal("true", query["Is3D"]);
        Assert.Equal("true", query["HasSubtitles"]);
        Assert.Equal("true", query["HasTrailer"]);
        Assert.Equal("true", query["HasSpecialFeature"]);
        Assert.Equal("true", query["HasThemeSong"]);
        Assert.Equal("true", query["HasThemeVideo"]);

        // The series status is stored by the filter dialog but, as in the original, never sent.
        Assert.False(query.ContainsKey("SeriesStatus"));
        Assert.True(filters.HasAny);
        Assert.False(new ListFilters { SeriesStatus = "Ended" }.HasAny);
    }

    [Fact]
    public void Standard_definition_wins_and_a_saved_genre_replaces_the_one_of_the_url()
    {
        var filters = new ListFilters { IsSD = true, IsHD = true, IsUnplayed = true, GenreIds = "g2" };

        var request = Request(Parameters("/list?type=Programs&genreId=g1"), filters: filters);

        var query = Query(request);
        Assert.Equal("false", query["IsHD"]);
        Assert.Equal("IsUnplayed", query["Filters"]);
        Assert.Equal("g2", query["GenreIds"]);
        Assert.Single(request.Query, entry => entry.Key == "GenreIds");
    }

    [Theory]
    [InlineData("#", "NameLessThan", "A")]
    [InlineData("C", "NameStartsWith", "C")]
    public void The_alphabet_picker_limits_the_names(string letter, string key, string value)
    {
        var query = Query(Request(Parameters("/list?parentId=p1"), Item("Folder"), alphabet: letter));

        Assert.Equal(value, query[key]);
    }

    [Fact]
    public void Filter_flags_are_read_and_written_by_name()
    {
        var filters = new ListFilters();
        foreach (var flag in ListFilters.Flags)
        {
            Assert.False(filters.Get(flag));
            filters = filters.With(flag, true);
            Assert.True(filters.Get(flag));
        }

        Assert.Equal(13, ListFilters.Flags.Count);
        Assert.False(filters.Get("Unknown"));
        Assert.Same(filters, filters.With("Unknown", true));
        Assert.Equal(new[] { "Dvd", "BluRay" }, ListFilters.Split("Dvd,BluRay"));
        Assert.Empty(ListFilters.Split(null));
        Assert.Empty(ListFilters.Split(string.Empty));
    }

    [Fact]
    public void Cards_of_a_folder_follow_the_view_settings()
    {
        var parameters = Parameters("/list?parentId=p1");

        var titled = ListPageModel.Cards(parameters, Item("Folder"), new ListViewSettings(true, true, "primary", "images"));
        var bare = ListPageModel.Cards(parameters, Item("Folder"), new ListViewSettings(false, true, "primary", "images"));

        Assert.Equal(CardShape.AutoVertical, titled.Shape);
        Assert.True(titled.ShowTitle.IsTruthy);
        Assert.True(titled.ShowYear);
        Assert.True(titled.ShowParentTitle);
        Assert.True(titled.CenterText);
        Assert.True(titled.CoverImage);
        Assert.False(titled.OverlayText);
        Assert.True(titled.OverlayMoreButton);
        Assert.False(titled.OverlayPlayButton);
        Assert.Equal(2, titled.Lines);
        Assert.Null(titled.Action);
        Assert.Null(titled.Context);

        Assert.False(bare.ShowTitle.IsTruthy);
        Assert.False(bare.ShowYear);
        Assert.True(bare.OverlayText);
        Assert.Equal(0, bare.Lines);
    }

    [Theory]
    [InlineData("banner", "banner", false, false, false)]
    [InlineData("disc", "square", true, false, false)]
    [InlineData("logo", "backdrop", false, true, false)]
    [InlineData("thumb", "backdrop", false, false, true)]
    public void The_image_type_sets_the_card_shape(string imageType, string shape, bool disc, bool logo, bool thumb)
    {
        var cards = ListPageModel.Cards(Parameters("/list?parentId=p1"), Item("Folder"), new ListViewSettings(true, true, imageType, "images"));

        Assert.Equal(shape, cards.Shape);
        Assert.Equal(disc, cards.PreferDisc);
        Assert.Equal(logo, cards.PreferLogo);
        Assert.Equal(thumb, cards.PreferThumb.IsTruthy);
    }

    [Fact]
    public void Cards_of_special_lists()
    {
        var settings = new ListViewSettings(true, true, "primary", "images");

        var songs = ListPageModel.Cards(Parameters("/list?type=Audio"), null, settings);
        var people = ListPageModel.Cards(Parameters("/list?type=Person"), null, settings);
        var artists = ListPageModel.Cards(Parameters("/list?type=MusicArtist"), null, settings);
        var nextUp = ListPageModel.Cards(Parameters("/list?type=nextup"), null, settings);
        var programs = ListPageModel.Cards(Parameters("/list?type=Programs"), null, settings);
        var moviePrograms = ListPageModel.Cards(Parameters("/list?type=Programs&IsMovie=true"), null, settings);
        var folders = ListPageModel.Cards(Parameters("/list?parentId=p1"), Item("CollectionFolder", "folders"), settings);

        Assert.Equal(ItemAction.PlayAllFromHere, songs.Action);
        Assert.False(people.ShowYear);
        Assert.False(people.ShowParentTitle);
        Assert.Equal(1, people.Lines);
        Assert.False(artists.ShowYear);
        Assert.Equal(1, artists.Lines);
        Assert.Equal(CardShape.Backdrop, nextUp.Shape);

        Assert.Equal(CardShape.AutoVertical, programs.Shape);
        Assert.True(programs.PreferThumb.IsAuto);
        Assert.Equal(CardShape.Backdrop, programs.DefaultShape);
        Assert.Equal("livetv", programs.Context);
        Assert.True(programs.ShowParentTitle);
        Assert.True(programs.ShowAirTime);
        Assert.False(programs.ShowYear);
        Assert.Equal(3, programs.Lines);

        Assert.Equal(CardShape.Portrait, moviePrograms.Shape);
        Assert.Equal(CardShape.Portrait, moviePrograms.DefaultShape);
        Assert.False(moviePrograms.ShowParentTitle);
        Assert.Equal(2, moviePrograms.Lines);

        Assert.Equal("folders", folders.Context);
    }

    [Fact]
    public void The_sort_order_is_saved_per_list()
    {
        var settings = new ListPageSettings(_app.Get<UserSettings>());
        var folder = Parameters("/list?parentId=p1");
        var movies = Parameters("/list?type=Movie&parentId=p1");

        Assert.Equal(new ListSort("IsFolder,SortName", "Ascending"), settings.GetSort(folder));
        Assert.Equal(new ListSort("SortName", "Ascending"), settings.GetSort(movies));

        settings.SetSort(folder, new ListSort("DateCreated,SortName", "Descending"));

        Assert.Equal(new ListSort("DateCreated,SortName", "Descending"), settings.GetSort(folder));
        Assert.True(settings.GetSort(folder).Descending);
        Assert.Equal(new ListSort("SortName", "Ascending"), settings.GetSort(movies));
        Assert.Equal("DateCreated,SortName", _app.Get<UserSettings>().Get("items-p1-Folder-sortby"));
        Assert.Equal("Descending", _app.Get<UserSettings>().Get("items-p1-Folder-sortorder"));
    }

    [Fact]
    public void Filters_are_saved_per_list()
    {
        var userSettings = _app.Get<UserSettings>();
        var settings = new ListPageSettings(userSettings);
        var folder = Parameters("/list?parentId=p1");

        Assert.Equal(new ListFilters(), settings.GetFilters(folder));

        var filters = new ListFilters { IsUnplayed = true, HasTrailer = true, VideoTypes = "Dvd", SeriesStatus = "Ended", GenreIds = "g1,g2" };
        settings.SetFilters(folder, filters);

        Assert.Equal(filters, settings.GetFilters(folder));
        Assert.Equal("true", userSettings.Get("items-p1-Folder-filter-IsUnplayed"));
        Assert.Equal("true", userSettings.Get("items-p1-Folder-filter-HasTrailer"));
        Assert.Null(userSettings.Get("items-p1-Folder-filter-IsPlayed"));
        Assert.Equal("Dvd", userSettings.Get("items-p1-Folder-filter-VideoTypes"));
        Assert.Equal("g1,g2", userSettings.Get("items-p1-Folder-filter-GenreIds"));
        Assert.Equal(new ListFilters(), settings.GetFilters(Parameters("/list?parentId=p2")));

        settings.SetFilters(folder, new ListFilters());

        Assert.Equal(new ListFilters(), settings.GetFilters(folder));
        Assert.Null(userSettings.Get("items-p1-Folder-filter-IsUnplayed"));
    }

    [Fact]
    public void View_settings_default_by_list_and_are_saved()
    {
        var userSettings = _app.Get<UserSettings>();
        var settings = new ListPageSettings(userSettings);
        var folder = Parameters("/list?parentId=p1");

        Assert.Equal(new ListViewSettings(true, true, "primary", "images"), settings.GetView(folder, Item("Folder")));
        Assert.False(settings.GetView(folder, Item("PhotoAlbum")).ShowTitle);
        Assert.False(settings.GetView(Parameters("/list?type=Movie"), null).ShowTitle);
        Assert.True(settings.GetView(Parameters("/list?type=Audio"), null).ShowTitle);

        settings.SetView(folder, false, "list");

        var saved = settings.GetView(folder, Item("Folder"));
        Assert.False(saved.ShowTitle);
        Assert.True(saved.IsList);
        Assert.Equal("false", userSettings.Get("items-p1-Folder-showTitle"));
        Assert.Equal("list", userSettings.Get("items-p1-Folder-imageType"));
    }

    [Fact]
    public void Next_up_shows_thumbs_unless_episode_images_are_preferred()
    {
        var userSettings = _app.Get<UserSettings>();
        var settings = new ListPageSettings(userSettings);
        var nextUp = Parameters("/list?type=nextup");

        Assert.Equal("thumb", settings.GetView(nextUp, null).ImageType);

        userSettings.UseEpisodeImagesInNextUpAndResume = true;

        Assert.Equal("primary", settings.GetView(nextUp, null).ImageType);
        Assert.True(settings.GetView(nextUp, null).ShowTitle);
    }
}
