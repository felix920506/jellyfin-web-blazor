using Jellyfin.Sdk.Generated.Models;
using Jellyfin.Web.Interop;
using Jellyfin.Web.Services;
using Jellyfin.Web.Tests.Support;

namespace Jellyfin.Web.Tests.Items;

/// <summary>Which image a card gets (cardbuilder/utils/url.ts) and how its URL is sized.</summary>
public class CardImagesTests : ItemsTestContext
{
    private const string Server = TestApp.ServerUrl;

    private CardImages Images => Get<CardImages>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    [Fact]
    public void A_poster_fills_a_portrait_card()
    {
        var item = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Movie",
            PrimaryImageAspectRatio = 0.6666666667,
            ImageTags = new { Primary = "ptag" },
            ImageBlurHashes = new { Primary = new { ptag = "LEHV6nWB2yk8" } }
        });

        var image = Images.GetCardImage(item, new CardOptions { Width = 200 }, CardShape.Portrait);

        Assert.Equal($"{Server}/Items/{TestItems.Id(1)}/Images/Primary?fillHeight=300&fillWidth=200&quality=96&tag=ptag", image.Url);
        Assert.Equal("LEHV6nWB2yk8", image.BlurHash);
        Assert.True(image.CoverImage);
        Assert.False(image.ForceName);
    }

    [Fact]
    public void The_height_comes_from_the_shape_when_the_image_ratio_is_unknown()
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", ImageTags = new { Primary = "ptag" } });

        var image = Images.GetCardImage(item, new CardOptions { Width = 320 }, CardShape.Backdrop);

        Assert.Contains("fillHeight=180&fillWidth=320", image.Url, StringComparison.Ordinal);
        Assert.False(image.CoverImage);
    }

    [Fact]
    public void Sizes_follow_the_device_pixel_ratio()
    {
        App.Browser.Environment = App.Browser.Environment with { DevicePixelRatio = 2 };
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", ImageTags = new { Primary = "ptag" } });

        var image = Images.GetCardImage(item, new CardOptions { Width = 320 }, CardShape.Backdrop);

        Assert.Contains("fillHeight=360&fillWidth=640", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wide_poster_does_not_cover_a_portrait_card()
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", PrimaryImageAspectRatio = 1.7777, ImageTags = new { Primary = "ptag" } });

        Assert.False(Images.GetCardImage(item, new CardOptions { Width = 200 }, CardShape.Portrait).CoverImage);
    }

    [Fact]
    public void PreferThumb_takes_the_thumb_then_the_backdrop()
    {
        var withThumb = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Movie",
            ImageTags = new { Primary = "ptag", Thumb = "ttag" },
            BackdropImageTags = new[] { "btag" }
        });
        var withBackdrop = TestItems.Item(new
        {
            Id = TestItems.Id(2),
            Type = "Movie",
            ImageTags = new { Primary = "ptag" },
            BackdropImageTags = new[] { "btag" }
        });
        var options = new CardOptions { PreferThumb = true, Width = 320 };

        var thumb = Images.GetCardImage(withThumb, options, CardShape.Backdrop);
        var backdrop = Images.GetCardImage(withBackdrop, options, CardShape.Backdrop);

        Assert.StartsWith($"{Server}/Items/{TestItems.Id(1)}/Images/Thumb?", thumb.Url, StringComparison.Ordinal);
        Assert.Contains("tag=ttag", thumb.Url, StringComparison.Ordinal);
        Assert.False(thumb.ForceName);
        Assert.StartsWith($"{Server}/Items/{TestItems.Id(2)}/Images/Backdrop?", backdrop.Url, StringComparison.Ordinal);
        Assert.True(backdrop.ForceName);
    }

    [Fact]
    public void Without_PreferThumb_the_poster_wins_over_the_thumb()
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", ImageTags = new { Primary = "ptag", Thumb = "ttag" } });

        var image = Images.GetCardImage(item, new CardOptions { Width = 200 }, CardShape.Portrait);

        Assert.Contains("/Images/Primary?", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void An_episode_inherits_the_series_thumb_unless_told_not_to()
    {
        var episode = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Episode",
            SeriesId = TestItems.Id(7),
            SeriesThumbImageTag = "sthumb",
            ImageTags = new { Primary = "etag" }
        });

        var inherited = Images.GetCardImage(episode, new CardOptions { PreferThumb = true, Width = 320 }, CardShape.Backdrop);
        var own = Images.GetCardImage(episode, new CardOptions { PreferThumb = true, InheritThumb = false, Width = 320 }, CardShape.Backdrop);

        Assert.StartsWith($"{Server}/Items/{TestItems.Id(7)}/Images/Thumb?", inherited.Url, StringComparison.Ordinal);
        Assert.Contains("tag=sthumb", inherited.Url, StringComparison.Ordinal);
        Assert.StartsWith($"{Server}/Items/{TestItems.Id(1)}/Images/Primary?", own.Url, StringComparison.Ordinal);
        Assert.True(own.ForceName);
    }

    [Fact]
    public void PreferParentPoster_shows_the_series_poster_for_an_episode()
    {
        var episode = TestItems.Item(new
        {
            Id = TestItems.Id(1),
            Type = "Episode",
            SeriesId = TestItems.Id(7),
            SeriesPrimaryImageTag = "sposter",
            ImageTags = new { Primary = "etag" }
        });

        var image = Images.GetCardImage(episode, new CardOptions { PreferParentPoster = true, Width = 200 }, CardShape.Portrait);

        Assert.StartsWith($"{Server}/Items/{TestItems.Id(7)}/Images/Primary?", image.Url, StringComparison.Ordinal);
        Assert.Contains("tag=sposter", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void A_song_shows_its_album_cover()
    {
        var song = TestItems.Item(new { Id = TestItems.Id(1), Type = "Audio", AlbumId = TestItems.Id(5), AlbumPrimaryImageTag = "atag" });

        var image = Images.GetCardImage(song, new CardOptions { Width = 200 }, CardShape.Square);

        Assert.StartsWith($"{Server}/Items/{TestItems.Id(5)}/Images/Primary?", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void A_banner_card_takes_the_banner()
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Series", ImageTags = new { Primary = "ptag", Banner = "bantag" } });

        var image = Images.GetCardImage(item, new CardOptions { Width = 1000 }, CardShape.Banner);

        Assert.Contains("/Images/Banner?", image.Url, StringComparison.Ordinal);
        Assert.Contains("fillWidth=1000", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_without_images_has_no_url()
    {
        var item = TestItems.Item(new { Id = TestItems.Id(1), Type = "Movie", Name = "Bare" });

        Assert.Equal(CardImage.None, Images.GetCardImage(item, new CardOptions { Width = 200 }, CardShape.Portrait));
    }

    [Fact]
    public void A_timer_shows_the_image_of_its_programme()
    {
        var timer = new TimerInfoDto
        {
            Id = "timer1",
            Name = "News",
            ProgramInfo = TestItems.Item(new { Id = TestItems.Id(9), Type = "Program", ImageTags = new { Primary = "ptag" } })
        }.ToItem();

        var image = Images.GetCardImage(timer, new CardOptions { Width = 320 }, CardShape.Backdrop);

        Assert.StartsWith($"{Server}/Items/{TestItems.Id(9)}/Images/Primary?", image.Url, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_image_width_is_the_viewport_divided_by_the_cards_per_row()
    {
        Assert.Equal(384, Images.GetImageWidth(CardShape.Backdrop));
        Assert.Equal(213, Images.GetImageWidth(CardShape.Portrait));

        App.Browser.Resize(400);

        // A window narrower than the screen is rounded down to a multiple of 100.
        Assert.Equal(400, Images.GetImageWidth(CardShape.Backdrop));
        Assert.Equal(133, Images.GetImageWidth(CardShape.Portrait));
    }

    [Fact]
    public void Scaled_urls_carry_the_default_quality()
    {
        var logo = Images.GetScaledImageUrl(TestItems.Id(3), "Logo", "ltag", height: 40);
        var backdrop = Images.GetScaledImageUrl(TestItems.Id(3), "Backdrop", "btag", maxWidth: 800);

        Assert.Equal($"{Server}/Items/{TestItems.Id(3)}/Images/Logo?height=40&tag=ltag&quality=90", logo);
        Assert.Equal($"{Server}/Items/{TestItems.Id(3)}/Images/Backdrop?maxWidth=800&tag=btag&quality=80", backdrop);
    }
}

/// <summary>Shapes and the per-list option handling of the card builder.</summary>
public class CardOptionsTests : ItemsTestContext
{
    private CardBuilder Builder => Get<CardBuilder>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    [Theory]
    [InlineData(true, "overflowPortrait", "overflowBackdrop", "overflowSquare")]
    [InlineData(false, "portrait", "backdrop", "square")]
    public void Shape_helpers(bool overflow, string portrait, string backdrop, string square)
    {
        Assert.Equal(portrait, CardShape.GetPortraitShape(overflow));
        Assert.Equal(backdrop, CardShape.GetBackdropShape(overflow));
        Assert.Equal(square, CardShape.GetSquareShape(overflow));
    }

    [Theory]
    [InlineData("portrait", 2d / 3)]
    [InlineData("overflowPortrait", 2d / 3)]
    [InlineData("backdrop", 16d / 9)]
    [InlineData("overflowSmallBackdrop", 16d / 9)]
    [InlineData("square", 1d)]
    [InlineData("banner", 1000d / 185)]
    public void Desired_aspect_of_a_shape(string shape, double expected) =>
        Assert.Equal(expected, CardShape.GetDesiredAspect(shape)!.Value, 6);

    [Fact]
    public void Mixed_and_unknown_shapes_have_no_desired_aspect()
    {
        Assert.Null(CardShape.GetDesiredAspect(CardShape.Mixed));
        Assert.Null(CardShape.GetDesiredAspect(null));
    }

    [Theory]
    [InlineData(null, "mixedSquare")]
    [InlineData(1.78, "mixedBackdrop")]
    [InlineData(1.0, "mixedSquare")]
    [InlineData(0.67, "mixedPortrait")]
    public void A_mixed_card_takes_the_shape_of_its_image(double? ratio, string expected) =>
        Assert.Equal(expected, CardShape.ResolveMixedShape(ratio));

    [Fact]
    public void The_list_ratio_is_the_median_snapped_to_a_standard_ratio()
    {
        BaseItemDto WithRatio(double ratio) => new() { PrimaryImageAspectRatio = ratio };

        Assert.Equal(2d / 3, CardShape.GetPrimaryImageAspectRatio([WithRatio(0.66), WithRatio(0.7), WithRatio(1.7)])!.Value, 6);
        Assert.Equal(16d / 9, CardShape.GetPrimaryImageAspectRatio([WithRatio(1.8), WithRatio(1.75)])!.Value, 6);
        Assert.Equal(1d, CardShape.GetPrimaryImageAspectRatio([WithRatio(1.02)])!.Value, 6);
        Assert.Null(CardShape.GetPrimaryImageAspectRatio([new BaseItemDto()]));
    }

    [Fact]
    public void An_auto_shape_follows_the_images_of_the_list()
    {
        BaseItemDto WithRatio(double ratio) => new() { PrimaryImageAspectRatio = ratio };

        Assert.Equal(CardShape.Portrait, Builder.ResolveOptions([WithRatio(0.67)], new CardOptions { Shape = CardShape.Auto }).Shape);
        Assert.Equal(CardShape.Backdrop, Builder.ResolveOptions([WithRatio(1.78)], new CardOptions { Shape = CardShape.Auto }).Shape);
        Assert.Equal(CardShape.OverflowSquare, Builder.ResolveOptions([WithRatio(1)], new CardOptions { Shape = CardShape.AutoOverflow }).Shape);
        Assert.Equal(CardShape.Square, Builder.ResolveOptions([WithRatio(1)], new CardOptions()).Shape);

        var banner = Builder.ResolveOptions([WithRatio(5.4)], new CardOptions { Shape = CardShape.Auto });
        Assert.Equal(CardShape.Banner, banner.Shape);
        Assert.True(banner.CoverImage);
    }

    [Fact]
    public void An_auto_shape_without_image_ratios_uses_the_default_shape()
    {
        var withDefault = Builder.ResolveOptions([new BaseItemDto()], new CardOptions { Shape = CardShape.AutoOverflow, DefaultShape = CardShape.OverflowBackdrop });
        var without = Builder.ResolveOptions([], new CardOptions { Shape = CardShape.AutoOverflow });

        Assert.Equal(CardShape.OverflowBackdrop, withDefault.Shape);
        Assert.Equal(CardShape.OverflowSquare, without.Shape);
    }

    [Fact]
    public void PreferThumb_auto_means_thumbs_on_backdrop_cards_only()
    {
        var backdrop = Builder.ResolveOptions([], new CardOptions { Shape = CardShape.OverflowBackdrop, PreferThumb = CardToggle.Auto });
        var portrait = Builder.ResolveOptions([], new CardOptions { Shape = CardShape.OverflowPortrait, PreferThumb = CardToggle.Auto });

        Assert.True(backdrop.PreferThumb.IsTruthy);
        Assert.False(portrait.PreferThumb.IsTruthy);
        Assert.False(portrait.PreferThumb.IsAuto);
    }

    [Fact]
    public void The_image_width_is_computed_unless_given()
    {
        Assert.Equal(384, Builder.ResolveOptions([], new CardOptions { Shape = CardShape.Backdrop }).Width);
        Assert.Equal(123, Builder.ResolveOptions([], new CardOptions { Shape = CardShape.Backdrop, Width = 123 }).Width);
    }

    [Fact]
    public void A_toggle_distinguishes_unset_false_true_and_auto()
    {
        CardToggle unset = default;
        CardToggle off = false;
        CardToggle on = true;

        Assert.False(unset.IsSet);
        Assert.False(unset.IsFalse);
        Assert.False(unset.IsTruthy);
        Assert.True(off.IsSet);
        Assert.True(off.IsFalse);
        Assert.True(on.IsTruthy);
        Assert.True(CardToggle.Auto.IsAuto);
        Assert.True(CardToggle.Auto.IsTruthy);
    }
}

/// <summary>The card model: classes, text lines, indicators and buttons (cardBuilder.js buildCard).</summary>
public class CardBuilderTests : ItemsTestContext
{
    private CardBuilder Builder => Get<CardBuilder>();

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        SignIn();
    }

    private CardModel Card(object item, CardOptions options) => Card(TestItems.Item(item), options);

    private CardModel Card(BaseItemDto item, CardOptions options) =>
        Builder.BuildCard(0, item, Builder.ResolveOptions([item], options));

    private static string LineText(CardTextLine line) => string.Concat(line.Parts.Select(part => part.Text));

    [Fact]
    public void A_movie_card_on_desktop()
    {
        var card = Card(
            TestItems.Movie(1, "The Movie"),
            new CardOptions { Shape = CardShape.OverflowPortrait, ShowTitle = true, ShowYear = true, CenterText = true, Context = "home", ParentId = "parent1" });

        Assert.Equal("card overflowPortraitCard card-hoverable card-withuserdata", card.CssClass);
        Assert.Equal("cardBox cardBox-bottompadded", card.BoxClass);
        Assert.Equal("cardImageContainer coveredImage", card.ImageContainerClass);
        Assert.Equal(ItemAction.Link, card.Action);
        Assert.Equal($"#/details?id={TestItems.Id(1)}&serverId={TestApp.ServerId}", card.RouteUrl);
        Assert.Contains("/Images/Primary?", card.ImageUrl, StringComparison.Ordinal);
        Assert.Null(card.DefaultIcon);
        Assert.Null(card.InnerFooter);

        var footer = card.OuterFooter!;
        Assert.Null(footer.CssClass);
        Assert.Equal(new[] { "The Movie", "2001" }, footer.Lines.Select(LineText));
        Assert.Equal("cardText cardTextCentered cardText-first", footer.Lines[0].CssClass);
        Assert.Equal("cardText cardTextCentered cardText-secondary", footer.Lines[1].CssClass);
        Assert.NotNull(footer.Lines[0].Parts[0].Link);

        Assert.True(card.HoverMenu);
        Assert.True(card.HoverPlayButton);
        Assert.True(card.HoverPlayedButton);
        Assert.True(card.HoverRatingButton);
        Assert.False(card.OverlayPlayButton);

        Assert.Equal("0", card.Attributes["data-index"]);
        Assert.Equal(TestItems.Id(1), card.Attributes["data-id"]);
        Assert.Equal(TestApp.ServerId, card.Attributes["data-serverid"]);
        Assert.Equal("Movie", card.Attributes["data-type"]);
        Assert.Equal("Video", card.Attributes["data-mediatype"]);
        Assert.Equal("false", card.Attributes["data-isfolder"]);
        Assert.Equal("home", card.Attributes["data-context"]);
        Assert.Equal("parent1", card.Attributes["data-parentid"]);
        Assert.Equal("THE", card.Attributes["data-prefix"]);
        Assert.False(card.Attributes.ContainsKey("data-positionticks"));
    }

    [Fact]
    public void The_mobile_layout_has_overlay_buttons_instead_of_the_hover_menu()
    {
        Get<AppHost>().SetLayout("mobile");

        var card = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.OverflowPortrait, OverlayPlayButton = true });
        var automatic = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.OverflowPortrait });
        var withMore = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.OverflowPortrait, OverlayMoreButton = true });

        Assert.Equal("card overflowPortraitCard card-withuserdata", card.CssClass);
        Assert.False(card.HoverMenu);
        Assert.True(card.OverlayPlayButton);
        Assert.True(automatic.OverlayPlayButton);
        Assert.False(withMore.OverlayPlayButton);
        Assert.True(withMore.OverlayMoreButton);
    }

    [Fact]
    public void A_card_without_image_shows_an_icon_or_the_name_on_a_coloured_background()
    {
        var folder = Card(new { Id = TestItems.Id(1), Name = "Stuff", Type = "Folder", IsFolder = true }, new CardOptions { Shape = CardShape.Backdrop });
        var movie = Card(new { Id = TestItems.Id(2), Name = "Bare", Type = "Movie", MediaType = "Video" }, new CardOptions { Shape = CardShape.Portrait });
        var trailer = Card(new { Id = TestItems.Id(4), Name = "Teaser", Type = "Trailer", MediaType = "Video" }, new CardOptions { Shape = CardShape.Portrait });
        var withDefault = Card(new { Id = TestItems.Id(4), Name = "Teaser", Type = "Trailer", MediaType = "Video" }, new CardOptions { Shape = CardShape.Portrait, DefaultCardImageIcon = "theaters" });
        var library = Card(new { Id = TestItems.Id(3), Name = "Films", Type = "CollectionFolder", CollectionType = "movies", IsFolder = true }, new CardOptions { Shape = CardShape.Backdrop });

        Assert.Null(folder.ImageUrl);
        Assert.Matches("^cardImageContainer defaultCardBackground defaultCardBackground[1-5]$", folder.ImageContainerClass);
        Assert.Equal("folder", folder.DefaultIcon);
        Assert.Equal("movie", movie.DefaultIcon);
        Assert.Null(movie.DefaultText);
        Assert.Null(trailer.DefaultIcon);
        Assert.Equal("Teaser", trailer.DefaultText);
        Assert.Equal("theaters", withDefault.DefaultIcon);
        Assert.Equal("movie", library.DefaultIcon);
        Assert.Contains("emby-itemrefreshindicator", library.IndicatorsHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_background_colour_is_stable_for_a_name()
    {
        Assert.Equal(CardBuilder.GetDefaultColorIndex("Stuff"), CardBuilder.GetDefaultColorIndex("Stuff"));
        Assert.InRange(CardBuilder.GetDefaultColorIndex("Stuff"), 1, 5);
        Assert.Equal(1, CardBuilder.GetDefaultColorIndex(null));
        // 'b' is 98: 9 + 8 = 17 → 7 % 5 + 1.
        Assert.Equal(3, CardBuilder.GetDefaultColorIndex("abc"));
    }

    [Fact]
    public void A_resume_card_has_a_progress_bar_and_a_position()
    {
        var card = Card(
            TestItems.Movie(1, "The Movie", new { PlayedPercentage = 33.5, PlaybackPositionTicks = 24_000_000_000L, Played = false }),
            new CardOptions { Shape = CardShape.OverflowBackdrop, ShowTitle = true });

        Assert.Equal("innerCardFooter fullInnerCardFooter innerCardFooterClear", card.InnerFooter!.CssClass);
        Assert.Equal(
            "<div class=\"itemProgressBar\"><div class=\"itemProgressBarForeground\" style=\"width:33.5%;\"></div></div>",
            card.InnerFooter.ProgressHtml);
        Assert.Equal("24000000000", card.Attributes["data-positionticks"]);
        Assert.Equal(string.Empty, card.OuterFooter!.ProgressHtml);
    }

    [Fact]
    public void Played_and_unplayed_count_indicators()
    {
        var played = Card(TestItems.Movie(1, "Seen", new { Played = true }), new CardOptions { Shape = CardShape.Portrait });
        var series = Card(
            new { Id = TestItems.Id(2), Name = "Show", Type = "Series", IsFolder = true, UserData = new { UnplayedItemCount = 120, Played = false } },
            new CardOptions { Shape = CardShape.Portrait });
        var disabled = Card(TestItems.Movie(1, "Seen", new { Played = true }), new CardOptions { Shape = CardShape.Portrait, DisableIndicators = true });

        Assert.Contains("playedIndicator", played.IndicatorsHtml, StringComparison.Ordinal);
        Assert.Contains("<div class=\"countIndicator indicator\">99+</div>", series.IndicatorsHtml, StringComparison.Ordinal);
        Assert.Equal(string.Empty, disabled.IndicatorsHtml);
    }

    [Fact]
    public void An_episode_shows_its_series_as_a_link_above_its_numbered_title()
    {
        var card = Card(
            new
            {
                Id = TestItems.Id(1),
                Name = "Pilot",
                Type = "Episode",
                MediaType = "Video",
                ServerId = TestApp.ServerId,
                SeriesId = TestItems.Id(7),
                SeriesName = "Test Show",
                IndexNumber = 2,
                ParentIndexNumber = 1,
                ImageTags = new { Primary = "etag" }
            },
            new CardOptions { Shape = CardShape.OverflowBackdrop, ShowTitle = true, ShowParentTitle = true });

        var lines = card.OuterFooter!.Lines;
        Assert.Equal(new[] { "Test Show", "S1:E2 - Pilot" }, lines.Select(LineText));
        var series = lines[0].Parts[0];
        Assert.Equal("Series", series.Link!.TypeName());
        Assert.Equal($"#/details?id={TestItems.Id(7)}&serverId={TestApp.ServerId}", series.Href);
    }

    [Fact]
    public void An_album_shows_its_artists_below_the_title()
    {
        var card = Card(
            new
            {
                Id = TestItems.Id(1),
                Name = "First Album",
                Type = "MusicAlbum",
                IsFolder = true,
                ServerId = TestApp.ServerId,
                AlbumArtist = "Band",
                AlbumArtists = new[] { new { Name = "Band", Id = TestItems.Id(4) }, new { Name = "Guest", Id = TestItems.Id(5) } }
            },
            new CardOptions { Shape = CardShape.OverflowSquare, ShowTitle = true, ShowParentTitle = true });

        var lines = card.OuterFooter!.Lines;
        Assert.Equal(new[] { "First Album", "Band / Guest" }, lines.Select(LineText));
        Assert.Equal("MusicArtist", lines[1].Parts[0].Link!.TypeName());
        Assert.DoesNotContain("card-withuserdata", card.CssClass, StringComparison.Ordinal);
    }

    [Fact]
    public void Lines_limits_the_text_and_keeps_the_height()
    {
        var item = TestItems.Movie(1, "The Movie");

        var limited = Card(item, new CardOptions { Shape = CardShape.Portrait, ShowTitle = true, ShowYear = true, ShowRuntime = true, Lines = 2 });
        var padded = Card(
            new { Id = TestItems.Id(2), Name = "No Year", Type = "Movie" },
            new CardOptions { Shape = CardShape.Portrait, ShowTitle = true, ShowYear = true, Lines = 2 });

        Assert.Equal(new[] { "The Movie", "2001" }, limited.OuterFooter!.Lines.Select(LineText));
        Assert.Equal(2, padded.OuterFooter!.Lines.Count);
        Assert.Equal("No Year", LineText(padded.OuterFooter.Lines[0]));
        Assert.Empty(padded.OuterFooter.Lines[1].Parts);
    }

    [Fact]
    public void A_card_without_text_options_has_no_footer()
    {
        var card = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.Portrait });

        Assert.Null(card.OuterFooter);
        Assert.Equal("cardBox", card.BoxClass);
    }

    [Fact]
    public void Overlay_text_goes_into_the_inner_footer()
    {
        var card = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.Backdrop, ShowTitle = true, OverlayText = true });

        Assert.Null(card.OuterFooter);
        Assert.Equal("innerCardFooter", card.InnerFooter!.CssClass);
        Assert.Equal("The Movie", LineText(Assert.Single(card.InnerFooter.Lines)));
    }

    [Fact]
    public void The_card_layout_draws_a_visual_box()
    {
        var card = Card(TestItems.Movie(1, "The Movie"), new CardOptions { Shape = CardShape.Backdrop, ShowTitle = true, CardLayout = true });

        Assert.Equal("cardBox visualCardBox", card.BoxClass);
        Assert.Equal("cardFooter", card.OuterFooter!.CssClass);
    }

    [Fact]
    public void A_series_year_shows_its_run()
    {
        string Year(object item) => LineText(Card(item, new CardOptions { Shape = CardShape.Portrait, ShowYear = true }).OuterFooter!.Lines[0]);

        Assert.Equal("2010 - Present", Year(new { Id = TestItems.Id(1), Name = "A", Type = "Series", ProductionYear = 2010, Status = "Continuing" }));
        Assert.Equal("2010 - 2014", Year(new { Id = TestItems.Id(1), Name = "A", Type = "Series", ProductionYear = 2010, Status = "Ended", EndDate = "2014-06-01T00:00:00.0000000Z" }));
        Assert.Equal("2010", Year(new { Id = TestItems.Id(1), Name = "A", Type = "Series", ProductionYear = 2010, Status = "Ended", EndDate = "2010-06-01T00:00:00.0000000Z" }));
    }

    [Fact]
    public void Item_counts_of_a_genre_and_a_playlist()
    {
        var genre = TestItems.Item(new { Id = TestItems.Id(1), Name = "Drama", Type = "Genre", MovieCount = 1, SeriesCount = 3 });
        var playlist = TestItems.Item(new { Id = TestItems.Id(2), Name = "Mix", Type = "Playlist", RunTimeTicks = 3_000_000_000L });

        Assert.Equal("1 movie, 3 series", Builder.GetItemCounts(genre, new CardOptions()));
        Assert.Equal("5 min", Builder.GetItemCounts(playlist, new CardOptions()));
    }

    [Fact]
    public void The_action_of_a_card()
    {
        var movie = TestItems.Movie(1, "The Movie");

        Assert.Equal(ItemAction.Link, Card(movie, new CardOptions { Shape = CardShape.Portrait }).Action);
        Assert.Equal(ItemAction.Play, Card(movie, new CardOptions { Shape = CardShape.Portrait, Action = ItemAction.Play }).Action);
        Assert.Equal(ItemAction.None, Card(movie, new CardOptions { Shape = CardShape.Portrait, Action = ItemAction.None }).Action);
        Assert.Equal(ItemAction.Play, ItemAction.Resolve(null, isFolder: false, isPhoto: true));
        Assert.Equal(ItemAction.Link, ItemAction.Resolve(ItemAction.Play, isFolder: true, isPhoto: false));
    }

    [Fact]
    public void A_list_is_split_into_columns_and_indexed_sections()
    {
        var items = new[]
        {
            TestItems.Item(new { Id = TestItems.Id(1), Name = "A", Type = "Movie", ProductionYear = 2001 }),
            TestItems.Item(new { Id = TestItems.Id(2), Name = "B", Type = "Movie", ProductionYear = 2001 }),
            TestItems.Item(new { Id = TestItems.Id(3), Name = "C", Type = "Movie", ProductionYear = 2002 })
        };

        var plain = Builder.Build(items, new CardOptions { Shape = CardShape.Portrait });
        var rows = Builder.Build(items, new CardOptions { Shape = CardShape.Portrait, Rows = 2 });
        var indexed = Builder.Build(items, new CardOptions { Shape = CardShape.Portrait, IndexBy = "ProductionYear" });

        Assert.Equal(3, plain.Cards.Count);
        Assert.Null(Assert.Single(plain.Sections).Title);
        Assert.Equal(new[] { 2, 1 }, Assert.Single(rows.Sections).Columns.Select(column => column.Count));
        Assert.Equal(new string?[] { "2001", "2002" }, indexed.Sections.Select(section => section.Title));
        Assert.Equal(new[] { 0, 1, 2 }, indexed.Cards.Select(card => card.Index));
    }

    [Fact]
    public void A_programme_card_shows_its_air_time_and_progress()
    {
        var card = Card(
            new
            {
                Id = TestItems.Id(1),
                Name = "News",
                Type = "Program",
                ChannelId = TestItems.Id(8),
                ChannelName = "One",
                StartDate = "2024-05-17T11:30:00.0000000Z",
                EndDate = "2024-05-17T12:30:00.0000000Z"
            },
            HomeSections.OnNowOptions);

        Assert.Equal(CardShape.OverflowBackdrop, card.Shape);
        Assert.Equal(TestItems.Id(8), card.Attributes["data-channelid"]);
        Assert.Contains("data-automode=\"time\"", card.InnerFooter!.ProgressHtml, StringComparison.Ordinal);
        Assert.Contains("width:50%;", card.InnerFooter.ProgressHtml, StringComparison.Ordinal);
        var lines = card.OuterFooter!.Lines;
        Assert.Equal(3, lines.Count);
        Assert.Equal("News", LineText(lines[0]));
        Assert.Equal("2024-05-17 11:30:00 - 2024-05-17 12:30:00", LineText(lines[1]));
        Assert.False(card.HoverPlayedButton);
        Assert.False(card.HoverRatingButton);
    }
}
