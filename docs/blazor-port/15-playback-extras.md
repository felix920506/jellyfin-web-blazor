# Playback extras

## Delivered: session remote control

The original `plugins/sessionPlayer/plugin.js` is ported as `SessionPlayer`, an `IRemotePlayer`
(`IMediaPlayer` with self-managed state and queue). `PlaybackManager.Remote.cs` owns target
selection, display mirroring, remembered targets and transfer of local playback. Manager entry
points delegate whole requests to the target rather than negotiating streams or reporting remote
playback locally. `/Sessions?ControllableByUserId=` supplies targets, excluding this device;
`SessionsStart` (`100,800`) websocket messages supply state. Subscriptions restart on reconnect,
with a five-second administrator watchdog for another page stopping the shared subscription.

`RemotePlayButton` fills the toolbar slot, using the modern client's cast/connected menus, target
icons, auto-cast/display-mirror switches, remote-control link and disconnect. The Google Cast entry
follows the Chromecast player (below). `/queue` displays the remote state and exposes
capability-gated navigation, message and text sections using the original markup and translations.
Transport, volume/mute, track changes, repeat, shuffle, queue jump and queue move send the same
session endpoints and payloads as the original. Remote queue removal/clearing is a no-op, as in
the original session player; it is not a new server queue API.

`RemoteCommandReceiver` accepts websocket `Play`, `Playstate` and `GeneralCommand` messages in
the reverse direction. `IRemoteInput` abstracts focus movement, selection and text entry, with a
small DOM-only JS module. Capabilities were already reported by `ServerConnections`; that shared
file is unchanged. Commands intentionally ignored by the original remain ignored (for example
SendKey, PageUp/Down and ToggleContextMenu). Navigation focus scopes use the topmost Mud dialog
or popover rather than legacy focuscontainer classes, which ported pages do not consistently have.
DisplayMessage with TimeoutMs uses the existing toast duration, not a custom timeout.

## Delivered: Google Cast (Chromecast)

`plugins/chromecastPlayer` is ported as `Services/Playback/Cast/CastPlayer.cs`, a second
`IRemotePlayer` registered like `SessionPlayer` (`[AppService(typeof(IMediaPlayer))]`, also an
`IStartupTask`). The SDK is behind `ICastInterop` (`Cast/CastInterop.cs`, `JsCastInterop`) and the
module `wwwroot/js/playback/cast.js`; all decisions are in C#.

- **Loading.** Only in Chrome, Edge (Chromium) and Opera, as `index.tsx` filters the plugin, and only
  when `window.chrome` exists (the original's check in `initializeCastPlayer`). The module then adds
  `https://www.gstatic.com/cv/js/sender/v1/cast_sender.js` and waits for `chrome.cast.isAvailable`
  (1 s polling like the original; `__onGCastApiAvailable(false)` or a failed script load end it
  quietly). Afterwards, and again after every sign-in, `GET /Users/{id}` supplies
  `Configuration.CastReceiverId` for `chrome.cast.initialize` (no initialisation without one).
- **Menu.** While the SDK is unavailable the menu keeps the disabled "Google Cast Unsupported" entry.
  Once available, that entry goes, the target "Google Cast" (`deviceType` `cast`) is listed while
  the receiver listener reports receivers, and "No cast targets available" is shown when there are
  no targets at all, as `RemotePlayMenu.tsx` does with the plugin loaded.
- **Pairing.** Choosing the target opens the browser's device picker (`requestSession`); a cancel
  leaves playback here. A session that survives a reload (`sessionListener`) makes the player active
  by itself (`PlaybackManager.SetActivePlayer`); a session that ends returns to this device. After
  connecting the receiver gets `Identify`, and what plays here moves to the receiver
  (`TransferPlaybackAsync`, as for sessions).
- **Messages** (`urn:x-cast:com.connectsdk`, JSON text): `PlayNow`, `PlayNext`, `PlayLast`,
  `Shuffle`, `InstantMix` with the items reduced to `Id, ServerId, Name, Type, MediaType, IsFolder`
  (one id is loaded with `Users/{u}/Items/{id}`, several with `/Items`, limit 100, no virtual
  items, sorted like the requested ids); `Pause`, `Unpause`, `PlayPause`, `Stop`, `Seek`
  (`position` in seconds), `NextTrack`, `PreviousTrack`, `SetAudioStreamIndex` and
  `SetSubtitleStreamIndex` (`index`), `Mute`, `Unmute`, `ToggleMute`, `SetRepeatMode`
  (`RepeatMode`), `SetMaxStreamingBitrate`, `DisplayContent` (display mirroring) and `Identify`.
  Every message carries `userId, deviceId, accessToken, serverAddress` (a localhost address is
  replaced by the server's local address), `serverId, serverVersion, receiverName`, `maxBitrate`
  from the Chromecast quality preference (`chromecastBitrate1`), and with items
  `subtitleAppearance` and `subtitleBurnIn`. Volume (set, up/down by 5 %) is the receiver's own
  (`setReceiverVolumeLevel`). Queue shuffle and trailers are refused with a warning, as in the
  original; other general commands are dropped.
- **Receiver state.** `playbackstart`, `playbackstop`, `playbackprogress` (as `timeupdate`),
  `timeupdate`, `pause`, `unpause`, `volumechange`, `repeatmodechange`, `shufflequeuemodechange`
  (only with an `ItemId`) and `playstatechange` (as `pause`) replace the player's state; the first
  one after connecting also raises `statechange`. Item images are normalised like the original.
  The manager passes the events on, so the now-playing bar, `/queue` and the toolbar show the
  receiver's item, position, pause, volume, tracks and repeat mode. `playbackerror` and
  `connectionerror` are shown as alerts after 300 ms.

Deviations and open points (`PORT-TODO(cast)` in the code):

- The SDK script is not requested without `window.chrome` (the original requests it in every
  Chromium browser and checks `window.chrome` afterwards). Headless Chromium has no
  `window.chrome`, so the browser tests never fetch it.
- The original re-runs `initializeCastPlayer()` on every `playbackstart`; the port initialises on
  load and sign-in only.
- Not ported: the Cordova `volumeupbutton`/`volumedownbutton` listeners and the `chromecastloaded`
  document event for native shells (no native shell in this port); the unused media-session
  bookkeeping of `CastPlayer` (`currentMediaSession`, `castPlayerState`).
- `serverVersion` comes from `/System/Info/Public` (fetched once per server address), because the
  port does not keep the version of the connection.
- Without a session, commands are logged and dropped instead of failing; a `playbackerror` without
  a code shows the plain `MessagePlaybackError` text.
- The play options sent are the port's `PlayOptions` fields (`ids, startPositionTicks, startIndex,
  mediaSourceId, audioStreamIndex, subtitleStreamIndex, shuffle, fullscreen, serverId, items`);
  the original forwards whatever object its caller passed.
- The modern menu labels the target "Google Cast - Google Cast" (`name - appName`), as the original
  modern menu does. `EndSession` is not among the target's commands, so Disconnect leaves the
  receiver application running, as in the original.
- No Cast device was available: the JS module has not been run against the real SDK or a receiver.

## Delivered: graphical subtitles

The local HTML video player advertises and renders externally delivered ASS/SSA subtitles with the
vendored `@jellyfin/libass-wasm` 4.2.4 package. Embedded font attachments are filtered by font MIME
type and passed to the renderer; when the server's fallback-font setting is enabled, its font list is
fetched and added as well. PGS is rendered with vendored `libbitsub` 1.13.0 when the browser has a
2D canvas, `subtitlerenderpgs` is enabled, and the subtitle burn-in policy permits external image
tracks. Both renderers support the current transcoding offset and user subtitle offset. Renderer
errors use the existing playback retry path, causing the server to negotiate a burned-in fallback.
Graphical tracks are not offered as secondary subtitle pairs, matching the original client's overlap
avoidance policy.

## Delivered: synced lyrics

`/lyrics` loads the current audio item's lyrics from `/Audio/{id}/Lyrics`. Plain lyrics remain
read-only; synced lines are classified from server ticks, autoscroll to the active line, pause-aware
click/Enter/Space seeking, and resume playback when a seek was made while paused. Wheel, touch and
arrow-key input temporarily suppresses automatic scrolling, while the small
`wwwroot/js/playback/lyrics.js` module is limited to DOM scrolling and input listeners. The
now-playing bar and `/queue` expose the lyrics action for audio items with lyrics.

## Delivered: AirPlay

The local HTML audio and video players probe for the Safari/WebKit playback-target API and expose
the original `AirPlay` supported command only when it is available. The now-playing bar, `/queue`
and video OSD then show their AirPlay button; it invokes the native target picker and reflects the
wireless-target state. The element binds WebKit availability and current-target events, while the
managed side remains behind `IPlayerInterop` for fake-interoperability unit coverage.

## Delivered: touch brightness

The video OSD translates vertical, off-control touch swipes into two-percent brightness changes.
It preserves the original player's 0–100 logical value and 20% rendered minimum through the HTML
video CSS filter, and presents the original-style orange brightness indicator while a change is
active. The input bridge deliberately ignores short movements and controls, so normal taps and
slider/button gestures retain their existing behavior.

## Delivered: iOS native-fullscreen exit

The HTML video element forwards WebKit's `webkitendfullscreen` event as a distinct playback event.
When native iOS fullscreen is dismissed, the OSD stops local playback and returns to the previous
route. This intentionally does not treat ordinary document fullscreen transitions as a stop.

### Extension points and deviations

- Implement/register `IRemotePlayer` for another self-managing target; session-specific HTTP and
  websocket details do not belong in `PlaybackManager`.
- Session queues are reconstructed by item id, preserving duplicate entries and omitting missing
  items without shifting their playlist ids. A version guards against stale loads after target or
  queue changes. A completion event refreshes Blazor even if the target is paused; this is an
  additional statechange versus the legacy promise-based queue getter.
- The modern toolbar uses MudBlazor rather than the legacy player-selection action sheet.
- Non-administrator session-state websocket restrictions are server-side and unchanged; no REST
  polling fallback is implemented. Test coverage uses the administrator test account.

### Verification

- `./dev check`: 1,890 tests pass, zero build warnings/errors (remote logic, lyrics timeline and bUnit interactions
  live in `tests/Jellyfin.Web.Tests/PlaybackExtras/`).
- Google Cast: `./dev check` with 2,177 tests, zero warnings. `CastPlayerTests.cs` drives the
  player and the menu through `FakeCastInterop` (availability, initialisation per sign-in, pairing,
  joined/ended sessions, every message and its payload, mirrored state, alerts, transfer of local
  playback, the menu entries and the now-playing bar). `playback_extras.py` opens the cast menu in
  headless Chromium, where the SDK is unavailable (no `window.chrome`, no request to gstatic), and
  checks the "unsupported" entry next to the session target without console or request errors.
- `./dev e2e playback_extras.py`: two isolated browser contexts select a target and start audio
  from the detail page, check remote pause/state, volume/mute, navigation, text and message, then
  stop/disconnect. It then temporarily uploads synced lyrics, checks autoscroll, click/keyboard
  seeking and desktop/400px layouts, and restores the server state. Desktop and 400px screenshots
  are inspected. This scenario uses the public playback UI.

## Remaining priorities

- Live TV-specific controls, additional
  settings entries and the requested Playing/Ping policy (the original itself sends no such ping).
- Pre-play access validation, weekly experimental media warnings and still-watching episode prompts
  are implemented by `PlaybackInterceptors` at the playback boundary. The remaining playback gaps
  are Live TV-specific controls, additional settings entries and the requested Playing/Ping policy.

### SyncPlay design (not implemented)

Use a dedicated startup service under `Services/Playback/`, subscribing to SyncPlayCommand and
SyncPlayGroupUpdate through `ServerEvents`. Keep group membership and clock offset separate
from the session player: a group synchronizes **local** playback, not remote session controls.
Add a toolbar slot component for list/create/join/leave, a settings dialog using the original
user-setting keys, and a time estimator with injected `IPlaybackTimers`. Route server-timestamped
play/pause/seek through local transport with suppression of outbound echo. The complete original
buffering/readiness, queue identity and drift-correction protocol needs tests before claiming a
functional subset; do not advertise SyncPlay capabilities before that.

No shared source files outside the owned playback paths have been changed by this workstream.
