I've written the spec below. Everything comes from reading the repo source, except the Material Icons font CSS: `node_modules` isn't installed, so that part relies on knowledge of the package. Paths are relative to `/home/user/jellyfin-web/src`.

Two cross-cutting points first:
- **Utility classes outside SCSS.** `.hide`, `.hide-mobile`, `.hide-desktop`, `.hide-tv`, `.hide-mouse-idle` and `.transparentDocument` are defined in an inline `<style>` in `index.html`, not in any `.scss` file. You must copy that block.
- **Element upgrades.** Every `is="emby-*"` attribute is a v0 custom-element upgrade (`document.registerElement`). For static Razor output, emit the post-upgrade markup listed in section 4.

---

## 1. Cards (`components/cardbuilder/cardBuilder.js`, `utils/builder.ts`, `utils/url.ts`, `utils/shape.ts`, `card.scss`)

### DOM of one card
Non-TV layout, poster with an image, mobile overlay play button, indicators and an outer footer (`buildCard`, cardBuilder.js:736-989):
```html
<div data-index="0" [data-timerid] [data-seriestimerid] data-isfolder="false" data-serverid="…" data-id="…"
     data-type="Movie" [data-mediatype="Video"] [data-collectiontype] [data-channelid] [data-path]
     [data-positionticks] [data-collectionid] [data-playlistid] [data-context] [data-parentid]
     [data-startdate] [data-enddate] data-prefix="ABC"
     class="card portraitCard [cardCssClass] [cardClass] card-hoverable(desktop) show-focus show-animation(tv) groupedCard card-withuserdata">
  <div class="cardBox [visualCardBox] [cardBox-bottompadded]">
    <div class="cardScalable">
      <div class="cardPadder cardPadder-portrait"><span class="cardImageIcon material-icons movie" aria-hidden="true"></span></div>
      <a href="#/details?id=…" data-action="link" class="cardImageContainer [coveredImage] [coveredImage-contain] cardContent itemAction lazy"
         data-src="IMGURL" [data-blurhash="…"] aria-label="Name" role="img">
        <div class="cardIndicators">…indicators…</div>
        <!-- no image: class adds "defaultCardBackground defaultCardBackgroundN"; content is the getDefaultText icon/text -->
        <div class="innerCardFooter fullInnerCardFooter innerCardFooterClear"><div class="itemProgressBar">…</div></div>
        <div class="mediaSourceIndicator">2</div>
      </a>
      <!-- mobile only -->
      <button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-br itemAction" data-action="play" title="Play">
        <span class="material-icons cardOverlayButtonIcon play_arrow" aria-hidden="true"></span></button>
      <!-- desktop only (unless disableHoverMenu) -->
      <div class="cardOverlayContainer itemAction" data-action="link">
        <button is="paper-icon-button-light" class="cardOverlayButton cardOverlayButton-hover itemAction paper-icon-button-light cardOverlayFab-primary" data-action="resume">…play_arrow…</button>
        <div class="cardOverlayButton-br flex">
          <button is="emby-playstatebutton" type="button" data-action="none" class="…same btn classes" data-id data-serverid data-itemtype data-played>…check…</button>
          <button is="emby-ratingbutton" type="button" data-action="none" … data-likes data-isfavorite>…favorite…</button>
          <button is="paper-icon-button-light" … data-action="menu" title="More">…more_vert…</button>
        </div>
      </div>
    </div>
    <div class="cardFooter cardFooter-transparent [cardFooter-withlogo]">   <!-- wrapper only when cardLayout or logo -->
      [<div class="lazy cardFooterLogo" data-src="…"></div>]
      [<button is="paper-icon-button-light" class="itemAction btnCardOptions cardText-secondary" data-action="menu">…more_vert…</button>]
      <div class='cardText cardText-first [cardTextCentered] [cardText-rightmargin]'><bdi><a href="…" data-id… class="itemAction textActionButton" title="X" data-action="link">X</a></bdi></div>
      <div class='cardText cardText-secondary'><bdi>2019</bdi></div>
    </div>
  </div>
</div>
```

**Variants**
- **TV layout:** the outer tag is `<button>` (when there are no overlay buttons) with classes `itemAction` and `data-action`. The image container is a `<div>`, not an `<a>`, with no href or aria. There is no hover menu.
- **Outer footer wrapper:** the `.cardFooter` div is emitted only if `cardLayout` is set or a logo exists. Otherwise the `cardText` divs sit directly inside `.cardBox`.
- **`overlayText`:** lines go into `<div class="innerCardFooter [fullInnerCardFooter]">` inside the image container, and there is no outer footer.
- **Indexing:** with `indexBy`, cards are grouped as `<div class="horizontalSection|verticalSection"><div|h2 class="sectionTitle">…`. With `autoVertical` shape, a `<div class="itemsContainer vertical-wrap">` is added.
- **Rows:** `rows` wraps cards in `<div class="cardColumn">`.

**Class resolvers** (builder.ts:90-128):
- Card: `card`, `${shape}Card`, `card-hoverable` (desktop), `show-focus` / `show-animation` (TV; animation unless `browser.slow`/`edge`), `groupedCard` (`showChildCountIndicator` && ChildCount), `card-withuserdata` (all types except MusicAlbum, MusicArtist, Audio), `itemAction` (button tag).
- Default colour: `defaultCardBackground1..5`, computed from the middle character code of the name (builder.ts:142).

### Options (`types/cardOptions.ts`; behaviour in cardBuilder.js)
- `shape`: see the shapes subsection below.
- `defaultShape`: used when auto-shape finds no aspect ratio.
- `showTitle`:
  - `true`/`'auto'` show the title line.
  - It is forced on for PhotoAlbum and Folder.
  - With `'auto'`, Photo items get a blank name.
- `showParentTitle`:
  - Adds SeriesName, Album or AlbumArtist above the title.
  - For MusicAlbum, Audio and MusicVideo it goes underneath, as linked AlbumArtists.
- `showParentTitleOrTitle`: parent title, or the title if there is no parent.
- `showYear` / `showSeriesYear`:
  - Shows `ProductionYear`.
  - For Series: "YYYY - Present" if Continuing, otherwise "start - end".
- `showPremiereDate`, `showRuntime`, `showAirTime` (+`showAirDateTime`, `showAirEndTime`), `showChannelName`, `showCurrentProgram`, `showCurrentProgramTime`, `showSeriesTimerTime`, `showSeriesTimerChannel`, `showPersonRoleOrType`, `showItemCounts`, `showSongCount`, `textLines(item)`: each appends one line (cardBuilder.js:439-582).
- `includeParentInfoInTitle`: passed to `itemHelper.getDisplayName`.
- `overlayText`: inner footer instead of outer; with `showTitle` the lines are replaced by just the Name.
- `centerText`: adds `cardTextCentered`.
- `cardLayout`:
  - `visualCardBox`, and footer class `cardFooter` without `-transparent`.
  - Enables the logo.
  - On mobile, adds the `btnCardOptions` button and `cardText-rightmargin` unless `cardFooterAside === 'none'`.
- `lines`: maximum number of lines.
  - When `overlayText` is false, missing lines are padded with `<div class='cardText'>&nbsp;</div>`.
- `coverImage`: adds `coveredImage`, plus `coveredImage-contain` for TvChannel. It is also set automatically when the image ratio is within 20% of the shape ratio, and for auto→banner.
- `preferThumb` (`true`/`'auto'` = backdrop shapes), `preferBanner`, `preferDisc`, `preferLogo`, `preferParentPoster`, `inheritThumb`: image selection, see below.
- `overlayPlayButton`:
  - Mobile only.
  - Defaults to `MediaType==='Video'` when no other overlay or `cardLayout` option is set.
  - Hidden for placeholders, virtual items and Person.
- `overlayMoreButton`, `centerPlayButton` (`cardOverlayButton-centered`): mobile only.
- `disableHoverMenu`, `disableIndicators`, `missingIndicator:false`, `showGroupCount` (count indicator replaces the played indicator).
- `showChannelLogo` / `showLogo`: footer logo at height 40.
- `action`:
  - Default `link`.
  - `play` on a folder becomes `link`.
  - Photos always use `play`.
- `context`, `parentId`, `collectionId`, `playlistId`, `serverId`: data attributes.
- `width` / `widths` / `rows`, `sectionTitleTagName`, `indexBy`: PremiereDate, ProductionYear or CommunityRating.
- `cardCssClass` / `cardClass`.
- Declared but unused by the legacy builder: `lazy`, `scalable`, `allowBottomPadding`, `showDetailsMenu` (React only), `transition`.

### Shapes and the auto shape (shape.ts, cardBuilder.js:80-133)
- **Enum:** `backdrop`, `overflowBackdrop`, `banner`, `portrait`, `overflowPortrait`, `square`, `overflowSquare`, `auto`, `autohome`, `autooverflow`, `autoVertical`, `mixed`, `mixedSquare`, `mixedBackdrop`, `mixedPortrait`.
- **SCSS-only shapes:** `smallBackdrop` and `overflowSmallBackdrop` also exist (card.scss:58-83, 515, 675).
- **Auto shapes** (`auto*`) use the median `PrimaryImageAspectRatio` of the items:
  - ≥3 → `banner` + coverImage.
  - ≥1.33 → backdrop.
  - >0.8 → square.
  - else → portrait.
  - The `overflow*` variant is used only for `autooverflow`.
  - Fallback: `defaultShape`, then square.
- **`mixed`:** resolved per item to `mixedBackdrop`, `mixedSquare` or `mixedPortrait`.
- **Aspect ratios:** portrait 2/3, backdrop 16/9, square 1, banner 1000/185 (builder.ts:8).
- **Image width:** `screenWidth / postersPerRow(shape)`. The tables are in builder.ts:195-320; `screenWidth` is floored to 100 when the window is resizable.

### Image URL selection (`utils/url.ts:19-181`)
- **Program items:** `item.ProgramInfo` is used if present.
- **Priority order:**
  1. `preferThumb` && Thumb
  2. (`preferBanner` || shape `banner`) && Banner
  3. `preferDisc` Disc
  4. `preferLogo` Logo, then ParentLogo
  5. `preferThumb`: SeriesThumb, then ParentThumb (not for Photo), then own Backdrop[0] (sets `forceName`), then ParentBackdrop (Episode only). Parent sources are skipped when `inheritThumb === false`.
  6. `preferParentPoster` (Episode, not `preferThumb`): ParentPrimary, else SeriesPrimary
  7. own Primary (not an Episode with ChildCount 0)
  8. SeriesPrimary (skipped for Episode when shape aspect >1)
  9. `PrimaryImageTag` / `PrimaryImageItemId`
  10. ParentPrimary
  11. AlbumPrimary
  12. Season Thumb
  13. Backdrop[0]
  14. Thumb
  15. SeriesThumb
  16. ParentThumb
  17. ParentBackdrop
- **URL:** `GET /Items/{id}/Images/{type}?fillHeight=ceil(h*dpr)&fillWidth=ceil(w*dpr)&quality=96&tag=…`.
  - `h` = width / item ratio for Primary types; otherwise width / shape ratio.
- **Blurhash:** `(options.imageBlurhashes || item.ImageBlurHashes)[imgType][imgTag]`.

### Blurhash (`components/images/imageLoader.js`)
- **Gate:** only runs if `userSettings.enableBlurhash()`.
- **Decode:** each `.lazy[data-blurhash]` is decoded at 20×20 in a worker. A `<canvas class="blurhash-canvas" aria-hidden="true">` is inserted before the element, the element gets `blurhashed`, and the attribute is removed.
- **Elements without a hash** get `non-blurhashable`.
- **Load sequence:**
  1. The element gets `lazy-hidden`.
  2. On load, `style.backgroundImage=url(…)` (or `src` for `<img>`) is set, `data-src` is removed, and `lazy-image-fadein` (0.5s) or `lazy-image-fadein-fast` is added.
  3. On animationend the canvas gets `lazy-hidden`, and `.cardPadder` gets `lazy-hidden-children` (hides the placeholder icon).
- Styles are in `components/images/style.scss`.
- Lazy loading uses an IntersectionObserver (`lazyLoader/lazyLoaderIntersectionObserver`).

### Default placeholder (`getDefaultText`, cardBuilder.js:1037)
- **Markup:** `<span class="cardImageIcon material-icons {icon}">`; otherwise `<div class="cardText cardDefaultText">Name</div>`.
- **When an image exists:** the same icon is placed in `.cardPadder` (default `folder`), except for TvChannel.
- **Library icons** (`utils/image.ts:98`):

| CollectionType | Icon |
|---|---|
| movies | `movie` |
| music | `music_note` |
| homevideos, photos | `photo` |
| livetv | `live_tv` |
| tvshows | `tv` |
| trailers | `theaters` |
| musicvideos | `music_video` |
| books | `book` |
| boxsets | `video_library` |
| playlists | `queue` |
| channels | `videocam` |
| undefined | `ondemand_video` |
| anything else | `folder` |

- **Item-type icons** (`utils/image.ts:130`):

| Item type | Icon |
|---|---|
| MusicAlbum | `album` |
| MusicArtist, Person | `person` |
| Audio | `audiotrack` |
| Movie | `movie` |
| Episode, Series | `tv` |
| Program | `live_tv` |
| Book | `book` |
| Folder | `folder` |
| BoxSet | `video_library` |
| Playlist | `queue` |
| Photo | `photo` |
| PhotoAlbum | `photo_album` |

### People and chapter cards
- **`peoplecardbuilder.js`:** forces `cardLayout:false`, `centerText`, `showTitle`, `cardFooterAside:'none'`, `showPersonRoleOrType`, `cardCssClass:'personCard'`, `defaultCardImageIcon:'person'`.
  - Role text: "as {Role}" (wrapped in `<span title>`) for Actor/GuestStar; merged crew roles are joined with " / ".
- **`chaptercardbuilder.js`:**
```html
<button type="button" class="card itemAction chapterCard overflowBackdropCard [block]" data-action="play" data-isfolder data-id data-serverid data-type data-mediatype data-positionticks="…">
 <div class="cardBox"><div class="cardScalable"><div class="cardPadder-overflowBackdrop"></div>
  <div class="cardContent cardContent-shadow cardImageContainer chapterCardImageContainer [coveredImage] lazy" data-src="/Items/{id}/Images/Chapter/{i}?maxWidth=400&tag=…">[local_movies icon if none]</div></div>
  <div class="innerCardFooter"><div class="cardText">Name</div><div class="cardText">0:05:00</div></div></div></div></button>
```
  - The shape becomes square when the video aspect ratio is ≤1.2.
  - Note: the source emits one stray extra `</div>` (chaptercardbuilder.js:107).

### React cards (`cardbuilder/Card/*`)
The structure is mostly the same, with these differences:
- `cardContent` is a wrapper `div` around a `div.cardImageContainer`.
- Indicators sit in `div.indicators > div.cardIndicators`.
- Mobile overlay buttons are inside an absolutely positioned `<a>`.
- The hover menu contains `<a class="cardImageContainer">`.
- **Likely inverted class:** `CardOuterFooter.tsx` adds `cardFooter-transparent` when `cardLayout` is **true**, the opposite of the legacy builder. Follow the legacy builder.

### Click / `data-action` mechanism (`components/shortcuts.js`)
- **Binding:** `emby-itemscontainer` binds a click handler.
- **Click flow:**
  1. Find the nearest ancestor with class `.itemAction`.
  2. Read its `data-action`, or the nearest ancestor `data-action`.
  3. If the action isn't `none`, call `executeAction(card, actionElement, action)`.
- **Item reconstruction:** the item is rebuilt from the nearest `[data-id]` attributes: `data-type`, `id`, `timerid`, `collectiontype`, `channelid`, `seriesid`, `serverid`, `mediatype`, `path`, `isfolder`, `startdate`, `enddate`, `positionticks`.
- **Actions** (`constants/itemAction.ts`):

| Action | Behaviour |
|---|---|
| `link` | Navigates via `appRouter.showItem` with `data-context` / `data-parentid` (Photo becomes play). |
| `play`, `resume` | Plays from `data-positionticks`. Program items play the channel. Uses sort settings `items-{id}-Folder`. |
| `queue` | Queues the item. |
| `playallfromhere`, `queueallfromhere` | Use sibling cards, or `itemsContainer.fetchData`. |
| `instantmix` | Starts an instant mix. |
| `menu` | Item context menu. `data-playoptions="false"` hides the play entries. |
| `playmenu` | Opens the play menu. |
| `edit` | Metadata editor, or the recording editor for timers. |
| `playtrailer` | Plays local trailers. |
| `addtoplaylist` | Opens the playlist editor. |
| `record` | Toggles recording. |
| `programdialog` | Opens the program dialog. |
| `setplaylistindex` | Uses `data-playlistitemid`. |
| `custom` | Dispatches `action-{data-customaction}`. |
| `none` | Stops propagation and does nothing. |

- **Text links:** `getShortcutAttributesHtml` emits the data attributes used on text links.
- **Right-click:** opens the menu on non-touch devices; `data-contextmenu="false"` disables it.

---

## 2. Indicators (`components/indicators/indicators.js`, `indicators.scss`)
- **Played:** `<div class="playedIndicator indicator"><span class="material-icons indicatorIcon check"></span></div>`. Shown when `canMarkPlayed` and (Played or PlayedPercentage ≥100).
  - `canMarkPlayed` = Video media (except TvChannel), AudioBook, Series, Season, BoxSet, Folder, or Book media; never Program (itemHelper.js:247).
- **Unplayed count:** `<div class="countIndicator indicator">N</div>`. Shown when UnplayedItemCount is set; takes priority over played. Counts of 100 or more show `99+`.
  - Child count: same markup, from ChildCount > minCount (when `showGroupCount`).
- **Progress bar:**
```html
<div class="itemProgressBar"><div class="itemProgressBarForeground" style="width:42%;"></div></div>
```
  - Shown for Video (not TvChannel), Book or AudioBook when `0 < PlayedPercentage < 100`.
  - For Program, Timer and Recording with start and end dates:
```html
<div is="emby-progressbar" data-automode="time" data-starttime data-endtime class="itemProgressBar">
  <div class="itemProgressBarForeground [itemProgressBarForeground-recording]" …>
```
  - The `is="emby-progressbar"` element updates its width every 60 seconds.
- **Timer:** `<span class="material-icons timerIndicator [timerIndicator-inactive] indicatorIcon fiber_manual_record|fiber_smart_record">`.
  - SeriesTimer or active series timer → `fiber_smart_record`.
  - Cancelled series timer → adds `-inactive`.
  - Otherwise `fiber_manual_record`.
- **Missing:** Episode with LocationType Virtual → `<div class="missingIndicator">Missing</div>`, or `<div class="unairedIndicator">Unaired</div>` if the premiere is in the future. The text is not localised.
- **Type:** `<div class="indicator videoIndicator"><span class="material-icons indicatorIcon videocam|folder|photo_album|photo">` for Type Video, Folder, PhotoAlbum or Photo.
- **Sync:** `syncIndicator indicator fullSyncIndicator|emptySyncIndicator` with a `file_download` icon, when SyncPercent is set.
- **Refresh (CollectionFolder or CollectionType):** `<div is="emby-itemrefreshindicator" [class="hide"] data-progress data-status>`, which becomes a progress ring (section 4).
- **Card order:** missing, sync, timer, type, played-or-count, refresh. Then `mediaSourceIndicator` (MediaSourceCount > 1) in the inner footer.

---

## 3. List view (`components/listview/listview.js`)
```html
<h2 class="listGroupHeader [listGroupHeader-first]">A</h2><div>   <!-- showIndex groups -->
<div|button(tv) class="listItem [listItem-border] [itemAction listItem-button](tv) [listItem-focusscale](tv) [listItem-largeImage] [listItem-withContentWrapper]"
     [data-playlistitemid] data-action="link" data-isfolder data-id data-serverid data-type [data-mediatype …]>
  [<div class="listItem-content">]
  [<span class="listViewDragHandle material-icons listItemIcon listItemIcon-transparent drag_handle">]
  <div data-action="link" class="listItemImage [listItemImage-large] [listItemImage-channel] [listItemImage-large-tv] itemAction lazy" data-src="…" item-icon>
     <!-- no image: class adds "cardImageContainer defaultCardBackgroundN" + default icon -->
     [<div class="mediaSourceIndicator">2</div>]
     [<div class="indicators listItemIndicators">played/count</div>]
     [<button is="paper-icon-button-light" class="listItemImageButton itemAction" data-action="resume"><span class="material-icons listItemImageButton-icon play_arrow">]
     [<div class="itemProgressBar listItemProgressBar">…]
  </div>
  [<div class="listItem-indexnumberleft">3</div>]
  <div class="listItemBody itemAction [listItemBody-noleftpadding]">
     <div class="listItemBodyText"><bdi>Title</bdi></div>   <!-- h2 when imageSize large on TV -->
     <div class="secondary listItemBodyText"><bdi>…</bdi></div>
     [<div class="secondary listItemMediaInfo listItemBodyText">mediainfo</div>]   <!-- enableSideMediaInfo=false -->
     [<div class="secondary listItem-overview listItemBodyText"><bdi>markdown</bdi></div>]
  </div>
  [<div class="secondary listItemMediaInfo">mediainfo</div>]   <!-- side media info (default) -->
  [timer icon with "indicatorIcon listItemAside"]
  <div class="listViewUserDataButtons">  <!-- buttons omitted on TV -->
     [playlist_add / info_outline / rightButtons(data-action="custom" data-customaction)]  class="listItemButton itemAction"
     <button is="emby-playstatebutton" type="button" class="listItemButton paper-icon-button-light" data-id data-serverid data-itemtype data-played><span class="material-icons check">
     <button is="emby-ratingbutton" … data-likes data-isfavorite><span class="material-icons favorite">
     <button is="paper-icon-button-light" class="listItemButton itemAction" data-action="menu">more_vert
  </div>
  [</div><div class="listItem-bottomoverview secondary"><bdi>…</bdi></div>]
</div>
```
- **Image URL:** `fillWidth`/`fillHeight` 80 (500 for large), Primary.
  - Fallback order: own Primary, then Album, then Series, then ParentPrimary; or the channel image when `imageSource:'channel'`.
- **Text lines:**
  1. `showProgramDateTime` / `showProgramTime`
  2. `showChannel`
  3. `showParentTitle`: SeriesName for Episode, or Name for IsSeries
  4. Display name, with "N. " when `showIndexNumber`
  5. AlbumArtist for MusicAlbum (folders), or ArtistItems when `artist`
  6. CurrentProgram for channels
- **Options:** `index:'disc'`, `sortBy` (group key: sortname letter, officialrating, community/critic rating, albumartist), `border`, `highlight`, `dragHandle`, `image`, `imageSize`, `imagePlayButton`, `showIndexNumberLeft`, `mediaInfo`, `enableSideMediaInfo`, `enableOverview`, `addToListButton`, `infoButton`, `rightButtons`, `enableUserDataButtons`, `enablePlayedButton`, `enableRatingButton`, `moreButton`, `recordButton`, `action`, `includeParentInfoInTitle`, `parentTitleWithTitle`, `collectionId`, `playlistId`, `disableIndicators`.

---

## 4. Elements: markup to render statically
| Element | Classes and DOM added on upgrade | Behaviour |
|---|---|---|
| `button is="emby-button"`, `a is="emby-linkbutton"` | adds `emby-button` (+`show-focus` on TV) | Anchors route through `appRouter.show(href)`; `data-autohide="true"` hides when external links aren't supported. Style variants used: `raised`, `button-flat`, `button-flat-mini`, `button-link`, `block`, `fab`, `emby-button-foreground`. |
| `button is="paper-icon-button-light"` | adds `paper-icon-button-light` (+`show-focus` on TV) | Icon button. Child: `<span class="material-icons {name}">`. |
| `div is="emby-itemscontainer"` | adds `itemsContainer`; `itemsContainer-tv` on TV | Click delegation to shortcuts, context menu, multi-select, drag reorder (`data-dragreorder`), websocket refresh (`data-monitor`, `data-refreshinterval`, `data-parentid`), `fetchData`/`getItemsHtml` render. |
| `div is="emby-scroller"` with child `.scrollSlider` | adds `emby-scroller`. Native mode: `scrollX` + (`hiddenScrollX` unless desktop-with-scrollbar) [+`smoothScrollX` on TV] [+`hiddenScrollX-forced` when scroll buttons are on]. Transform mode: `animatedScrollX` on the slider. Slider gets `white-space:nowrap`. Attribute `data-scroll-mode-x="custom"`. | Horizontal scroller (`data-horizontal="false"` makes it vertical); `data-centerfocus` (TV); `data-scrollbuttons="false"`. |
| `emby-scrollbuttons` | Inserted before the scroller on desktop with a fine pointer: `<div is="emby-scrollbuttons" class="emby-scrollbuttons padded-right">` with two `<button is="paper-icon-button-light" data-ripple="false" data-direction="left\|right" class="emby-scrollbuttons-button"><span class="material-icons chevron_left\|chevron_right">`. The parent gets `emby-scroller-container`. | Buttons are hidden or disabled based on scroll position. |
| `div is="emby-tabs"` | adds `emby-tabs focusable`; with no `.emby-tabs-slider` child: `scrollX hiddenScrollX smoothScrollX` | Children: `.emby-tabs-slider > button.emby-tab-button[data-index]`. Active button gets `emby-tab-button-active`; last-focused gets `lastFocused`. Fires `beforetabchange`/`tabchange`. |
| `input is="emby-input"` | adds `emby-input`, auto id `embyinputN`; inserts before it `<label class="inputLabel inputLabelUnfocused [inputLabel-float]" for=id>{label attr}</label>` | Label toggles `inputLabelFocused`; floats when empty. Wrap in `.inputContainer`. |
| `select is="emby-select"` | adds `emby-select-withcolor emby-select` (+`emby-select-focusscale` on TV); inserts `<label class="selectLabel" for>` before it; appends `<div class="selectArrowContainer"><div style="visibility:hidden;display:none;">0</div><span class="selectArrow material-icons keyboard_arrow_down"></span></div>` to the parent | Wrap in `.selectContainer`. Uses the action sheet instead of the native menu on TV. Label gets `selectLabelFocused`. |
| `input type=checkbox is="emby-checkbox"` inside `<label><input/><span>text</span></label>` | input gets `emby-checkbox`, `data-embycheckbox="true"`; label gets `emby-checkbox-label`; span gets `checkboxLabel`; appends `<span class="checkboxOutline [data-outlineclass]"><span class="material-icons checkboxIcon checkboxIcon-checked check"></span><span class="material-icons checkboxIcon checkboxIcon-unchecked "></span></span>` | Enter toggles. Usually wrapped in `.checkboxContainer`. |
| `input type=radio is="emby-radio"` inside `<label><input/><span/></label>` | input gets `mdl-radio__button`, `data-radio`; label gets `mdl-radio mdl-js-radio mdl-js-ripple-effect` (+`show-focus` unless mobile); span gets `radioButtonLabel mdl-radio__label`; after the input: `<div class="mdl-radio__circles"><svg>…<circle class="mdl-radio__outer-circle"…/><circle class="mdl-radio__inner-circle"…/></svg>[<div class="mdl-radio__focus-circle"></div>]</div>` | Radio button. |
| `input type=checkbox is="emby-toggle"` inside a label | input gets `mdl-switch__input`; label gets `mdl-switch mdl-js-switch`; span gets `toggleButtonLabel mdl-switch__label`; appends `<div class="mdl-switch__trackContainer"><div class="mdl-switch__track"></div><div class="mdl-switch__thumb"><span class="mdl-switch__focus-helper"></span></div></div>` | Switch. |
| `textarea is="emby-textarea"` | adds `emby-textarea`, `rows=1`; inserts `<label class="textareaLabel" for>` before it | Grows with content; label gets `textareaLabelFocused`/`Unfocused`. |
| `input type=range is="emby-slider"` | input gets `mdl-slider mdl-js-slider` [`slider-browser-edge`] [`mdl-slider-hoverthumb` unless mobile] [`show-focus` on TV]; parent gets `mdl-slider-container`; appends `<div class="mdl-slider-background-flex-container"><div class="mdl-slider-background-flex"><div class="mdl-slider-background-flex-inner"><div class="mdl-slider-background-upper"></div><div class="mdl-slider-background-lower"></div></div></div></div><div class="sliderBubbleTrack"><div class="sliderBubble hide"></div></div>`; label `sliderLabel` inside `.sliderContainer-settings` | Seek slider with bubble (`<h1 class="sliderBubbleText">`), chapter markers `sliderMarker watched/unwatched`. |
| `div is="emby-collapse" title="…"` with child `.collapseContent` | adds `emby-collapse`; content gets `hide`; prepends `<button is="emby-button" type="button" class="emby-collapsible-button iconRight"><h3 class="emby-collapsible-title" title>T</h3><span class="material-icons emby-collapse-expandIcon expand_more"></span></button>` | Expanding: content `expanded`, icon `emby-collapse-expandIconExpanded`; `data-expanded="true"` starts open. |
| `button is="emby-ratingbutton"` | `emby-button` + `ratingbutton-withrating` when favourite; icon gets `favorite ratingbutton-icon-withrating`; title "Favorite"/"Add to favorites" | Toggles favourite (`data-isfavorite`, `data-likes`). |
| `button is="emby-playstatebutton"` | `emby-button` + `playstatebutton-played`; icon `playstatebutton-icon-played`/`-unplayed`; title "Watched"/"Played"/"Mark played" | Toggles played (`data-played`). |
| `div is="emby-progressring"` | adds `progressring`, `dir="ltr"`, innerHTML from `emby-progressring.template.html` (`progressring-bg > progressring-text`, four `spiner-holder-one animate-X-Y-a > spiner-holder-two animate-X-Y-b > progressring-spiner`) | Rotates `-b` quarters from `data-progress`; text shows the percentage. |
| `div is="emby-itemrefreshindicator"` | same as the progress ring | Shown while a RefreshProgress websocket message is between 0 and 100. |

**React equivalents:** `elements/emby-button/{Button,IconButton,LinkButton}.tsx` emit the same classes directly. For example, LinkButton = `emby-button` + className + `show-focus` (TV).

---

## 5. Home screen (`components/homesections/*`, `apps/legacy/controllers/home.js`, `hometab.js`, `home.html`)
- **Page:**
```html
<div id="indexPage" class="page homePage libraryPage allLibraryPage backdropPage pageWithAbsoluteTabs withTabs">
  <div class="tabContent pageTabContent" id="homeTab" data-index="0"><div class="sections"></div></div>
  <div class="tabContent pageTabContent" id="favoritesTab" data-index="1">…</div>
</div>
```
  - Tabs are Home and Favorites.
- **Container:** `.sections` gets `homeSectionsContainer` and 10 children `<div class="verticalSection section{i}">` (11 on TV).
- **Section settings:** `userSettings.get('homesection'+i)`; `folders` maps to the default for index 0.
- **Default order** (`constants/homeSectionType.ts`): `smalllibrarytiles`, `resume`, `resumeaudio`, `resumebook`, `livetv`, `nextup`, `latestmedia`, `none`×3.
  - TV prepends `smalllibrarytiles` if no library section is configured.
- **Data:** `GET /UserViews?userId`. If empty, renders `<div class="centerMessage padded-left padded-right"><h2>…</h2><p>…</p></div>`.
- **Common section scaffold** (`enableOverflow` is always true):
```html
<div class="sectionTitleContainer sectionTitleContainer-cards padded-left">
  <a is="emby-linkbutton" href="…" class="[more] button-flat button-flat-mini sectionTitleTextButton">
    <h2 class="sectionTitle sectionTitle-cards">Title</h2><span class="material-icons chevron_right"></span></a>   <!-- plain h2 on TV -->
</div>
<div is="emby-scroller" class="padded-top-focusscale padded-bottom-focusscale" data-centerfocus="true">
  <div is="emby-itemscontainer" class="itemsContainer scrollSlider focuscontainer-x" [data-monitor="…"]>cards</div></div>
```
  - The non-overflow form is `itemsContainer padded-left padded-right vertical-wrap focuscontainer-x`.
  - Sections start with `hide` and are shown once items arrive (`parentContainer`).

| Section | API | Card options |
|---|---|---|
| smalllibrarytiles ("My Media", `<h2 class="sectionTitle sectionTitle-cards padded-left">`) | user views | `shape: overflowBackdrop`, `showTitle`, `centerText`, `overlayText: false` |
| librarybuttons | user views | `<a is="emby-linkbutton" class="raised homeLibraryButton"><span class="material-icons homeLibraryIcon {libIcon}"></span><span class="homeLibraryText">Name</span></a>` inside `homeLibraryButtonContainer itemsContainer … vertical-wrap` |
| resume / resumeaudio / resumebook (Continue Watching / Listening / Reading) | `GET /UserItems/Resume` limit 12, fields PrimaryImageAspectRatio, imageTypeLimit 1, enableImageTypes Primary,Backdrop,Thumb, enableTotalRecordCount false, mediaTypes Video/Audio/Book | `preferThumb`, `inheritThumb: !useEpisodeImages`, `shape: overflowBackdrop` (Book: `overflowPortrait`), `showTitle`, `showParentTitle`, `overlayPlayButton`, `context: home`, `centerText`, `cardLayout: false`, `showYear`, `lines: 2`; `data-monitor` `videoplayback,markplayed` / `audioplayback,markplayed` |
| nextup | `GET /Shows/NextUp` limit 24, fields PrimaryImageAspectRatio,DateCreated,Path,MediaSourceCount, imageTypeLimit 1, same image types, nextUpDateCutoff = today − maxDaysForNextUp, enableResumable false, enableRewatching (setting) | Like resume without year/lines; title links to `nextup` |
| latestmedia (one section per view, excluding playlists, livetv, boxsets, channels, folders and user `LatestItemsExcludes`) | `GET /Items/Latest` limit 16 (30 for music), fields PrimaryImageAspectRatio,Path, imageTypeLimit 1, parentId | Shape: portrait for movies/books/tvshows/Channel, square for music/homevideos, otherwise backdrop (overflow variants). `preferThumb: 'auto'` except movies/tvshows/music/Channel; `preferParentPoster`; `showChildCountIndicator`; `overlayPlayButton` (except photos); `showTitle` (except photos); `showYear` (movies, tvshows, none); `showParentTitle` (music, tvshows, none); `lines: 2`. Title "Latest {Name}" links to `section=latest` |
| livetv (requires `Policy.EnableLiveTvAccess`, and at least one airing program) | `GET /LiveTv/Programs/Recommended` isAiring, limit 24, fields ChannelInfo,PrimaryImageAspectRatio | First block: links `<a is="emby-linkbutton" class="raised"><span>Programs</span></a>` ×6 (Programs, Guide, Channels, Recordings, Schedule, Series) in a scroller with `data-scrollbuttons="false"`. Then "On Now" cards: `shape: autooverflow`, `preferThumb: 'auto'`, `inheritThumb: false`, `showParentTitleOrTitle`, `showTitle`, `centerText`, `coverImage`, `showAirTime`, `showAirEndTime`, `lines: 3`, `defaultShape: overflowBackdrop`, `overlayPlayButton` |
| activerecordings | `GET /LiveTv/Recordings` isInProgress, limit 12 | `shape: autooverflow`, `centerPlayButton`, `overlayMoreButton`, `action: none` |

---

## 6. Library browse pages (`apps/legacy/controllers/movies/movies.html`, `movies.js`, `shows/*`, `music/*`, `scripts/libraryBrowser.js`)
- **Toolbar** (movies.html):
```html
<div class="flex align-items-center justify-content-center flex-wrap-wrap padded-top padded-left padded-right padded-bottom focuscontainer-x">
  <div class="paging"></div>
  <button is="paper-icon-button-light" class="btnPlayAll autoSize hide"><span class="material-icons play_arrow"></span></button>
  <button … class="btnShuffle autoSize hide">shuffle</button>
  <button … class="btnSelectView autoSize">view_comfy</button>
  <button … class="btnSort autoSize">sort_by_alpha</button>
  <div class="btnFilter-wrapper [btnFilterWithIndicator]">[<div class="filterIndicator">!</div>]<button … class="btnFilter autoSize">filter_alt</button></div>
</div>
<div class="alphaPicker alphaPicker-fixed alphaPicker-vertical alphabetPicker-right alphaPicker-fixed-right">
  <div class="alphaPickerRow alphaPickerRow-vertical"><button data-value="#" class="alphaPickerButton alphaPickerButton-vertical [alphaPickerButton-tv] [alphaPickerButton-selected]">#</button>…A–Z</div></div>
<div is="emby-itemscontainer" class="itemsContainer padded-left padded-right vertical-wrap|vertical-list padded-right-withalphapicker"></div>
```
- **Alpha picker:** JS adds `alphaPicker`, `focusable`, and `alphaPicker-tv` on TV (`components/alphaPicker/alphaPicker.js`).
  - Selecting a letter sets `NameStartsWith`; `#` sets `NameLessThan=A`.
- **Paging** (`getQueryPagingHtml`):
```html
<div class="listPaging"><span style="vertical-align:middle;">1-100 of 532</span><div style="display:inline-block;">
  <button is="paper-icon-button-light" class="btnPreviousPage autoSize" [disabled]>arrow_back</button>
  <button … class="btnNextPage autoSize">arrow_forward</button></div></div>
```
- **View styles:** Poster (default), PosterCard, Thumb, ThumbCard, Banner, List. List switches the container to `vertical-list` and uses listView.
  - Poster: `shape: portrait`, `overlayPlayButton`, `showTitle`, `showYear`, `centerText`.
  - Thumb: `backdrop` + `preferThumb`.
  - Card variants add `cardLayout: true`.
  - Banner: `banner` + `preferBanner`.
- **Queries** (all via `GET /Users/{u}/Items` with `Recursive`, `ParentId=topParentId`, `StartIndex`, `Limit=libraryPageSize` (default 100, 0 = no paging), `ImageTypeLimit=1`):

| Page | IncludeItemTypes | Fields | EnableImageTypes | Default SortBy |
|---|---|---|---|---|
| Movies | Movie | PrimaryImageAspectRatio,MediaSourceCount | Primary,Backdrop,Banner,Thumb | SortName,ProductionYear |
| Series | Series | PrimaryImageAspectRatio | Primary,Backdrop,Banner,Thumb | SortName |
| Episodes | Episode | PrimaryImageAspectRatio,MediaSourceCount | Primary,Backdrop,Thumb | SeriesSortName,SortName |
| Albums | MusicAlbum | PrimaryImageAspectRatio,SortName | Primary,Backdrop,Banner,Thumb | SortName |
| Songs | Audio | ParentId | Primary | Album,SortName |
| Collections | BoxSet | — | — | SortName |

  - Favorites tab: `IsFavorite=true`.
  - Shuffle: `SortBy=Random`, `Limit=300`.
- **Sort and filter UI:**
  - Sort: dialog with `emby-radio` options; each id is a comma-separated SortBy list.
  - Filter: `components/filterdialog`; the `!` indicator appears when any filter field is set (`filterdialog/filterIndicator.js`).
- **Persistence** (`scripts/settings/userSettings.js:605-638`):
  - Key: `{topParentId}-{mode}` (e.g. `-series`, `-movies`, `-favorites`).
  - SortBy and SortOrder are saved as JSON under that key, in server display prefs `usersettings` / client `emby` CustomPrefs.
  - Filters are saved under `key+'-filter'` in localStorage as `{userId}-{key}-filter`.
  - The view is saved under `key+'-view'` (movies.js) or `key+'-_view'` (`getSavedView`, used in shows).
  - `libraryPageSize` is in localStorage.
- `components/libraryoptionseditor`: skipped. It is the admin library editor, not the browse page.

---

## 7. Item details (`apps/legacy/controllers/itemDetails/index.html` + `index.js`)
- **Skeleton** (index.html):
  - Root: `div#itemDetailPage.page.libraryPage.itemDetailPage.noSecondaryNavPage.selfBackdropPage`
  - `#itemBackdrop.itemBackdrop`: mobile header backdrop, only when `detailsBanner` is enabled and the item is not Person/Book.
  - `.detailLogo`
  - `.detailPageWrapperContainer`
    - `.detailPagePrimaryContainer`
      - `.detailImageContainer.hide-mobile`
      - `.detailRibbon.padded-left.padded-right`
        - `.infoWrapper`:
          - `.detailImageContainer.hide-desktop.hide-tv`
          - `.nameContainer`
          - `.itemMiscInfo.itemMiscInfo-primary`
          - `.itemMiscInfo.itemMiscInfo-secondary`
        - `.mainDetailButtons.focuscontainer-x`
      - `.detailPagePrimaryContent.padded-right > .detailSection`
        - `form.trackSelections`
        - `.recordingFields`
        - `.detailSectionContent`: `p.itemGenres`, `h3.tagline`, `p.overview`, `.overview-controls > a.overview-expand`, `#itemBirthday`, `#itemBirthLocation`, `#itemDeathDate`, `#seriesAirTime`, `.itemTags`, `.itemExternalLinks`, `.seriesRecordingEditor`
        - `.itemDetailsGroup`
        - `#seriesTimerScheduleSection`
        - `.collectionItems`
        - `.nextUpSection`
        - `.programGuideSection`
        - `#listChildrenCollapsible`
    - `.detailPageSecondaryContainer.padded-left.padded-bottom-page`
      - `#childrenCollapsible`, `#additionalPartsCollapsible`, `.moreFromSeasonSection`, `#lyricsSection`, `.moreFromArtistSection`, `#castCollapsible`, `#guestCastCollapsible`, `#seriesScheduleSection`, `#specialsCollapsible`, `#musicVideosCollapsible`, `#scenesCollapsible`, `#collectionsCollapsible`, `#similarCollapsible`
  - Each of these is `div.verticalSection.detailVerticalSection > h2.sectionTitle.sectionTitle-cards.padded-right + div[is=emby-scroller].padded-top-focusscale.padded-bottom-focusscale.no-padding[data-centerfocus] > div[is=emby-itemscontainer].scrollSlider.focuscontainer-x.itemsContainer`.
- **Buttons:**
  - Markup: `<button is="emby-button" type="button" class="button-flat btnX hide detailButton"><div class="detailButton-content"><span class="material-icons detailButton-icon {icon}">`.
  - Buttons and icons: btnPlay `play_arrow` (`data-action="resume"`), btnReplay `replay`, btnDownload `get_app`, btnPlayTrailer `theaters`, btnInstantMix `explore`, btnShuffle `shuffle`, btnCancelSeriesTimer `delete`, btnCancelTimer `stop`, btnPlaystate (emby-playstatebutton) `check`, btnUserRating (emby-ratingbutton) `favorite`, btnSplitVersions `call_split`, btnMoreCommands `more_vert`.
  - Visibility rules (index.js:330-404, 556-676):
    - Replay: shown if resumable.
    - InstantMix: Audio, MusicAlbum, MusicGenre, MusicArtist.
    - Shuffle: folders and music.
    - Program: play only while airing.
    - Trailer: needs LocalTrailerCount or RemoteTrailers.
    - Split versions: admin with Grouping sources.
    - Download: Book && CanDownload.
- **Data source:** `GET /Users/{u}/Items/{id}`, or genre/musicgenre/person/series-timer depending on the route params.
- **Sections:**
  - **Primary image:** `buildCardImage` (`cardbuilder/cardImage.ts`) produces `div.card.{shape}Card > .cardBox > .cardScalable > .cardPadder + div.cardImageContainer.coveredImage.cardContent.lazy`, at width = 25% of the window.
  - **Logo:** `/Items/{id}/Images/Logo`, or ParentLogo.
  - **Backdrop:** full-page backdrop when not mobile and width ≥1000.
  - **Name** (`renderName`):
    - Episode: `h1.parentName` (Series link `a.button-link.itemAction[is=emby-linkbutton][data-action=link]`), then `h3.itemName.infoText.subtitle` "Season - Name".
    - Music: `h3.parentName.musicParentName` with artist links.
    - Otherwise: `h1.itemName.infoText.parentNameLast[.withOriginalTitle]`, plus `h4.itemName.infoText.originalTitle`.
  - **Media info:** section 8 (primary with `interactive`; secondary only for Program).
  - **Overview:** markdown → DOMPurify, `<bdi>`, `detail-clamp-text`; "Show more" toggles.
  - **Tagline:** `Taglines[0]`.
  - **Tags:** "Tags: " plus `a.button-link` links to the `tag` route.
  - **External links:** HomePageUrl + ExternalUrls as `a.button-link[target=_blank]`.
  - **Details group:** a React `ItemDetailsMetadataList` for each of Author, Creator, Director, Writer, Studio, Genre: `div.detailsGroupItem > .label + .focuscontainer-x > span > a.emby-button.button-link`.
  - **Track selections:** `.selectContainer.selectSourceContainer|selectVideoContainer|selectAudioContainer|selectSubtitlesContainer.trackSelectionFieldContainer > select[is=emby-select].detailTrackSelect`, filled from `MediaSources`.
  - **Children:**

| Type | Call | Render |
|---|---|---|
| Series | `GET /Shows/{id}/Seasons` | Cards: `overflowPortrait`, `overlayPlayButton` |
| Season | `/Shows/{sid}/Episodes?seasonId`, fields + Overview | listView: `imageSize: large`, `enableOverview`, `imagePlayButton`, `enableSideMediaInfo: false`, `highlight: false` |
| Episode | Episodes in the same season | Cards: `overflowBackdrop`, `overlayText` (children container gets `scrollX hiddenScrollX`) |
| MusicAlbum | Items with SortBy ParentIndexNumber,IndexNumber,SortName | listView: `image: false`, `showIndexNumberLeft`, `index: 'disc'`, `action: playallfromhere` |
| BoxSet | Items | Grouped by type into `.collectionItems` sections (portrait/square cards, `collectionItemsContainer`, `overlayMoreButton`, `collectionId`) |
| Playlist | — | `scripts/playlistViewer` |
| Person, Genre, Studio, MusicGenre, MusicArtist | — | `scripts/itemsByName` (not read in detail) |

  - **Next Up** (Series): `GET /Shows/NextUp?SeriesId`; `overflowBackdrop` cards in `.nextUpItems`.
  - **More from season** (Episode): `getEpisodes`, `overflowBackdrop`, scrolled to the current episode.
  - **More from artist / Appears on:** Items with IncludeItemTypes MusicAlbum, AlbumArtistIds or ContributingArtistIds; `autooverflow`.
  - **Collections:** `GET /Items/{id}/Collections`; `overflowPortrait`.
  - **More like this:** `GET /Items/{id}/Similar` limit 12, for Movie, Trailer, Series, Program, Recording, MusicAlbum, MusicArtist, Playlist, Audio; `autooverflow`.
  - **Cast and guest cast:** from `item.People` (GuestStar split out; Writer/Director/Producer merged into RoleList); people cards `overflowPortrait`, `coverImage`. The header reads "People" for audio, book and photo items.
  - **Scenes:** `item.Chapters` (only if the first chapter has an ImageTag); chapter cards.
  - **Specials:** `/Users/{u}/Items/{id}/SpecialFeatures`. **Additional parts:** `/Videos/{id}/AdditionalParts`. **Music videos:** Items with AlbumIds. All three use `autooverflow`, `action: play`, `showRuntime`, `overlayMoreButton`.
  - **Lyrics** (Audio with HasLyrics): `GET /Audio/{id}/Lyrics`, lines joined with `<br/>`.
  - **Live TV:** channel guide or series schedule via `/LiveTv/Programs`; series timer schedule via `/LiveTv/Timers`; recording fields for Program.
  - **Person:** birthday (with age), death date, and birthplace with an OpenStreetMap link.

---

## 8. Media info line (`components/mediainfo/mediainfo.js`)
- **Item markup:** each entry is `<div class="mediaInfoItem [extra]">text</div>`.
- **Primary line, in order:**
  1. Track count + duration for MusicAlbum or Playlist, or item count for PhotoAlbum or BoxSet.
  2. PremiereDate for Episode or Photo.
  3. SeriesTimer time and channel.
  4. StartDate date + time (not Program or Timer).
  5. Series year range.
  6. Program / Timer:
     - `<div class="mediaInfoProgramAttribute mediaInfoItem liveTvProgram|premiereTvProgram|newTvProgram|repeatTvProgram">`, gated by the `guide-indicator-*` settings.
     - Then episode title, year, or "Original air date".
  7. Year: ProductionYear or the premiere year, for types other than Series, Episode, Person, Photo, Program and Season.
  8. Runtime: `getDisplayDuration`; Audio uses `getDisplayRunningTime`; not shown for Series, Program, Timer, Book or folders.
  9. Official rating: `mediaInfoItem mediaInfoText mediaInfoOfficialRating` (not for Season or Episode).
  10. `3D`, photo dimensions, Audio container.
  11. Star rating: `<div class="starRatingContainer mediaInfoItem"><span class="material-icons starIcon star"></span>7.4</div>`.
  12. `<div class="mediaInfoItem mediaInfoText closedCaptionMediaInfoText">CC</div>`.
  13. Critic rating: `mediaInfoItem mediaInfoCriticRating mediaInfoCriticRatingFresh|Rotten` (fresh at ≥60).
  14. Ends at: `<div class="endsAt mediaInfoItem">Ends at 9:41 PM</div>` for Video with a runtime and no StartDate; refreshed every 60 seconds.
  15. Missing indicator.
- **Options that switch parts off:** `year`, `episodeTitle`, `originalAirDate`, `runtime`, `officialRating`, `container`, `starRating`, `subtitles`, `criticRating`, `endsAt`, `programIndicator`, `interactive`.
- **Secondary line (Program only):** time range, `CH n`, channel link `<a is="emby-linkbutton" class="button-flat mediaInfoItem">`, timer icon `material-icons mediaInfoItem mediaInfoIconItem mediaInfoTimerIcon fiber_*`.
- **React versions:** `PrimaryMediaInfo.tsx` and related files in the same folder.

---

## 9. Icons and fonts
- **Material Icons:**
  - Markup is always `<span class="material-icons {icon_name}" aria-hidden="true"></span>`: an empty span with the icon name as a class.
  - Source: npm `material-design-icons-iconfont@6.7.0`, imported as a side effect (`import 'material-design-icons-iconfont'` in indicators.js, mediainfo.js and others), which loads its dist CSS and fonts.
  - Unverified: `node_modules` isn't installed, so I couldn't open that CSS. From knowledge of the package, it defines the "Material Icons" `@font-face` and `.material-icons.{name}::before { content: "\eXXX" }` rules, which is why the empty spans render.
  - Use that package's CSS, or put the ligature text inside the span.
  - `styles/site.scss:68` adds `font-feature-settings:"liga"`.
- **Fonts:**
  - `@fontsource/noto-sans` plus `-hk`, `-jp`, `-kr`, `-sc`, `-tc` at 5.3.0, as woff2 via `styles/noto-sans/*.scss` and the `styles/_font.scss` face mixin (weights 400 and 700).
  - `styles/fonts.noto.scss` sets the `html` font-family stack, with `html[lang|=ja|ko|zh-CN|zh-TW|zh-HK]` reordering.
  - `index.tsx:143-157 loadFonts()`:
    - TV (non-Android): `fonts.sized.scss`.
    - `__USE_SYSTEM_FONTS__`: `fonts.scss` only.
    - Otherwise: `fonts.scss` + `fonts.noto.scss`.
  - `fonts.scss` sets `html { font-size: 93% }`, h1 1.8em, h2 1.5em, h3 1.17em.
  - The base fallback is Arial (site.scss:42).
- **Themes:** `themes/{id}/theme.css`, built from `src/themes/**/*.scss` (webpack.common.js:36, 116), loaded by `components/ThemeCss.tsx`. `<html data-theme="{id}">` is set by `scripts/themeManager.js`.

---

## 10. Layout modes and root classes
- **`components/layoutManager.js`:**
  - Toggles `layout-desktop`, `layout-mobile` and `layout-tv` on `<html>` (`document.documentElement`).
  - `LayoutMode` values (`constants/layoutMode.ts`): `auto`, `desktop`, `desktop-legacy`, `modern`, `mobile`, `mobile-legacy`, `tv`. The `-legacy` suffix is stripped.
  - `modern` applies `layout-mobile` or `layout-desktop` based on `browser.mobile`.
  - Auto: TV if `browser.tv`, else `appHost.getDefaultLayout()`, else TV.
  - Saved in localStorage as `layout`.
  - The SCSS depends heavily on these classes: about 50 `.layout-mobile`, 46 `.layout-tv` and 23 `.layout-desktop` selectors.
- **Other root classes:**

| Class | Element | Set by |
|---|---|---|
| `transparentDocument` | `<html>` | `backdrop/backdrop.js:294-304` |
| `mouseIdle`, `mouseIdle-tv` | `<body>` | `scripts/mouseManager.js:21-37` |
| `libraryDocument`, `hideMainDrawer` | `<body>` | `scripts/libraryMenu.js:584` |
| `withSectionTabs` | `<body>` | `maintabsmanager.js` |
| `bodyWithPopupOpen` | `<body>` | libraryMenu |
| `noScroll` | `<body>` | dialogHelper |
| `screensaver-noScroll` | `<body>` | screensaver manager |
| `hide-scroll` | `<body>` | video players |
| `dashboardDocument` | `<body>` | `apps/dashboard/AppLayout.tsx` |
| `noHomeButtonHeader`, `noHeaderRight`, `headroomDisabled` | `.skinHeader` | page controllers / index.tsx |
| `dir="ltr|rtl"` | `<html>` and `<body>` | — |

- **Not found:** no `touch` or browser-detection classes are added to `<html>`/`<body>`. Browser detection (`scripts/browser.js`) is JS-only, and pointer type uses `matchMedia` (`utils/pointer.ts`). The only touch-related class is `touch-menu-la` on the nav drawer.
- **Inline `<style>` in `src/index.html` (must be reproduced):**
  - `.hide`, `.layout-desktop .hide-desktop`, `.layout-mobile .hide-mobile`, `.layout-tv .hide-tv`, `.mouseIdle .hide-mouse-idle` and `.mouseIdle-tv .hide-mouse-idle-tv` set `display:none !important`.
  - `.transparentDocument` background rules.
  - `cursor:none` rules under `.mouseIdle`.
  - `html{background:#101010; color:rgba(255,255,255,.7)}`.
  - `.mainDrawerHandle`.
