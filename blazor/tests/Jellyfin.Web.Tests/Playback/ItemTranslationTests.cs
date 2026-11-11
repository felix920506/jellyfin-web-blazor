using System.Text.Json.Nodes;
using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services.Playback;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Playback;

/// <summary>
/// translateItemsForPlayback(): what is asked of the server to turn an album, an artist, a series,
/// an episode and so on into the items of the queue.
/// </summary>
public class ItemTranslationTests
{
    private const string FolderId = "f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0";
    private const string SeriesId = "5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e5e";
    private const string SeasonId = "5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a5a";
    private const string FirstEpisodeId = "e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1";
    private const string SecondEpisodeId = "e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2e2";
    private const string ThirdEpisodeId = "e3e3e3e3e3e3e3e3e3e3e3e3e3e3e3e3";

    private static BaseItemDto Folder(BaseItemDto_Type type, string name = "First Album") => new()
    {
        Id = Guid.Parse(FolderId),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = type,
        IsFolder = true,
    };

    private static BaseItemDto[] Episodes(bool firstPlayed = false, bool secondPlayed = false) =>
    [
        PlaybackServer.Episode(FirstEpisodeId, "Pilot", 1, firstPlayed),
        PlaybackServer.Episode(SecondEpisodeId, "The Second One", 2, secondPlayed),
        PlaybackServer.Episode(ThirdEpisodeId, "The Third One", 3),
    ];

    private static JsonObject Result(IEnumerable<BaseItemDto> items) => new()
    {
        ["Items"] = new JsonArray(items.Select(PlaybackServer.ToJson).ToArray()),
    };

    /// <summary>Answers every Items query that is not by id with the songs of the album.</summary>
    private static async Task<PlaybackHarness> WithFolderAsync(BaseItemDto folder)
    {
        var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet($"/Users/{PlaybackServer.UserId}/Items/{FolderId}", _ => PlaybackServer.ToJson(folder));
        harness.Server.OnGet("/Items", _ => Result(PlaybackServer.Songs()));
        return harness;
    }

    /// <summary>A series whose episode listing and next-up answer are scripted; every episode can be played.</summary>
    private static async Task<PlaybackHarness> WithSeriesAsync(BaseItemDto[] episodes, BaseItemDto? nextUp = null)
    {
        var harness = await PlaybackHarness.WithMediaAsync();
        var server = harness.Server;
        var series = Folder(BaseItemDto_Type.Series, "Test Show");
        series.Id = Guid.Parse(SeriesId);
        server.OnGet($"/Users/{PlaybackServer.UserId}/Items/{SeriesId}", _ => PlaybackServer.ToJson(series));
        server.OnGet("/Shows/NextUp", _ => Result(nextUp is null ? [] : [nextUp]));
        server.OnGet($"/Shows/{SeriesId}/Episodes", _ => Result(episodes));
        PlaybackServer.ScriptItems(server, episodes);
        foreach (var id in new[] { FirstEpisodeId, SecondEpisodeId, ThirdEpisodeId })
        {
            PlaybackServer.ScriptPlaybackInfo(server, id);
        }

        return harness;
    }

    private static RecordedRequest ItemsQuery(PlaybackHarness harness) => Assert.Single(harness.Server.RequestsTo("/Items"));

    [Fact]
    public async Task An_album_is_expanded_to_its_tracks()
    {
        await using var harness = await WithFolderAsync(Folder(BaseItemDto_Type.MusicAlbum));

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [FolderId] });

        var query = ItemsQuery(harness);
        Assert.Equal(FolderId, query.Query("parentId"));
        Assert.Equal("IsNotFolder", query.Query("filters"));
        Assert.Equal("true", query.Query("recursive"));
        Assert.Equal("SortName", query.Query("sortBy"));
        Assert.Equal("Audio,Video", query.Query("mediaTypes"));
        Assert.Equal(PlaybackServer.UserId, query.Query("userId"));
        Assert.Equal("Chapters,MediaSources,Trickplay", query.Query("fields"));
        Assert.Equal("Virtual", query.Query("excludeLocationTypes"));
        Assert.Equal("false", query.Query("enableTotalRecordCount"));
        Assert.Equal("false", query.Query("collapseBoxSetItems"));
        Assert.Equal("300", query.Query("limit"));
        Assert.Null(query.Query("ids"));

        Assert.Equal(new[] { "Opening", "Middle", "Closing" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);
        Assert.True(harness.Manager.IsPlayingAudio);
    }

    [Fact]
    public async Task Shuffling_asks_the_server_for_a_random_order()
    {
        var album = Folder(BaseItemDto_Type.MusicAlbum);
        await using var harness = await WithFolderAsync(album);

        await harness.Manager.ShuffleAsync(album);

        Assert.Equal("Random", ItemsQuery(harness).Query("sortBy"));
        Assert.Equal(3, harness.Manager.GetPlaylist().Count);
    }

    [Fact]
    public async Task A_music_library_plays_in_album_order()
    {
        var library = Folder(BaseItemDto_Type.CollectionFolder, "Music");
        library.CollectionType = BaseItemDto_CollectionType.Music;
        await using var harness = await WithFolderAsync(library);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [library] });

        Assert.Equal("Album,ParentIndexNumber,IndexNumber,SortName", ItemsQuery(harness).Query("sortBy"));
    }

    [Fact]
    public async Task A_collection_keeps_the_order_of_the_server()
    {
        await using var harness = await WithFolderAsync(Folder(BaseItemDto_Type.BoxSet, "Collection"));

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [FolderId] });

        var query = ItemsQuery(harness);
        Assert.Null(query.Query("sortBy"));
        Assert.Equal(FolderId, query.Query("parentId"));
    }

    [Fact]
    public async Task An_artist_is_expanded_to_the_songs_in_album_order()
    {
        var artist = Folder(BaseItemDto_Type.MusicArtist, "Test Artist");
        artist.IsFolder = false;
        await using var harness = await WithFolderAsync(artist);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [artist] });

        var query = ItemsQuery(harness);
        Assert.Equal(FolderId, query.Query("artistIds"));
        Assert.Null(query.Query("parentId"));
        Assert.Equal("true", query.Query("recursive"));
        Assert.Equal("Album,ParentIndexNumber,IndexNumber,SortName", query.Query("sortBy"));
        Assert.Equal("Audio", query.Query("mediaTypes"));
        Assert.Equal("IsNotFolder", query.Query("filters"));
    }

    [Fact]
    public async Task A_playlist_plays_in_its_own_order_unless_shuffled()
    {
        var playlist = Folder(BaseItemDto_Type.Playlist, "Mix");
        await using var harness = await WithFolderAsync(playlist);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [playlist] });
        var query = ItemsQuery(harness);
        Assert.Equal(FolderId, query.Query("parentId"));
        Assert.Null(query.Query("sortBy"));
        Assert.Null(query.Query("recursive"));

        await harness.Manager.ShuffleAsync(playlist);
        Assert.Equal("Random", harness.Server.RequestsTo("/Items")[^1].Query("sortBy"));
    }

    [Theory]
    [InlineData(BaseItemDto_Type.MusicGenre, "genreIds", "Audio")]
    [InlineData(BaseItemDto_Type.Genre, "genreIds", "Video")]
    [InlineData(BaseItemDto_Type.Studio, "studioIds", "Video")]
    [InlineData(BaseItemDto_Type.Person, "personIds", "Video")]
    public async Task Genres_studios_and_people_play_what_belongs_to_them(BaseItemDto_Type type, string parameter, string mediaTypes)
    {
        var item = Folder(type, "Something");
        item.IsFolder = false;
        await using var harness = await WithFolderAsync(item);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [item] });

        var query = ItemsQuery(harness);
        Assert.Equal(FolderId, query.Query(parameter));
        Assert.Equal(mediaTypes, query.Query("mediaTypes"));
        Assert.Equal("true", query.Query("recursive"));
        Assert.Equal("SortName", query.Query("sortBy"));
    }

    [Fact]
    public async Task The_filters_of_the_list_being_played_replace_the_defaults_but_folders_stay_out()
    {
        var album = Folder(BaseItemDto_Type.MusicAlbum);
        await using var harness = await WithFolderAsync(album);

        await harness.Manager.PlayAsync(new PlayOptions
        {
            Items = [album],
            QueryOptions = new Dictionary<string, string>
            {
                ["Filters"] = "IsFavorite",
                ["SortBy"] = "DateCreated",
                ["SortOrder"] = "Descending",
                ["Bogus"] = "x",
                ["Limit"] = string.Empty,
            },
        });

        var query = ItemsQuery(harness);
        Assert.Equal("IsFavorite,IsNotFolder", query.Query("filters"));
        Assert.Equal("DateCreated", query.Query("sortBy"));
        Assert.Equal("Descending", query.Query("sortOrder"));
        Assert.Equal("300", query.Query("limit"));
        Assert.Null(query.Query("Bogus"));
        Assert.Null(query.Query("bogus"));
    }

    [Fact]
    public void A_legacy_query_is_mapped_by_name_whatever_its_casing()
    {
        var request = PlaybackManager.ToItemsRequest(new Dictionary<string, string>
        {
            ["SORTBY"] = "Name",
            ["parentid"] = "abc",
            ["IncludeItemTypes"] = "Movie",
            ["NoSuchParameter"] = "1",
            ["Recursive"] = string.Empty,
        });

        Assert.Equal(new[] { "sortBy", "parentId", "includeItemTypes" }, request.Select(p => p.Key));
        Assert.Equal("Name", request["sortBy"]);
        Assert.Equal("abc", request["parentId"]);
        Assert.Equal("Movie", request["includeItemTypes"]);
        Assert.Empty(PlaybackManager.ToItemsRequest(null));
    }

    [Fact]
    public async Task Several_ids_are_fetched_together_and_played_in_the_order_asked()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        // The server answers in its own order (first movie, second movie).
        await harness.Manager.PlayAsync(new PlayOptions { Ids = [PlaybackServer.SecondMovieId, PlaybackServer.MovieId] });

        var query = ItemsQuery(harness);
        Assert.Equal(PlaybackServer.SecondMovieId + "," + PlaybackServer.MovieId, query.Query("ids"));
        Assert.Equal("300", query.Query("limit"));
        Assert.Equal(new[] { "Second Movie", "Test Movie" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Second Movie", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task Long_id_lists_are_split_over_several_requests()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var ids = Enumerable.Range(0, 85).Select(i => Guid.NewGuid().ToString("N")).ToList();

        await harness.Manager.GetItemsForPlaybackAsync(new PlaybackManager.ItemsQuery { ["ids"] = ids });

        var requests = harness.Server.RequestsTo("/Items");
        Assert.Equal(new[] { 40, 40, 5 }, requests.Select(r => r.Query("ids")!.Split(',').Length));
        Assert.Equal(ids, requests.SelectMany(r => r.Query("ids")!.Split(',')));
        Assert.All(requests, r => Assert.Equal("300", r.Query("limit")));
    }

    [Fact]
    public async Task A_series_starts_at_the_next_episode_to_watch()
    {
        var episodes = Episodes();
        await using var harness = await WithSeriesAsync(episodes[1..], nextUp: episodes[1]);

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [SeriesId] });

        var nextUp = Assert.Single(harness.Server.RequestsTo("/Shows/NextUp"));
        Assert.Equal(SeriesId, nextUp.Query("SeriesId"));
        Assert.Equal(PlaybackServer.UserId, nextUp.Query("UserId"));

        var listing = Assert.Single(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes"));
        Assert.Equal("false", listing.Query("IsVirtualUnaired"));
        Assert.Equal("false", listing.Query("IsMissing"));
        Assert.Equal("100", listing.Query("limit"));
        Assert.Equal(SecondEpisodeId, listing.Query("startItemId"));
        Assert.Equal(PlaybackServer.UserId, listing.Query("UserId"));
        Assert.Equal("Chapters,MediaSources,Trickplay", listing.Query("Fields"));
        Assert.Null(listing.Query("SeasonId"));
        Assert.Null(listing.Query("SortBy"));

        Assert.Equal("The Second One", harness.Manager.CurrentItem?.Name);
        Assert.Equal(new[] { "The Second One", "The Third One" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.True(harness.Manager.IsPlayingVideo);
    }

    [Fact]
    public async Task Without_a_next_up_answer_a_series_starts_at_its_first_unplayed_episode()
    {
        await using var harness = await WithSeriesAsync(Episodes(firstPlayed: true, secondPlayed: true));

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [SeriesId] });

        Assert.Null(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes")[0].Query("startItemId"));
        Assert.Equal("The Third One", harness.Manager.CurrentItem?.Name);
        Assert.Equal(2, harness.Manager.CurrentPlaylistIndex);
        Assert.Equal(3, harness.Manager.GetPlaylist().Count);

        // Not the first item of the list: no intros.
        Assert.DoesNotContain(harness.Server.Requests, r => r.Path.EndsWith("/Intros", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_shuffled_series_asks_for_random_episodes_and_not_for_the_next_one()
    {
        await using var harness = await WithSeriesAsync(Episodes(firstPlayed: true));
        var series = Folder(BaseItemDto_Type.Series, "Test Show");
        series.Id = Guid.Parse(SeriesId);

        await harness.Manager.ShuffleAsync(series);

        Assert.Empty(harness.Server.RequestsTo("/Shows/NextUp"));
        var listing = Assert.Single(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes"));
        Assert.Equal("Random", listing.Query("SortBy"));
        Assert.Null(listing.Query("startItemId"));
        Assert.Equal("Pilot", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task A_season_plays_its_own_episodes()
    {
        await using var harness = await WithSeriesAsync(Episodes(firstPlayed: true));
        var season = Folder(BaseItemDto_Type.Season, "Season 1");
        season.Id = Guid.Parse(SeasonId);
        season.SeriesId = Guid.Parse(SeriesId);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [season] });

        Assert.Empty(harness.Server.RequestsTo("/Shows/NextUp"));
        var listing = Assert.Single(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes"));
        Assert.Equal(SeasonId, listing.Query("SeasonId"));
        Assert.Null(listing.Query("limit"));
        Assert.Equal("The Second One", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task An_episode_is_followed_by_the_rest_of_its_series()
    {
        var episodes = Episodes();
        await using var harness = await WithSeriesAsync(episodes);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [episodes[1]] });

        var listing = Assert.Single(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes"));
        Assert.Equal(SecondEpisodeId, listing.Query("startItemId"));
        Assert.Equal("100", listing.Query("limit"));
        Assert.Equal("false", listing.Query("IsMissing"));
        Assert.Empty(harness.Server.RequestsTo("/Shows/NextUp"));

        Assert.Equal("The Second One", harness.Manager.CurrentItem?.Name);
        Assert.Equal(1, harness.Manager.CurrentPlaylistIndex);
        Assert.Equal(3, harness.Manager.GetPlaylist().Count);
    }

    [Fact]
    public async Task Several_episodes_are_played_as_they_are()
    {
        var episodes = Episodes();
        await using var harness = await WithSeriesAsync(episodes);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [episodes[2], episodes[0]] });

        Assert.Empty(harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes"));
        Assert.Equal(new[] { "The Third One", "Pilot" }, harness.Manager.GetPlaylist().Select(i => i.Name));
    }

    [Fact]
    public async Task Another_version_of_an_episode_plays_as_a_source_of_the_listed_episode()
    {
        const string versionId = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeee9";
        await using var harness = await WithSeriesAsync(Episodes());
        PlaybackServer.ScriptPlaybackInfo(harness.Server, FirstEpisodeId, direct: PlaybackServer.MediaSource(versionId));
        var version = PlaybackServer.Episode(versionId, "Pilot (4K)", 1);
        PlaybackServer.ScriptItems(harness.Server, version);

        await harness.Manager.PlayAsync(new PlayOptions { Items = [version] });

        Assert.Equal(versionId, harness.Server.RequestsTo($"/Shows/{SeriesId}/Episodes")[0].Query("startItemId"));
        var negotiation = Assert.Single(PlaybackServer.PlaybackInfoRequests(harness.Server, FirstEpisodeId));
        Assert.Equal(versionId, (string?)negotiation["MediaSourceId"]);
        Assert.Equal("Pilot", harness.Manager.CurrentItem?.Name);
        Assert.Equal(0, harness.Manager.CurrentPlaylistIndex);
    }

    [Fact]
    public async Task A_movie_in_parts_is_followed_by_its_other_parts()
    {
        const string partId = "33333333333333333333333333333333";
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var movie = PlaybackServer.Movie();
        movie.PartCount = 2;
        harness.Server.OnGet($"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}", _ => PlaybackServer.ToJson(movie));
        harness.Server.OnGet($"/Videos/{PlaybackServer.MovieId}/AdditionalParts", _ => Result([PlaybackServer.Movie(partId, "Test Movie, part 2")]));

        await harness.PlayMovieAsync();

        var parts = Assert.Single(harness.Server.RequestsTo($"/Videos/{PlaybackServer.MovieId}/AdditionalParts"));
        Assert.Equal(PlaybackServer.UserId, parts.Query("userId"));
        Assert.Equal(new[] { "Test Movie", "Test Movie, part 2" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Test Movie", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task Cinema_mode_intros_are_played_before_the_movie()
    {
        const string introId = "44444444444444444444444444444444";
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet(
            $"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}/Intros",
            _ => Result([PlaybackServer.Movie(introId, "Trailer")]));
        PlaybackServer.ScriptItems(harness.Server, PlaybackServer.Movie(introId, "Trailer"));
        PlaybackServer.ScriptPlaybackInfo(harness.Server, introId);

        await harness.PlayMovieAsync();

        Assert.Equal(new[] { "Trailer", "Test Movie" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Trailer", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task An_empty_folder_shows_that_there_is_nothing_to_play()
    {
        await using var harness = await WithFolderAsync(Folder(BaseItemDto_Type.MusicAlbum));
        harness.Server.OnGet("/Items", _ => Result([]));

        await harness.Manager.PlayAsync(new PlayOptions { Ids = [FolderId] });

        Assert.Equal(1, harness.DialogCount);
        Assert.False(harness.Manager.IsPlaying);
        Assert.Empty(harness.Player.Loads);
    }

    [Fact]
    public async Task Queueing_expands_like_playing_and_adds_at_the_end_or_after_the_current_item()
    {
        var album = Folder(BaseItemDto_Type.MusicAlbum);
        await using var harness = await WithFolderAsync(album);
        var single = PlaybackServer.Song("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa4", "Single");
        var other = PlaybackServer.Song("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa5", "Other");
        PlaybackServer.ScriptItems(harness.Server, single, other);
        await harness.Manager.PlayAsync(new PlayOptions { Items = [single, other] });

        await harness.Manager.QueueAsync(new PlayOptions { Ids = [FolderId] });
        Assert.Equal(new[] { "Single", "Other", "Opening", "Middle", "Closing" }, harness.Manager.GetPlaylist().Select(i => i.Name));

        await harness.Manager.QueueNextAsync(new PlayOptions { Items = [PlaybackServer.Song("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa6", "Next")] });
        Assert.Equal(new[] { "Single", "Next", "Other", "Opening", "Middle", "Closing" }, harness.Manager.GetPlaylist().Select(i => i.Name));
        Assert.Equal("Single", harness.Manager.CurrentItem?.Name);
        Assert.Single(harness.Player.Loads);
    }

    [Fact]
    public async Task Queueing_with_nothing_playing_starts_playback()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();

        await harness.Manager.QueueAsync(new PlayOptions { Items = PlaybackServer.Songs() });

        Assert.True(harness.Manager.IsPlayingAudio);
        Assert.Equal("Opening", harness.Manager.CurrentItem?.Name);
    }

    [Fact]
    public async Task An_instant_mix_plays_what_the_server_puts_together()
    {
        await using var harness = await PlaybackHarness.WithMediaAsync();
        harness.Server.OnGet($"/Items/{PlaybackServer.SongId}/InstantMix", _ => Result(Enumerable.Reverse(PlaybackServer.Songs())));

        await harness.Manager.InstantMixAsync(PlaybackServer.Song());

        var request = Assert.Single(harness.Server.RequestsTo($"/Items/{PlaybackServer.SongId}/InstantMix"));
        Assert.Equal(PlaybackServer.UserId, request.Query("UserId"));
        Assert.Equal("200", request.Query("Limit"));
        Assert.Equal(new[] { "Closing", "Middle", "Opening" }, harness.Manager.GetPlaylist().Select(i => i.Name));
    }

    [Fact]
    public async Task Local_trailers_are_fetched_and_played()
    {
        const string trailerId = "77777777777777777777777777777777";
        await using var harness = await PlaybackHarness.WithMediaAsync();
        var movie = PlaybackServer.Movie();
        movie.LocalTrailerCount = 1;
        harness.Server.OnGet(
            $"/Users/{PlaybackServer.UserId}/Items/{PlaybackServer.MovieId}/LocalTrailers",
            _ => new JsonArray(PlaybackServer.ToJson(PlaybackServer.Movie(trailerId, "Trailer"))));
        PlaybackServer.ScriptItems(harness.Server, PlaybackServer.Movie(trailerId, "Trailer"));
        PlaybackServer.ScriptPlaybackInfo(harness.Server, trailerId);

        await harness.Manager.PlayTrailersAsync(movie);

        Assert.Equal("Trailer", harness.Manager.CurrentItem?.Name);
        Assert.True(harness.Manager.IsPlayingVideo);
    }
}
