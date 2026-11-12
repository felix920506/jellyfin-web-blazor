I've written the spec below from the source. Two pieces are not in the repo and are marked **[INFERRED]**: the `jellyfin-apiclient@1.11.0` package (it provides `Credentials`, `ApiClient` and its `.ajax` and header logic) and `@jellyfin/sdk` internals (the auth header string and websocket transport). Everything else is quoted from files.

---

## 1. Bootstrap sequence

**`src/index.tsx` `init()`, in order:**
1. Import `@/lib/legacy` polyfills. Importing `@/lib/jellyfin-apiclient` builds the `ServerConnections` singleton; it reads `appHost.deviceId()` lazily.
2. Module side effects: `emby-button`, `displayMirrorManager`, `playerSelectionMenu`, `themeMediaPlayer`, `mouseManager`, `screensavermanager`, and the site SCSS (`site`, `livetv`, `dashboard`, `detailtable`, `librarybrowser`). `layoutManager.init()` also runs at import: it reads localStorage `layout` and adds the `layout-<mode>` class to `<html>`.
3. `unregisterServiceWorkers()`, then log the version and commit.
4. Set `window.Events` and `window.TaskButton`.
5. `pageClassOn('viewshow','standalonePage')` adds `.noHeaderRight` to `.skinHeader`; `viewhide` removes it.
6. `await appHost.init()`.
7. Pick the server URL:
   - If there is a saved server, use `getServerAddress(ServerConnections.getLastUsedServer())`.
   - Otherwise use `serverAddress()` from `src/utils/dashboard.js` (rules in §2).
   - If a URL was found, call `ServerConnections.initApiClient(url)`.
8. `initializeAutoCast()`.
9. `await loadCoreDictionary()`. Then hook `localusersignedin` and `localusersignedout` to `globalize.updateCurrentCulture`. On `LANGUAGE_CHANGE`, `updateApiClientSdk(apiClient)` re-sends the languages.
10. `loadFonts()`:
    - TV (not Android): `fonts.sized.scss`.
    - `__USE_SYSTEM_FONTS__`: `fonts.scss`.
    - Otherwise: `fonts.scss` and `fonts.noto.scss`.
    - iOS also loads `ios.scss`.
11. `loadPlugins()`:
    - Start from `webSettings.getPlugins()`, i.e. config.json `plugins`.
    - Drop `sessionPlayer*` and `chromecastPlayer*` if the RemoteControl feature is unsupported.
    - Drop `chromecastPlayer*` on browsers other than Chrome, Edge Chromium or Opera.
    - Add `NativeShell.getPlugins()` if present.
    - Load all with `pluginManager.loadPlugin`.
12. Register `appRouter.onRequestFail` on each ApiClient's `requestfail` event, and on `apiclientcreated` for future clients.
13. `initializeServerConnections()` (websocket subscriptions, §9).
14. `renderApp()`: clear `#reactRoot` (removes `.splashLogo`), `loading.show()`, then `createRoot(...).render(<RootApp/>)`.
15. `loadPlatformFeatures()` loads these modules when the condition holds:
    - `nowPlayingBar`: not TV, Xbox or PS4.
    - `remotecontrolautoplay`: RemoteControl supported.
    - `volumeosd`: no PhysicalVolumeControl, or a touch device.
    - `playbackorientation`: not TV or Xbox.
    - `notifications`: same as above and `window.Notification` exists.
16. `keyboardNavigation.enable()`, `autoFocuser.enable()`.
17. On any error: render `<main class="page mainAnimatedPage standalonePage" role="alert">` with "Jellyfin Web failed to start", the error text, and a Reload button.

**`src/RootApp.tsx` provider nesting:** `PersistQueryClientProvider` (IndexedDB persister named `'jellyfin-query-cache'`, `buster: __JF_BUILD_VERSION__`) → `ApiProvider` → `UserSettingsProvider` → `WebConfigProvider` → `QueryClientEventHandler` + `RootAppRouter`.

**`src/hooks/useApi.tsx` (`ApiProvider`):**
- Holds `{api, serverId, user}`.
- Calls `currentApiClient()?.getCurrentUser()` once.
- `localusersignedin` sets the user and serverId; `localusersignedout` clears them.
- `api = ServerConnections.getApi(serverId)`.

**`src/RootAppRouter.tsx`:**
- Uses `createHashRouter`, so URLs look like `#/home`.
- Root `RootAppLayout` renders the MUI `ThemeProvider`, `<Backdrop/>` and `<AppHeader isHidden={layoutManager.modern || path starts with /dashboard|/metadata|/configurationpage}/>`.
- Children, in order:
  - `MODERN_APP_ROUTES` if `layoutManager.modern`, else `LEGACY_APP_ROUTES`.
  - `DASHBOARD_APP_ROUTES`.
  - `WIZARD_APP_ROUTES`.
  - `'!/*'` → `BangRedirect` (strips the legacy `#!/`).
- `history` is a `RouterHistory` wrapper that fires the document event `HISTORY_UPDATE`.

**Layout mode** (`src/components/layoutManager.js`, `src/constants/layoutMode.ts`):
- Values: `auto|desktop|desktop-legacy|modern|mobile|mobile-legacy|tv`.
- `modern = !['desktop-legacy','mobile-legacy','tv'].includes(value)`.
- The default comes from `appHost.getDefaultLayout()`, which returns `'modern'`.

## 2. Server connection

**Files:** `src/lib/jellyfin-apiclient/{ServerConnections.js, connectionManager.js, connectionState.ts, connectionMode.ts, utils/*}`.

**`ConnectionState` strings:** `SignedIn`, `ServerMismatch`, `ServerSignIn`, `ServerSelection`, `ServerUpdateNeeded`, `Unavailable`.

**`ConnectionMode`:** `Local=0`, `Remote=1`, `Manual=2`.

**`getServerAddress(server)`:**
- Picks by `LastConnectionMode`: Local → `LocalAddress`, Manual → `ManualAddress`, Remote → `RemoteAddress`.
- Otherwise falls back to `ManualAddress || LocalAddress || RemoteAddress`.

**First-run discovery, `serverAddress()` in `src/utils/dashboard.js`:**
1. If a current ApiClient exists, use its address.
2. Otherwise read config.json `servers`. Config is fetched with `fetchLocal('config.json',{cache:'no-store'})`; if the fetch fails, the bundled `src/config.json` is used.
3. If the list is empty, compute the URL: take `location.href` up to the last `/web` (case-insensitive), else `location.origin`. A `file:` URL means no server.
4. `GET {url}/System/Info/Public` (`cache:'no-cache'`) on every candidate.
5. Pick the first one whose `StartupWizardCompleted` is false, else the first that answered.

**`initApiClient(url)`:**
- Builds `new ApiClient(url, enc(appName), enc(appVersion), enc(deviceName), enc(deviceId))`, where `enc = encodeURIComponent(safeDecodeURIComponent(x))`.
- Sets `enableAutomaticNetworking=false` and `manualAddressOnly=true`.
- Calls `addApiClient`:
  - Find a saved server whose Manual, Local or Remote address equals the URL (case-insensitive); otherwise use `apiClient.serverInfo()`.
  - Set `DateLastAccessed=now`, `LastConnectionMode=2`, `ManualAddress=url`, `manualAddressOnly=true`.
  - If no saved server matched, **replace** `Servers` with `[server]`.
  - Fire `apiclientcreated`.
- Then `setLocalApiClient`, which sets `window.ApiClient`.

**`connect(options)`:**
- `options` defaults to `{enableAutoLogin: appSettings.enableAutoLogin()}`.
- Calls `getAvailableServers()`: saved servers plus `NativeShell.findServers(1000)` if available, sorted by `DateLastAccessed` (newest first) and persisted.
- Then `connectToServers`:
  - With no servers, the result is `{State:'ServerSelection', Servers:[]}`.
  - Otherwise it runs `connectToServer(servers[0])`.

**`connectToServer(server)`:**
1. `tryReconnect` races `GET {addr}/System/Info/Public` (20 s timeout) on the addresses below; the first success wins. If every address fails → `Unavailable`.
   - LocalAddress at 0 ms, skipped if `manualAddressOnly`.
   - ManualAddress at 100 ms.
   - RemoteAddress at 200 ms, skipped if `manualAddressOnly`.
2. If `compareVersions(MINIMUM_VERSION from SDK, info.Version) === 1` → `{State:'ServerUpdateNeeded', Servers:[server]}`.
3. If `server.Id` is set and differs from `info.Id` → `{State:'ServerMismatch'}`.
4. Otherwise `onSuccessfulConnection`:
   - If `enableAutoLogin === false`, clear the token and UserId.
   - Else if a token exists, validate it with `GET {url}/System/Info` and the auth header. On failure, clear the token and UserId.
   - Update `Name`, `Id` and `LocalAddress` from SystemInfo. Set `LastConnectionMode` and `DateLastAccessed`, then persist.
   - Result: `{Servers:[server], ApiClient, SystemInfo, State}`, where State is `SignedIn` if a token remains, else `ServerSignIn`.
   - If SignedIn: `reportCapabilities` (§ capabilities below), `getCurrentUser`, then the `localusersignedin` event.
   - Fires `connected`.

**`connectToAddress(addr)`:**
- Trims the address and lowercases `Http:` and `Https:`.
- With no scheme, tries `https://` and then `http://`.
- Returns `Unavailable` if both fail.

**Auth success (`apiClient.onAuthenticated(instance, result)`):**
- Set `server.Id`, `UserId`, `AccessToken` and `DateLastAccessed`, then persist.
- `setAuthenticationInfo(token, userId)`, `updateApiClientSdk`, `reportCapabilities`.
- `ServerConnections.onLocalUserSignedIn(user)`:
  - `setLocalApiClient`.
  - After 6 s, run a bitrate test.
  - `userSettings.setUserInfo(userId, apiClient)`.
  - Then trigger `localusersignedin`.

**`logout()`:**
- For each client with a token, run `apiClient.logout()` and `SessionApi.reportSessionEnded()`, then trigger `localusersignedout`.
- Null out `UserId`, `AccessToken` and `ExchangeToken` on servers where `UserLinkType !== 'Guest'`.
- `Dashboard.logout` then clears the query cache and calls `viewContainer.reset()`. It goes to `selectserver` if MultiServer is supported, else `login`.

**Other operations:**
- `deleteServer(id)` removes the server from `Servers`.
- `updateSavedServerId(server)` is the "force connect" action on a mismatch: it re-reads the Id, clears the token, then reloads the page.

**Credentials storage [INFERRED from jellyfin-apiclient 1.11; the class is imported but not in the repo]:**
- localStorage key `jellyfin_credentials`.
- Shape:
  ```json
  {"Servers":[{"Id","Name","ManualAddress","LocalAddress","RemoteAddress","LastConnectionMode":0|1|2,"DateLastAccessed":<ms>,"UserId","AccessToken","manualAddressOnly":true,"UserLinkType"?,"ExchangeToken"?}]}
  ```
- `credentials()` reads; `credentials(obj)` writes.
- `addOrUpdateServer(list, server)` merges by `Id`.
- The field names are confirmed by their use in `connectionManager.js`.

**Device and client identity (`src/components/apphost.js`):**
- Device ID:
  - localStorage key `_deviceId2` (raw, no user prefix).
  - Generated as `btoa(navigator.userAgent + '|' + Date.now()).replaceAll('=','1')`.
- Device name:
  - Default `'Web Browser'`.
  - Otherwise the first match from: `tizen:'Samsung Smart TV'`, `web0s:'LG Smart TV'`, `titanos:'Titan OS'`, `vega:'Vega OS'`, `operaTv:'Opera TV'`, `xboxOne:'Xbox One'`, `ps4:'Sony PS4'`, `chrome:'Chrome'`, `edgeChromium:'Edge Chromium'`, `edge:'Edge'`, `firefox:'Firefox'`, `opera:'Opera'`, `safari:'Safari'`.
  - Then append `' iPad'`, `' iPhone'` or `' Android'`.
- App name `'Jellyfin Web'`; version `__PACKAGE_JSON_VERSION__` (package.json `13.0.0`).
- `NativeShell.AppHost.*` overrides all of these.

**Authorization header:**
- Name: `Authorization` (SDK `AUTHORIZATION_HEADER`).
- Value from SDK `getAuthorizationHeader(clientInfo, deviceInfo, token)` **[INFERRED format]**:
  `MediaBrowser Client="Jellyfin%20Web", Device="Firefox", DeviceId="<id>", Version="13.0.0", Token="<token>"`
- Values are URL-encoded, and `Token` is omitted when there is none.
- `compat.ts` decodes before handing values to the SDK to avoid double encoding.
- The SDK also gets `deviceInfo.languages = getLanguages()`, e.g. `['en-us','en']`.

**Capabilities** (`Dashboard.capabilities`):
- `PlayableMediaTypes: ['Audio','Video']`, `SupportsMediaControl: true`.
- `SupportsPersistentIdentifier`: true only for `appMode` `cordova` or `android`.
- `SupportedCommands`: the 33 names in `src/utils/dashboard.js:188`.

**Events emitted on `ServerConnections`:** `apiclientcreated`, `connected`, `localusersignedin`, `localusersignedout`.

## 3. Route guarding (`src/components/ConnectionRequired.tsx`)

**Access levels:** `admin`, `public`, `user` (default), `wizard`.

**Which routes use which level:**
- `user`: every user route under `/*`.
- `public`: `/addserver`, `/selectserver`, `/login`, `/forgotpasswordpin` (legacy views) and `/forgotpassword` (React). The fallback `*` → `FallbackRoute` also sits here.
- `admin`: `/dashboard/*`, `/metadata`, `/configurationpage`.
- `wizard`: `/wizard/{start,user,settings,library,remoteaccess,finish}`.
- `/` redirects to `/home`.

**Logic on mount:**
1. If `ServerConnections.firstConnection` is already set, `conn = null`; otherwise `conn = await ServerConnections.connect()`. Then set `firstConnection = true`.
2. If `conn.State` is `ServerMismatch`, `ServerUpdateNeeded` or `Unavailable`, render `ConnectionErrorPage`:
   - Mismatch: `HeaderServerMismatch` / `MessageServerMismatch` plus a force-connect button.
   - Update needed: `HeaderUpdateRequired` / `ServerUpdateNeeded`.
   - Unavailable: `HeaderServerUnavailable` / `MessageUnableToConnectToServer`.
3. Wizard level: fetch `/System/Info/Public`. If `StartupWizardCompleted`, go to `/home`; otherwise `setLocalApiClient` and render.
4. If `conn` exists, is not SignedIn, and the current client is not logged in:
   - If State is `ServerSignIn` and the wizard is not complete, go to `/wizard/start`.
   - Otherwise bounce.
5. Otherwise validate access:
   - user or admin level and not `isLoggedIn()` → `bounce(await connect())`.
   - admin level and not `user.Policy.IsAdministrator` → bounce.
   - Otherwise render `<Outlet/>`.

**`bounce(result)`:**
- `SignedIn` → `/home`.
- `ServerSignIn` → `/login?serverid=<id>&url=<encodeURIComponent(pathname+search)>`, unless already on `/login`.
- `ServerSelection` → `/selectserver`.

**Return URL:** the login page decodes `params.url` and defaults to `/home`; `Dashboard.navigate(url)` runs after success.

**Forced logout:** `appRouter.onRequestFail` on HTTP 403 with `errorCode==='ParentalControl'`, unless the current path is in `PUBLIC_PATHS`:
- Alert `AccessRestrictedTryAgainLater`.
- Navigate to `login?serverid=`.

**`FallbackRoute`:**
- Redirects `/wizardX.html` → `/wizard/X`.
- Strips a trailing `.html`.
- Otherwise shows `HeaderPageNotFound`.

## 4. Login and session pages (`src/apps/legacy/controllers/session/*`)

**Login (`login/index.js` + `index.html`, `#loginPage.standalonePage.backdropPage`, `data-backbutton="false"`)**

On `viewshow`:
- `loading.show()`, `setTransparentMenu(true)`.
- Hide `.btnSelectServer` unless MultiServer is supported.

API calls on `viewshow` (using the SDK api for `params.serverid`):
- `GET /QuickConnect/Enabled`: if true, show `.btnQuick`.
- `GET /Users/Public`:
  - Non-empty: render the visual form, `#divUsers` cards (`.card.squareCard.scalableCard.squareCard-scalable`, `.cardContent[data-haspw][data-username][data-userid]`). Image is the SDK `getUserImageUrl(user,{width:round(300*dpr)})`; without one, `material-icons person` on the default background class.
  - Empty: manual form.
- `GET /Branding/Configuration`: `LoginDisclaimer` is rendered with markdown-it (`html:true`) and DOMPurify into `.loginDisclaimer`. Links get `target=_blank`, `rel=noopener noreferrer`, class `button-link`.

Interactions:
- **User card click:**
  - `haspw=='false'`: authenticate with an empty password.
  - Otherwise: prefill the name and show the manual form focused on the password.
- **Manual form** (`.manualLoginForm`):
  - `#txtManualName` and `#txtManualPassword`; `.chkRememberLogin` saves to `appSettings.enableAutoLogin`.
  - `POST /Users/AuthenticateByName` with body `{Username, Pw}`.
  - 401 → toast `MessageInvalidUser`; 403 → toast `MessageUnauthorizedUser`; any other error → alert `HeaderConnectionFailure` / `MessageUnableToConnectToServer`. On failure the password field is cleared.
- **Success:** `getOrCreateApiClient(result.ServerId).onAuthenticated(apiClient, result)`, then `setLocalApiClient`, then `navigate(url||'home')`.
- **Quick Connect:**
  1. `POST /QuickConnect/Initiate` returns `{Secret, Code}`.
  2. Show an alert (`id='quickConnectAlert'`) titled `QuickConnect` with text `QuickConnectAuthorizeCode` formatted with the code.
  3. Every 5000 ms, `GET /QuickConnect/Connect?secret=`. Once `Authenticated`: close the dialog and wait for it, then `POST /Users/AuthenticateWithQuickConnect` with `{Secret}`, then run the success path.
  4. A poll error → alert `QuickConnectDeactivated`. An initiate error → alert `QuickConnectNotActive`.
- **Other buttons:**
  - `.btnForgotPassword` → `forgotpassword`.
  - `.btnSelectServer` → `Dashboard.selectServer()` (`NativeShell.selectServer` or `selectserver`).
  - `.btnManual` shows the manual form.
  - `.btnCancel` shows the visual form.

**Forgot password (`src/apps/legacy/routes/session/forgotPassword/index.tsx`):**
- `POST /Users/ForgotPassword` with `{EnteredUsername}`. Result `Action`:
  - `ContactAdmin` → `MessageContactAdminToResetPassword`.
  - `InNetworkRequired` → `MessageForgotPasswordInNetworkRequired`.
  - `PinCode` with a valid URL `PinFile` → open it, then `/login`.
  - `PinCode` otherwise → show the file path, then `/forgotpasswordpin`.

**Reset PIN (`resetPassword/index.js`):**
- `POST /Users/ForgotPassword/Pin` with `{Pin}`.
- `Success` → list `UsersReset`, then `location.href=''`. Otherwise `MessageInvalidForgotPasswordPin`.

**Select server (`selectServer/index.js`):**
- Cards come from `getAvailableServers()`. With none: `MessageNoServersAvailable`.
- Clicking a card opens an actionsheet with Connect or Delete.
- Connect calls `connectToServer(server,{enableAutoLogin})`:
  - SignedIn → `home`.
  - ServerSignIn → `login?serverid=`.
  - UpdateNeeded → alert.
  - Anything else → `MessageUnableToConnectToServer`.
- Delete asks for confirmation (`DeleteName` / `DeleteServerConfirmation`), then `deleteServer`.
- `.btnAddServer` links to `#/addserver`.

**Add server (`addServer/index.js`):**
- `#txtServerHost` with trailing slashes stripped, then `connectToAddress`.
- SignedIn → `home`.
- ServerSignIn → `login?serverid=` if `SystemInfo.StartupWizardCompleted`, else `/wizard/start`.
- ServerSelection → `selectserver`.
- UpdateNeeded or Unavailable → alert.

## 5. Settings

**`appSettings.js` (localStorage only):**
- Key is `name`, or `${userId}-${name}` when a userId is given. `set` fires `change`.
- Global keys and defaults:

| Key | Default |
|---|---|
| `enableAutoLogin` | true |
| `enableGamepad` | false |
| `enableSmoothScroll` | `browser.tizen` |
| `enableSystemExternalPlayers` | false |
| `enableautobitratebitrate-${mediaType}-${isInNetwork}` | true; in-network Audio is always true |
| `maxbitrate-${mediaType}-${isInNetwork}` | 1500000; in-network Audio always returns 150000000 |
| `maxStaticMusicBitrate` | 320000 |
| `chromecastBitrate1` | null |
| `maxVideoWidth` | 0 |
| `limitSupportedVideoResolution` | false |
| `preferredTranscodeVideoCodec` | `''` |
| `preferredTranscodeVideoAudioCodec` | `''` |
| `alwaysBurnInSubtitleWhenTranscoding` | false |
| `enableDts` | false |
| `enableTrueHd` | false |
| `enableHi10p` | false |
| `disableVbrAudio` | false |
| `alwaysRemuxFlac` | false |
| `alwaysRemuxMp3` | false |
| `aspectRatio` | `''` |

- Also stored here: `layout` and `_deviceId2`.
- Booleans are stored as `"true"`/`"false"` and parsed with `toBoolean(v, default)`.

**`userSettings.js` (per user):**
- `set(name, value, enableOnServer)` always writes localStorage `${userId}-${name}`.
- Unless `enableOnServer === false`, it also writes `displayPrefs.CustomPrefs[name] = String(value)` and debounces (50 ms) `updateDisplayPreferences('usersettings', prefs, userId, 'emby')`.
- `get(name, enableOnServer)`: if not false and prefs are loaded, read `CustomPrefs[name]`; otherwise read localStorage.
- `setUserInfo` loads `GET /DisplayPreferences/usersettings?userId=&client=emby`.

**Server-synced settings (CustomPrefs):**

| Key | Default / notes |
|---|---|
| `skipBackLength` | 10000 |
| `skipForwardLength` | 30000 |
| `dashboardTheme` | — |
| `useEpisodeImagesInNextUpAndResume` | false |
| `enableNextVideoInfoOverlay` | true; written to the server but read locally |
| `enableVideoRemainingTime` | true; written to the server but read locally |
| query sort `<key>` | JSON of `SortBy`, `SortOrder` |
| `<key>-_view` | saved view |
| filters via `setFilter` | — |

**Local-only per-user settings:**

| Key | Default |
|---|---|
| `allowedAudioChannels` | `'-1'` |
| `preferFmp4HlsContainer` | true on Safari, Firefox, Chrome, Edge Chromium |
| `limitSegmentLength` | false |
| `enableCinemaMode` | true |
| `selectAudioNormalization` | `'TrackGain'` |
| `enableThemeSongs` | false |
| `enableThemeVideos` | false |
| `fastFadein` | true |
| `blurhash` | true |
| `enableBackdrops` | false |
| `disableCustomCss` | false |
| `customCss` | — |
| `detailsBanner` | true |
| `language` | — |
| `datetimelocale` | — |
| `skin` | — |
| `appTheme` | — |
| `screensaver` | — |
| `backdropScreensaverInterval` | 5 |
| `slideshowInterval` | 5 |
| `screensaverTime` | 180 |
| `libraryPageSize` | 100; 0 is allowed |
| `maxDaysForNextUp` | 365; 0 is allowed |
| `enableRewatchingInNextUp` | false |
| `soundeffects` | — |
| `stillWatchingPrompt` | `StillWatchingOptions.Disabled` |
| `<key>-filter` | JSON of the allowed filter fields |
| `localplayersubtitleappearance3` | JSON, defaults `{verticalPosition:-3, aspectMode:'contain'}` |
| `comicsPlayerSettings` | JSON map keyed by mediaSourceId, defaults `{langDir:'ltr', pagesPerView:1}` |

- `serverConfig()` reads and writes `User.Configuration` through `updateUserConfiguration`.

**`webSettings.js` (config.json, cached once):**
- `includeCorsCredentials`, `multiserver` (forced true under webpack-serve), `servers`, `themes`, `menuLinks` (`{name,url,icon?}`), `plugins`.
- The default theme is whichever entry has `default:true`, else `{name:'Dark', id:'dark'}`.

## 6. Localization (`src/lib/globalize/*`)

- **Dictionaries:** `locales.ts` lists language codes; each maps to `{lang, path:`${lang}.json`}` and is loaded by dynamic import from `src/strings/`. `loadCoreDictionary()` registers module `'core'` as the default.
- **Culture choice:**
  1. `userSettings.language()`.
  2. Else `<html data-culture>`.
  3. Else `navigator.language`, then `userLanguage`, then `languages[0]`.
  4. Else `'en-us'`.
  - The result is normalized: `_` becomes `-` and it is lowercased.
- **File lookup:** exact match → base language (strip `-.*`) → `en-us`.
- **Loading:** the current culture and `en-us` (`FALLBACK_CULTURE`) are both loaded.
- **Key lookup:** current dictionary → `en-us` → the key itself (logs an error).
- **Module prefix:** `'module#Key'`, e.g. `core#ServerUpdateNeeded`.
- **`translate(key, ...args)`:** replaces every `{i}` with `args[i].toLocaleString(currentCulture)`.
- **`translateHtml(html)`:** recursively replaces `${Key}` in legacy view templates.
- **RTL:** a culture containing `ar`, `fa`, `ur` or `he` sets `dir="rtl"` on `<html>` and `<body>` and loads `rtl.scss`.
- **On culture change:** set `<html lang>` and fire `LANGUAGE_CHANGE`. This clears the react-query cache and updates SDK languages.
- **Re-evaluation:** the culture is recomputed on `localusersignedin`/`localusersignedout` and on user-setting changes to `language` or `datetimelocale`.
- **Dates:**
  - `dateTimeLocale` is `datetimelocale` if set, else the UI culture.
  - `utils/dateFnsLocale.ts` maps it to a date-fns locale (`LOCALE_MAP`, base-language fallback, default `en-US`).
  - `scripts/datetime.js` uses `toLocale*String(getCurrentDateTimeLocale(), options)`.

## 7. Themes

- **config.json `themes`:** `[{name, id, color, default?}]`. Built-in ids: `appletv`, `blueradiance`, `catppuccin-{latte,frappe,macchiato,mocha}`, `dark` (default), `light`, `purplehaze`, `wmc`.
- **CSS files:** webpack compiles each `src/themes/<id>/theme.scss` to `themes/<id>/theme.css`.
- **`ThemeCss.tsx`** renders `<link rel="stylesheet" href="themes/${id}/theme.css">`, swapping the `href` when the id changes.
  - `id`: the user theme (`appTheme`) for legacy and modern layouts; `dashboardTheme` for `<ThemeCss dashboard/>` in the dashboard layout.
  - Fallback for both: config default, then `'dark'` (`useUserTheme.ts`).
- **`scripts/themeManager.js` `setTheme(id)`:**
  - Resolves the id against config (or the default).
  - Sets `document.documentElement.setAttribute('data-theme', id)`.
  - Sets `#themeColor` meta content to `theme.color`.
  - Fires `THEME_CHANGE`.
- **MUI** (`src/themes/index.ts`): `createTheme` with `cssVariables:{cssVarPrefix:'jf', colorSchemeSelector:'[data-theme="%s"]'}` and one colorScheme per id. This produces CSS variables like `--jf-palette-background-paperChannel`. `ThemeStorageManager` only subscribes to `THEME_CHANGE`.
- **Custom CSS** (`CustomCss.tsx`):
  - `<style>` with the branding `CustomCss`, unless `disableCustomCss` is set.
  - Then `<style>` with the user's `customCss`.
  - Both are used by the legacy and modern layouts (not the dashboard layout).
- **Body classes:**
  - The dashboard layout adds `dashboardDocument` to `<body>`.
  - Legacy `libraryMenu` toggles `libraryDocument` and `hideMainDrawer`.

## 8. App shell

**Always-present DOM** (`src/components/AppHeader.tsx`, hidden with `display:none` in modern and dashboard layouts):
```html
<div class="mainDrawer hide"><div class="mainDrawer-scrollContainer scrollContainer focuscontainer-y"></div></div>
<div class="skinHeader focuscontainer-x"></div>
<div class="mainDrawerHandle"></div>
```
`AppBody.tsx` renders `<div class="mainAnimatedPages skinBody"></div><div class="skinBody">{children}</div>`. Legacy views mount into `mainAnimatedPages`.

**Header** (`src/scripts/libraryMenu.js` `renderHeader()`; adds `skinHeader-withBackground skinHeader-blurred` to `.skinHeader`):
```
div.flex.align-items-center.flex-grow.headerTop
  div.headerLeft
    button.headerButton.headerButtonLeft.headerBackButton.hide   (arrow_back | chevron_left on Safari)
    button.headerButton.headerHomeButton.hide.barsMenuButton.headerButtonLeft (home)
    button.headerButton.mainDrawerButton.barsMenuButton.headerButtonLeft.hide (menu)
    h3.pageTitle
  div.headerRight
    button.headerSyncButton.syncButton.headerButton.headerButtonRight.hide (groups)
    span.headerSelectedPlayer
    button.headerAudioPlayerButton.audioPlayerButton... .hide (music_note)
    button.headerCastButton.castButton... .hide (cast / cast_connected + castButton-active)
    button.headerButton.headerButtonRight.headerSearchButton.hide (search)
    button.headerButton.headerButtonRight.headerUserButton.hide (person, or .headerUserButtonRound with background-image)
    div.currentTimeText.hide   (TV only: clock updated every 1 s)
div.headerTabs.sectionTabs.hide
```
All header buttons use `is="paper-icon-button-light"`.

Header button visibility when a user is signed in:
- Home and Search: shown.
- Cast: shown when not in TV layout.
- SyncPlay: shown when the SyncPlay plugin is loaded and `Policy.SyncPlayAccess !== 'None'`.
- User button: shown, with the image or name as title. Clicking it goes to `mypreferencesmenu`.

Click targets:
- Cast → `playerSelectionMenu.show(btn)`.
- SyncPlay → `groupSelectionMenu.show`.
- Search → `inputManager.handleCommand('search')`.
- Back → `appRouter.back()`.
- Audio button (TV, while audio plays) → `queue`.

Other header behaviour:
- `setTransparentMenu(bool)` toggles `.semiTransparent`.
- Mobile layout uses Headroom.
- Dashboard pages that are not mobile get `.headroomDisabled`.

**Page lifecycle events** (dispatched by `Page.tsx` and viewManager): `viewbeforeshow`, `pagebeforeshow`, `viewshow`, `pageshow`, `viewhide`.

What `libraryMenu` does on `pageshow`:
- Page type flags: `type-interior` means a dashboard page; `libraryPage`, `homePage`, `standalonePage` are also checked.
- Title: `data-title` → `setTitle`. A `standalonePage` without one → `setDefaultTitle()` (`.pageTitleWithLogo.pageTitleWithDefaultLogo`; `document.title` = server name from `/System/Info/Public`, else `'Jellyfin'`).
- Back button: shown if `data-backbutton != 'false'` and `canGoBack()`. `canGoBack` is false on `/home`, `/login` and `/selectserver` unless a `.dialogContainer` is open.
- `navMenuOption-selected` goes on the `lnkMediaFolder` link matching `topParentId`.
- `setTitle('-')` sets an empty title.

**Legacy drawer** (`.mainDrawer`, `src/lib/navdrawer/navdrawer.js`, width `clamp(availWidth-50, 240, 320)`). Contents of `.mainDrawer-scrollContainer`:
- `a.navMenuOption.lnkMediaFolder[href="#/home"]` Home.
- `.customMenuOptions`: config `menuLinks` (`target=_blank`, icon defaults to `link`).
- `.libraryMenuOptions`:
  - Header `h3.sidebarHeader` `HeaderMedia`.
  - One link per item from `GET /UserViews?userId=`, href from `appRouter.getRouteUrl(view,{context:CollectionType})`.
  - A `livetv` view adds an extra `Guide` link → `#/livetv?tab=1`.
- `.adminMenuOptions` (only if `Policy.IsAdministrator`): `HeaderAdmin`; `#/dashboard` (`TabDashboard`); `#/metadata` (`ManageMetadata`).
- `.userMenuOptions`: `HeaderUser`; `.btnSelectServer` (MultiServer only); `.btnSettings` → `mypreferencesmenu`; `.btnLogout` (`ButtonSignOut`); `.exitApp` (ExitMenu only).

**Tabs** (`src/components/maintabsmanager.js`):
- `LibraryMenu.setTabs(type, idx, builder)` fills `.headerTabs` with `div[is=emby-tabs].tabs-viewmenubar > .emby-tabs-slider > button|a.emby-tab-button[data-index]`.
- Adds `withSectionTabs` to `<body>` and fires `SET_TABS`.
- Pages without the `withTabs` class clear the tabs on `pagebeforeshow`.

**Modern layout** (`src/apps/modern/AppLayout.tsx`):
- MUI `OffsetAppBar` > `AppToolbar`; `LibraryToolbar` on library paths.
- Toolbar left: drawer button (`MenuOpen`/`MenuClose`), back button (NativeShell only), `ServerButton` (logo plus `ServerName`, links to `/`), `UserViewNav`.
- Toolbar right (hidden on `PUBLIC_PATHS`): `SyncPlayButton`, `RemotePlayButton`, `SearchButton`, `UserMenuButton`.
- No toolbar on `/video`.
- **User menu** (`src/components/toolbar/AppUserMenu.tsx`), in order:
  - Profile `/userprofile?userId=`.
  - Settings `/mypreferencesmenu`.
  - DownloadManager and ClientSettings (NativeShell features).
  - Admin only: `ManageServer` `/dashboard`, `ManageMetadata` `/metadata`.
  - QuickConnect `/quickconnect`, if enabled.
  - SelectServer (MultiServer).
  - `ButtonSignOut`.
  - `ButtonExitApp` (ExitMenu).
- **Drawer** (`drawers/MainDrawerContent.tsx`): only on user routes, below the `md` breakpoint.
  - Home `/home`, Favorites `/home?tab=1`.
  - menuLinks.
  - `HeaderLibraries` user views.

**Dashboard layout** (`src/apps/dashboard/AppLayout.tsx` + `components/drawer/sections/*`):
- Drawer sections:
  - `TabServer`: `/dashboard`, `/dashboard/settings`, `/dashboard/branding`, `/dashboard/users`, `/dashboard/libraries{,/display,/metadata,/nfo}`, `/dashboard/playback/{transcoding,resume,streaming,trickplay}`.
  - `HeaderDevices`: `/dashboard/devices`, `/dashboard/activity`.
  - `LiveTV`: `/dashboard/livetv`, `/dashboard/livetv/recordings`.
  - `TabPlugins`: `/dashboard/plugins` plus `/configurationpage?name=` per plugin page.
  - `TabAdvanced`: `/dashboard/{networking,keys,backups,logs,tasks}`.
- The toolbar adds `HelpButton` and `AppTabs`.

**`appFooter.js`:** appends `div.appfooter` to `<body>`. API: `add(elem)`, `insert(elem|html)`. The now-playing bar lives here.

## 9. Real-time

- No hand-written websocket code is in use. ApiClients get `subscribe = apiClient._sdk.subscribe` (`ServerConnections.js`, on `apiclientcreated`). The connection itself, keep-alives and reconnects come from `@jellyfin/sdk/lib/websocket` **[INFERRED, SDK internal]**. The legacy `openWebSocket`/`ensureWebSocket` methods are only declared in `apiclient.d.ts`.
- **`src/scripts/serverNotifications.js`** subscribes on every ApiClient:
  - `Play`: `PlayCommand` of `PlayNext` → `queueNext`, `PlayLast` → `queue`, otherwise `play({ids, startPositionTicks, mediaSourceId, audioStreamIndex, subtitleStreamIndex, startIndex, serverId})`.
  - `Playstate`: `Stop`, `Pause`, `Unpause`, `PlayPause`, `Seek`, `NextTrack`, `PreviousTrack`, `Rewind`, `FastForward`.
  - `GeneralCommand`: navigation, volume and channel commands go to `inputManager`. `SetVolume`, `SetAudioStreamIndex`, `SetSubtitleStreamIndex`, `SetRepeatMode`, `SetShuffleQueue`, `PlayTrailers`, `DisplayContent` (`showItem` unless playing video or a book locally), `DisplayMessage` (toast if `TimeoutMs`, else alert), and `SendString` are handled directly.
  - `SyncPlayCommand`, `SyncPlayGroupUpdate`: forwarded to the SyncPlay plugin.
  - Note: the returned cleanup calls `subscriptions.get` on an array, which is a bug, but the cleanup is never used.
- **Other subscribers:**
  - `notifications.js` (desktop notifications): `LibraryChanged`, `PackageInstall*`, `ServerShuttingDown`, `ServerRestarting`, `RestartRequired`.
  - `QueryClientEventHandler`: `UserDataChanged` invalidates `['User',userId,'Items']`.
  - Item containers: `UserDataChanged`, `Timer*`, `SeriesTimer*`, `LibraryChanged`.
  - `RefreshProgress` (refresh indicators). The Blazor item and wizard library views share the
    event through `RefreshProgressStore`, keyed by library/item id, so scan progress updates cards
    without polling.
  - `ScheduledTasksInfo` (taskbutton, dashboard).
  - `Sessions` (dashboard, sessionPlayer with a 100/800 ms periodic interval).
  - Dashboard and playbackManager react to `ServerRestarting`/`ServerShuttingDown`.

## 10. Cross-cutting UI utilities

- **`components/loading/loading.ts`:** `show()`/`hide()`. A single `div.docspinner.mdl-spinner[dir=ltr]` (4 `mdl-spinner__layer-N` blocks) is appended to `<body>`; `.mdlSpinnerActive` toggles it. `LoadingComponent.tsx` is the React version. Exposed as `window.Loading`.
- **`components/toast/toast.ts`:** `toast(text | {text})`. Uses `div.toastContainer` > `div.toast`. Adds `toastVisible` after 300 ms, `toastHide` after 3300 ms more, then removes the element after 300 ms. Only `text` is shown; `title` is ignored.
- **`components/dialog/dialog.js`** (generic button dialog, used by alert and confirm):
  - Template: `.formDialogHeader > h1.formDialogHeaderTitle`; `.formDialogContent > .dialogContentInner.dialog-content-centered > .text` (DOMPurify HTML); `.formDialogFooter.formDialogFooter-flex`.
  - Buttons: `button.btnOption.raised.formDialogFooterItem.button-{type}[data-id]`.
  - Resolves with the clicked id; rejects on dismiss.
  - TV uses `size:'fullscreen'`; otherwise `.dialog-fullscreen-lowres`.
- **`components/alert.js`:** `alert(text, title)` or `alert({title, text, html, dialogOptions})`. Single button `ButtonGotIt` (id `ok`). Waits for `appRouter.ready()`.
- **`components/confirm/confirm.ts`:** buttons `cancel` (`ButtonCancel`) and `ok` (`ButtonOk`; type `delete` if `primary:'delete'`). Resolves on ok, rejects otherwise.
- **`components/prompt/prompt.js` + `prompt.template.html`:** `formDialog` with `#txtInput` (`label`, `value`), `.fieldDescription`, `.btnSubmit`, `.btnCancel`. Resolves with the value.
- **`components/actionSheet/actionSheet.ts`:** `show({items:[{id|value,name,icon,selected,secondaryText,asideText,divider}], title, text, positionTo, showCancel, resolveOnClick, dialogClass, ...})`.
  - Classes: `.actionSheet` plus `.actionsheet-fullscreen` (TV) or `.actionsheet-not-fullscreen`; `.actionSheetContent`, `.actionSheetTitle`, `.actionSheetText`, `.actionsheetMenuItemIcon`, `.actionSheetItemText`, `.actionsheetDivider`, `.btnCloseActionSheet`.
  - Positioned next to `positionTo` when not fullscreen.
  - Resolves with the selected id; rejects with "ActionSheet closed without resolving".
- **`components/dialogHelper/dialogHelper.js` + `dialoghelper.scss`:**
  - `createDialog({size, removeOnClose, scrollY, scrollX, modal, autoFocus, enableHistory, lockScroll, entryAnimation='scaleup', exitAnimation='scaledown', id})` returns `div.dialog.focuscontainer.hide`.
    - Optional classes: `.smoothScrollY`, `.dialog-fixedSize.dialog-{size}`.
    - Attributes: `data-history`, `data-lockscroll`, `data-autofocus`, `data-removeonclose`.
  - `open(dlg)`:
    - Wraps the dialog in `div.dialogContainer` on `<body>` and adds `div.dialogBackdrop.dialogBackdropOpened`.
    - Adds `.centeredDialog` and `.opened`; `body.noScroll` if the scroll lock applies.
    - Pushes a history entry with `state.dialogs:[...,'dlg<ts>']`, so the browser Back button closes it.
    - Resolves on close.
  - `close(dlg)`: plays the exit animation, then fires `close`.
  - Clicking the backdrop or container closes the dialog.
- **Global helpers** in `utils/dashboard.js` (`window.Dashboard`):
  - `navigate(url)` → `appRouter.show`.
  - `alert(string)` gives a toast; `alert({title,message,callback})` gives a dialog.
  - `confirm`, `showLoadingMsg`/`hideLoadingMsg`, `processErrorResponse` (alert built from the `X-Application-Error-Code` header), `selectServer`, `logout`.

**Not found:**
- No `src/components/navdrawer` (the drawer lives in `src/lib/navdrawer`).
- No `src/apps/legacy/components`.
- No literal `jellyfin_credentials` or Authorization header string in the repo.
