# 07 – Library pages, the list page and search

Reference for the library browsing pages (`/movies`, `/tv`, `/music`, `/books`, `/boxsets`, `/homevideos`,
`/musicvideos`, `/playlists`, `/mixed`), the generic list page (`/list`), search (`/search`) and multi-select.
Sources: `blazor/src/Jellyfin.Web/Pages/Library/`, `Pages/Search/`, `Components/Library/`, `Services/Library/`,
`Styles/port/_library.scss`, `wwwroot/js/library.js`. Tests: `blazor/tests/Jellyfin.Web.Tests/Library/` and
`Search/`; end to end: `blazor/e2e/library.py`, `blazor/e2e/search.py` (helpers in `blazor/e2e/librarylib.py`).

Originals: `src/apps/modern/features/libraries/` (library pages, MUI), `src/controllers/list.js` + `list.html`
(list page, legacy markup), `src/apps/legacy/features/search/` (search), `src/components/multiSelect/`.

## 1. Page structure

```
Pages/Library/LibraryViews.razor      nine [Route]s; picks LibraryRoutes.Find(Location.Path)
└─ LibraryPage (Config)               reads ?topParentId= and ?tab=, loads and saves the view settings
   ├─ LibraryToolbar                  view menu, count/paging, play all / shuffle / new, sort, filter, view settings
   └─ one view, by LibraryTabContent
      ├─ Content.Component            a custom component (extension point, section 5)
      ├─ LibrarySuggestionsView       LibraryTab.Suggestions: sections + movie recommendations
      ├─ LibraryUpcomingView          LibraryTab.Upcoming: upcoming episodes grouped by day
      ├─ LibraryGenresView            LibraryTab.Genres: one lazily loaded row per genre
      └─ LibraryItemsView             everything else: one page of items as cards or list rows, alphabet picker
```

- `LibraryViews` renders `<LibraryPage @key="config.Path" Config="config" />`, so changing between two library
  routes starts a fresh page; changing the tab or the library of one route re-keys only the view.
- The view is `Config.ViewAt(tab)`; without `?tab=` it is `Config.GetDefaultViewIndex(libraryId, settings)`, which
  honours the user setting `landing-<libraryId>` as the original does. Selecting a view navigates to `?tab=<index>`.
- The page title is the name of the library (`UserViews`), the page id is `Config.PageId` (`moviesPage`, ...).
- `LibraryItemsView` asks `LibraryQueryBuilder.Build` for the request (path + query, the original's
  `useGetItemsViewByType`), `LibraryData.GetPageAsync` for the page and reports the result to the toolbar
  (`OnLoaded`). Play all and shuffle pass the query of the view to the playback manager
  (`LibraryQueryBuilder.PlayQueryOptions`).
- The library page size is the display setting `libraryPageSize`; with 0 the request has no limit and the view
  shows everything, as the original does. The genres and upcoming views load their next chunk when a sentinel
  below them scrolls into view (`ILibraryInterop.WatchVisibilityAsync`).

## 2. Tab configuration

Everything a page shows is data in `Services/Library/LibraryConfig.cs`:

| Type | Role |
|------|------|
| `LibraryRouteConfig` | a page: `Path`, `PageId`, `CollectionType`, `BackdropTypes`, `Views` |
| `LibraryViewDefinition(Index, Label, Content, IsDefault)` | an entry of the view menu; `Label` is a translation key |
| `LibraryTabContent` | what a view lists and which toolbar buttons it has (the original's `LibraryTabContent`, same defaults) |
| `LibraryTab` | the view type strings (`movies`, `favorites`, `suggestions`, ...), which are also part of the settings key |
| `LibraryRoutes` | the nine configurations, `All` and `Find(path)` |

```csharp
public static readonly LibraryRouteConfig Movies = new()
{
    Path = "/movies",
    PageId = "moviesPage",
    CollectionType = "movies",
    BackdropTypes = ["Movie"],
    Views =
    [
        new(0, "Movies", new() { ViewType = LibraryTab.Movies, CollectionType = "movies", ItemTypes = ["Movie"],
                                 IsBtnPlayAllEnabled = true, IsBtnShuffleEnabled = true }, true),
        new(1, "Suggestions", new() { ViewType = LibraryTab.Suggestions, CollectionType = "movies",
                                      SuggestionSections = [LibrarySectionType.ContinueWatchingMovies, LibrarySectionType.LatestMovies],
                                      IsMovieRecommendations = true }),
        ...
    ]
};
```

The view type decides the rest through static tables that mirror the original's hooks and constants:
`LibraryQueryBuilder` (`Build`, `Fields`, `SortOptions`, `ImageTypes`, `FilterSections`, `FiltersQuery`),
`LibraryViewOptions` (`Cards`, `List`, `GenreCards`, `RecommendationCards`, `UpcomingCards`) and
`LibrarySections` (the suggestion sections). A new view of an existing kind needs a `LibraryTab` constant, an
entry in those tables and a `LibraryViewDefinition`.

### View settings

`LibraryViewSettings` (sort, start index, card layout, image type, grid/list, show title/year, filters, alphabet)
is stored by `LibraryViewSettingsStore` in `localStorage` under the original's key
`"<viewType> - <libraryId>"` (for example `movies - 7a2175bc...`), with the original's JSON shape, so both
clients share the settings of a browser. Every change is saved at once and resets the start index where the
original does.

## 3. Reusable components (`Components/Library/`)

| Component | Use |
|-----------|-----|
| `LibraryPage` | a whole library page for a `LibraryRouteConfig` |
| `LibraryToolbar` | the toolbar of a library page; sticky below the app bar (`.jf-library-toolbar`) |
| `LibraryViewMenu`, `LibrarySortButton`, `LibraryFilterButton` + `LibraryFilterPanel`, `LibraryViewSettingsButton`, `LibraryPagination`, `LibraryAlphabetPicker` | the toolbar parts (MudBlazor, as the original uses MUI); all take and return a `LibraryViewSettings` |
| `LibraryItemsView`, `LibrarySuggestionsView`, `LibraryUpcomingView`, `LibraryGenresView` | the built-in views; parameters `Content` and `ParentId` |
| `NoItemsMessage` | the legacy "nothing here" block (`.noItemsMessage`) |
| `AlphaPicker` | the legacy vertical alphabet picker (`.alphaPicker`), used by the list page |
| `ListPaging`, `ListSortDialog`, `ListFilterDialog`, `ListViewSettingsDialog` | the list page's paging block and dialogs (legacy markup) |
| `SearchFields`, `SearchSuggestions`, `SearchResults` | the parts of the search page |
| `MultiSelectToolbar` | the selection toolbar; a shell overlay (`[ShellSlot(ShellSlot.Overlay)]`), nothing to place by hand |

Port-specific classes are prefixed `jf-library-` (`jf-library-btn-play`, `-shuffle`, `-sort`, `-filter`,
`-viewsettings`, `-previous`, `-next`, `jf-library-view-button`, `jf-library-count`, ...); they are what the e2e
scripts select. Items are always drawn by `ItemsContainer` / `Card` / list rows of doc 05.

## 4. The list page (`/list`)

`Pages/Library/ListPage.razor` keeps the markup of `list.html` (element id `listPage`); all decisions are in the
static `ListPageModel` so they can be tested without rendering:

- `ListParameters.From(location)`: `type`, `parentId`, `genreId`, `musicGenreId`, `studioId`, `personId`,
  `artistId`, `tag`, `serverId` and the `Is...` flags.
- `Request(...)` builds the request for every case of the original's `fetchData` (folder children, items of a
  type, genre/studio/person/artist, favorites, `nextup`, `tag`, Live TV `Programs` and `Recordings`).
- `SortOptions`, `VisibleFilters`, `VisibleViewSettings`, `Title`, `ShowPlay`, `ShowShuffle`, `ShowAlphaPicker`,
  `Cards` mirror the functions of the same purpose in `list.js`.
- `ListPageSettings` stores sort, filters and view in the user settings under the original's keys
  (`ListPageModel.SettingsKey` + `-sortby`, `-sortorder`, `-filter-<name>`, `-showTitle`, `-imageType`, ...).

Program lists (`type=Programs`) reload every five minutes, as `data-refreshinterval` does.

## 5. Adding Live TV (or any other tabbed library page)

Nothing has to be registered. The Live TV workstream defines its own configuration and page:

```razor
@attribute [Route("/livetv")]
@attribute [Access(AccessLevel.User)]
<LibraryPage Config="LiveTvRoutes.LiveTv" />
```

```csharp
public static readonly LibraryRouteConfig LiveTv = new()
{
    Path = "/livetv",
    PageId = "liveTvPage",
    CollectionType = "livetv",
    Views =
    [
        new(0, "Programs", new() { ViewType = LibraryTab.Programs, Component = typeof(LiveTvProgramsView) }, true),
        new(1, "Guide", new() { ViewType = LibraryTab.Guide, Component = typeof(LiveTvGuideView) }),
        new(2, "Channels", new()
        {
            ViewType = LibraryTab.Channels,
            Request = context => new LibraryRequest("LiveTv/Channels", [("userId", context.UserId), ...]),
            ConfigureCards = (cards, settings) => cards with { ... }
        }),
        ...
    ]
};
```

The three extension points of `LibraryTabContent`:

- `Component`: a component type that draws the whole view (guide, programme sections, recordings, schedule). It
  gets the parameters `Content` (`LibraryTabContent`) and `ParentId` (`string?`). The toolbar still shows the
  view menu; switch the buttons off with the `IsBtn...Enabled` flags.
- `Request`: replaces `LibraryQueryBuilder.Build` for a view that is an ordinary paged item list from another
  endpoint. `LibraryQueryContext` carries the content, parent id, view settings, page size and user id.
- `ConfigureCards`: adjusts the card options after the built-in rules.

`LibraryTab` already has `Channels`, `Guide`, `Programs`, `Recordings`, `Schedule`, `SeriesTimers`; the ones in
`LibraryTab.MenuOnly` have no item list of their own, so they need a `Component`. With `?collectionType=livetv`
the view settings are stored under the key `livetv` instead of the library id, as in the original. The search
page already lists Live TV programmes and channels, and `/list?type=Programs|Recordings` works.

## 6. Multi-select and command conventions

`Services/Library/MultiSelect.cs`, `Components/Library/MultiSelectToolbar.razor`, `wwwroot/js/library.js`.

- `MultiSelectState` (one per app) holds the selection. Every `Card` attaches itself for its lifetime
  (`Attach(item, context, changed)`); while `IsActive`, a card adds the class `withMultiSelect` and the original's
  `itemSelectionPanel` with its check box, and a click on the card toggles it. Only cards take part, as in the
  original (list rows do not).
- A selection starts with a long press / long click on a card or, on desktop, ctrl/meta-click (document-level
  listeners in `library.js`, reported through `ILibraryInterop.WatchSelectionGesturesAsync`), or with the "Select"
  entry of the item menu (`MultiSelectCommandHandler` handles `ItemCommand.MultiSelect`). It ends with the close
  button, when the last item is deselected, after a command (other than "select all"), and on every navigation.
- `MultiSelectToolbar` draws `.selectionCommandsPanel` with the count; its menu comes from
  `MultiSelectCommands.MenuAsync()` (same entries, order and policy checks as `showMenuForSelectedItems`) and runs
  through `MultiSelectCommands.ExecuteAsync(command)`.
- Changes made to `ItemsContainer`: none. The additive changes are in `Components/Items/Card.razor` (attach,
  panel, class) and `Services/Items/ItemActions.cs` (`ItemActionContext.SelectedItems`).

Convention for commands about several items: the dialogs for collections, playlists and metadata refresh belong
to other workstreams and are reached through `ItemCommandHandlers` with the usual command and

```csharp
await handlers.ExecuteAsync(ItemCommand.AddToCollection, items[0], new ItemActionContext { SelectedItems = items });
```

- `SelectedItems == null`: the command is about its item alone (the existing behaviour).
- `SelectedItems` with items: the command applies to all of them (`AddToCollection`, `AddToPlaylist`, `Refresh`).
- `SelectedItems == []`: "new collection" / "new playlist" from the library toolbar and the list page; the item is
  a placeholder that only carries the server id.

Mark played/unplayed, delete, group versions and select all are done by `MultiSelectCommands` itself. After a
command the lists of the selected items reload through `ItemActionContext.NotifyRefreshNeeded`.

## 7. Search

`Pages/Search/SearchPage.razor` keeps the legacy markup (`#searchPage`, `searchFields`, `searchSuggestions`,
`searchResults`). The field follows `?query=` (the address is replaced while typing); the search itself runs
after `SearchService.Debounce` (500 ms). `SearchQueries.Requests` produces the original's requests per collection
type (`useSearchItems`, `usePeopleSearch`, Live TV, videos, artists), `Sections` and `Sort` group the results into
rows in the original's order. With `?parentId=` and `?collectionType=` the search stays in that library and a
search without results offers a "search everywhere" link. People and artists are looked up without the library, as
in the original. The shell's search button (`Components/Shell/ToolbarButtons.razor`) passes `parentId` /
`topParentId` and `collectionType` of the current page on.

## 8. Known gaps

Library pages load matching result items into the shared `Backdrop` service for routes that declare
`BackdropTypes`; the service is cleared when leaving the route or switching to a non-backdrop view.
Live TV channels use `ShowDetailsMenu`, songs use `SmallIcon`, and search rows use `Scalable` plus
`AllowBottomPadding = false`, matching the source option tables.

Deviations and limits:

- The library toolbar is a sticky bar at the top of the page content, not a second row of the fixed app bar.
- The Live TV views themselves (section 5) and the dialogs behind add to collection / playlist, refresh metadata
  and download are other workstreams; without a registered handler `ItemCommandHandlers` shows its "not available
  yet" toast.
- As in the original there is no "Download" in the multi-select menu (it is commented out there); ctrl/meta-click
  to start a selection is an addition.
- Shuffle of a filtered library view plays the fetched items with `SortBy=Random`.
- The list page (`/list`) adds `Recursive=true` when a filter is set (`ListPageModel`). The original sends the
  filtered query non-recursively, and Jellyfin 12.4 then applies user-data filters such as `IsFavorite` to nothing:
  `ParentId=<library>&Filters=IsFavorite` returns 0 items, the recursive query returns the favourites. So the
  original shows an empty list here.
- The list page stores the `SeriesStatus` filter but does not send it (as the original), and its view settings
  dialog offers image type and "show title" only.
- The TV layout's on-screen alphabet of the search field is not ported.
- `CardOptions` has no `showUnplayedIndicator` / `showLocationTypeIndicator`; the views rely on the defaults.
