# Dashboard libraries, playback and networking

This workstream ports the dashboard administration pages listed below to the Blazor application.
All pages require an administrator and use `DashboardLayout`.

## Ported routes and behavior

| Route | Implementation |
| --- | --- |
| `/dashboard/libraries` | `Pages/Dashboard/Libraries/DashboardLibraryList.razor`; library cards, add/manage/rename/remove/image actions, scanning and scheduled-task progress updates. |
| `/dashboard/libraries/display` | `DashboardLibraryDisplay.razor`; display settings persisted to the server configuration. |
| `/dashboard/libraries/metadata` | `DashboardLibraryMetadata.razor`; preferred language/country and chapter image settings. |
| `/dashboard/libraries/nfo` | `DashboardLibraryNfo.razor`; Kodi/NFO metadata settings. |
| `/dashboard/playback/transcoding` | `DashboardPlaybackTranscoding.razor`; acceleration-dependent device/codec/tonemapping controls and the remaining encoding fields. |
| `/dashboard/playback/resume` | `DashboardPlaybackResume.razor`; resume thresholds. |
| `/dashboard/playback/streaming` | `DashboardPlaybackStreaming.razor`; streaming bitrate settings. |
| `/dashboard/playback/trickplay` | `DashboardPlaybackTrickplay.razor`; trickplay generation settings. |
| `/dashboard/networking` | `DashboardNetworking.razor`; server addresses, ports, HTTPS, remote access, IP networking, discovery and published URIs. |

Library UI is in `Components/Dashboard/DashboardLibraryCard.razor` and
`Components/Libraries/{MediaLibraryEditor,ImageOptionsEditor}.razor`. The existing wizard library
creator, directory browser, options editor and item image editor are reused. Configuration handling
and wire-compatible option logic are in `Services/Dashboard/`.

## Extending

Use `DashboardSettings.razor` for ordinary server configuration forms, and the named configuration
helpers in `Services/Dashboard/NamedConfiguration.cs` for `/System/Configuration/{key}` JSON
endpoints. Read the current configuration before saving so fields not owned by a page are retained
where the original does so. Library option edits should go through `DashboardLibraries.MergeOptions`
to preserve path information and fields the editor does not expose. Transcoding codec capability
lists and dependent settings live in `TranscodingLogic.cs`; keep these aligned with the original
`src/apps/dashboard/features/playback/constants/codecs.ts`.

## Field-level fidelity audit

The port was checked against the original route and feature sources, including labels, help text,
conditional rendering, validation, confirmation text and the request sent on save:

| Area | Checked source and behavior | Result |
| --- | --- | --- |
| Libraries list/card | `routes/libraries/index.tsx`, `features/libraries/components/LibraryCard.tsx` | Add, manage, image editing, rename (`name`, `newName`, `refreshLibrary`), scan, destructive remove confirmation and refresh are represented. |
| Library creator/editor | `mediaLibraryCreator`, `mediaLibraryEditor`, `libraryoptionseditor` | Content type/name requirements, folder add/remove, box-set folder visibility, option merge preservation, invalid item-id alert and refresh flags are represented. |
| Image options | `imageOptionsEditor.js` | Supported image types, default-vs-library precedence, primary limit, backdrop limit/width visibility and saved `Type/MinWidth/Limit` entries are represented. |
| Display/metadata/NFO | `routes/libraries/{display,metadata,nfo}.tsx` | Server configuration and named `metadata`/`xbmcmetadata` payloads retain untouched fields; language/country, chapter and NFO fields are covered by component tests. |
| Transcoding | `routes/playback/transcoding.tsx`, `features/playback/constants/codecs.ts` | Acceleration list, complete codec matrix, dependent devices/codecs, NVENC/QSV/VAAPI/AMF/RKMPP conditions, tone mapping, encoder presets, thread/fallback, throttle and segment deletion fields are present and tested. |
| Resume/streaming/trickplay | `routes/playback/{resume,streaming,trickplay}.tsx` | Threshold validation, bitrate conversion, trickplay visibility and resolution validation/save behavior are covered. |
| Networking | `routes/networking/index.tsx` | Ports, HTTPS/certificate, remote access, IPv4/IPv6, discovery, proxy/filter lists, base URL and published URI conversion/payload are represented. The original route has no warning or confirmation dialog; none was invented. |

Exact saved payloads are asserted in `DashboardSettingsPageTests.razor`; codec and option matrices are
asserted in `DashboardLibraryLogicTests.cs`; card actions and scheduled-task progress are asserted in
`DashboardLibraryCardTests.razor`.

## Deviations and remaining verification

- The pages are implemented with MudBlazor, while the reused legacy library editors retain the
  original markup and styles.
- Ported save failures are logged and do not show a success alert; some original pages likewise do
  not surface an error. Server-side validation behavior has not been comprehensively checked in a
  browser.
- The pages are component-tested with scripted responses and payload assertions. The private-server
  browser script additionally performs mutation/API verification/restore; its final run is recorded
  with the workstream checks.
- A private Jellyfin instance is required for browser tests that save server/network/library
  settings. Do not run those against the shared test server. Follow the private-instance procedure
  in `docs/blazor-port/ARCHITECTURE.md` and restore/reset the instance immediately afterward.
