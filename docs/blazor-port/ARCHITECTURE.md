# Blazor WebAssembly port: architecture and working rules

The port lives in `blazor/`. The original client in `src/` stays untouched and is the reference
for behaviour, markup, CSS class names and translation keys. Read this file before changing
anything under `blazor/`.

Behaviour specifications extracted from the original client (read the ones for your area):

| File | Content |
| --- | --- |
| `docs/blazor-port/01-route-inventory.md` | every route, its access level, its original source file |
| `docs/blazor-port/02-connection-auth-shell.md` | connection flow, route guard, session pages, settings, globalize, themes, shell, realtime, UI utilities |
| `docs/blazor-port/03-ui-building-blocks.md` | elements, cards, list views, scrollers, tabs, dialogs, media info |
| `docs/blazor-port/04-playback-architecture.md` | playback manager, players, queue, reporting, OSD, remote control, SyncPlay |

## 1. Scope decisions

- Blazor WebAssembly standalone app on .NET 10. No `wasm-tools` workload: nothing that needs a
  native relink. `InvariantGlobalization=true`: never format dates or numbers with .NET cultures
  and never use `TimeZoneInfo.Local`; use `Globalize.FormatDate/FormatNumber` and
  `IBrowser.UtcOffsetMinutes`.
- Only the modern layout (the experimental/stable desktop+mobile app) is ported. The TV layout is
  out of scope. One active server at a time.
- The original SCSS is compiled unchanged into one stylesheet. Ported pages therefore reproduce
  the original markup and class names (`.page`, `.card`, `.emby-button`, `.verticalSection`, ...)
  and inherit the original look. The app shell and the dashboard use MudBlazor (the original
  uses MUI there).
- Translation keys are the original ones (`src/strings/*.json`). Do not invent keys; if a text
  has no key in the original, reuse the closest key or keep the original's literal.

## 2. Toolchain: everything runs in Docker

`blazor/dev` wraps Docker; nothing else is needed on the host. Never run `dotnet`, `node` or
`sass` on the host.

```
./dev build            prepare assets + dotnet build (warnings are errors)
./dev test [args]      unit/component tests (e.g. ./dev test --filter FullyQualifiedName~Login)
./dev check            build + test: must pass before every commit
./dev e2e smoke.py     serve the app, run a Playwright script from e2e/ against it, stop
./dev serve            dev server on http://localhost:${PORT:-5080}; ./dev stop ends it
./dev api <type-rx> [member-rx]   print SDK / MudBlazor API signatures (use it instead of guessing)
./dev publish          release publish to blazor/artifacts/publish
./dev clean            remove bin/obj/artifacts/generated assets (frees ~1 GB per checkout)
```

Rules for this machine (ARM64 board, 8 cores, ~6 GB free RAM, shared by several agents):

- Heavy commands take a machine-wide lock, so builds from parallel worktrees queue up. A
  `./dev check` takes 1 to 4 minutes plus queueing; give it a 10 minute timeout and do not start
  several at once. Batch your edits and build less often.
- **`/tmp` is RAM-backed. Anything in /tmp eats into RAM: do not write there**; put scratch files
  in `blazor/artifacts/` (git-ignored) and delete them when done.
- Do not leave `./dev serve` running. Prefer `./dev e2e <script>` (starts and stops its own
  server). Do not stop, reset or reconfigure the shared test server or other containers.
- Do not pull additional Docker images and do not add NuGet packages without a strong reason
  (say so in your report if you did). Package versions are central: `Directory.Packages.props`.
- Never push. Commit locally on your branch only.

### Test server and browser tests

`scripts/test-server.sh up` starts (or reuses) one shared Jellyfin server at
`http://localhost:18096` (admin user `admin`, password `jellyfin`, startup wizard completed) with
synthetic media: library Movies (`Big Test Movie (2019)` 90 s, `Second Feature (2021)` mkv with
English and French audio and an external English subtitle, `Third Film (2008)`, all H.264/AAC,
and `Open Codec Movie (2015)` in VP9/Opus webm, which Playwright's Chromium can decode (it has
no H.264/AAC decoders); posters and backdrops), Shows (`Test Show`, 4 episodes in 2 seasons) and Music (`Test Artist` / `First Album`
with 3 songs, `Other Band` / `Second Album` with 1 song). Tests may change user data (played,
favorites, playback position) but must not delete media, libraries or the admin user.
`e2e/lib.py` has the helpers (`new_page`, `sign_in`, `go`, `shot`, `finish`); `e2e/smoke.py` is
the template for a script. Screenshots go to `blazor/artifacts/e2e/` (read them to check your
layout). A browser test for your area is expected for anything visual; keep scripts short.
Note for the websocket and other raw requests: this server only accepts `ApiKey=` (not the
legacy `api_key=`) as a query-string token.

## 3. Solution layout

```
blazor/
  dev, docker/, scripts/           toolchain (Docker wrapper, asset pipeline, test server)
  e2e/                             Playwright scripts (Python, run in Docker)
  src/Jellyfin.Web/
    Program.cs, App.razor          startup; root component (providers, router, global hosts)
    Routing/                       hash router, route table, guard, AppLocation
    Services/                      app services (see section 5)
    Interop/                       IBrowser (sync JS interop) and its JS implementation
    Layout/                        MainLayout (app bar, drawer, page area)
    Components/                    shared components (elements, dialogs, shell)
    Pages/                         routable pages, one folder per area
    Styles/port/_*.scss            styles that exist only in the port
    wwwroot/                       index.html, js/, config.json; generated/ is build output
  tests/Jellyfin.Web.Tests/        xUnit + bUnit; Support/ has the test doubles
  tools/ApiSearch/                 backs `./dev api`
```

## 4. Rules that keep parallel work mergeable

Several agents work in separate git worktrees at the same time and their branches are merged
afterwards. Central files are conflict magnets, so the foundation offers conventions instead:

- **Stay inside the directories your task owns.** If you must change a shared file (anything in
  `Services/` you do not own, `Routing/`, `Layout/`, `Components/*.razor`, `Components/Shell/`,
  `Interop/`, `wwwroot/index.html`, `_Imports.razor`, the test `Support/` folder, `dev`,
  `scripts/`), keep the change minimal and additive and list it in your final report.
- **Namespaces**: do not add `@using` lines to the root `_Imports.razor`. Shared components in a
  new folder under `Components/` get a folder-level `_Imports.razor` containing
  `@namespace Jellyfin.Web.Components`, so they are visible everywhere without imports. C#
  services use `namespace Jellyfin.Web.Services;` whatever folder they are in. Pages use the
  default namespace of their folder. Prefix names that could collide across areas
  (`DashboardUserCard`, not `UserCard`).
- **Services**: mark a class `[AppService]` to register it as a singleton; do not edit
  `AppStartup.cs`. `[AppService(typeof(IFoo))]` also registers it as `IFoo` and overrides an
  earlier registration. Implement `IStartupTask` on such a service to run code once at startup
  (subscribe to `ServerEvents`, `ServerConnections.LocalUserSignedIn/Out`, ...).
- **Shell extension points**: mark a parameterless component
  `@attribute [ShellSlot(ShellSlot.Toolbar)]` (app bar button), `ShellSlot.Footer` (bottom of the
  layout, e.g. now-playing bar) or `ShellSlot.Overlay` (root level, above pages) instead of
  editing the layout.
- **Routes** are discovered by reflection from `[Route]` attributes; there is no route list.
- **Styles**: add `Styles/port/_<area>.scss`; every partial in that folder is compiled in
  automatically (alphabetical order, after the original CSS). No scoped `.razor.css` files, no
  `<style>` blocks. Prefix new classes with `jf-`. Never edit the original `src/**/*.scss`.
- **JavaScript**: add an ES module `wwwroot/js/<feature>.js` and load it with
  `IJSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/<feature>.js")`; do not add script
  tags to `index.html`. Hide it behind an interface registered with `[AppService(typeof(I...))]`
  so tests can substitute it. Keep JS to what C# cannot do (media elements, observers, gestures).
- **Vendor libraries** come from the npm registry through the `vendor` helper in
  `scripts/prepare-assets.sh` (versions mirror the original `package.json`).

## 5. Foundation contracts

### Pages and routing

Hash routing (`#/path?query`), independent of `NavigationManager` (do not use it).

```razor
@attribute [Route("/mypage")]
@attribute [Access(AccessLevel.User)]   @* Public | User (default) | Admin | Wizard *@

<Page Id="myPage" Class="mainAnimatedPage libraryPage" Title="@Globalize["HeaderX"]">
    ...original page markup...
</Page>

@code {
    [CascadingParameter] public AppLocation Location { get; set; } = AppLocation.Empty;
    [Parameter] public string? ParentId { get; set; }   // bound from ?parentId= (case-insensitive)
}
```

- `Page` renders the original `div.page` wrapper (`Id`, `Class`, `Title`, `BackButton`).
- Query-string values bind to `[Parameter]` properties by name (case-insensitive). Make them
  nullable: a parameter that is absent from the URL is reset to `null`. The page instance is
  reused when only the query changes; react in `OnParametersSet(Async)`.
- `AppLocation`: `Path`, `Query`, `this[key]`, `Tab`, `With((key, value))`.
- Navigation: `AppRouter.Navigate(url, replace)`, `Back()`, `GoHome()`, `ShowItem(item)`,
  `GetRouteUrl(item, options)` (returns `#/...` hrefs), `GetListUrl`, `ShowLogin`,
  `ShowSelectServer`, ... Plain links are `<a href="#/home">`. Item ids in URLs use
  `Guid.ToString("N")`.
- The guard (`Routing/RouteGuard.cs`) implements the original `ConnectionRequired` redirects.
- Dialogs close on navigation.

### Server access

- `JellyfinApi` (inject it): `Client` is the Kiota `JellyfinApiClient` from `Jellyfin.Sdk`
  2026.10.12, e.g. `await Api.Client.Items.GetAsync(c => c.QueryParameters.ParentId = id)`.
  Find request builders and model members with `./dev api` (e.g.
  `./dev api 'ItemsRequestBuilder' 'QueryParameters'`, `./dev api '^.*\.BaseItemDto$'`).
  Also: `UserId`, `User`, `IsLoggedIn`, `IsAdmin`, `ServerId`, `ServerAddress`, `AccessToken`,
  `GetUrl(path, (key, value)...)` for image/stream URLs, `SendAsync`/`GetJsonAsync<T>` for
  endpoints the SDK lacks, `StatusOf(exception)` for the HTTP status of a failure, `Changed`.
- Enum to wire text: `value.ToApiString()` (`Services/EnumText.cs`).
- `ServerConnections`: connect / authenticate / logout, saved servers, `LocalUserSignedIn/Out`.
- `ServerEvents`: the websocket. `Subscribe("UserDataChanged", json => ...)` returns an
  `IDisposable`; `SendAsync(type, data)`.
- `UserViews`: the user's libraries (`GetAsync()`, `Current`, `Changed`).

### Settings, text, theme

- `Globalize["Key"]`, `Globalize.Translate("Key", args...)`, `FormatDate`, `FormatNumber`,
  `LanguageChanged`.
- `UserSettings` (per user, synced with server display preferences; typed properties plus
  `Get/Set/GetBool/GetInt`), `AppSettings` (device-local), `WebConfig` (`config.json`).
- `ThemeManager`, `ShellState` (`SetTitle`, `SetTransparentHeader`), `AppHost` (browser info,
  `IsMobileLayout`, device id/name), `Viewport.Sm/Md/Lg/Xl` breakpoints with
  `IBrowser.ViewportWidth` and `IBrowser.Resized`.

### UI utilities

- `AppDialogs` (not MudBlazor's `IDialogService`, to keep the original dialog look):
  `AlertAsync`, `ConfirmAsync`, `PromptAsync`, `ActionSheetAsync(ActionSheetOptions)`,
  `ShowAsync<TComponent>(parameters, AppDialogOptions)`; a dialog component receives
  `[CascadingParameter] DialogInstance Dialog` and calls `Dialog.Close(result)`.
  See `Components/Dialogs/` for the markup conventions (`formDialogHeader`, ...).
- `ToastService.Show(text)`, `LoadingService.Show()/Hide()`.
- Elements with the original markup: `EmbyButton`, `LinkButton`, `IconButton`, `EmbyInput`,
  `EmbyTextarea`, `EmbySelect`, `EmbyCheckbox`, `EmbyToggle`, `EmbyRadio`, `EmbyCollapse`,
  `Icon` (`<Icon Name="home" />`). They take `Class`, splat extra attributes, and bind with
  `@bind-Value`.
- MudBlazor 9.12: icons are font icons, `Icon="@MdIcon.Of("home")"`. In `MudMenu` the activator
  must toggle explicitly: `<ActivatorContent Context="menu"><MudButton OnClick="@(e =>
  menu.ToggleAsync(e))" ...`. Names that clash with MudBlazor and must not be reused:
  `DialogService`, `Breakpoints`.
- Diagnostics: `AppLog.Warn(...)`. **Never write to `Console.Error`**: in Blazor WebAssembly it
  raises the "unhandled error" banner. Let unexpected exceptions propagate; catch only what you
  handle.

### Playback

Pages call `IPlaybackManager` (`Services/Playback/IPlaybackManager.cs`): `PlayAsync(PlayOptions)`,
`QueueAsync`, `ShuffleAsync`, `InstantMixAsync`, `CanPlay`, state and `StateChanged`. Until the
playback workstream lands, a null implementation is registered. Extend the interface only in the
playback workstream.

## 6. Tests

`tests/Jellyfin.Web.Tests`, xUnit + bUnit 2.x. `Support/` provides:

- `TestApp`: a full service container with `FakeBrowser` (in-memory `IBrowser`), `FakeHttp` (the
  scripted server: `app.Server.OnGet("/Items", new { Items = ..., TotalRecordCount = 1 })`,
  `OnPost`, `Requests`, `RequestsTo(suffix)`), real strings and config; `SignIn(admin: true)`.
- `AppTestContext`: base class for component tests (`@inherits AppTestContext` in a `.razor`
  test file, or `: AppTestContext` in C#); `App` is the `TestApp`, `SignIn()` signs in.
  See `Foundation/ShellTests.razor`.

Every workstream adds tests for its logic (services, state machines, URL building, formatting)
and at least a render/interaction test per page or complex component. JSON for `FakeHttp` uses
the server's PascalCase names.

## 7. Definition of done for a workstream

1. `./dev check` passes (no warnings, all tests).
2. For visual work: a Playwright script under `e2e/` that exercises it against the test server,
   run with `./dev e2e <script>`, screenshots looked at.
3. Work committed on the worktree's branch (several commits are fine). Commit messages end with
   `Co-Authored-By: Claude Code <noreply@anthropic.com>`. No push, no pull request.
4. `./dev clean` run in the worktree after the last commit; nothing left in `/tmp`; no container
   of yours left running.
5. Final report: what was ported (routes/components), what is missing or stubbed (be exact),
   shared files touched, test and e2e results, the branch name and last commit hash.
