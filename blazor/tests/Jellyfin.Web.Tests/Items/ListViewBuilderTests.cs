using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Items;

/// <summary>The rows of a list (listview.js getListViewHtml).</summary>
public class ListViewBuilderTests : ItemsTestContext
{
    private ListViewBuilder Builder => Get<ListViewBuilder>();

    private static BaseItemDto Episode(int number, string name) => TestItems.Item(new
    {
        Id = TestItems.Id(number),
        Name = name,
        ServerId = TestApp.ServerId,
        Type = "Episode",
        MediaType = "Video",
        SeriesName = "Series",
        SeriesId = TestItems.Id(90),
        SeriesPrimaryImageTag = "seriestag",
        IndexNumber = number,
        ParentIndexNumber = 1,
        UserData = new { Played = false, PlaybackPositionTicks = 0 }
    });

    [Theory]
    [InlineData("SortName", "alpha", "A")]
    [InlineData("SortName,ProductionYear", "zulu", "Z")]
    [InlineData("sortname", "2001", "#")]
    [InlineData("SortName", "Émile", "#")]
    [InlineData("OfficialRating", "alpha", "PG-13")]
    [InlineData("CommunityRating,SortName", "alpha", "7")]
    [InlineData("CriticRating", "alpha", "85")]
    [InlineData("AlbumArtist", "alpha", "B")]
    [InlineData("DateCreated", "alpha", "")]
    [InlineData(null, "alpha", "")]
    public void Group_titles_follow_the_sort_order(string? sortBy, string sortName, string expected)
    {
        var item = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Name = "Name",
            SortName = sortName,
            Type = "Movie",
            OfficialRating = "PG-13",
            CommunityRating = 7.8,
            CriticRating = 85.5,
            AlbumArtist = "band"
        });

        Assert.Equal(expected, Builder.GetIndex(item, new ListViewOptions { SortBy = sortBy }));
    }

    [Fact]
    public void Group_titles_for_discs_unrated_items_and_episodes()
    {
        var song = TestItems.Item(new { Id = TestItems.Id(1), Name = "Song", Type = "Audio", ParentIndexNumber = 2 });
        var plain = TestItems.Item(new { Id = TestItems.Id(2), Name = "movie", Type = "Movie" });

        Assert.Equal("Disc 2", Builder.GetIndex(song, new ListViewOptions { Index = "disc", SortBy = "SortName" }));
        Assert.Equal(string.Empty, Builder.GetIndex(plain, new ListViewOptions { Index = "disc" }));
        Assert.Equal("Unrated", Builder.GetIndex(plain, new ListViewOptions { SortBy = "OfficialRating" }));
        Assert.Equal("Unrated", Builder.GetIndex(plain, new ListViewOptions { SortBy = "CommunityRating" }));
        Assert.Equal("Unrated", Builder.GetIndex(plain, new ListViewOptions { SortBy = "CriticRating" }));
        Assert.Equal(string.Empty, Builder.GetIndex(plain, new ListViewOptions { SortBy = "AlbumArtist" }));
        Assert.Equal("M", Builder.GetIndex(plain, new ListViewOptions { SortBy = "SortName" }));
        Assert.Equal(string.Empty, Builder.GetIndex(Episode(1, "Pilot"), new ListViewOptions { SortBy = "SortName" }));
    }

    [Fact]
    public void Group_headers_appear_where_the_group_changes()
    {
        var items = new[] { "Alien", "Aliens", "Brazil", "Casablanca", "Cabaret" }
            .Select((name, index) => TestItems.Item(new { Id = TestItems.Id(index + 1), Name = name, Type = "Movie" }))
            .ToArray();

        var grouped = Builder.Build(items, new ListViewOptions { ShowIndex = true, SortBy = "SortName" });
        var plain = Builder.Build(items, new ListViewOptions { SortBy = "SortName" });

        Assert.Equal(new string?[] { "A", null, "B", "C", null }, grouped.Select(row => row.GroupTitle));
        Assert.All(plain, row => Assert.Null(row.GroupTitle));
        Assert.Same(items[2], grouped[2].Item);
    }

    [Fact]
    public void Episode_lines()
    {
        var episode = Episode(3, "Pilot");

        Assert.Equal(new[] { "S1:E3 - Pilot" }, Builder.GetTextLines(episode, new ListViewOptions()));
        Assert.Equal(new[] { "Series", "S1:E3 - Pilot" }, Builder.GetTextLines(episode, new ListViewOptions { ShowParentTitle = true }));
        Assert.Equal(
            new[] { "Series - S1:E3 - Pilot" },
            Builder.GetTextLines(episode, new ListViewOptions { ShowParentTitle = true, ParentTitleWithTitle = true }));
        Assert.Equal(new[] { "3. Pilot" }, Builder.GetTextLines(episode, new ListViewOptions { IncludeParentInfoInTitle = false }));
    }

    [Fact]
    public void Song_and_album_lines()
    {
        var song = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Name = "Song",
            Type = "Audio",
            MediaType = "Audio",
            IndexNumber = 4,
            ArtistItems = new[] { new { Name = "One", Id = TestItems.Id(2) }, new { Name = "Two", Id = TestItems.Id(3) } }
        });
        var album = TestItems.Item(new { Id = TestItems.Id(5), Name = "Album", Type = "MusicAlbum", IsFolder = true, AlbumArtist = "Band" });

        Assert.Equal(new[] { "Song" }, Builder.GetTextLines(song, new ListViewOptions()));
        Assert.Equal(new[] { "4. Song", "One, Two" }, Builder.GetTextLines(song, new ListViewOptions { ShowIndexNumber = true, Artist = true }));
        Assert.Equal(new[] { "", "Song" }, Builder.GetTextLines(song, new ListViewOptions { ShowParentTitle = true }));
        Assert.Equal(new[] { "Album", "Band" }, Builder.GetTextLines(album, new ListViewOptions()));
        Assert.Equal(new[] { "Album" }, Builder.GetTextLines(album, new ListViewOptions { Artist = false }));
    }

    [Fact]
    public void Channel_and_programme_lines()
    {
        var channel = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Name = "One",
            Type = "TvChannel",
            ChannelNumber = "101",
            CurrentProgram = new { Id = TestItems.Id(2), Name = "News", Type = "Program" }
        });
        var program = TestItems.Item(new
        {
            Id = TestItems.Id(3),
            Name = "Show",
            Type = "Program",
            ChannelName = "One",
            EpisodeTitle = "The One",
            IsSeries = true,
            StartDate = "2024-05-17T18:30:00.0000000Z"
        });

        Assert.Equal(new[] { "101 One", "News" }, Builder.GetTextLines(channel, new ListViewOptions()));
        Assert.Equal(
            new[] { "One", "Show", "The One" },
            Builder.GetTextLines(program, new ListViewOptions { ShowChannel = true, ShowParentTitle = true }));
        Assert.Equal(3, Builder.GetTextLines(program, new ListViewOptions { ShowProgramTime = true, ShowProgramDateTime = true }).Count);
    }

    [Fact]
    public void Row_classes_attributes_and_buttons()
    {
        var movie = TestItems.Item(TestItems.Movie(1, "Movie", new { Played = true, IsFavorite = false, PlaybackPositionTicks = 600 }));
        movie.PlaylistItemId = "entry1";

        var row = Assert.Single(Builder.Build([movie], new ListViewOptions { CollectionId = "c1", PlaylistId = "pl1" }));
        var bare = Assert.Single(Builder.Build([movie], new ListViewOptions
        {
            Highlight = false,
            ImageSize = "large",
            EnableOverview = true,
            Action = ItemAction.Play,
            ImagePlayButton = true,
            EnableUserDataButtons = false
        }));
        var bordered = Assert.Single(Builder.Build([movie], new ListViewOptions { Highlight = false, Border = true, Image = false, EnablePlayedButton = false }));

        Assert.Equal("listItem listItem-border", row.CssClass);
        Assert.Equal(ItemAction.Link, row.Action);
        Assert.Equal(ItemAction.Link, row.ImageAction);
        Assert.Equal("link", row.Attributes["data-action"]);
        Assert.Equal("entry1", row.Attributes["data-playlistitemid"]);
        Assert.Equal(TestItems.Id(1), row.Attributes["data-id"]);
        Assert.Equal("Movie", row.Attributes["data-type"]);
        Assert.Equal("Video", row.Attributes["data-mediatype"]);
        Assert.Equal("false", row.Attributes["data-isfolder"]);
        Assert.Equal("600", row.Attributes["data-positionticks"]);
        Assert.Equal("c1", row.Attributes["data-collectionid"]);
        Assert.Equal("pl1", row.Attributes["data-playlistid"]);
        Assert.Equal("listItemImage itemAction", row.ImageClass);
        Assert.Equal("listItemBody itemAction", row.BodyClass);
        Assert.True(row.PlayedButton);
        Assert.True(row.RatingButton);
        Assert.NotNull(row.SideMediaInfoHtml);
        Assert.Null(row.BodyMediaInfoHtml);
        Assert.Null(row.Overview);

        Assert.Equal("listItem listItem-largeImage listItem-withContentWrapper", bare.CssClass);
        Assert.Equal("play", bare.Action);
        Assert.Equal("link", bare.ImageAction);
        Assert.Equal("listItemImage listItemImage-large itemAction", bare.ImageClass);
        Assert.False(bare.PlayedButton);
        Assert.False(bare.RatingButton);

        Assert.Equal("listItem listItem-border", bordered.CssClass);
        Assert.Null(bordered.ImageClass);
        Assert.Null(bordered.ImageUrl);
        Assert.Equal("listItemBody itemAction listItemBody-noleftpadding", bordered.BodyClass);
        Assert.False(bordered.PlayedButton);
        Assert.True(bordered.RatingButton);
    }

    [Fact]
    public void Overview_and_media_info_placement()
    {
        var movie = TestItems.Item(new { Id = TestItems.Id(1), Name = "Movie", Type = "Movie", MediaType = "Video", Overview = "A story.", ProductionYear = 1999 });

        var row = Assert.Single(Builder.Build([movie], new ListViewOptions { EnableOverview = true, EnableSideMediaInfo = false }));
        var none = Assert.Single(Builder.Build([movie], new ListViewOptions { MediaInfo = false }));

        Assert.Equal("A story.", row.Overview);
        Assert.Contains("1999", row.BodyMediaInfoHtml, StringComparison.Ordinal);
        Assert.Null(row.SideMediaInfoHtml);
        Assert.Null(none.BodyMediaInfoHtml);
        Assert.Null(none.SideMediaInfoHtml);
        Assert.False(row.RatingButton);
    }

    [Fact]
    public void Small_icon_uses_the_compact_song_image_class()
    {
        var song = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Name = "Song",
            Type = "Audio",
            ImageTags = new { Primary = "tag" }
        });

        var row = Assert.Single(Builder.Build([song], new ListViewOptions { SmallIcon = true }));

        Assert.Equal("listItemIcon listItemIcon-small itemAction", row.ImageClass);
    }

    [Fact]
    public void Row_image_falls_back_to_album_series_and_parent()
    {
        SignIn();
        var own = TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", ImageTags = new { Primary = "own" }, AlbumId = TestItems.Id(2), AlbumPrimaryImageTag = "album" });
        var album = TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", AlbumId = TestItems.Id(2), AlbumPrimaryImageTag = "album" });
        var parent = TestItems.Item(new { Id = TestItems.Id(1), Type = "Video", ParentPrimaryImageItemId = TestItems.Id(4), ParentPrimaryImageTag = "parent" });
        var program = TestItems.Item(new { Id = TestItems.Id(1), Type = "Program", ChannelId = TestItems.Id(9), ChannelPrimaryImageTag = "channel" });
        var none = TestItems.Item(new { Id = TestItems.Id(1), Name = "Nothing", Type = "Video" });

        Assert.Equal(
            $"{TestApp.ServerUrl}/Items/{TestItems.Id(1)}/Images/Primary?fillWidth=80&fillHeight=80&tag=own&quality=90",
            Builder.GetImageUrl(own, 80));
        Assert.StartsWith($"{TestApp.ServerUrl}/Items/{TestItems.Id(2)}/Images/Primary?", Builder.GetImageUrl(album, 80), StringComparison.Ordinal);
        Assert.Contains("&tag=album&", Builder.GetImageUrl(album, 80), StringComparison.Ordinal);
        Assert.StartsWith($"{TestApp.ServerUrl}/Items/{TestItems.Id(90)}/Images/Primary?", Builder.GetImageUrl(Episode(1, "Pilot"), 500), StringComparison.Ordinal);
        Assert.Contains("fillWidth=500", Builder.GetImageUrl(Episode(1, "Pilot"), 500), StringComparison.Ordinal);
        Assert.StartsWith($"{TestApp.ServerUrl}/Items/{TestItems.Id(4)}/Images/Primary?", Builder.GetImageUrl(parent, 80), StringComparison.Ordinal);
        Assert.StartsWith($"{TestApp.ServerUrl}/Items/{TestItems.Id(9)}/Images/Primary?", Builder.GetImageUrl(program, 80, channel: true), StringComparison.Ordinal);
        Assert.Null(Builder.GetImageUrl(none, 80, channel: true));
        Assert.Null(Builder.GetImageUrl(none, 80));

        var row = Assert.Single(Builder.Build([none], new ListViewOptions()));
        var channelRow = Assert.Single(Builder.Build([program], new ListViewOptions { ImageSource = "channel" }));

        Assert.Null(row.ImageUrl);
        Assert.StartsWith("listItemImage itemAction cardImageContainer defaultCardBackground defaultCardBackground", row.ImageClass, StringComparison.Ordinal);
        Assert.Equal("listItemImage listItemImage-channel itemAction", channelRow.ImageClass);
        Assert.NotNull(channelRow.ImageUrl);
        Assert.False(channelRow.PlayedButton);
        Assert.False(channelRow.RatingButton);
    }
}
