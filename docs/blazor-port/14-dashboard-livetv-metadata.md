# Dashboard Live TV administration and metadata manager

This workstream ports the administrator-facing Live TV pages and the legacy metadata manager.
It retains the original routes, translation keys, JSON payload shapes and legacy element markup
where those pages were not modern dashboard pages.

## Routes

| Route | Port | Original |
| --- | --- | --- |
| `/dashboard/livetv` | tuner and guide-provider overview, provider deletion/channel mapping, guide refresh task progress | `apps/dashboard/routes/livetv/index.tsx` |
| `/dashboard/livetv/tuner` | tuner type selection, detection, type-specific fields, file chooser and save | `apps/dashboard/controllers/livetvtuner.*` |
| `/dashboard/livetv/guide` | XMLTV and Schedules Direct forms | `apps/dashboard/controllers/livetvguideprovider.*`, `components/tvproviders/**` |
| `/dashboard/livetv/recordings` | DVR settings, directory choices and padding conversion | `apps/dashboard/routes/livetv/recordings.tsx` |
| `/metadata?id=<item-id>` | lazy library/Live TV tree plus embedded metadata editor | `legacy/controllers/edititemmetadata.*`, `scripts/editorsidebar.js` |

All routes require `AccessLevel.Admin`; Live TV dashboard routes use `DashboardLayout`. Metadata
also uses that layout but hides its dashboard drawer, just as the legacy manager supplies its own
library sidebar.

## Components and services

`LiveTvAdminApi` owns the raw JSON endpoints. `LiveTvAdmin` and `LiveTvTunerForm` keep the small
legacy behaviour rules testable (tuner names/routes, M3U variants, numeric parsing and DVR
padding). `DashboardLiveTvTunerCard`, `DashboardLiveTvProvider`,
`DashboardLiveTvChannelMapper` and `DashboardLiveTvTunerPicker` are the dashboard affordances.
`XmlTvProvider`, `SchedulesDirectProvider` and `TvProviderTuners` preserve the legacy forms and
their provider/tuner payloads.

`MetadataTree` renders the jstree-equivalent markup directly rather than loading jQuery/jstree.
It lazily fetches folders, expands selected ancestors, exposes Live TV only when channels exist,
and supports saved/deleted item updates from the embedded `MetadataEditor`. The editor’s added
`Embedded`, `OnSaved` and `OnDeleted` API is additive; its existing dialog `ShowAsync` API is
unchanged.

## Tests and browser coverage

- bUnit: `tests/Jellyfin.Web.Tests/DashboardLiveTv/DashboardLiveTvTests.razor` and
  `tests/Jellyfin.Web.Tests/Metadata/MetadataTests.razor` cover payload and parsing rules, DVR
  saving, reset endpoint construction, tree item labels and manager startup.
- Browser: `e2e/dashboard_livetv.py` is intended for the private `livetvadmin` server. Create
  `/media/livetv-e2e.m3u` and `/media/livetv-e2e.xml` in that instance before running it, then
  reset the instance immediately afterwards. `e2e/metadata.py` is safe against the shared server
  and does not save metadata.

Schedules Direct needs a real external account/service, so its sign-in and lineup responses are
covered with bUnit-compatible mocked API responses rather than a browser test against the seed
server. The test server has no hardware tuner; the Live TV browser test uses M3U/XMLTV fixtures.

## Deliberate deviations and remaining gaps

- The original dynamically imports jstree. The port uses a small accessible Razor tree with the
  same lazy-node and selection semantics, avoiding that legacy JavaScript dependency. Its visual
  styling is supplied by the original metadata styles plus `_metadata.scss`, rather than the
  vendored jstree theme.
- `AppTabs` is not added: none of these original Live TV route components dispatches the
  dashboard `SET_TABS` event, so an empty tab host would add no behaviour.
- The tuner reset request is ported as `POST /LiveTv/Tuners/{id}/Reset`; the original client
  exposes this API but has no dashboard control for it. The port exposes it from the tuner card
  menu for complete administration.
