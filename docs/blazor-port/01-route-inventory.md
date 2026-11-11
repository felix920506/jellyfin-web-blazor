# Jellyfin-web route and page inventory (for the Blazor WASM port)

Repo root is `/home/user/jellyfin-web`. **All paths below are relative to `src/`.** Line counts come from `wc -l`. "Ctl" means `apps/legacy/controllers/`. The router is a hash router (`createHashRouter` in `RootAppRouter.tsx`), so URLs look like `#/home`.

**How routes are resolved**
- **Async (React) routes** (`components/router/AsyncRoute.tsx`) lazy-import `apps/{legacy|modern|dashboard}/routes/<page ?? path>`. If no type is given, the default is **Legacy**.
- **"Legacy" routes** (`components/router/LegacyRoute.tsx` → `components/viewManager/ViewManagerPage.tsx`, 144 lines) import a controller `.js` and a view `.html` from `apps/<appType>/controllers/`. The base folder is chosen by `appType`, defaulting to legacy. The DOM swapping is done in `components/viewManager/viewManager.js` (187).
- **Access levels** come from `components/ConnectionRequired.tsx` (229): `public`, `user` (the default), `admin`, and `wizard` (only allowed while the startup wizard is not complete).

## Legacy app (`apps/legacy/routes/routes.tsx`). Shell: `apps/legacy/AppLayout.tsx` (18)

| Path | Access | Files | LOC | Description |
|---|---|---|---|---|
| `/` | – | redirects to `/home` | – | Index redirect |
| `/home` | user | Ctl `home.js` + `home.html` | 74 (532 with tabs) | Home screen with Home and Favorites tabs |
| `/movies` | user | Ctl `movies/moviesrecommended.js` + `movies/movies.html` | 495 (1358 with tabs) | Movies library with tabs |
| `/tv` | user | Ctl `shows/tvrecommended.js` + `.html` | 466 (1455 with tabs) | TV shows library with tabs |
| `/music` | user | Ctl `music/musicrecommended.js` + `music/music.html` | 534 (1513 with tabs) | Music library with tabs |
| `/livetv` | user | Ctl `livetv/livetvsuggested.js` + `livetv.html` | 492 (961 with tabs) | Live TV hub with tabs |
| `/details` | user | Ctl `itemDetails/index.js` + `.html` | 2462 | Item detail page (any item type) |
| `/list` | user | Ctl `list.js` + `list.html` | 1386 | Generic filtered/sorted item list |
| `/lyrics` | user | Ctl `lyrics.js` + `.html` | 273 | Synced lyrics display for current track |
| `/video` | user, fullscreen | Ctl `playback/video/index.js` + `.html` | 2199 | Video player OSD (on-screen controls) |
| `/queue` | user, fullscreen | Ctl `playback/queue/index.js` + `.html` | 256 | Now playing / play queue screen |
| `/mypreferencescontrols` | user | Ctl `user/controls/index.js` + `.html` | 67 | Keyboard and controls preferences |
| `/mypreferencesdisplay` | user | Ctl `user/display/index.*` + `components/displaySettings` (589) | 42 + 589 | Display preferences |
| `/mypreferenceshome` | user | Ctl `user/home/index.*` + `components/homeScreenSettings` (830) | 44 + 830 | Home screen section preferences |
| `/mypreferencesplayback` | user | Ctl `user/playback/index.*` + `components/playbackSettings` (700) | 43 + 700 | Playback preferences |
| `/mypreferencessubtitles` | user | Ctl `user/subtitles/index.*` + `components/subtitlesettings` (702) | 41 + 702 | Subtitle appearance preferences |
| `/mypreferencesmenu` | user | `apps/legacy/routes/user/settings/index.tsx` | 387 | User settings menu (links to the above) |
| `/quickconnect` | user | `apps/legacy/routes/quickConnect/index.tsx` + `.scss` | 158 | Authorize a Quick Connect code |
| `/search` | user | `apps/legacy/routes/search.tsx` + `apps/legacy/features/search/**` (974) | 1020 | Search with suggestions and results rows |
| `/userprofile` | user | `apps/legacy/routes/user/userprofile.tsx` | 236 | User avatar and password change |
| `/addserver` | public | Ctl `session/addServer/index.*` | 94 | Manually add a server address |
| `/selectserver` | public | Ctl `session/selectServer/index.*` | 241 | Choose a known or discovered server |
| `/login` | public | Ctl `session/login/index.*` + `login.scss` | 429 | Login form with user picker |
| `/forgotpasswordpin` | public | Ctl `session/resetPassword/index.*` | 65 | Enter password-reset PIN |
| `/forgotpassword` | public | `apps/legacy/routes/session/forgotPassword/index.tsx` | 138 | Start forgot-password flow |
| `*` | public | `components/router/FallbackRoute.tsx` | ~60 | 404 page; rewrites old `.html` and `wizardX.html` URLs |

## Modern app (`apps/modern/routes/routes.tsx`). Shell: `apps/modern/AppLayout.tsx` (82) + `apps/modern/components/**` (1391: toolbar, drawer, remote-play/SyncPlay menus)

| Path | Access | Files | LOC | Description |
|---|---|---|---|---|
| `/` | – | redirects to `/home` | – | Index redirect |
| `/home` | user | `apps/modern/routes/home.tsx`, which loads the legacy Ctl `hometab.js` / `favorites.js` | 188 + 458 | Home with Home and Favorites tabs |
| `/movies`, `/tv` (page `shows`), `/music`, `/livetv`, `/books`, `/boxsets`, `/homevideos`, `/musicvideos`, `/playlists`, `/mixed` | user | `apps/modern/routes/<x>/index.tsx` (12–13 lines each) → `apps/modern/features/libraries/components/LibraryPage.tsx` | about 12 each, plus shared `features/libraries/**` = 6217 | Library pages built from the `LibraryRoutes` tab config |
| `/mypreferencesdisplay` | user | `apps/modern/routes/user/display/index.tsx` + `apps/modern/features/preferences/**` (935) | 1026 | Display preferences rewritten in React/MUI |
| `/video` | user | `apps/modern/routes/video/index.tsx`, which wraps the legacy Ctl `playback/video/index.*` | 92 + 2199 | New toolbar over the legacy video OSD |
| `/search`, `/mypreferencesmenu`, `/quickconnect`, `/userprofile`, `/forgotpassword` | as in legacy | **Same files under `apps/legacy/routes/`** (no type given, so legacy is used) | as in legacy | Shared with legacy |
| `/details`, `/list`, `/lyrics`, `/queue`, `/mypreferencescontrols`, `/mypreferenceshome`, `/mypreferencesplayback`, `/mypreferencessubtitles` | user | Same legacy controllers as above | as in legacy | Shared with legacy |
| `/addserver`, `/selectserver`, `/login`, `/forgotpasswordpin`, `*` | public | Same as legacy | as in legacy | Shared with legacy |

## Dashboard app (`apps/dashboard/routes/routes.tsx`). All routes require **admin**. Shell: `apps/dashboard/AppLayout.tsx` (118) + `apps/dashboard/components/**` (1477)

Unless noted, files are `apps/dashboard/routes/<path>`.

| Path | Files | LOC | Description |
|---|---|---|---|
| `/dashboard` | `index.tsx` | 138 (+ widgets ~470) | Server overview: widgets, sessions, item counts |
| `/dashboard/activity` | `activity/index.tsx` + `features/activity` (253) | 546 | Activity log table |
| `/dashboard/backups` | `backups/index.tsx` + `features/backups` (520) | 750 | Create, restore and inspect backups |
| `/dashboard/branding` | `branding/index.tsx` | 326 | Login disclaimer, custom CSS, splash screen |
| `/dashboard/devices` | `devices/index.tsx` + `features/devices` (368) | 663 | Device list: rename, delete |
| `/dashboard/settings` | `settings/index.tsx` | 275 | General server settings |
| `/dashboard/keys` | `keys/index.tsx` | 179 (+ API ~67) | API key management |
| `/dashboard/libraries` | `libraries/index.tsx` + `features/libraries` (410) | 521 | Library cards; add/edit opens `mediaLibraryCreator`/`Editor` dialogs |
| `/dashboard/libraries/display` | `libraries/display.tsx` | 191 | Library display settings |
| `/dashboard/libraries/metadata` | `libraries/metadata.tsx` | 166 | Metadata language, country, chapter settings |
| `/dashboard/libraries/nfo` | `libraries/nfo.tsx` | 180 | NFO settings |
| `/dashboard/livetv` | `livetv/index.tsx` + `features/livetv` (329) | 508 | Tuners and guide providers list |
| `/dashboard/livetv/recordings` | `livetv/recordings.tsx` | 414 | DVR recording settings |
| `/dashboard/livetv/guide` | `apps/dashboard/controllers/livetvguideprovider.js` + `.html` (legacy style), hosts `components/tvproviders` (634) | 37 + 634 | XMLTV / SchedulesDirect provider setup |
| `/dashboard/livetv/tuner` | `apps/dashboard/controllers/livetvtuner.js` + `.html` (legacy style) | 381 | Add/edit tuner device |
| `/dashboard/logs` | `logs/index.tsx` + `features/logs` (150) | 286 | Server log file list |
| `/dashboard/logs/:file` | `logs/file.tsx` | 181 | View one log file |
| `/dashboard/networking` | `networking/index.tsx` | 407 (+ utils 50) | Network and port settings |
| `/dashboard/playback/resume` | `playback/resume.tsx` | 170 | Resume thresholds |
| `/dashboard/playback/streaming` | `playback/streaming.tsx` | 102 | Streaming bitrate limits |
| `/dashboard/playback/transcoding` | `playback/transcoding.tsx` + `codecs.ts` | 1023 | Transcoding / hardware acceleration settings |
| `/dashboard/playback/trickplay` | `playback/trickplay.tsx` | 277 | Trickplay image generation settings |
| `/dashboard/plugins` | `plugins/index.tsx` | 261 (+ `features/plugins` 931 shared) | Installed and catalog plugins |
| `/dashboard/plugins/:pluginId` | `plugins/plugin.tsx` | 456 | Plugin details, revisions, install |
| `/dashboard/plugins/repositories` | `plugins/repositories.tsx` | 107 | Plugin repository list |
| `/dashboard/plugins/catalog` | redirects to `/dashboard/plugins` | – | Legacy redirect |
| `/dashboard/tasks` | `tasks/index.tsx` + `features/tasks` (653) | 696 | Scheduled tasks list (live updates) |
| `/dashboard/tasks/:id` | `tasks/task.tsx` | 181 | Task triggers editor |
| `/dashboard/users` | `users/index.tsx` | 191 | User list |
| `/dashboard/users/add` | `users/add.tsx` | 308 | Create user |
| `/dashboard/users/:userId/:tab` | `users/edit.tsx` + `features/users` (1727: Profile 604, ParentalControl 522, Access 361, Password 21) | 1799 | Edit user (profile, access, parental control, password tabs) |
| `/metadata` | legacy Ctl `edititemmetadata.js` + `.html` (no `appType` set, so it resolves to `apps/legacy/controllers`), hosts `components/metadataEditor` | 54 + 1603 | Metadata manager with library tree |
| `/configurationpage` | `components/ServerContentPage.tsx` (62) | 62 | Renders plugin config HTML/JS served by the server |

## Wizard app (`apps/wizard/routes/routes.tsx`, 76). Access: **wizard**. Shell: legacy `AppLayout`. Controllers are in `apps/wizard/controllers/`.

| Path | Files | LOC | Description |
|---|---|---|---|
| `/wizard` | redirects to `start` | – | Index redirect |
| `/wizard/start` | `start/index.js` + `.html` | 99 | Welcome screen and UI language |
| `/wizard/user` | `user/index.*` | 117 | Create the first admin user |
| `/wizard/library` | `library.js` + `library.html` | 421 | Add media libraries |
| `/wizard/settings` | `settings/index.*` | 120 | Metadata language and country |
| `/wizard/remoteaccess` | `remote/index.*` | 73 | Remote access / port mapping |
| `/wizard/finish` | `finish/index.*` | 36 | Completion screen |

## Global route

`!/*` → `components/router/BangRedirect.tsx` (strips the old `#!/` prefix).

---

### 1. How legacy vs modern is chosen

- `RootAppRouter.tsx` spreads `layoutManager.modern ? MODERN_APP_ROUTES : LEGACY_APP_ROUTES`. Dashboard and wizard routes are always added.
- In `components/layoutManager.js`, `modern = !LegacyLayoutModes.has(layout)`. Per `constants/layoutMode.ts`, the legacy modes are `desktop-legacy`, `mobile-legacy` and `tv`. Every other mode is modern, including `desktop`, `mobile`, `modern` and auto.
- **The default is modern.** `appHost.getDefaultLayout()` returns `LayoutMode.Modern` (`components/apphost.js:186`), unless `NativeShell` overrides it. `autoLayout()` uses `tv` when `browser.tv` is true, which makes TVs legacy.
- The saved setting is stored under the key `layout`.
- The old `AppHeader` is hidden in modern layout and on dashboard paths.
- **Both layouts expose the same user-facing path set**, with these differences:
  - Modern adds `/books`, `/boxsets`, `/homevideos`, `/musicvideos`, `/playlists` and `/mixed`. Legacy has none of these.
  - `/home`, `/movies`, `/tv`, `/music`, `/livetv` and `/mypreferencesdisplay` are React in modern but controllers in legacy.
  - Modern `/video` is a React wrapper around the same legacy controller.

### 2. Tabbed pages

**Legacy.** The tab index maps to a controller via a `switch` in the parent controller (`getTabController`). Tabs are wired through `components/maintabsmanager.js` (200).
- **Home** (`home.js`): 0 Home → `hometab.js` (74); 1 Favorites → `favorites.js` (384).
- **Movies** (`movies/moviesrecommended.js`): 0 Movies → `movies.js` (347); 1 Suggestions → `moviesrecommended.js`; 2 Favorites → `movies.js` (the default case); 3 Collections → `moviecollections.js` (292); 4 Genres → `moviegenres.js` (224).
- **TV** (`shows/tvrecommended.js`): 0 Shows → `tvshows.js` (306); 1 Suggestions → `tvrecommended.js`; 2 Upcoming → `tvupcoming.js` (143); 3 Genres → `tvgenres.js` (220); 4 Studios → `tvstudios.js` (71); 5 Episodes → `episodes.js` (249).
- **Music** (`music/musicrecommended.js`): 0 Albums → `musicalbums.js` (302); 1 Suggestions → `musicrecommended.js`; 2 Album Artists and 3 Artists → `musicartists.js` (243); 4 Playlists → `musicplaylists.js` (86); 5 Songs → `songs.js` (216); 6 Genres → `musicgenres.js` (132).
- **Live TV** (`livetv/livetvsuggested.js`): 0 Programs → `livetvsuggested.js`; 1 Guide → `livetvguide.js` (28, wraps `components/guide`); 2 Channels → `livetvchannels.js` (147); 3 Recordings → `livetvrecordings.js` (115); 4 Schedule → `livetvschedule.js` (125); 5 Series → `livetvseriestimers.js` (54).

**Modern.** Tabs are config-driven in `apps/modern/features/libraries/constants/libraryRoutes.ts` (358). They render through `PageTabContent.tsx`:
- Suggestions → `SuggestionsSectionView`
- Programs / Recordings / Schedule → `ProgramsSectionView`
- Upcoming → `UpcomingView`
- Genres → `GenresView`
- Guide → `GuideView` (wraps `components/guide`)
- Everything else → `ItemsView` (228)

| Page | Tabs |
|---|---|
| livetv | Programs, Guide, Channels, Recordings, Schedule, Series |
| books | Folders, Books, Authors, Suggestions, Genres, Collections, Favorites |
| boxsets | Collections, Favorites, Genres |
| movies | Movies, Suggestions, Favorites, Collections, Genres, Studios, Playlists, Persons |
| music | Albums, Suggestions, AlbumArtists, Artists, Playlists, Songs, Genres, Collections |
| tv | Series, Suggestions, Upcoming, Genres, Studios, Episodes, Collections, Playlists, Persons |
| homevideos | Folders, Photos, PhotoAlbums, Videos |
| musicvideos | Folders, Suggestions, MusicVideos, Playlists |
| playlists | Playlists, Favorites |
| mixed | Folders, Suggestions, Mixed, Collections, Playlists |

Modern home reuses the legacy `hometab.js` and `favorites.js`.

Dashboard user edit has four tabs (Profile, Access, ParentalControl, Password; listed in `features/users/constants/userTab.ts`). Its shared tab bar is `apps/dashboard/components/AppTabs.tsx` (96).

### 3. Large shared non-route UI

Directories are under `components/` unless noted. LOC is the directory total, which includes SCSS.

| UI | Main files | LOC |
|---|---|---|
| Playback manager (core engine) | `playback/playbackmanager.js` (4458), `playersettingsmenu.js` (294), `playerSelectionMenu.js` (284) | ~6100 (whole dir) |
| Card / list rendering | `cardbuilder/**`, `listview/**` | 5397 / 1690 |
| Metadata editor | `metadataEditor/metadataEditor.js` + `.template.html`, `personEditor.*` | 1603 |
| TV guide | `guide/guide.js`, `tvguide.template.html`, `guide-settings.*` | 1932 |
| Media info strips | `mediainfo/mediainfo.js` + React `PrimaryMediaInfo.tsx` etc. | 1544 |
| Remote control / now playing | `remotecontrol/remotecontrol.js` | 1416 |
| Recording creator/editor | `recordingcreator/` (`recordingcreator`, `recordingeditor`, `seriesrecordingeditor`, `recordingfields`, `recordinghelper`, `recordingbutton` + templates) | 1361 |
| Library options editor | `libraryoptionseditor/*` | 1094 |
| Now playing bar | `nowPlayingBar/nowPlayingBar.js` | 976 |
| Slideshow (photo viewer) | `slideshow/slideshow.js` | 942 |
| Item context menu | `itemContextMenu.js` | 780 |
| Filter dialog | `filterdialog/filterdialog.js`, `filterIndicator.js`, template | 781 |
| Modern filter / sort / view menus | `apps/modern/features/libraries/components/filter/*` (FilterButton 543 + 12 filter parts ≈ 1400), `SortButton` 262, `ViewSettingsButton` 267 | ~1930 |
| Multi-select | `multiSelect/multiSelect.js` | 677 |
| Live TV providers | `tvproviders/xmltv.*`, `schedulesdirect.*` | 634 |
| Player stats overlay | `playerstats/playerstats.js` | 625 |
| Action sheet (generic menu) | `actionSheet/actionSheet.ts` | 535 |
| Image editor | `imageeditor/imageeditor.js` + template | 535 |
| Subtitle editor | `subtitleeditor/subtitleeditor.js` + template | 532 |
| Item identifier | `itemidentifier/*` | 478 |
| Playlist editor | `playlisteditor/playlisteditor.ts` | 457 |
| Image downloader / uploader | `imageDownloader/*`, `imageUploader/*` | 445 / 255 |
| Lyrics editor / uploader | `lyricseditor/*`, `lyricsuploader/*` | 437 / 222 |
| Filter menu, sort menu, view settings (legacy) | `filtermenu/*`, `sortmenu/*`, `viewSettings/*` | 394 / 131 / 179 |
| Up-next dialog | `upnextdialog/upnextdialog.js` | 317 |
| Media library creator / editor | `mediaLibraryCreator/*`, `mediaLibraryEditor/*` | 295 / 299 |
| Directory browser | `directorybrowser/directorybrowser.js` | 278 |
| Item media info dialog | `itemMediaInfo/itemMediaInfo.js` + template | 277 |
| Collection editor | `collectionEditor/collectionEditor.js` | 275 |
| Subtitle uploader / sync | `subtitleuploader/*`, `subtitlesync/*` | 253 / 233 |
| Refresh dialog | `refreshdialog/refreshdialog.js` | 176 |
| Image options editor | `imageOptionsEditor/*` | 176 |
| Channel mapper | `channelMapper/channelMapper.js` | 150 |
| Access schedule | `accessSchedule/accessSchedule.js` + template | 134 |
| SyncPlay UI | `plugins/syncPlay/ui/**` (groupSelectionMenu, `settings/SettingsEditor.js` + `editor.html`, player wrappers) 1347; modern `SyncPlayMenu.tsx` 253 + `features/syncPlay` 249 | ~1850 |
| Dialog infrastructure | `dialogHelper/*` (681), `dialog/*` (149), `prompt/*` (127), `confirm/*` (55), `toast/*` (106) | ~1120 |

The video OSD is the `/video` route itself (2097-line controller). It is listed above, not in this table.

### 4. Plugins (`plugins/*`)

| Plugin | Purpose | LOC |
|---|---|---|
| `syncPlay` | SyncPlay group sync core (time sync, queue, playback) plus UI | 4229 |
| `htmlVideoPlayer` | HTML5 video player (HLS, subtitles, etc.) | 2480 |
| `chromecastPlayer` | Chromecast cast target player | 1157 |
| `bookPlayer` | EPUB reader | 809 |
| `sessionPlayer` | "Remote Control": controls other server sessions as players | 666 |
| `htmlAudioPlayer` | HTML5 audio player | 618 |
| `comicsPlayer` | Comic archive (cbz/cbr) reader | 432 |
| `youtubePlayer` | YouTube trailer player | 390 |
| `pdfPlayer` | PDF reader | 353 |
| `logoScreensaver` | Bouncing-logo screensaver | 187 |
| `stillWatching` | "Still watching?" prompt intercept before episodes | 106 |
| `experimentalWarnings` | Warnings for experimental playback paths | 68 |
| `backdropScreensaver` | Backdrop slideshow screensaver | 54 |
| `photoPlayer` | Photo "player" that opens the slideshow | 48 |
| `playAccessValidation` | Playback access validation (pre-play check) | 44 |

**Couldn't pin down:**
- Exact LOC for `/dashboard/keys` beyond 179 + ~67 of API hooks.
- The dashboard home widget total: about 470 by summing the widget files; not checked separately.
- Which shared components each dashboard page pulls in transitively. Page LOC numbers count only the route file and the `features/<area>` directory named in each row.
