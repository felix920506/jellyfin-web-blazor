namespace Jellyfin.Web.Services;

/// <summary>
/// How the items of a library view are drawn: ports of <c>getCardOptions</c> and <c>getListOptions</c> of
/// <c>ItemsView.tsx</c>.
/// PORT-TODO(library): the card options <c>showDetailsMenu</c> (Live TV channels) and the list option
/// <c>smallIcon</c> (songs) have no counterpart in the item components.
/// </summary>
public static class LibraryViewOptions
{
    /// <summary>The card options of a view for its settings.</summary>
    public static CardOptions Cards(LibraryTabContent content, LibraryViewSettings settings)
    {
        var viewType = content.ViewType;
        var options = new CardOptions
        {
            Shape = settings.ImageType switch
            {
                "Banner" => CardShape.Banner,
                "Disc" => CardShape.Square,
                "Logo" or "Thumb" => CardShape.Backdrop,
                _ => CardShape.Auto
            },
            ShowTitle = settings.ShowTitle,
            ShowYear = settings.ShowYear,
            CardLayout = settings.CardLayout,
            CenterText = true,
            Context = content.CollectionType,
            CoverImage = true,
            PreferThumb = settings.ImageType == "Thumb" ? true : CardToggle.Unset,
            PreferDisc = settings.ImageType == "Disc",
            PreferLogo = settings.ImageType == "Logo",
            OverlayText = !settings.ShowTitle
        };

        options = viewType switch
        {
            LibraryTab.Songs or LibraryTab.Albums or LibraryTab.Episodes =>
                options with { ShowParentTitle = settings.ShowTitle, OverlayPlayButton = true },
            LibraryTab.Artists or LibraryTab.Authors or LibraryTab.Persons =>
                options with { Lines = 1, ShowYear = false, OverlayPlayButton = true },
            LibraryTab.Channels =>
                options with { Shape = CardShape.Square, ShowCurrentProgram = true, ShowCurrentProgramTime = true },
            LibraryTab.SeriesTimers => options with
            {
                Shape = CardShape.Backdrop,
                ShowSeriesTimerTime = true,
                ShowSeriesTimerChannel = true,
                OverlayMoreButton = true,
                Lines = 3
            },
            LibraryTab.Movies => options with { OverlayPlayButton = true },
            LibraryTab.Series or LibraryTab.Studios => options with { OverlayMoreButton = true },
            _ => options
        };

        return content.ConfigureCards is { } configure ? configure(options, settings) : options;
    }

    /// <summary>The list options of a view for its settings.</summary>
    public static ListViewOptions List(LibraryTabContent content, LibraryViewSettings settings) => content.ViewType switch
    {
        LibraryTab.Songs => new ListViewOptions
        {
            ShowParentTitle = true,
            Action = ItemAction.PlayAllFromHere,
            Artist = true,
            AddToListButton = true
        },
        LibraryTab.Albums => new ListViewOptions { SortBy = settings.SortBy.FirstOrDefault(), AddToListButton = true },
        LibraryTab.Episodes => new ListViewOptions { ShowParentTitle = true },
        _ => new ListViewOptions()
    };

    /// <summary>The cards of a genre row (<c>GenresSectionContainer.tsx</c>).</summary>
    public static CardOptions GenreCards(LibraryTabContent content)
    {
        var music = content.CollectionType == "music";
        return new CardOptions
        {
            OverlayPlayButton = true,
            ShowTitle = true,
            CenterText = true,
            CardLayout = false,
            Shape = music ? CardShape.OverflowSquare : CardShape.OverflowPortrait,
            ShowParentTitle = music,
            ShowYear = !music,
            Context = content.CollectionType
        };
    }

    /// <summary>The cards of a movie recommendation row (<c>SuggestionsSectionView.tsx</c>).</summary>
    public static CardOptions RecommendationCards(LibraryTabContent content) => new()
    {
        Shape = CardShape.OverflowPortrait,
        ShowYear = true,
        OverlayPlayButton = true,
        ShowTitle = true,
        CenterText = true,
        CardLayout = false,
        Context = content.CollectionType
    };

    /// <summary>The cards of the upcoming view (<c>UpcomingView.tsx</c>).</summary>
    public static CardOptions UpcomingCards(LibraryTabContent content) => new()
    {
        Shape = CardShape.Backdrop,
        ShowTitle = true,
        ShowParentTitle = true,
        PreferThumb = true,
        MissingIndicator = false,
        CardLayout = false,
        Context = content.CollectionType
    };
}
