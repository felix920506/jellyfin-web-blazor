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

## Deviations and remaining verification

- The pages are implemented with MudBlazor, while the reused legacy library editors retain the
  original markup and styles.
- Ported save failures are logged and do not show a success alert; some original pages likewise do
  not surface an error. Server-side validation behavior has not been comprehensively checked in a
  browser.
- The inherited WIP has logic/unit tests for named configurations, published URI conversion,
  transcoding choices and library/image-option semantics. `blazor/e2e/dashboard_libraries.py`
  visits each route at desktop and phone sizes and saves screenshots, but it does not yet change,
  save, API-verify or restore settings. Per-page component tests and save/restore browser coverage
  remain required before considering this workstream fully verified.
- A private Jellyfin instance is required for browser tests that save server/network/library
  settings. Do not run those against the shared test server. Follow the private-instance procedure
  in `docs/blazor-port/ARCHITECTURE.md` and restore/reset the instance immediately afterward.
