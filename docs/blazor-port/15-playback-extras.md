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
icons, auto-cast/display-mirror switches, remote-control link and disconnect. Chromecast is not
implemented; the menu says it is unsupported. `/queue` displays the remote state and exposes
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

- `./dev check`: 1,886 tests pass, zero build warnings/errors (remote logic and bUnit interactions
  live in `tests/Jellyfin.Web.Tests/PlaybackExtras/`).
- `./dev e2e playback_extras.py`: two isolated browser contexts select a target and start audio
  from the detail page, check remote pause/state, volume/mute, navigation, text and message, then
  stop/disconnect. Desktop and 400px screenshots are inspected. This scenario does not use the
  playback test hook.

## Remaining priorities

- `/lyrics`, its now-playing/queue buttons, synced scrolling and click-to-seek.
- OSD media segments, subtitle offset/secondary subtitles, Live TV-specific controls, additional
  settings entries and the requested Playing/Ping policy (the original itself sends no such ping).
- Remove `PlaybackTestHook`/`testCommand` and rewrite the older `e2e/playback.py` around public UI.
- SyncPlay is not started. `PORT-TODO(syncplay)` belongs at its future integration points.

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
