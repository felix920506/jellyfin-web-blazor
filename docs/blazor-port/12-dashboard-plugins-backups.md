# Dashboard plugins, repositories and backups

This workstream ports the administrator pages from `src/apps/dashboard/routes/plugins` and
`src/apps/dashboard/routes/backups`.

## Routes

| Route | Port |
| --- | --- |
| `/dashboard/plugins` | Installed and catalog packages are merged by plugin GUID. Search, installed/available/all status filters, all repository categories, the `Other` category, card images, status chips and empty/error states are included. |
| `/dashboard/plugins/catalog` | Replaces the legacy alias with `/dashboard/plugins`. |
| `/dashboard/plugins/{pluginId}` | Details, installed/catalog version selection, changelog panels, repository/developer/status information, official/unofficial install confirmation, update/install, enable/disable, settings link, uninstall confirmation and restart-required notices. |
| `/dashboard/plugins/repositories` | Lists repositories, validates the name and URL, adds/removes repositories through the complete repository list, and confirms removal. |
| `/dashboard/backups` | Lists backups newest-first, creates backups with database/metadata/subtitles/trickplay semantics, shows backup details and copy-path action, warns about running tasks, confirms restore, waits for the restarted server, and reports success/failure. |
| `/configurationpage` | Authenticated server HTML, inline scripts, AMD/ESM `data-controller` resources, legacy APIs and editable persisted plugin forms in a disposable compatibility document. |

All routes use `DashboardLayout` and require `AccessLevel.Admin`.

## Server contracts

The pages use the generated SDK rather than hand-built request payloads:

- `GET /Plugins`, `GET /Packages`, `GET /web/ConfigurationPages` for the merged plugin model.
- `POST /Packages/Installed/{name}` with `assemblyGuid`, `version`, and `repositoryUrl` for install/update.
- `POST /Plugins/{id}/{version}/Enable` and `/Disable`; `DELETE /Plugins/{id}/{version}`.
- `GET /Repositories` and `POST /Repositories` with the complete `RepositoryInfo[]` list.
- `GET /Backup`, `POST /Backup/Create` with `BackupOptionsDto`, and `POST /Backup/Restore` with
  `BackupRestoreRequestDto.ArchiveFileName`.

`Services/Dashboard/DashboardPlugins.cs` owns the duplicate installed-plugin selection used by the
original client: disabled entries win, then entries requiring restart, then the first entry. It
also chooses a main-menu configuration page when a plugin has several pages.

## Plugin configuration compatibility host

`DashboardPluginConfiguration` resolves `GET /web/ConfigurationPages` and fetches
`GET /web/ConfigurationPage?name=...` with the current user's Authorization header. The
`[AppService]`-registered `IPluginConfigurationInterop` imports `js/plugincompat.js`, which inserts
the HTML into a same-origin iframe below the Blazor page. The iframe is **lifecycle isolation, not
a security sandbox**: administrator-installed plugin scripts are trusted with the administrator's
token, as they are in the original client. They never own nodes in Blazor's render tree.

The compatibility document inherits the app's stylesheet/theme, translates `${Key}` placeholders,
executes inline/external scripts, loads `__plugin/` resources through the authenticated server
endpoint and constructs AMD or ESM `data-controller` factories. It provides isolated jQuery 3.7.1
(including legacy `checked`/`selectmenu` helpers), `require`/`define`, `ApiClient`, `Dashboard`,
`Loading`, `globalize` and the common loading/router/events/DOM modules. API helpers cover plugin,
named and server configuration, raw `ajax`/`fetch`/`getJSON`, users, items, virtual folders, tasks,
localization and system information. HTTP errors reject with the Response object. Authentication
is not forwarded to third-party origins.

Lifecycle events bubble from the plugin `.page`: `viewinit`/`pageinit`, `viewbeforeshow`/
`pagebeforeshow`, `viewshow`/`pageshow`, and the corresponding hide/destroy events on disposal.
Query-only navigation replaces the old document. Disposal aborts outstanding shim requests,
disconnects observers, calls controller `destroy` when present and removes the entire frame,
including its globals, listeners, timers and inserted nodes. Script exceptions and unhandled
rejections show a plugin-local error message and are logged without raising Blazor's error banner.

Native input/select/textarea/button/checkbox/link semantics are enhanced with original classes,
accessible generated labels, and `_plugin-configuration.scss`. Dynamically inserted controls
are enhanced too. Plugin list cards lead to details, the settings link leads here, and the existing
`EnableInMainMenu` drawer links lead directly here.

Drawer entries are keyed by configuration-page name, not plugin ID: a single plugin can expose
several main-menu pages without a Blazor duplicate-key exception.

### Compatibility boundary

This is a focused compatibility layer, not the entire original client runtime. Unsupported AMD
modules report a visible load error; native controls replace custom-element internals and
`selectmenu` is a chainable no-op because selects are native. Legacy TV/device polyfills,
directory browser/item-identifier/dialog-helper modules and arbitrary npm module resolution are
not included. Plugin ESM controllers may use absolute URLs or `__plugin/` imports; relative ESM
import graphs and original webpack aliases are not resolved. Dialogs are native HTML dialogs
and saved/error notifications are local to the plugin document, rather than global legacy toasts.
`PORT-TODO(plugin-config)`: expand this explicit module/API subset as plugins require it; do not
silently pretend unsupported modules succeeded.

## Deviations and gaps

- Catalog package metadata is unavailable when the server cannot reach its repository. In that
  case installed/bundled plugin details still render; the page shows the original repository-load
  warning where appropriate. bUnit scripts the catalog responses so this behavior is covered
  without depending on internet access.
- Changelog Markdown uses the existing details Markdown interop. If that module cannot be loaded,
  the raw changelog text is shown; the empty revision fallback is `LabelNoChangelog`.
- The backup restore probe follows the original 45-second interval. The component tests shorten
  the interval; the browser test opens and cancels the restore confirmation and never restarts a
  server.
- MudBlazor supplies the port's cards, chips, tables, expansion panels, switches and dialogs. The
  original's MUI-only density/full-screen controls are not applicable to these pages.
- The original image fallback is an MUI extension icon; the port uses MudBlazor's equivalent
  material icon. New classes are isolated in `_dashboard-plugins.scss` and prefixed
  `jf-dashboard-`.

## Tests and browser coverage

`tests/Jellyfin.Web.Tests/DashboardPlugins/` covers merge/filter logic and loading, success,
confirmation/cancellation, validation, request payloads and failure paths for each page. The
private-instance Playwright script is `blazor/e2e/dashboard_plugins.py`; it is intentionally
guarded to require `JELLYFIN_URL` on port `18102`. It adds/removes one real repository, creates a
real backup, exercises plugin filters/details/settings and opens/cancels restore, then repeats
visual checks at phone width.

`blazor/e2e/plugin_config.py` requires the separate `pluginconfig` instance on port `18107`.
It discovers the bundled **MusicBrainz** configuration page through the real server API (no
installed package or fixture needed), checks stored input/checkbox values, edits rate limit and
replace-artist-name using normal form interaction, saves, reloads and verifies persistence both
in the UI and through `GET /Plugins/{id}/Configuration`. Original values are restored in `finally`.
It also opens the bundled TMDb form, checks phone layout, query navigation and teardown. Separate,
explicitly intercepted test resources cover AMD/ESM controllers, drawer routing, lifecycle events
and contained script errors; these are not substitutes for the real persistence test. No browser
console/HTTP errors are filtered. Screenshots are written under `blazor/artifacts/e2e/`.

Validation in this worktree: `./dev check` passed with **2,154 tests**, zero warnings/errors.
Both private-instance scripts passed (`plugin configuration: ok`, `dashboard plugins/backups: ok`).
Desktop and phone screenshots were inspected for MusicBrainz, TMDb and AudioDB; controls, saved
values and responsive wrapping render correctly. Error/controller screenshots were inspected too.
