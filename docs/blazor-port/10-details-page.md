# The item details page in the Blazor port

Reference for the details workstream: what exists, which pieces other pages can reuse, and what
is missing. The original is `src/controllers/itemDetails/` (template `index.html`, controller
`index.js`) plus `components/backdrop`, `components/itemMediaInfo`, `scripts/itemHelper.js`.

## 1. Structure

Route `/details` (`Pages/Details/ItemDetails.razor`), parameters `id`, `serverId`, `context`,
`seriesTimerId`, `genre`, `musicgenre`, `musicartist`. Every item gets its own page instance
(the router keys pages by their query), so the page loads once in `OnParametersSetAsync` and
scrolls to the top when it is created.

| Original | Port |
| --- | --- |
| `index.html` + `reloadFromItem`, `renderDetails`, the button handlers | `Pages/Details/ItemDetails.razor` |
| visibility rules of buttons and sections (`reloadPlayButtons`, `setInitialCollapsibleState`, `render*` guards) | `Services/Details/DetailsRules.cs` (pure functions, unit tested) |
| `getPromise`, the section queries (`renderChildren`, `renderSimilarItems`, `renderNextUp`, ...) | `Services/Details/DetailsQueries.cs`, `ItemsByName.cs` |
| `renderName`, `renderLinks`, `renderTags`, `renderDirector`, person lines | `Services/Details/DetailsText.cs` (returns the original HTML strings) |
| `renderTrackSelections`, `renderVideoSelections`, `renderAudioSelections`, `renderSubtitleSelections`, `getPlayOptions` | `Services/Details/TrackSelection.cs` + `Components/Details/TrackSelections.razor` |
| `renderDetailImage`, `renderImage` | `Components/Details/DetailImage.razor` |
| `renderOverview` (Markdown, "more" toggle) | `Components/Details/DetailOverview.razor` + `renderOverview` in `wwwroot/js/details.js` (marked + DOMPurify, as the original) |
| sections | `DetailSection` (frame: title, scroller or items container), `DetailChildren` (seasons, episodes, album tracks by disc, artist albums, folder children), `DetailCollectionItems`, `DetailPlaylistItems`, `DetailItemsByName` (person, genre, studio, artist), `DetailListSection`, `DetailScenes`, `DetailLyrics`, `DetailMetadataList` (genres, directors, writers, studios, tags lines) |
| the main buttons | `Components/Details/DetailButton.razor`; played and favorite are the shared `PlayStateButton` / `RatingButton` |
| `components/backdrop/backdrop.js` | `Services/Details/Backdrop.cs` + `wwwroot/js/backdrop.js` |

Markup, class names and translation keys are the original's. Sections that cannot apply to the
item type are not rendered (the original renders them hidden); applicable ones carry `hide` until
they have items. All main buttons are rendered, with `hide` when the rules say so. After a full
reload the sections are recreated (`@key` on a version counter).

Refresh: `ItemUserData.Changed` (played, favorite, resume position: buttons, header and lists),
`LibraryChanged` from the server (reload; navigate back when the item itself was removed), the
result of a context-menu command (`deleted` navigates back, anything else reloads), the layout
(desktop/phone) and the language. The document and app bar title is the item name; the app bar
is see-through above the banner except on phones.

Playback goes through `IPlaybackManager`: `PlayAsync` gets the item with `MediaSourceId`,
`AudioStreamIndex`, `SubtitleStreamIndex` from the track form (`TrackSelection.Apply`), the
resume position for `btnPlay` when the item can be resumed and 0 for `btnReplay`.

## 2. Reusable pieces

- **`Backdrop`** (`[AppService]`, inject it anywhere): `SetBackdrops(items, enabled)` shows the
  backdrop images of one or several items behind the page and rotates them every 10 s;
  `Clear()` removes them. Without `enabled: true` nothing shows unless the user turned on
  "Backdrops" in the display settings (`UserSettings.EnableBackdrops`), as in the original;
  the details page passes `true` only for what its design needs. `GetImageUrls(item)`,
  `GetBackdropUrl`, `GetImageUrl` give screen-sized image addresses (`getScaledImageUrl`).
  `backdrop.js` keeps the two layers the themes style (`.backdropContainer` with
  `.backdropImage`, `.backgroundContainer.withBackdrop`) first in `body` and steps aside while a
  video plays (`.transparentDocument`). Home and library pages only need the two calls.
- **`DetailsRules`**: static predicates per item type (`ShowDownload`, `ShowTrailerButton`,
  `ShowSplitVersions`, section visibility); usable wherever the same question comes up.
- **`TrackSelection`**: version/video/audio/subtitle options and defaults of an item's media
  sources, independent of the page.
- **`DetailsText`**: name lines with their links, artist links, external links, tags.
- **`DetailsLiveTv`**: the Live TV extension points are implemented:
  `RecordingFields(program)`, `SeriesTimerEditor(timer)`, `SeriesTimerSchedule(timer)` and
  `ChannelGuide(channel)` render the original controls and lists. `CancelTimerAsync` and
  `CancelSeriesTimerAsync` confirm and call the matching Live TV delete endpoint. The page decides
  when each shows with the original rules.
- `PlayStateButton` and `RatingButton` (`Components/Items`) gained an optional `ContentClass`
  parameter (the `div.detailButton-content` around the icon).
- `Components/Shell/UserViewNav.razor` highlights the library of the item on `/details`: it asks
  `Items/{id}/Ancestors` once per item and takes the first `CollectionFolder`.

## 3. Styles

`Styles/port/_details.scss` holds only what the original does in script or what the bundle of
this port changes:

- backdrop layers hidden while a video plays;
- the collection sections stack (`.jf-collection-items`);
- **text metrics**: MudBlazor's stylesheet sets its typography and the primary text colour on
  `body` and resets `margin`/`padding` on every element. The original inherits 93% font size,
  line height 1.35 and the theme's *secondary* text colour from `html` and relies on browser
  margins for `p`, `h1`-`h6`, lists. `#itemDetailPage` restores these (the margins with
  `:where()`, so every original rule still wins). Measured against the stock client at 1366 and
  400 px width the page is now identical in geometry. The same difference affects every ported
  page; the general fix belongs in the shell (see gaps);
- `styles/videoosd.scss` hides `.btnUserRating` below 30em with `!important`; the original loads
  that file with the OSD only, the port's bundle always has it, so the page re-shows its button.

## 4. Tests

- `tests/Jellyfin.Web.Tests/Details/`: `DetailsRulesTests` (buttons and sections per item
  type), `TrackSelectionTests` (defaults, hidden/disabled selects, play options),
  `DetailsTextTests` (name lines, links), `BackdropTests` (setting, rotation, URLs),
  `ItemDetailsPageTests.razor` (bUnit with `FakeHttp`: movie, series, season, episode, album,
  artist, song, playlist, collection, person, genre and music genre by name, programme, series
  timer; refresh and navigation).
- `e2e/details.py` (`./dev e2e details.py`): every item type of the test server at desktop and
  phone width, track selection and playback with it, played/favorite, the menu, and reference
  screenshots of the stock client (`details-reference-*.png`). It restores the user data and the
  remembered tracks it changes. The test server has no genres and no Live TV, so those pages are
  covered by the bUnit tests only.

## 5. Gaps

- Live TV details controls are ported. The only intentional deviation is that the channel guide
  uses the Blazor list container and groups by the browser's local calendar day; it keeps the
  original classes, labels, fields and request parameters.
- `PORT-TODO(details)`: the item's own website (`HomePageUrl`) is not in the SDK model, so it is
  missing from the external links.
- Shared, not of this page: list-row overviews are plain text (`ListView`), scroller sections
  scroll natively (`Scroller`), the context menu opens centred, people cards carry no
  blurhash. Dialogs reached from the menu belong to the item-dialog workstream.
- Playlist entries are reordered with "Up"/"Down" row buttons (the same
  `Playlists/{id}/Items/{entry}/Move/{index}` request) instead of drag and drop.
- Shell: the MudBlazor typography/reset described above is corrected for this page only. The
  general fix is `ThemeManager.BuildMudTheme` (`DefaultTypography`: font size `1rem`, line height
  `1.35`, letter spacing `normal`), `body { color: inherit }` and restoring the browser margins
  the `*` reset removes. The router scrolls to the top only when the path changes, not when a
  page of the same path is recreated for another query; this page does it itself.
- Things the original does not have at this commit and the port therefore does not add: an
  "edit images" button on the poster, indicators on the poster, a resume/restart dialog
  (`btnPlay` resumes, `btnReplay` starts over), a songs section on the artist page.
