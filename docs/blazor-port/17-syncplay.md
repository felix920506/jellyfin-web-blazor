# SyncPlay

## Delivered

The Blazor port now has the SyncPlay core and the modern toolbar UI under
`Services/Playback/SyncPlay/` and `Components/SyncPlay/`.

* `SyncPlayService` subscribes to the existing `ServerEvents` websocket for
  `SyncPlayCommand` and `SyncPlayGroupUpdate` and keeps group membership,
  queue identity, server clock offset, following/ignore-wait state, and
  buffering state separate from the session remote player.
* Server time is estimated from the eight lowest-latency `/GetServerTime`
  measurements. Timestamped pause, unpause, seek and stop commands are
  scheduled against that clock. Commands applied locally use echo suppression,
  so a local command is not sent back as a duplicate server command.
* The local playback manager is used for all transport operations. Ready and
  buffering reports include `When`, `PositionTicks`, `IsPlaying`, and the
  current `PlaylistItemId`. Queue updates load the shared item ids and preserve
  the server playlist identity.
* The original SkipToSync and SpeedToSync thresholds/settings are available in
  the settings editor. Active drift correction is disabled by default, matching
  the original client; when enabled, playback rate correction is preferred for
  the configured range and seeks are used for larger drift.
* The toolbar group menu supports listing, creating, joining, resuming,
  halting, settings, and leaving. The playlist editor offers the SyncPlay queue
  when a group is active. Playback stats include SyncPlay method, drift, and
  clock offset; the video OSD shows an active-group marker and buffering state.

## Verification

* `./dev check`: 2,118 tests pass, zero warnings/errors.
* `./dev e2e syncplay.py`: two Chromium pages sign in to the shared test server,
  temporarily grant `CreateAndJoinGroups`, create/join through the toolbar,
  play the seeded movie, verify a pause command on the second device, leave,
  and restore the original policy in `finally`.

## Known limitations

The browser scenario and core cover the requested local transport path, but
the UI intentionally remains a compact modern-menu port rather than reproducing
the legacy action-sheet's every secondary description. Queue move/repeat/
shuffle controls are exposed by the core API but do not yet have dedicated
toolbar menu entries. Media-ready timing depends on the local player's existing
`playing`/`waiting` events; there is no separate SyncPlay media wrapper.

No SyncPlay capability is added to the server capabilities report: SyncPlay is
only activated after a group is joined.
