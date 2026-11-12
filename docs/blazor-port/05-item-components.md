# 05 – Item components and the home screen

Reference for the pages built on the shared item components: library pages, details, search,
Live TV. Source: `blazor/src/Jellyfin.Web/Components/Items/`, `Services/Items/`, `Pages/Home/`,
`Styles/port/_items.scss`, `wwwroot/js/items.js`. Tests: `blazor/tests/Jellyfin.Web.Tests/Items/`
and `Home/`; browser test `blazor/e2e/home.py`.

The components are in the namespace `Jellyfin.Web.Components` (already imported everywhere), the
services in `Jellyfin.Web.Services`. All services are registered with `[AppService]`; inject them.

The markup is the original's (post-upgrade markup of the `emby-*` elements, same classes and
`data-*` attributes), so the original stylesheets apply. Items are the SDK's `BaseItemDto`
everywhere. Type, media type and collection type are compared as strings through the extensions
`item.TypeName()`, `item.MediaTypeName()`, `item.CollectionTypeName()`; ids as dash-less text
through `item.IdText()` (see "Item helpers").

## 1. Quick start

A page with tabs, one row of cards that reloads by itself, and a grid:

```razor
@attribute [Route("/movies")]
@attribute [Access(AccessLevel.User)]
@inject ItemQueries Queries
@inject Globalize Globalize

<Page Id="moviesPage" Class="mainAnimatedPage libraryPage backdropPage pageWithAbsoluteTabs withTabs">
    <PageTabs Tabs="@(new[] { Globalize["Movies"], Globalize["Favorites"] })">
        <TabContent Index="0" Id="moviesTab">
            <ItemsSection Title="@Globalize["HeaderContinueWatching"]" Monitor="videoplayback,markplayed"
                          Fetch="@(() => Queries.GetResumeItemsAsync("Video"))" CardOptions="ResumeCards" />

            <ItemsContainer Class="vertical-wrap padded-left padded-right" ParentId="@Location["topParentId"]"
                            Context="movies" Fetch="LoadMoviesAsync" CardOptions="PosterCards" />
        </TabContent>
        <TabContent Index="1" Id="favoritesTab">
            <ItemsContainer Class="vertical-list" Monitor="markfavorite" Fetch="LoadFavoritesAsync"
                            ListOptions="@(new ListViewOptions { SortBy = "SortName" })" />
        </TabContent>
    </PageTabs>
</Page>

@code {
    private static readonly CardOptions PosterCards = new()
    {
        Shape = CardShape.Portrait, ShowTitle = true, ShowYear = true, CenterText = true, Context = "movies"
    };

    private static readonly CardOptions ResumeCards = PosterCards with
    {
        Shape = CardShape.OverflowBackdrop, PreferThumb = true, OverlayPlayButton = true
    };

    [CascadingParameter]
    public AppLocation Location { get; set; } = AppLocation.Empty;

    private Task<IReadOnlyList<BaseItemDto>> LoadMoviesAsync() => Queries.GetUserItemsAsync(
        ("ParentId", Location["topParentId"]), ("IncludeItemTypes", "Movie"), ("Recursive", true),
        ("Fields", "PrimaryImageAspectRatio"), ("SortBy", "SortName"));
}
```

Rules that matter:

- Keep `CardOptions`, `ListViewOptions` and `Fetch` delegates **stable** (static fields, fields,
  method groups). `ItemsContainer` fetches once when it is first shown; it does not refetch when a
  parameter changes. Call `RefreshAsync()` (through `@ref`) after changing a filter or sort order.
- Put lists inside `TabContent`: the lists of a tab that is not selected neither load nor refresh,
  and catch up when the tab is selected again.

## 2. Components

### `ItemsContainer` (elements/emby-itemscontainer)

`<div is="emby-itemscontainer" class="itemsContainer {Class}" data-monitor data-parentid>`. Owns the
items and reloads them; tells the item actions which list a clicked item belongs to.

| Parameter | Meaning |
| --- | --- |
| `Items` / `Fetch` | Fixed items, or `Func<Task<IReadOnlyList<BaseItemDto>>>` called on first display and on every refresh. |
| `CardOptions` / `ListOptions` / `ItemsTemplate` / `ChildContent` | Draw the items as cards, as list rows, with a `RenderFragment<IReadOnlyList<BaseItemDto>>`, or static content. |
| `Class` | Classes besides `itemsContainer`: `vertical-wrap`, `vertical-list`, `scrollSlider focuscontainer-x`, padding classes. |
| `Monitor` | The original's `data-monitor`, comma separated: `videoplayback`, `audioplayback`, `markplayed`, `markfavorite`, `timers`, `seriestimers`. |
| `ParentId`, `Context`, `CollectionId`, `PlaylistId` | Handed to links, play requests and the context menu. `ParentId` also limits `LibraryChanged` reloads to that folder. |
| `Paused` | True while the list is not shown (also cascaded as `ItemsPaused` by `TabContent`). |
| `OnItemsChanged` | After the items were loaded or refreshed. |
| `OnCustomAction` | `(customAction, item)` for elements with `data-action="custom"` (list right buttons). |
| `ConfigureMenu` | `ItemMenuOptions => ItemMenuOptions`: adjust the context menu of the items. |
| other attributes | Splatted onto the element. |

Public members: `RefreshAsync()`, `NotifyRefreshNeeded(bool foreground = false)` (delayed unless
foreground, as the original's 10 seconds), `CurrentItems`.

Live refresh, as in the original:

| Event | Effect |
| --- | --- |
| `UserDataChanged` (websocket, or a button of any card) | Cards and rows of the item update in place (played, favorite, progress). With `markplayed` / `markfavorite` in `Monitor` the list also reloads after the delay. |
| Playback stopped | Reloads at once for video with `videoplayback`, for audio with `audioplayback`. |
| `TimerCreated`, `TimerCancelled`, `SeriesTimerCreated`, `SeriesTimerCancelled` | With `timers` / `seriestimers`: reload. Otherwise the timer indicator of the affected programme is set or cleared in place. |
| `LibraryChanged` | Reload after the delay unless the list monitors timers; with `ParentId` only when that folder is among `FoldersAddedTo`, `FoldersRemovedFrom` or `CollectionFolders`; ignored when nothing was added or removed. |
| Context menu command that changed or deleted an item | Reload. |

### `Cards` and `Card` (components/cardbuilder)

`<Cards Items="items" Options="options" />` draws the cards of `CardBuilder.Build`; inside an
`ItemsContainer` use its `CardOptions` parameter instead. `Card` draws one `CardModel`. Clicks on
the image, the title links and the buttons run `ItemActions` with their `data-action`.

`CardOptions` is a record with the original option names in Pascal case (`cardBuilder.js`
`setCardData` / `buildCard`):

- Shape: `Shape` (a `CardShape` constant; `Auto`, `AutoHome`, `AutoOverflow`, `AutoVertical` are
  resolved from the primary image aspect ratio of the items), `DefaultShape`, `Width`.
- Image choice: `PreferThumb` (`true`, `false` or `CardToggle.Auto`), `PreferBanner`, `PreferDisc`,
  `PreferLogo`, `PreferParentPoster`, `InheritThumb`, `CoverImage`, `DefaultCardImageIcon`.
- Text: `ShowTitle` (`true`, `false`, `CardToggle.Auto`), `ShowParentTitle`,
  `ShowParentTitleOrTitle`, `ShowYear`, `ShowSeriesYear`, `ShowRuntime`, `ShowAirTime`,
  `ShowAirDateTime`, `ShowAirEndTime`, `ShowChannelName`, `ShowCurrentProgram`,
  `ShowCurrentProgramTime`, `ShowSeriesTimerTime`, `ShowSeriesTimerChannel`, `ShowPersonRoleOrType`,
  `ShowItemCounts`, `ShowSongCount`, `ShowPremiereDate`, `IncludeParentInfoInTitle`, `TextLines`
  (own lines per item), `Lines` (pad to this many), `CenterText`, `OverlayText`, `CardLayout`,
  `CardFooterAside`, `ShowChannelLogo`, `ShowLogo`.
- Indicators: `ShowChildCountIndicator`, `ShowGroupCount`, `MissingIndicator`, `DisableIndicators`.
- Buttons: `OverlayPlayButton`, `OverlayMoreButton`, `OverlayInfoButton`, `CenterPlayButton`,
  `DisableHoverMenu`, `Action` (an `ItemAction`, default `link`).
- Context: `Context`, `ParentId`, `CollectionId`, `PlaylistId`.
- Layout: `CardCssClass`, `CardClass`, `IndexBy` (`PremiereDate`, `ProductionYear`,
  `CommunityRating`), `SectionTitleTagName`, `Rows`.

Additional parity options are available for callers that need the newer card layouts: `Scalable`,
`AllowBottomPadding` and `ShowDetailsMenu`. `ListViewOptions.SmallIcon` uses the compact image
classes used by song and other dense rows. Collection-folder cards subscribe to `RefreshProgress`
websocket messages and expose the live progress/status through the original refresh-indicator
markup.

The desktop layout shows the hover menu (play, played, favorite, more); the mobile layout shows
the overlay buttons asked for by the options. Cards are rebuilt when the layout changes.

Shapes and sizes: `CardShape.GetPortraitShape(enableOverflow)`, `GetBackdropShape`,
`GetSquareShape`, `GetDesiredAspect`, `GetPostersPerRow(shape, screenWidth, landscape)`.

### `ListView` (components/listview)

`<ListView Items="items" Options="options" />`, or `ItemsContainer ListOptions`. `ListViewOptions`
has the original options: `Action`, `ImageSize` (`large` / default small), `Image`, `ImageSource`
(`channel`), `ImagePlayButton`, `EnableOverview`, `EnableSideMediaInfo`, `MediaInfo`, `ShowIndex` +
`Index` / `SortBy` (group headers), `Border`, `Highlight`, `DragHandle`, `ShowIndexNumberLeft`,
`ShowIndexNumber`, `ShowProgramDateTime`, `ShowProgramTime`, `ShowChannel`, `ShowParentTitle`,
`ParentTitleWithTitle`, `IncludeParentInfoInTitle`, `Artist`, `RecordButton`, `AddToListButton`,
`InfoButton`, `RightButtons` (`ListRightButton(Id, Title, Icon)`; clicks arrive at
`ItemsContainer.OnCustomAction`), `EnableUserDataButtons`, `EnablePlayedButton`,
`EnableRatingButton`, `MoreButton`, `DisableIndicators`, `CollectionId`, `PlaylistId`,
`DefaultCardImageIcon`.

### `PlayStateButton`, `RatingButton` (elements/emby-playstatebutton, emby-ratingbutton)

```razor
<PlayStateButton Item="item" Class="button-flat btnPlaystate" IconClass="detailButton-icon" />
<RatingButton Item="item" Class="button-flat btnUserRating" IconClass="detailButton-icon" />
```

They toggle played (`POST` / `DELETE /UserPlayedItems/{id}`) and favorite
(`POST` / `DELETE /UserFavoriteItems/{id}`), update `item.UserData`, and follow changes made
elsewhere. Without a `Class` they render with the original's default classes.

### `MediaInfoLine` (components/mediainfo)

`<MediaInfoLine Item="item" />` renders `<div class="itemMiscInfo itemMiscInfo-primary">` with the
primary media info (year, runtime, rating, ends at…); `Secondary="true"` the secondary line; `Class`
replaces the classes; `Options` is a `MediaInfoOptions` (every part can be turned off). For HTML
inside other markup use `MediaInfo.GetPrimaryHtml(item, options)` / `GetSecondaryHtml`.

### `Scroller` (elements/emby-scroller, emby-scrollbuttons)

```razor
<div class="verticalSection emby-scroller-container">
    <Scroller Class="padded-top-focusscale padded-bottom-focusscale" data-centerfocus="true">
        <ItemsContainer Class="scrollSlider focuscontainer-x" ... />
    </Scroller>
</div>
```

A horizontally scrolling row with the previous / next buttons of the desktop layout (hidden while
everything fits, none on touch devices and in the mobile layout, `ScrollButtons="false"` turns them
off). The parent element must have the class `emby-scroller-container`: the original styles place
the buttons relative to it.

### `ItemsSection` and `SectionHeader`

`ItemsSection` is the usual "title + scrolling row of cards" block. It stays hidden (`hide`) until
it has items.

| Parameter | Meaning |
| --- | --- |
| `Title`, `Href`, `More` | Section title; with `Href` the title is a link (`More="false"`: without the `more` class). |
| `Header` | A fragment replacing the title, e.g. `<SectionHeader Title="..." Container="true" PadTitle="true" />`. |
| `Items` / `Fetch`, `CardOptions`, `Monitor`, `ParentId`, `Context` | As for `ItemsContainer`. |
| `Class`, `ScrollButtons`, `ItemsChanged`, other attributes | Section classes, buttons, callback, splatted attributes. |

`RefreshAsync()` reloads the row. `SectionHeader` alone renders the three title variants of the
original (`Title`; `Href` + `More`; `Container` + `PadTitle`; child content is placed next to the
title).

### Tabs: `PageTabs`, `TabContent`

```razor
<PageTabs Tabs="_tabNames" DefaultIndex="0">
    <TabContent Index="0" Id="suggestionsTab"> ... </TabContent>
    <TabContent Index="1" Id="moviesTab"> ... </TabContent>
</PageTabs>
```

- The selected tab is the `tab` query parameter of the page's address (`#/movies?topParentId=…&tab=1`),
  clamped to the tab count; `DefaultIndex` applies when the address names none (e.g. the user's
  "default view" setting). Selecting a tab navigates with `AppRouter.Navigate`, keeping the other
  query parameters, so back / forward walk through tabs as in the original.
- The tab bar appears in the app bar: `AppBarTabs` is mounted through the shell's toolbar slot
  (`[ShellSlot(ShellSlot.Toolbar)]`) and reads `PageTabsState`, which `PageTabs` fills. No layout
  file was changed. `Visible="false"` keeps the tab logic without a bar (the home page).
- `TabContent` renders `div.tabContent.pageTabContent[.is-active]`. With `Lazy` (default) its content
  is created when the tab is first selected and kept afterwards. Lists inside are paused while the
  tab is not selected. It also works without `PageTabs` (`Active` or the cascaded `AppLocation`).
- `PageTabs.GetSelectedIndex(location, count, defaultIndex)` is the pure function behind it.
- While tabs are visible `<body>` has the class `jf-with-tabs` (see `_items.scss`).

## 3. Services

### Item actions (components/shortcuts.js)

`ItemActions.ExecuteAsync(item, action, context?, customAction?, playOptions?)` runs an
`ItemAction` (`link`, `play`, `resume`, `queue`, `playallfromhere`, `queueallfromhere`,
`instantmix`, `playtrailer`, `menu`, `programdialog`, `record`, `playmenu`, `edit`,
`addtoplaylist`, `setplaylistindex`, `custom`, `none`). `ItemActionContext` describes the
surrounding list (`ParentId`, `Context`, `CollectionId`, `PlaylistId`, `Items`, `FetchItems`,
`NotifyRefreshNeeded`, `OnCustomAction`, `ConfigureMenu`); `ItemsContainer` cascades one, so
components inside a container need not build it.

For own markup that should behave like a card:

```razor
<button class="itemAction" data-action="play" @attributes="ItemActions.GetShortcutAttributeMap(item)"
        @onclick="@(() => Actions.ExecuteAsync(item, ItemAction.Play, Context))">…</button>

@code {
    [CascadingParameter] public ItemActionContext? Context { get; set; }
}
```

`ItemAction.Resolve(defaultAction, isFolder, isPhoto)` gives the click action of a card.

### Context menu (components/itemContextMenu.js)

`ItemContextMenu.ShowAsync(new ItemMenuOptions { Item = item, User = Api.User, ... })` shows the
original command list with the original visibility rules through `AppDialogs.ActionSheetAsync`
and runs the chosen command; it returns `ItemMenuResult(Command, Updated, Deleted, ItemId)` or null
when dismissed. `GetCommandsAsync(options)` returns the list alone. `ItemMenuOptions` has the
original switches (`Play`, `Queue`, `Shuffle`, `InstantMix`, `PlayAllFromHere`, `QueueAllFromHere`,
`StopPlayback`, `ClearQueue`, `Playlist`, `CancelTimer`, `DeleteItem`, `Edit`, `EditImages`,
`EditSubtitles`, `Identify`, `MoreMediaInfo`, `Record`, `Share`, `OpenAlbum`, `OpenArtist`,
`PlaylistId` + `CanEditPlaylist`, `CollectionId`). The item must be the full item
(`ItemQueries.GetFullItemAsync`). From a list, `ItemActions.ShowContextMenuAsync(item, context)` does
all of that, as the menu button of cards and rows does.

Implemented here: play, resume, queue, queue next, stop playback, shuffle, instant mix, play /
queue all from here, open album / artist / lyrics, copy stream URL, delete (`ItemDeleter`, with the
original confirmation), remove from playlist, move to top / bottom, remove from collection.

### Extension interface for the other commands: `IItemCommandHandler`

Commands whose feature belongs to another workstream are shown by the same rules and handed to a
handler. Without a handler the user gets the toast `ItemCommandHandlers.NotAvailableText`.

```csharp
[AppService(typeof(IItemCommandHandler))]
public sealed class MetadataCommands(AppDialogs dialogs) : IItemCommandHandler
{
    public bool CanHandle(string command, BaseItemDto item) =>
        command is ItemCommand.Edit or ItemCommand.EditImages or ItemCommand.Identify;

    public async Task<ItemCommandResult> ExecuteAsync(string command, BaseItemDto item, ItemActionContext context)
    {
        // open the editor …
        return new ItemCommandResult(Updated: true);   // the list the item is in reloads
    }
}
```

Any number of handlers may exist; the last registered one that claims a command wins. Commands
waiting for a handler (`ItemCommand` constants):

| Command | Feature |
| --- | --- |
| `addtocollection`, `addtoplaylist`, `editplaylist` | Collection and playlist editors |
| `edit`, `editimages`, `editsubtitles`, `editlyrics`, `identify`, `refresh`, `moremediainfo` | Metadata, image, subtitle and lyrics editors, item identifier, refresh dialog, media info dialog |
| `download`, `downloadall`, `share` | Downloads and sharing (shown only when the host supports them) |
| `record`, `canceltimer`, `cancelseriestimer`, `programdialog`, `togglerecording` | Live TV recording |
| `multiSelect` | Multi-select mode |
| `clearQueue`, `playmenu`, `setplaylistindex` | Playback queue (now-playing workstream) |

### Images

`CardImages.GetCardImage(item, options, shape)` is the card builder's image choice (thumb, banner,
disc, logo, parent poster, primary, backdrop, album / series / channel fallbacks, with tag,
`fillWidth` / `fillHeight` / `quality=96` as the original) and returns the URL, the blurhash and
whether the name must be shown. `GetImageUrl(itemId, imageType, tag, width?, height?)` builds a
plain image URL, `GetImageWidth(shape)` the pixel width a card of that shape needs.
`ListViewBuilder.GetImageUrl(item, size)` is the list view's choice.

Lazy loading: an element with the class `lazy` and `data-src` (optionally `data-blurhash` and a
preceding `canvas.blurhash-canvas`) inside an `ItemsContainer` is loaded by `js/items.js` when it
comes near the viewport (IntersectionObserver): the blurhash is drawn first if the user setting
allows it, then the image is set as background and faded in (`lazy-image-fadein` or
`lazy-image-fadein-fast`). For own markup outside a container call
`IItemsInterop.LoadImagesAsync(element)` after rendering.

### Other services

- `ItemQueries`: raw item requests that keep the original's query parameters:
  `GetItemsAsync(path, params (Key, Value)[])`, `GetUserItemsAsync(...)` (`/Items` with `userId`),
  `GetItemAsync(id)`, `GetFullItemAsync(item)` (also timers), `GetResumeItemsAsync`,
  `GetNextUpAsync`, `GetLatestItemsAsync`, `GetOnNowProgramsAsync`, `GetActiveRecordingsAsync`,
  `CanEditPlaylistAsync`.
- `ItemUserData`: `SetPlayedAsync(item, played)`, `SetFavoriteAsync(item, favorite)`, event
  `Changed` (own changes and `UserDataChanged` messages), `ItemUserData.Apply(item, data)`.
- `Indicators`: the indicator HTML of cards and rows (played, progress, timer, child count, missing,
  type).
- `MediaInfo`: media info HTML, `GetEndsAt`, `GetResolutionText`.
- `DateTimeText`: `GetDisplayRunningTime(ticks)`, `GetDisplayDuration(ticks)`, `GetDisplayTime(date)`,
  `ToLocaleDateString(date)`; settable `Clock` for tests.
- `ItemHelper`: `GetDisplayName(item, globalize)`, `CanEdit`, `CanEditImages`, `CanEditSubtitles`,
  `CanIdentify`, `CanShare`, `CanMarkPlayed`, `CanRate`, `CanRefreshMetadata`,
  `SupportsAddingToCollection`, `SupportsAddingToPlaylist`. `ItemIcons.ForLibrary(collectionType)`,
  `ItemIcons.ForItemType(type)`.
- `HomeSections`: the home screen's section list (`homesection0..9` of `UserSettings`), card
  options and requests of each section, the favorites sections.
- `CardBuilder`, `ListViewBuilder`: the view models behind `Cards` and `ListView`
  (`Build(items, options)`), usable without rendering.

### Item helpers

`ItemExtensions`: `TypeName()`, `MediaTypeName()`, `CollectionTypeName()`, `IdText()` (also on
`Guid?`), `ActionId()` (the id an action uses: timer id for timers), `ImageTag(type)`,
`BlurHash(type, tag)`, `UsesLiveTvNaming()`, `TimerInfoDto.ToItem()`, `SeriesTimerInfoDto.ToItem()`.
Values the original attaches to items that the SDK model lacks (`ProgramInfo`, `PlaylistIndex`,
`Role`, `SyncPercent`…) live in `item.Extras()` / `item.EnsureExtras()`.

## 4. Home page

`/home` (`Pages/Home/Home.razor`): tab 0 `HomeTab` (the sections chosen by `homesection0..9`;
defaults `smalllibrarytiles, resume, resumeaudio, resumebook, livetv, nextup, latestmedia`), tab 1
`FavoritesTab` (`#/home?tab=1`). Sections are `ItemsSection`s and therefore reload on playback stop,
user data and library changes; the tab redraws when the libraries (`UserViews.Changed`) or a
`homesection*` setting change. Without libraries the original's "Nothing here" message is shown.

## 5. Tests

`Items/ItemsTestContext.cs` is a bUnit base class with a controllable `IPlaybackManager`
(`Playback.Start(item)`, `Stop()`, `Calls`, `PlayRequests`), a fake `IItemsInterop`, a fixed clock,
`ServerMessage(type, data)` for websocket messages, `NextDialogAsync()` and `TestItems.Item(new { … })`
to build items from server-style JSON.

## 6. Known gaps

- Scrolling rows scroll natively (`overflow-x`), not with the original's transform-based scroller:
  no `scrollToElement`, no centre-focus, no mouse-wheel-to-horizontal translation.
- No TV layout: no `<button>` cards, no focus handling, no TV values in `GetPostersPerRow`.
- Lazy images are not unloaded again when they leave the viewport.
- Ctrl / middle click on a card image opens the item in the same tab (the click is intercepted).
- List-row overviews are rendered as Markdown through `markdown-it` and DOMPurify. If the browser
  module is unavailable, the component safely falls back to HTML-encoded text.
- Collection-folder cards show server refresh progress from the shared `RefreshProgressStore`.
- `MediaInfoLine` computes "Ends at" when rendered and does not keep it current.
- `ItemHelper.IsLocalItem` is always false (no downloaded items in the browser).
- "Stop playback" in the context menu follows `IPlaybackManager.IsPlaying`, not the original's
  per-player check.
- The "not available yet" toast is an English literal: the original has no such text.
- Multi-select, drag-and-drop reordering of lists and the alphabet picker are not part of these
  components.
- The item menu opens in the middle of the window, not at the button: `ActionSheetOptions` has no
  anchor (`PORT-TODO(items)` in `ItemContextMenu.cs`).
- Play buttons on cards and the Play / Queue entries of the menu follow `IPlaybackManager.CanPlay` /
  `CanQueue`. The foundation's placeholder answers false, so they only appear once the playback
  workstream's manager is registered (the tests cover them with a fake manager).
- The home page has no tab bar (`Visible="false"`): the shell's navigation links to Favorites
  (`#/home?tab=1`), as in the original's modern layout.
- Stylesheet order: the port's bundle emits `components/**` before `elements/**`, the reverse of the
  original's load order, so `.paper-icon-button-light` (and `.emby-button`) override single-class
  rules of component stylesheets. For the card and list-row buttons `Styles/port/_items.scss`
  repeats the original values with higher specificity. If an icon button of your page sits in the
  wrong place or has the wrong padding, this is the likely cause; do the same in your own partial.
