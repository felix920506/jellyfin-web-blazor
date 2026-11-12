# Item dialogs and editors in the Blazor port

Reference for the item dialog workstream: the dialogs behind the commands of an item's menu
(collections, playlists, metadata, images, subtitles, lyrics, identify, refresh, media info,
download, share), how other workstreams open them, and what is missing. The menu itself and its
capability rules are described in `05-item-components.md`.

## 1. What is ported

| Original | Port | Command |
| --- | --- | --- |
| `components/collectionEditor/collectionEditor.js` | `Components/ItemDialogs/CollectionEditor.razor` | `addtocollection` |
| `components/playlisteditor/playlisteditor.ts` | `PlaylistEditor.razor` (add and edit mode) | `addtoplaylist`, `editplaylist` |
| `components/metadataEditor/metadataEditor.js` + template | `MetadataEditor.razor`, `EditorInput.razor`, `EditorSelect.razor`, `Services/ItemDialogs/MetadataEditorLogic.cs` | `edit` |
| `components/metadataEditor/personEditor.js` + template | `PersonEditor.razor` | – |
| `components/imageeditor/imageeditor.js` + template | `ImageEditor.razor`, `ImageEditorLogic` | `editimages` |
| `components/imageDownloader/imageDownloader.js` + template | `ImageDownloader.razor` | – |
| `components/imageUploader/imageUploader.js` + template | `ImageUploader.razor` | – |
| `components/subtitleeditor/subtitleeditor.js` + template | `SubtitleEditor.razor`, `SubtitleLyricsLogic` | `editsubtitles` |
| `components/subtitleuploader/subtitleuploader.js` + template | `SubtitleUploader.razor` | – |
| `components/lyricseditor/lyricseditor.js` + templates | `LyricsEditor.razor`, `LyricsPreview.razor` | `editlyrics` |
| `components/lyricsuploader/lyricsuploader.js` + template | `LyricsUploader.razor` | – |
| `components/itemidentifier/itemidentifier.js` + template | `ItemIdentifier.razor`, `ItemIdentifierLogic` | `identify` |
| `components/refreshdialog/refreshdialog.js` | `RefreshDialog.razor` | `refresh` |
| `components/itemMediaInfo/itemMediaInfo.js` + template | `ItemMediaInfo.razor`, `MediaInfoBuilder.cs` | `moremediainfo` |
| `scripts/fileDownloader.js`, `scripts/multiDownload.js`, the `download`/`downloadall` branches of `itemContextMenu.js` | `ItemDialogService.DownloadAsync`/`DownloadAllAsync`, `wwwroot/js/itemdialogs.js` | `download`, `downloadall` |
| `navigator.share` branch of `itemContextMenu.js`, `apphost.js` `supports('sharing')` | `ItemDialogService.ShareAsync`, `CanShare` | `share` |
| `getPosition` of `components/actionSheet/actionSheet.ts` | `Components/Dialogs/ActionSheetPositioner.cs`, `wwwroot/js/actionsheet.js` | – |

Markup, class names, element ids, translation keys, server requests and their payloads are the
original's, so the original style sheets apply. The only style sheet of the workstream is
`Styles/port/_itemdialogs.scss` (see section 6).

## 2. How to open a dialog

### 2.1 Through the item menu

`ItemDialogCommandHandler` is registered with `[AppService(typeof(IItemCommandHandler))]`, so
`ItemActions.ExecuteAsync` reaches the dialogs without the caller knowing them. It handles

- `addtocollection`, `addtoplaylist`, `editplaylist`, `editimages`, `editsubtitles`, `editlyrics`,
  `identify`, `moremediainfo`, `refresh`, `download`, `downloadall`;
- `edit` unless the item is a `Timer` or `SeriesTimer` (those belong to the Live TV recording
  editors);
- `share` only where the browser has `navigator.share` (see section 4).

The result is `Updated: true` when the dialog changed something on the server (`edit` always, like
the original, which reloads after every close of the editor), and `None` for media info, refresh,
download and share. Whether a command is offered at all is decided by the rules of
`Services/Items/` (`ItemContextMenu`, `ItemHelper`); nothing is decided a second time here.

### 2.2 Directly

Inject `ItemDialogService` (`[AppService]`, `Services/ItemDialogs/ItemDialogService.cs`):

| Member | Does |
| --- | --- |
| `AddToCollectionAsync(itemIds)` | collection editor; `true` when a collection was created or items were added |
| `AddToPlaylistAsync(itemIds)` | playlist editor in add mode |
| `EditPlaylistAsync(playlistId)` | playlist editor in edit mode (name, public) |
| `EditMetadataAsync(itemId)` | metadata editor |
| `EditImagesAsync(itemId)` | image editor; `true` when an image changed |
| `EditSubtitlesAsync(itemId)`, `EditLyricsAsync(itemId)` | subtitle / lyrics editor; `true` on a change |
| `IdentifyAsync(itemId)` | identifier; a locked item only gets the toast `MessageItemMetadataLocked` |
| `RefreshAsync(itemIds, mode)` | refresh dialog; `mode` is `"scan"` for a `CollectionFolder` |
| `ShowMediaInfoAsync(itemId)` | media info dialog |
| `DownloadUrl(itemId)`, `DownloadAsync(itemId)` | `Items/{id}/Download?ApiKey=…` |
| `DownloadAllAsync(item)` | children of a box set or album, episodes of a season, all episodes of a series; only those with `CanDownload` |
| `CanShare`, `ShareAsync(item)`, `ShareUrl(item)` | `navigator.share` with `{ServerAddress}/web/{route of the item}` |

Each dialog component also has a static entry point that takes `AppDialogs`, for callers that
already have the data:

```csharp
CollectionEditor.ShowAsync(dialogs, itemIds)
PlaylistEditor.ShowAsync(dialogs, itemIds, enableAddToPlayQueue: true, defaultValue: null)
PlaylistEditor.ShowEditAsync(dialogs, playlistId)
MetadataEditor.ShowAsync(dialogs, itemId)
PersonEditor.ShowAsync(dialogs, person)                 // JsonObject; PersonEditor.PersonTypes
ImageEditor.ShowAsync(dialogs, itemId)
ImageUploader.ShowAsync(dialogs, itemId, imageType: null)
ImageDownloader.ShowAsync(dialogs, itemId, itemType, imageType: null, parentId: null)
SubtitleEditor.ShowAsync(dialogs, itemId)
SubtitleUploader.ShowAsync(dialogs, itemId, languages, language: null)
LyricsEditor.ShowAsync(dialogs, itemId)
LyricsUploader.ShowAsync(dialogs, itemId)
LyricsPreview.ShowAsync(dialogs, lines)
ItemIdentifier.ShowAsync(dialogs, item)                 // JsonObject of the full item
RefreshDialog.ShowAsync(dialogs, itemIds, mode: null)
ItemMediaInfo.ShowAsync(dialogs, item)                  // JsonObject of the full item
```

`AddToCollectionAsync`, `AddToPlaylistAsync` and `RefreshAsync` take several ids, which is what
multi-select needs; a page that already holds the full item can pass it to
`ItemMediaInfo.ShowAsync` or `ItemIdentifier.ShowAsync` directly.

The wizard library step and its library-card menu use these same dialog entry points: library
creation/editing, image editing and a scan refresh are all opened through the shared dialog host,
then the wizard reloads its virtual-folder cards when an editor reports a change.

### 2.3 Action sheet positioning

`ActionSheetOptions` (in `Services/UiServices.cs`) gained the members of the original's options:

| Member | Meaning |
| --- | --- |
| `PositionTo` | `ActionSheetAnchor.LastClicked` (the element of the last pointer or keyboard activation) or `ActionSheetAnchor.At(elementReference)`; `null` keeps the centred sheet |
| `PositionY` | `"top"`, `"bottom"` or `null` (centred on the anchor), as in the original |
| `OffsetLeft`, `OffsetTop` | added to the computed position |
| `ResolveOnClick` | ids that resolve before the close animation ends; kept for parity, the port's dialogs resolve at once |

`ActionSheetPosition.Compute` is the original's `getPosition`: centred on the anchor, moved back by
the overflow plus 20 px at the right and bottom edge, never closer than 10 px to the left and top
edge. `IActionSheetPositioner` measures and applies it (`wwwroot/js/actionsheet.js`); without an
anchor, or when the anchor has left the document, the sheet stays centred. The original skips the
positioning for its full-screen TV sheet, a variant the port's action sheet does not have.
`ItemMenuOptions.PositionTo` defaults to `ActionSheetAnchor.LastClicked`, so every item menu opens
at its button.

## 3. Architecture

```
ItemActions ──► ItemDialogCommandHandler ──► ItemDialogService ──► <Dialog>.ShowAsync(DialogService, …)
                                              │                         │
library/details pages ───────────────────────►│                         ├─► ItemDialogApi  (HTTP, raw JSON)
                                              └─► IItemDialogsInterop    └─► *Logic classes (pure, unit tested)
                                                   (share, download,
                                                    clipboard, file picker)
```

- **`ItemDialogApi`** wraps the api client for the dialogs: `GetItemAsync` (`Users/{uid}/Items/{id}`),
  `GetAsync`/`GetObjectAsync`/`GetArrayAsync`/`PostAsync`/`PostContentAsync`/`DeleteAsync`,
  `ReadFileAsync` (uploads, at most `MaxUploadSize` = 256 MB) and `IsFailure` (the exceptions a
  dialog treats as "the request failed").
- **Raw JSON.** The dialogs read and write `System.Text.Json.Nodes` instead of typed DTOs: the
  editors post the item back with every field the server sent, as the original does with its plain
  objects, and a typed model would drop what it does not know. `ItemJson` has the small readers
  (`Text`, `Flag`, `FlagOrNull`, `Number`, `Has`, `Child`, `List`, `Strings`).
- **Logic classes** hold what the original computes inline and are tested without a renderer:
  `MetadataEditorLogic` (which fields a type shows, labels, date round trip, name lists, field
  values, external ids), `MediaInfoBuilder` (the attribute lists of the media info dialog),
  `ImageEditorLogic`, `ItemIdentifierLogic`, `SubtitleLyricsLogic`.
- **`IItemDialogsInterop`** / `wwwroot/js/itemdialogs.js` is the only JavaScript: `canShare`,
  `share`, `copyText`, `download`, `click` (opens a file picker from a button).
- **Failures.** A failed request hides the loading indicator, is logged with `AppLog.Warn`, shows
  the toast or alert the original shows, and leaves the dialog open.

## 4. What the browser host supports

| Feature | Original in a browser | Port |
| --- | --- | --- |
| Download | `fileDownloader` → `multiDownload`: one `a[download]` click per URL, hidden iframes on browsers without `a.download` and on iOS | the same, in `itemdialogs.js` (`download(urls, useFrames)`, 100 ms apart) |
| Share | only where `navigator.share` exists (`supports('sharing')`); the command is absent otherwise | the same: `canShare()` is read once at start-up (`IStartupTask`); absent in desktop Chromium on Linux and in the headless test browser, present on Android/iOS/Safari/Edge/Chrome on Windows and ChromeOS |
| Copy (media info, stream URL) | `navigator.clipboard.writeText`, falling back to a hidden textarea and `execCommand('copy')` | the same (`copyText`) |
| File pickers | hidden `input[type=file]`, clicked from a button | the same; files are read through `InputFile` |

Downloads and shares need nothing from a native shell. The original first offers a download to the
native shell (`shell.downloadFiles`) and only then falls back to the browser path; the port has no
native shell and always takes the browser path.

## 5. Differences from the original

Intended, and without effect on what the user or the server sees unless stated:

- The genre, tag and studio lists of the metadata editor are sorted by an ordinal comparison of the
  lower-cased names; the original uses `localeCompare`. The order can differ for names with
  diacritics.
- The image uploader posts the base64 text with the file's own MIME type as `Content-Type`; the
  original's api client derives `image/<file extension>`. The server accepts both.
- The image editor and downloader load their images directly; the original lazy-loads them.
- The remote image list always pages by 30. The original takes 6 on slow (TV) browsers.
- The TV-layout variants of the dialogs (focus handling, the `layout.tv` scroll containers and
  button placement) are not ported; the port does not target TV browsers yet.
- The metadata editor shows the template's default labels until the item has loaded.
- The metadata editor stays open when saving fails; the upload dialogs hide the loading indicator
  when the upload fails (the original leaves it spinning).
- `edit` on a `Timer`/`SeriesTimer` is not handled here (section 2.1).

## 6. Style sheet

`Styles/port/_itemdialogs.scss` contains one rule. MudBlazor's style sheet, which the shell loads,
resets the margin of every element (`* { margin: 0 }`). The original has no such reset, and the
templates of these dialogs rely on the browser's own margins for their bare `<p>`, `<h1>` and
`<h2>` elements. The rule puts those margins back inside `.identifyDialog`,
`.subtitleEditorDialog`, `.lyricsEditorDialog` and `.editItemMetadataForm`, with the specificity of
a single element so that every class rule and inline style of the original still wins.

Two things seen while comparing screenshots with the original are outside this workstream and are
**not** corrected here:

1. The reset above affects every legacy-markup page, not only these dialogs.
2. MudBlazor's `body` rule (`font-size: var(--mud-typography-default-size)`, line height, letter
   spacing) makes the port's inherited text 13.02 px / 18.62 px / 0.139 px letter spacing; the
   original has 14.88 px / 20.09 px / normal (`html { font-size: 93%; line-height: 1.35 }`). A
   central correction would be the default typography of `ThemeManager`
   (`FontSize = "1rem"`, `LineHeight = "1.35"`, `LetterSpacing = "normal"`) or a `body` rule in
   `_shell.scss`.

## 7. Missing

- **The `/metadata` route** (the metadata manager with the library tree, `edititemmetadata.js`,
  line 91 of the route inventory) is not ported. The editor it hosts is; the page and its tree
  are not.
- `// PORT-TODO(itemdialogs)` in `PlaylistEditor.razor`: the original also offers "Play Queue" as a
  target while a SyncPlay group is active; SyncPlay is not ported, so the option only appears for
  local playback.
- TV-layout variants (section 5).
- Downloads through a native shell (section 4).

## 8. Tests

- `tests/Jellyfin.Web.Tests/ItemDialogs/`: bUnit tests for every dialog (markup, requests and
  payloads, validation, failures, results) and unit tests for the logic classes, the command
  handler, the service and the action sheet position. `ItemDialogsTestContext` provides the fake
  api, dialog host and interop. Remote searches (identify, subtitles, lyrics, remote images) are
  covered here, because the test server has no metadata providers that return results.
- `e2e/itemdialogs.py` (`./dev e2e itemdialogs.py`) opens the dialogs from the card menus of the
  home page against the test server at 1366×768 and again in a 400×800 window, checks the requests
  and takes screenshots (`artifacts/e2e/itemdialogs-*.png`). It edits only `Third Film (2008)`
  (name, year, a tag, a banner image), creates one collection and one playlist and makes the
  playlist and one song favourites for a moment; all of it is undone in a `finally` block.
- Limits of the browser test: the layout follows the user agent, and the mobile layout has no item
  menu on the home cards, so the narrow pass runs the desktop layout in a phone-sized window (the
  dialogs are full-screen there). It uploads no subtitles or lyrics and has no search results to
  pick; `share` is absent in the headless browser.
