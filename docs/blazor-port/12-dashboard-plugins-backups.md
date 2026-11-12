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
| `/configurationpage` | Resolves the selected server configuration page and keeps plugin drawer links working. |

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

## Configuration-page limitation

The original `/configurationpage` route fetches HTML and gives it to the legacy `viewManager`,
which inserts the HTML and executes plugin JavaScript against globals such as `ApiClient`,
`Dashboard`, `viewManager`, AMD `define`, and the legacy DOM helpers. The Blazor port does **not**
execute server-provided JavaScript, does not insert arbitrary server HTML, and does not emulate
those globals. It does fetch the selected page resource to validate that it exists, then displays a
clear warning: the plugin settings page is unavailable in this client and must be opened in the
original Jellyfin web client. This is intentional: executing untrusted plugin scripts in the
Blazor component tree would bypass the port's component lifecycle and sanitization boundary.

The plugin page itself, repository list and drawer configuration-page links remain usable. A
future compatible plugin can expose native Blazor settings through a dedicated port component;
the current server HTML/legacy-JavaScript contract cannot be rendered natively without a separate
trusted legacy-host integration.

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
