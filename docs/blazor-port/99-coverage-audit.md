# Blazor port coverage audit

## Executive summary

This is a source-level comparison of the original web client at `src/` with the Blazor
client at `blazor/src/Jellyfin.Web/`. The route inventory in
[`01-route-inventory.md`](01-route-inventory.md), original route declarations and
controllers, item actions and menus, `src/plugins/*`, the Blazor route table, service
registrations and the existing port workstream documents were compared. No build, test,
browser session or generated-code execution was used for this audit.

The port has a page for every concrete route in the route inventory, including the
metadata manager, Live TV administration and plugin configuration alias. The principal
remaining coverage issues are feature-level limitations inside those routes and missing
playback/plugin behaviors, not an unrepresented route.

### Prioritized real gaps

1. **Playback transport targets and browser playback features.** Chromecast is explicitly
   unsupported. The video OSD still lacks recording controls, TV key handling and iOS forced-fullscreen back
   navigation. These are visible regressions for users with those capabilities.
2. **Several original playback plugins have no functional counterpart.** The backdrop and
   logo screensaver plugins are exposed as preference choices but have no screensaver
   implementation. The playback-boundary portions of `stillWatching`, `experimentalWarnings`
   and `playAccessValidation` are now implemented; the remaining screensaver behavior is still
   open.
3. **Plugin configuration pages are only a warning page.** `/configurationpage` keeps the
   route and validates the resource, but deliberately does not render server HTML or run
   plugin JavaScript. This is safe and intentional, but plugin settings cannot be edited
   from the Blazor client.
4. **Queue drag/drop and item commands were previously incomplete.** `clearQueue`, `playmenu`
   and `setplaylistindex` now have a playback-owned `IItemCommandHandler`; the queue retains
   Up/Down controls and also supports native drag/drop. Remaining transport-target limitations
   are listed in the playback extras document.
5. **Multi-select now preserves the full selection.** `ItemDialogCommandHandler` derives its
   operation IDs from `ItemActionContext.SelectedItems`, with a single-item fallback for direct
   item actions.
6. **Playback format and interaction gaps remain.** There is no client VobSub renderer; TV key
     handling remains open, while graphical subtitles, touch brightness gestures and playback
     interceptors are implemented. Queue drag-reordering is now supported alongside the
    accessible up/down buttons.
7. **Small but observable library/image differences.** The port uses native scrolling
   instead of the original transform-based desktop scroller, and loaded lazy images are
   not unloaded when they leave the viewport. These are documented limitations rather
   than route or data-contract failures.

The TV layout and multi-server/connection-manager behavior are excluded from the gap
ranking. They are listed separately in [Out of scope](#out-of-scope).

## Status and evidence rules

| Status | Meaning in this audit |
|---|---|
| `ported` | The original route, controller, plugin or command has a working Blazor counterpart with the same principal behavior. Minor visual/framework substitutions are noted separately. |
| `partial` | A route or feature exists, but a documented behavior, capability, child flow or integration is absent or intentionally reduced. |
| `missing` | No functional counterpart was found in the Blazor source. |
| `out of scope` | Deliberately excluded by the port boundary; not counted as a real port gap. |

The port's `RouteTable` defaults an unannotated page to `AccessLevel.User`. Explicit
`Public`, `Admin` and `Wizard` annotations were checked against the original access
levels. The tables below use original source paths relative to the repository root and
Blazor paths relative to `blazor/src/Jellyfin.Web/`.

## 1. Route and controller coverage

### Legacy and modern user/session routes

The modern and legacy original layouts share these URLs unless the route inventory says
otherwise. `LibraryViews.razor` owns the library URL set; the other rows have the named
page shown below.

| Original route/controller | Blazor page | Access | Status |
|---|---|---:|---|
| `/` redirect; `apps/{legacy,modern}/routes/routes.tsx` | `AppRouter` home redirect | — | `ported` |
| `/home`; `apps/legacy/controllers/home.js`, `home.html` | `Pages/Home/Home.razor` plus `HomeTab.razor`, `FavoritesTab.razor` | User | `ported` |
| `/movies`; `movies/moviesrecommended.js`, `movies.html` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/tv`; `shows/tvrecommended.js`, `shows/*.js` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/music`; `music/musicrecommended.js`, `music.html` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/books`; `apps/modern/routes/books/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/boxsets`; `apps/modern/routes/boxsets/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/homevideos`; `apps/modern/routes/homevideos/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/musicvideos`; `apps/modern/routes/musicvideos/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/playlists`; `apps/modern/routes/playlists/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/mixed`; `apps/modern/routes/mixed/index.tsx` | `Pages/Library/LibraryViews.razor` | User | `ported` |
| `/livetv`; `apps/legacy/controllers/livetv/livetvsuggested.js` and tabs | `Pages/LiveTv/LiveTvPage.razor` | User | `ported` |
| `/details`; `apps/legacy/controllers/itemDetails/index.js` | `Pages/Details/ItemDetails.razor` | User | `ported` |
| `/list`; `apps/legacy/controllers/list.js` | `Pages/Library/ListPage.razor` | User | `partial` |
| `/lyrics`; `apps/legacy/controllers/lyrics.js` | `Pages/Playback/Lyrics.razor` | User | `ported` |
| `/video`; `apps/legacy/controllers/playback/video/index.js`, plus modern wrapper | `Pages/Playback/VideoOsd.razor` | User, fullscreen | `partial` |
| `/queue`; `apps/legacy/controllers/playback/queue/index.js` | `Pages/Playback/Queue.razor` | User, fullscreen | `partial` |
| `/search`; `apps/legacy/routes/search.tsx`, `features/search/**` | `Pages/Search/SearchPage.razor` | User | `ported` |
| `/mypreferencescontrols`; `controllers/user/controls/index.js` | `Pages/User/ControlsPreferences.razor` | User | `ported` |
| `/mypreferencesdisplay`; `controllers/user/display/index.*`, `components/displaySettings` | `Pages/User/DisplayPreferences.razor` | User | `partial` |
| `/mypreferenceshome`; `controllers/user/home/index.*`, `components/homeScreenSettings` | `Pages/User/HomePreferences.razor` | User | `ported` |
| `/mypreferencesplayback`; `controllers/user/playback/index.*`, `components/playbackSettings` | `Pages/User/PlaybackPreferences.razor` | User | `ported` |
| `/mypreferencessubtitles`; `controllers/user/subtitles/index.*`, `components/subtitlesettings` | `Pages/User/SubtitlePreferences.razor` | User | `ported` |
| `/mypreferencesmenu`; `apps/legacy/routes/user/settings/index.tsx` | `Pages/User/UserPreferencesMenu.razor` | User | `ported` |
| `/quickconnect`; `apps/legacy/routes/quickConnect/index.tsx` | `Pages/User/QuickConnectPage.razor` | User | `ported` |
| `/userprofile`; `apps/legacy/routes/user/userprofile.tsx` | `Pages/User/UserProfile.razor` | User | `ported` |
| `/addserver`; `controllers/session/addServer/index.*` | `Pages/Session/AddServer.razor` | Public | `ported` |
| `/selectserver`; `controllers/session/selectServer/index.*` | `Pages/Session/SelectServer.razor` | Public | `ported` |
| `/login`; `controllers/session/login/index.*` | `Pages/Session/Login.razor` | Public | `ported` |
| `/forgotpasswordpin`; `controllers/session/resetPassword/index.*` | `Pages/Session/ForgotPasswordPin.razor` | Public | `ported` |
| `/forgotpassword`; `apps/legacy/routes/session/forgotPassword/index.tsx` | `Pages/Session/ForgotPassword.razor` | Public | `ported` |
| `*`; `components/router/FallbackRoute.tsx` | App fallback/not-found handling | Public | `ported` |
| `!/*`; `components/router/BangRedirect.tsx` | Router normalization | — | `ported` |

The original route inventory also records `/tv` as the modern `shows` page and modern
library-only routes (`books`, `boxsets`, `homevideos`, `musicvideos`, `playlists`, and
`mixed`). All are represented by the shared Blazor library page rather than separate
components.

### Dashboard and metadata routes

| Original route/controller | Blazor page | Access | Status |
|---|---|---:|---|
| `/dashboard`; `apps/dashboard/routes/index.tsx` | `Pages/Dashboard/DashboardIndex.razor` | Admin | `ported` |
| `/dashboard/activity`; `routes/activity/index.tsx` | `Pages/Dashboard/DashboardActivity.razor` | Admin | `partial` |
| `/dashboard/backups`; `routes/backups/index.tsx` | `Pages/Dashboard/Backups/DashboardBackups.razor` | Admin | `ported` |
| `/dashboard/branding`; `routes/branding/index.tsx` | `Pages/Dashboard/DashboardBranding.razor` | Admin | `ported` |
| `/dashboard/devices`; `routes/devices/index.tsx` | `Pages/Dashboard/DashboardDevices.razor` | Admin | `partial` |
| `/dashboard/settings`; `routes/settings/index.tsx` | `Pages/Dashboard/DashboardSettings.razor` | Admin | `ported` |
| `/dashboard/keys`; `routes/keys/index.tsx` | `Pages/Dashboard/DashboardApiKeys.razor` | Admin | `ported` |
| `/dashboard/libraries`; `routes/libraries/index.tsx` | `Pages/Dashboard/Libraries/DashboardLibraryList.razor` | Admin | `ported` |
| `/dashboard/libraries/display`; `routes/libraries/display.tsx` | `Pages/Dashboard/Libraries/DashboardLibraryDisplay.razor` | Admin | `ported` |
| `/dashboard/libraries/metadata`; `routes/libraries/metadata.tsx` | `Pages/Dashboard/Libraries/DashboardLibraryMetadata.razor` | Admin | `ported` |
| `/dashboard/libraries/nfo`; `routes/libraries/nfo.tsx` | `Pages/Dashboard/Libraries/DashboardLibraryNfo.razor` | Admin | `ported` |
| `/dashboard/livetv`; `routes/livetv/index.tsx` | `Pages/Dashboard/LiveTv/DashboardLiveTv.razor` | Admin | `ported` |
| `/dashboard/livetv/recordings`; `routes/livetv/recordings.tsx` | `Pages/Dashboard/LiveTv/DashboardLiveTvRecordings.razor` | Admin | `ported` |
| `/dashboard/livetv/guide`; `controllers/livetvguideprovider.js`, `.html` | `Pages/Dashboard/LiveTv/DashboardLiveTvGuide.razor` | Admin | `ported` |
| `/dashboard/livetv/tuner`; `controllers/livetvtuner.js`, `.html` | `Pages/Dashboard/LiveTv/DashboardLiveTvTuner.razor` | Admin | `ported` |
| `/dashboard/logs`; `routes/logs/index.tsx` | `Pages/Dashboard/DashboardLogs.razor` | Admin | `ported` |
| `/dashboard/logs/:file`; `routes/logs/file.tsx` | `Pages/Dashboard/DashboardLogFile.razor` | Admin | `ported` |
| `/dashboard/networking`; `routes/networking/index.tsx` | `Pages/Dashboard/Networking/DashboardNetworking.razor` | Admin | `ported` |
| `/dashboard/playback/resume`; `routes/playback/resume.tsx` | `Pages/Dashboard/Playback/DashboardPlaybackResume.razor` | Admin | `ported` |
| `/dashboard/playback/streaming`; `routes/playback/streaming.tsx` | `Pages/Dashboard/Playback/DashboardPlaybackStreaming.razor` | Admin | `ported` |
| `/dashboard/playback/transcoding`; `routes/playback/transcoding.tsx` | `Pages/Dashboard/Playback/DashboardPlaybackTranscoding.razor` | Admin | `ported` |
| `/dashboard/playback/trickplay`; `routes/playback/trickplay.tsx` | `Pages/Dashboard/Playback/DashboardPlaybackTrickplay.razor` | Admin | `ported` |
| `/dashboard/plugins`; `routes/plugins/index.tsx` | `Pages/Dashboard/Plugins/DashboardPluginsIndex.razor` | Admin | `ported` |
| `/dashboard/plugins/:pluginId`; `routes/plugins/plugin.tsx` | `Pages/Dashboard/Plugins/DashboardPluginDetails.razor` | Admin | `ported` |
| `/dashboard/plugins/repositories`; `routes/plugins/repositories.tsx` | `Pages/Dashboard/Plugins/DashboardPluginRepositories.razor` | Admin | `ported` |
| `/dashboard/plugins/catalog`; legacy redirect | `Pages/Dashboard/Plugins/DashboardPluginCatalog.razor` | Admin | `ported` |
| `/dashboard/tasks`; `routes/tasks/index.tsx` | `Pages/Dashboard/DashboardTasks.razor` | Admin | `ported` |
| `/dashboard/tasks/:id`; `routes/tasks/task.tsx` | `Pages/Dashboard/DashboardTaskEdit.razor` | Admin | `ported` |
| `/dashboard/users`; `routes/users/index.tsx` | `Pages/Dashboard/DashboardUsers.razor` | Admin | `ported` |
| `/dashboard/users/add`; `routes/users/add.tsx` | `Pages/Dashboard/DashboardUserAdd.razor` | Admin | `ported` |
| `/dashboard/users/:userId/:tab`; `routes/users/edit.tsx` | `Pages/Dashboard/DashboardUserEdit.razor` | Admin | `ported` |
| `/metadata`; legacy `controllers/edititemmetadata.js`, `.html` | `Pages/Metadata/MetadataManager.razor` | Admin | `ported` |
| `/configurationpage`; `components/ServerContentPage.tsx` | `Pages/Dashboard/Plugins/DashboardPluginConfiguration.razor` | Admin | `partial` |

Dashboard tables use `MudTable` rather than material-react-table, so column hiding,
pinning, resizing, density/full-screen toggles, multi-column sorting and the activity
date-range filter are not present. Device rename uses a dialog rather than inline row
editing. These are partial behavior differences, not missing routes.

### Wizard routes

| Original route/controller | Blazor page | Access | Status |
|---|---|---:|---|
| `/wizard`; `apps/wizard/routes/routes.tsx` redirect | `Pages/Wizard/WizardIndex.razor` | Wizard | `ported` |
| `/wizard/start`; `controllers/start/index.*` | `Pages/Wizard/WizardStart.razor` | Wizard | `ported` |
| `/wizard/user`; `controllers/user/index.*` | `Pages/Wizard/WizardUser.razor` | Wizard | `ported` |
| `/wizard/library`; `controllers/library.js`, `.html` | `Pages/Wizard/WizardLibrary.razor` | Wizard | `ported` |
| `/wizard/settings`; `controllers/settings/index.*` | `Pages/Wizard/WizardSettings.razor` | Wizard | `ported` |
| `/wizard/remoteaccess`; `controllers/remote/index.*` | `Pages/Wizard/WizardRemoteAccess.razor` | Wizard | `ported` |
| `/wizard/finish`; `controllers/finish/index.*` | `Pages/Wizard/WizardFinish.razor` | Wizard | `ported` |

### Port-only route aliases

These do not represent additional original features; they are compatibility aliases that
make common legacy/dashboard links resolve in Blazor.

| Port-only route | Blazor page | Status |
|---|---|---|
| `/dashboard/tasks/edit` | `Pages/Dashboard/DashboardTaskEdit.razor` | `ported` |
| `/dashboard/users/profile` | `Pages/Dashboard/DashboardUserEdit.razor` | `ported` |
| `/dashboard/users/access` | `Pages/Dashboard/DashboardUserEdit.razor` | `ported` |
| `/dashboard/users/parentalcontrol` | `Pages/Dashboard/DashboardUserEdit.razor` | `ported` |
| `/dashboard/users/password` | `Pages/Dashboard/DashboardUserEdit.razor` | `ported` |

## 2. Item commands, shortcuts and multi-select

The original sources are `src/components/itemContextMenu.js`,
`src/components/shortcuts.js` and `src/components/multiSelect/multiSelect.js`. The port
uses direct implementations in `Services/Items/ItemContextMenu.cs` and
`ItemActions.cs`, plus `IItemCommandHandler` registrations. A command being listed in
the menu is not by itself proof that it is implemented: `ItemCommandHandlers` deliberately
shows `This feature is not available yet.` when no handler claims it.

| Original command/action | Port implementation | Status | Audit note |
|---|---|---|---|
| `resume`, `play`, `playallfromhere`, `queueallfromhere`, `queue`, `queuenext` | `ItemContextMenu.cs`, `ItemActions.cs`, `PlaybackManager` | `ported` | Queue-next is direct playback code, not a missing handler. |
| `stopPlayback`, `shuffle`, `instantmix` | `ItemContextMenu.cs`, `PlaybackManager` | `ported` | Directly handled. |
| `copy-stream` | `ItemContextMenu.cs`, `ItemDialogService` | `ported` | Copies/download URL according to the port UI. |
| `delete`, `removefromplaylist`, `movetotop`, `movetobottom`, `removefromcollection` | `ItemContextMenu.cs`, item/playlist services | `ported` | Confirmation and list refresh behavior are retained. |
| `album`, `artist`, `lyrics` | `ItemContextMenu.cs`, routing/playback services | `ported` | Direct navigation/action implementations. |
| `addtocollection`, `addtoplaylist`, `editplaylist` | `ItemDialogCommandHandler`, collection/playlist editors | `ported` | Multi-select forwards every selected id; direct item actions fall back to the clicked item. |
| `edit`, `editimages`, `editsubtitles`, `editlyrics`, `identify`, `moremediainfo`, `refresh` | `ItemDialogCommandHandler` and item dialogs | `ported` | `refresh` has the same collection-folder scan mode; multi-select refresh is the exception noted above. |
| `download`, `downloadall`, `share` | `ItemDialogCommandHandler`, `ItemDialogService` | `ported` | Download support remains browser/native-shell dependent as documented. |
| `record`, `programdialog`, `togglerecording`, `canceltimer`, `cancelseriestimer` | `LiveTvCommandHandler`, recording services | `ported` | Programme/timer type rules are represented. |
| `multiSelect` | `MultiSelectCommandHandler`, `MultiSelectCommands` | `ported` | Selection mode, select-all, delete, grouping, played state and refresh menu are present. |
| `clearQueue` | `Services/Playback/PlaybackItemCommands.cs` | `ported` | Clears the queue through `PlaybackManager.ClearQueue()`. |
| `playmenu` | `Services/Playback/PlaybackItemCommands.cs` | `ported` | Opens resume/start choices and passes the selected start position to playback. |
| `setplaylistindex` | `Services/Playback/PlaybackItemCommands.cs` | `ported` | Jumps by `PlaylistItemId`, preserving duplicate queue entries. |
| Multi-select `selectall`, `delete`, `groupvideos`, `markplayed`, `markunplayed` | `MultiSelectCommands` | `ported` | Per-item played updates and group-version operations are implemented. |
| Multi-select `addtocollection`, `playlist`, `refresh` | `MultiSelectCommands` → `IItemCommandHandler` | `ported` | The dialog handlers receive all selected item ids; browser coverage checks the payload. |

## 3. Original plugin inventory and port counterparts

Every directory under `src/plugins/` was checked. A counterpart means a functional
service/component, not merely a copied preference string.

| Original plugin | Purpose | Blazor counterpart | Status |
|---|---|---|---|
| `htmlVideoPlayer` | HTML5 video, HLS and subtitles | `Services/Playback/HtmlMediaPlayer.cs`, `wwwroot/js/playback/player.js` | `ported` |
| `htmlAudioPlayer` | HTML5 audio | `Services/Playback/HtmlMediaPlayer.cs`, `wwwroot/js/playback/player.js` | `ported` |
| `sessionPlayer` | Remote-control another server session | `Services/Playback/SessionPlayer.cs`, `PlaybackManager.Remote.cs` | `ported` |
| `bookPlayer` | EPUB reader | `Services/Players/BookPlayers.cs`, player interop | `ported` |
| `pdfPlayer` | PDF reader | `Services/Players/BookPlayers.cs` (`PdfPlayer`), managed player flow | `ported` |
| `comicsPlayer` | CBZ/CBR reader | `Services/Players/BookPlayers.cs` (`ComicsPlayer`), managed player flow | `ported` |
| `photoPlayer` | Open photo slideshow | `Services/Players/PhotoPlayer.cs` | `ported` |
| `youtubePlayer` | YouTube trailer playback | `Services/Players/UrlPlayers.cs` (`YoutubePlayer`) and playback item flow | `ported` |
| `syncPlay` | Synchronized local playback and group UI | `Services/Playback/SyncPlay/*`, `Components/SyncPlay/*` | `partial` |
| `chromecastPlayer` | Chromecast target | No functional counterpart; remote menu explicitly reports unsupported | `missing` |
| `backdropScreensaver` | Backdrop slideshow screensaver | `Pages/User/DisplayOptions.cs` only lists the option; no player/screensaver runtime | `missing` |
| `logoScreensaver` | Bouncing-logo screensaver | `Pages/User/DisplayOptions.cs` only lists the option; no player/screensaver runtime | `missing` |
| `stillWatching` | `Services/Playback/PlaybackInterceptors.cs`, `PlaybackActivity` | `ported` | Episode-count/time/idle thresholds and Continue/Stop dialog are enforced before negotiation. |
| `experimentalWarnings` | `Services/Playback/PlaybackInterceptors.cs` | `ported` | ISO/Blu-ray/DVD warnings are keyed once per ISO week. |
| `playAccessValidation` | `Services/Playback/PlaybackInterceptors.cs` | `ported` | Fullscreen access shows the restriction alert; non-fullscreen rejection is silent. |

PDF, comics and YouTube each have a registered player counterpart, while ordinary URL
playback is handled by `UrlPlayer` in the same file. SyncPlay is functional for local transport and has
the toolbar, settings and OSD marker, but its menu is intentionally compact and media
ready timing uses the local player's existing events rather than a separate wrapper.

## 4. `PORT-TODO` and documented gap inventory

The following is a deduplicated inventory. Repeated references in workstream documents are
grouped with the source marker and current status. The code currently contains five
`PORT-TODO` locations; some older documents contain references to those markers or to
work that has since been completed.

| Source marker/documentation | One-line summary | Current status |
|---|---|---|
| `blazor/src/Jellyfin.Web/wwwroot/js/items.js:150` (`PORT-TODO(items)`) | Original lazy loader unloads images outside the viewport; port only loads/fades them in. | Open, low priority. |
| `blazor/src/Jellyfin.Web/Components/Items/Scroller.razor:7` (`PORT-TODO(items)`) | Original desktop scroller moves the slider with transforms; port uses native scrolling. | Open, low priority. |
| `blazor/src/Jellyfin.Web/Services/Playback/AirPlay/AirPlayFeatures.cs` | HTML audio/video AirPlay target-picker support and capability-gated OSD, bar and queue controls. | Closed; WebKit target events and fake-interop UI tests cover it. |
| `blazor/src/Jellyfin.Web/Pages/Playback/VideoOsd.razor` | Touch brightness gesture, CSS-filter player bridge and original-style indicator. | Closed; swipe filtering and fake-interop UI tests cover the 0–100 behavior. |
| `blazor/src/Jellyfin.Web/wwwroot/js/playback/player.js` + `Pages/Playback/VideoOsd.razor` | Native iOS fullscreen exit stops playback and returns from the OSD. | Closed; the dedicated WebKit event and OSD back-navigation are covered by fake-interop UI tests. |
| `blazor/src/Jellyfin.Web/Pages/Playback/VideoOsd.razor:9-10` (`PORT-TODO(playback)`) | Video OSD lacks recording controls and TV key handling. | Open; TV portion is out of scope, recording controls are a real gap. |
| `docs/blazor-port/06-playback-port.md` | Missing cast/AirPlay, Live TV OSD details, ASS/SSA and image subtitles, gestures and several queue/OSD details. | Open; TV layout excluded separately; playback interceptors are now implemented. |
| `docs/blazor-port/15-playback-extras.md` | Chromecast unsupported; remote queue clearing is a no-op; SyncPlay and live-TV-specific extras were previously listed as future work. | Chromecast remains open; SyncPlay is now ported and its old “not started” text is stale. |
| `docs/blazor-port/07-library-pages.md` | Recursive list filtering differs; TV on-screen alphabet is not ported; shared shell typography/visual details differ. | Recursive filter and visual differences open; TV alphabet out of scope. |
| `docs/blazor-port/05-item-components.md` | Item scroller and image lazy-unload limitations; minor card/list indicator and styling differences. | Scroller/image items open; other differences are minor. |
| `docs/blazor-port/08-dashboard.md` | MudTable lacks several material-react-table controls; device rename is dialog-based; relative-time and app-bar differences exist. | Open intentional partials, not missing routes. |
| `docs/blazor-port/09-item-dialogs.md` | Native-shell downloads and SyncPlay playlist target limitations; typography/reset differences. | Downloads depend on host; SyncPlay target is now ported; old `/metadata` claim is stale. |
| `docs/blazor-port/10-details-page.md` | Shared shell layout/typography gap; channel guide is intentionally a link rather than embedded content. | Open visual difference; Live TV behavior intentional. |
| `docs/blazor-port/11-dashboard-libraries-playback-networking.md` | Small control/widget and verification differences in dashboard library, playback and networking pages. | Open partial/visual differences. |
| `docs/blazor-port/12-dashboard-plugins-backups.md` | Server-provided plugin HTML/JavaScript configuration pages cannot run inside Blazor. | Open by design; `/configurationpage` remains a warning page. |
| `docs/blazor-port/13-livetv.md` | Legacy widget styling is replaced by Razor/MudBlazor controls; shared shell gaps remain. | Intentional partial/visual difference. |
| `docs/blazor-port/14-dashboard-livetv-metadata.md` | jstree is replaced by an accessible Razor tree; no dashboard `AppTabs` event is emitted. | Ported behavior with intentional implementation differences. |
| `docs/blazor-port/17-syncplay.md` | Compact modern menu and local player media-ready timing differ from the legacy UI. | Current limitation; the old “not ported” claim elsewhere is stale. |
| `blazor/src/Jellyfin.Web/Services/ItemDialogs/ItemDialogService.cs` | Multi-item dialog commands derive ids from `SelectedItems`. | Closed; direct and multi-select tests cover the fallback and collection. |
| `blazor/src/Jellyfin.Web/Services/Playback/PlaybackInterceptors.cs` | Playback-boundary validation, warnings and still-watching checks. | Closed for the three pre-play plugins; screensavers remain open. |
| `blazor/src/Jellyfin.Web/Services/Playback/PlaybackItemCommands.cs` | Playback-owned implementations for queue/play commands. | Closed; focused command tests cover clear, jump and resume. |
| `blazor/src/Jellyfin.Web/Pages/Playback/Queue.razor` | Queue supports native drag/drop as well as accessible move buttons. | Closed; bUnit and browser coverage exercise both paths. |
| `blazor/src/Jellyfin.Web/Pages/User/DisplayOptions.cs:87-118` | Screensaver names/options are preserved without runtime screensaver implementations. | Open plugin gap; preference exposure should be revisited. |
| `blazor/src/Jellyfin.Web/Pages/User/PlaybackOptions.cs:12-54` | Still-watching option values are preserved without the original prompt plugin. | Open plugin gap; preference exposure should be revisited. |

### Stale documentation identified during the audit

`docs/blazor-port/09-item-dialogs.md` still says that `/metadata` is missing, while
`Pages/Metadata/MetadataManager.razor` and `docs/blazor-port/14-dashboard-livetv-metadata.md`
show that it is implemented. The same older item-dialog/playback text says SyncPlay is
not ported, while `Services/Playback/SyncPlay/*`, `Components/SyncPlay/*` and
`docs/blazor-port/17-syncplay.md` show a functional local SyncPlay implementation. Those
claims are not counted as current gaps in this audit.

## 5. Out of scope

The following are explicit boundary decisions and are not counted in the prioritized gap
list:

| Area | Boundary |
|---|---|
| TV layout | The original `tv`/TV-specific layout, remote/ten-foot navigation, TV key handling, on-screen alphabet and TV-specific visual variants are not a target of the Blazor port. The `/tv` library route itself is ported in the normal user layout; “TV layout” means the separate layout mode and its interaction model. |
| Multi-server and connection manager | The original server discovery, server switching, multi-server session orchestration and connection-manager behavior are outside this port. The public add/select/login routes are present so a supported single-server session can be established, but parity with the original multi-server manager is not claimed. |

TV-layout limitations are therefore recorded as `out of scope`, not `missing`. Chromecast,
AirPlay, plugin runtime behavior and ordinary desktop/mobile playback are not covered by
this exception and remain real gaps where marked above.

## 6. Overall assessment

The Blazor port is route-complete against the audited original inventory and has broad
coverage of library, dashboard, wizard, metadata, Live TV, dialogs, local playback,
remote session playback, synced lyrics and SyncPlay. The highest-value follow-up work is
to decide how unsupported screensaver settings should be represented and add the missing cast/AirPlay and playback
interactions. The remaining route-level `partial` statuses are intentional UI/control
substitutions or feature subsets and should not be mistaken for absent pages.
