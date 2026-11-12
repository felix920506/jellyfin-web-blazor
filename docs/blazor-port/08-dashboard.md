# The admin dashboard in the Blazor port

Reference for the dashboard workstream: what part 1 ported, how the dashboard is put together,
where it deviates from the original, and how a later workstream adds the remaining pages.
The original is `src/apps/dashboard/**` (React + MUI + material-react-table); the port lives in
`blazor/src/Jellyfin.Web`.

## 1. What is ported

| Route | Original | Port |
| --- | --- | --- |
| (shell) | `apps/dashboard/AppLayout.tsx`, `components/drawer/**`, `components/toolbar/AppToolbar.tsx` | `Layout/DashboardLayout.razor`, `Components/Dashboard/DashboardToolbar.razor`, `DashboardDrawerContent.razor`, `Services/Dashboard/DashboardNav.cs` |
| `/dashboard` | `routes/index.tsx`, `components/widgets/*`, `features/devices/components/DeviceCard.tsx`, `features/storage`, `features/activity` | `Pages/Dashboard/DashboardIndex.razor`, `DashboardWidget`, `DashboardDeviceCard`, `DashboardStorageItem`, `DashboardActivityItem`, `DashboardTaskProgress`, `DashboardSessions.cs`, `StorageText.cs`, `DeviceIcons.cs` |
| `/dashboard/settings` | `routes/settings/index.tsx` | `DashboardSettings.razor` |
| `/dashboard/branding` | `routes/branding/index.tsx` | `DashboardBranding.razor` |
| `/dashboard/users` | `routes/users/index.tsx`, `components/dashboard/users/UserCardBox.tsx` | `DashboardUsers.razor`, `DashboardUserCard` |
| `/dashboard/users/add` | `routes/users/add.tsx` | `DashboardUserAdd.razor`, `DashboardAccessContainer`, `DashboardCheckBox` |
| `/dashboard/users/{userId}/{tab}` and `/dashboard/users/profile\|access\|parentalcontrol\|password?userId=` | `routes/users/edit.tsx`, `features/users/components/*`, `components/accessSchedule` | `DashboardUserEdit.razor`, `DashboardUserProfileTab`, `DashboardUserAccessTab`, `DashboardUserParentalTab`, `DashboardAccessScheduleDialog`, `ParentalRatings.cs`, `DashboardUserCache.cs`; the password tab is `Pages/User/UserPasswordForm` |
| `/dashboard/activity` | `routes/activity/index.tsx` | `DashboardActivity.razor` |
| `/dashboard/devices` | `routes/devices/index.tsx` | `DashboardDevices.razor` |
| `/dashboard/keys` | `routes/keys/index.tsx` | `DashboardApiKeys.razor` |
| `/dashboard/logs` | `routes/logs/index.tsx` | `DashboardLogs.razor` |
| `/dashboard/logs/{file}` | `routes/logs/file.tsx` | `DashboardLogFile.razor`, `wwwroot/js/dashboard.js` (copy, download) |
| `/dashboard/tasks` | `routes/tasks/index.tsx`, `features/tasks/**` | `DashboardTasks.razor`, `DashboardTaskItem`, `TaskTriggerText.cs`, `LiveFeed.cs` |
| `/dashboard/tasks/{id}` and `/dashboard/tasks/edit?id=` | `routes/tasks/task.tsx`, `NewTriggerForm.tsx` | `DashboardTaskEdit.razor`, `DashboardTriggerDialog` |

The user-facing shell already linked to `#/dashboard` (user menu of the app bar, and the admin
section of `/mypreferencesmenu`, which the modern layout hides as the original does); nothing had
to change there.

Not ported yet (part 2): libraries, library display/metadata/NFO settings, playback
(transcoding, resume, streaming, trickplay), networking, plugins and repositories, Live TV
settings, backups. Their drawer links exist (they are the original's) and lead to the app's
"not found" page until the pages are added; no page stubs were created.

## 2. How the dashboard is put together

**Layout.** A page opts in with `@layout DashboardLayout`. The layout renders the app bar
(`DashboardToolbar`: drawer button on narrow screens, back button, page title, help link of the
page, user menu), the drawer (a fixed `aside.jf-dashboard-drawer` from `Viewport.Md` up, a
temporary `MudDrawer` below) and the page. While it is shown it

- sets the `dashboardDocument` class on `<body>` (the original style sheets use it), and
- applies the user's *dashboard* theme (`ThemeManager.ApplyUserTheme(dashboard: true)`), following
  changes of that setting; on dispose it removes the class and applies the user theme again.

**Navigation.** `Services/Dashboard/DashboardNav.cs` is the single list of drawer sections and
links (`DashboardNav.Sections`), with the rules for highlighting (`IsSelected`, `AlsoSelectedOn`),
the two collapsible groups (Libraries, Playback), and the help link of each page (`HelpUrl`).
The plugins section also lists the plugins' own menu pages
(`GET /web/ConfigurationPages?enableInMainMenu=true`).

**Page title.** Pages are the port's `<Page Title=...>`; the title goes to the browser tab as in
the original and is also shown in the app bar (`ShellState.Title`).

**Look.** Two families of pages exist in the original and the port keeps both:

- MUI pages (index, settings, branding, activity, devices, keys, logs, tasks): MudBlazor
  components (`MudTable`, `MudTextField Variant="Variant.Filled"`, `MudButton`, `MudAlert`, ...).
- Legacy-markup pages (users, add user, the tabs of the user editor): the original markup and
  classes with the port's `EmbyInput`/`EmbySelect`/`EmbyCheckbox`, so the original style sheets
  apply. Their dialogs are the legacy ones (`AppDialogs.ConfirmAsync`, `PromptAsync`,
  `ActionSheetAsync`).

All added classes are prefixed `jf-dashboard-` and live in `Styles/port/_dashboard.scss`.

**Dialogs.** `Services/Dashboard/DashboardDialogs.cs` adds `AppDialogs.MuiConfirmAsync`,
`MuiAlertAsync` and `MuiInputAsync` (ports of `ConfirmDialog.tsx`, `SimpleAlert.tsx`,
`InputDialog.tsx`, rendered by `DashboardMessageDialog`). A dialog with its own form passes
`DashboardDialogs.Options` to `AppDialogs.ShowAsync<T>` (see `DashboardTriggerDialog`).

**Live data.** `LiveFeed` subscribes to a periodic web socket feed (`SessionsStart`,
`ScheduledTasksInfoStart`, `ActivityLogEntryStart`) for as long as it is not disposed and repeats
the subscription when the socket reconnects. The index page and the task list use it;
`DashboardSessions` merges session updates as the original's `useLiveSessions` does.

**Texts.** `DashboardFormat` (dates through `Globalize.FormatDate`, device and app names),
`RelativeTime` ("5 minutes ago", through `Intl.RelativeTimeFormat` once `dashboard.js` is
loaded), `TaskTriggerText` (trigger descriptions, time and interval options), `StorageText`
(sizes), `ParentalRatings` (grouping of ratings with the same score).

## 3. Deviations from the original

- **Tables.** The original uses material-react-table. The port uses `MudTable` with the same
  columns, sorting, paging and (activity) server-side filtering, but without column
  hiding/pinning/resizing, density and full-screen toggles, multi-column sorting and the date
  range filter of the activity log.
- **Devices.** Renaming a device opens an input dialog; the original edits the row in place.
- **Relative times.** date-fns' "about"/"less than" qualifiers are not produced ("1 hour ago",
  not "about 1 hour ago"; a task that ran for a few seconds reads "1 minute" where the original
  says "less than a minute").
- **App bar.** The page title is shown in the app bar; the original shows it in the browser tab
  only. The original's `AppTabs` (tab bars in the app bar for some part 2 pages) is not ported;
  the user editor shows its tabs in the page as the original does.
- **Failed saves.** On the access and parental control tabs a failed save shows the
  `ErrorDefault` toast (the original leaves the loading indicator on). On the settings, branding
  and logs pages a failed save is logged and the "saved" notice is not shown (the original shows
  nothing either).
- **Drawer.** The server name in the drawer header is not refreshed after it is changed on the
  settings page (until the dashboard is opened again).
- **Missing translations.** `MessageNoDevices`, `Everyday`, `Weekday`, `Weekend` and `Music` are
  not in the original's string files either; like the original, the key is shown.

## 4. Tests

- bUnit and unit tests: `blazor/tests/Jellyfin.Web.Tests/Dashboard/`. `DashboardTestContext`
  is the base class: a fixed clock, a loose `dashboard.js` module, helpers for the dashboard
  dialogs (`ClickForDialogAsync`, `ConfirmDialogAsync`, `CancelDialogAsync`), MudBlazor inputs
  (`Box`, `Select`, `Chosen`, `ChooseAsync`), web socket messages (`Push`) and request bodies
  (`BodiesTo`).
- Browser: `./dev e2e dashboard.py` against the shared test server. It creates and deletes its
  own user, API key and task trigger, puts back what it saves (the server settings it changes,
  the cache and metadata paths, the login disclaimer, the triggers of the task and the name of
  its own device), and only opens and cancels the restart, shutdown and "delete all devices"
  questions. Screenshots of every page at
  desktop and phone width land in `blazor/artifacts/e2e/dashboard-*.png`.

## 5. How to add a dashboard page

1. **Page.** Create `Pages/Dashboard/Dashboard<Name>.razor`:

   ```razor
   @* What the page is. Port of apps/dashboard/routes/<path>.tsx. *@
   @attribute [Route("/dashboard/networking")]
   @attribute [Access(AccessLevel.Admin)]
   @layout DashboardLayout
   @inject JellyfinApi Api
   @inject Globalize Globalize

   <Page Id="networkingPage" Title="@Globalize["TabNetworking"]" Class="mainAnimatedPage type-interior">
       @if (_loading)
       {
           <DashboardLoading />
       }
       else
       {
           <div class="content-primary">
               <form class="jf-dashboard-form" @onsubmit="SaveAsync" @onsubmit:preventDefault="true">
                   <MudText Typo="Typo.h1">@Globalize["TabNetworking"]</MudText>
                   @if (_saved)
                   {
                       <MudAlert Severity="Severity.Success" Class="jf-dashboard-saved">@Globalize["SettingsSaved"]</MudAlert>
                   }
                   ...
                   <MudButton ButtonType="ButtonType.Submit" Variant="Variant.Filled" Color="Color.Primary" Size="Size.Large"
                              Disabled="_saving" Class="jf-dashboard-submit">@Globalize["Save"]</MudButton>
               </form>
           </div>
       }
   </Page>
   ```

   `DashboardSettings.razor` is the model for a settings form (load the configuration, change only
   the fields of the page, post the whole object back), `DashboardApiKeys.razor` for a table with
   row actions, `DashboardTaskEdit.razor` for a page with a dialog of its own. Use the original
   translation keys and the same requests (`./dev api <type> [member]` shows the Kiota signatures).
2. **Drawer.** Nothing to do for a page of the original: its link is already in
   `DashboardNav.Sections`. A sub page that should keep a link highlighted goes into that link's
   `AlsoSelectedOn`; a page with a documentation link goes into `DashboardNav.HelpUrl`.
3. **Shared parts.** Components go to `Components/Dashboard/` (first line
   `@namespace Jellyfin.Web.Components`, name prefixed `Dashboard`), logic to
   `Services/Dashboard/` (`namespace Jellyfin.Web.Services`), styles to
   `Styles/port/_dashboard.scss` (`jf-dashboard-` prefix). Questions and one-line inputs:
   `Dialogs.MuiConfirmAsync/MuiAlertAsync/MuiInputAsync`. Dates and numbers:
   `DashboardFormat`/`Globalize.FormatDate`, never .NET cultures.
4. **Tests.** Add `tests/Jellyfin.Web.Tests/Dashboard/Dashboard<Name>Tests.razor` inheriting
   `DashboardTestContext`: render with `RenderAt<TPage>(url)` from scripted `App.Server`
   responses, assert the requests sent on save, and the error paths. Add the route to `PAGES`
   in `blazor/e2e/dashboard.py` (it is then opened and photographed at phone width as well) and a
   step for what the page changes; put back whatever it changes on the shared test server.
