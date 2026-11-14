# Playback in the Blazor port

Reference for the local playback workstream: what exists, where the JS/C# boundary is, how later
workstreams extend it, and what is missing. The behaviour of the original is described in
`04-playback-architecture.md`; this document only describes the port.

## 1. What is ported

| Original | Port |
| --- | --- |
| `components/playback/playbackmanager.js` (local players) | `Services/Playback/PlaybackManager*.cs` |
| `components/playback/playqueuemanager.js` | `Services/Playback/PlayQueue.cs` |
| `scripts/browserDeviceProfile.js`, `apphost.js` `getDeviceProfile` | `DeviceProfileBuilder.cs`, `MediaCapabilities.cs`, `wwwroot/js/playback/capabilities.js` |
| `plugins/htmlVideoPlayer`, `plugins/htmlAudioPlayer`, `components/htmlMediaHelper.js` | `HtmlMediaPlayer.cs` (`HtmlVideoPlayer`, `HtmlAudioPlayer`), `wwwroot/js/playback/player.js` |
| `components/playback/mediasession.js` | `MediaSessionUpdater.cs` + `setMediaSession` in `player.js` |
| `components/qualityOptions.js`, bitrate detection of the api client | `QualityOptions.cs`, `BitrateDetector.cs` |
| `components/playback/playersettingsmenu.js` | `PlayerSettingsMenu.cs` |
| `components/playerstats/playerstats.js` | `PlayerStatsBuilder.cs`, `Components/Playback/PlayerStatsOverlay.razor` |
| `controllers/playback/video` (the OSD) | `Pages/Playback/VideoOsd.razor`, route `/video` |
| `controllers/playback/queue`, `components/remotecontrol/remotecontrol.js` | `Pages/Playback/Queue.razor`, route `/queue` |
| `components/nowPlayingBar/nowPlayingBar.js` | `Components/Playback/NowPlayingBar.razor` (`ShellSlot.Footer`) |
| `components/upnextdialog/upnextdialog.js` | `Components/Playback/UpNextDialog.razor` |
| the `emby-slider` of the players, trickplay/chapter bubble | `Components/Playback/PlaybackSlider.razor`, `Trickplay.cs`, `wwwroot/js/playback/ui.js` |

Markup, class names, translation keys and setting keys are the original's, so the original style
sheets apply. Classes the port adds are prefixed `jf-` and live in `Styles/port/_playback.scss`.

## 2. Architecture

```
pages, cards, lists ──► IPlaybackManager ──► PlaybackManager ──► IMediaPlayer ──► IPlayerInterop ──► player.js
                                             │  .Items      what to play (translation, queue)        (elements,
VideoOsd / NowPlayingBar / Queue ──────────► │  .Streams    how to play it (PlaybackInfo, URLs)       hls.js,
  (inject PlaybackManager directly)          │  .Reporting  telling the server                        text track,
                                             │  .Controls   transport, tracks, quality, queue         Media Session)
                                             └─ PlayQueue, BitrateDetector, DeviceProfileBuilder
```

- **`IPlaybackManager`** is what other workstreams call: `PlayAsync`, `QueueAsync`,
  `QueueNextAsync`, `ShuffleAsync`, `InstantMixAsync`, `PlayTrailersAsync`, `CanPlay`, `CanQueue`,
  transport commands, `IsPlaying*`, `CurrentItem`, `StateChanged`. `PlayOptions` carries `Items` or
  `Ids`, `StartPositionTicks`, `StartIndex`, `MediaSourceId`, audio/subtitle stream indexes,
  `Shuffle`, `Fullscreen`, `QueryOptions`.
- **`PlaybackManager`** (the registered implementation, also injectable as itself by the playback
  UI) adds everything the players' UI needs: `Event` (names in `PlaybackEvents`), `PlaybackStopped`,
  `Queue`, `CurrentPlayer`, `CurrentStreamInfo`, `GetPlayerState()`, tracks, quality, repeat and
  shuffle, volume, playback rate, aspect ratio, fullscreen, picture in picture, statistics.
- **`IMediaPlayer`** is the port of the player plugin contract. `HtmlVideoPlayer` and
  `HtmlAudioPlayer` are registered with `[AppService(typeof(IMediaPlayer))]`; the manager receives
  all of them through `IEnumerable<IMediaPlayer>`.
- **Play flow** (`PlayAsync`): translate the items (folders, series, albums, artists, genres,
  playlists, programmes, "play all from here" for episodes) → fill the queue → pick the player →
  device profile → bitrate (setting or detection) → `POST Items/{id}/PlaybackInfo` → choose the
  media source (direct play, then direct stream, then transcode; live streams are opened) → build
  the URL → `player.PlayAsync(streamInfo)` → `Sessions/Playing` → progress every 10 s and on
  pause/unpause/volume/track/repeat/shuffle/queue changes (rate limited like the original) →
  `Sessions/Playing/Stopped` → next item or clean-up. Stream changes (audio or subtitle track,
  quality, seeking in a transcode) go through `ChangeStreamAsync`, which asks for PlaybackInfo
  again and reloads the element at the current position.
- **Time and timers** come from `IPlaybackTimers` (clock, delay, interval), so the timing of the
  reports is testable.

## 3. The JS/C# boundary

All decisions are C#. JavaScript owns only what C# cannot do, behind three interfaces:

| Interface | Module | Contents |
| --- | --- | --- |
| `IPlayerInterop` (`JsPlayerInterop`) | `js/playback/player.js` | Creates the `<video>` (`.videoPlayerContainer > video.htmlvideoplayer`) and `<audio>` (`.mediaPlayerAudio`) elements outside of the Blazor tree, loads a source (direct, hls.js loaded on demand from `generated/lib/hls.js`, native HLS where the element supports it), start position, pause, seek, volume, mute, rate, aspect ratio, audio track of the element, native text cues, libass ASS/SSA and libbitsub PGS renderers, renderer offsets, fullscreen, picture in picture, fade out, audio normalisation gain, statistics, Media Session. Element events are sent back as `(kind, name, snapshot, errorType)`. |
| `IMediaCapabilityProbe` (`JsMediaCapabilityProbe`) | `js/playback/capabilities.js` | One call that answers all `canPlayType` / `MediaSource.isTypeSupported` questions the device profile needs. `DeviceProfileBuilder` works on the resulting `MediaCapabilities` record and is pure C#. |
| `IPlaybackUiInterop` (`JsPlaybackUiInterop`) | `js/playback/ui.js` | Document-level listeners of the OSD (pointer movement, keys, wheel) and the hover position of sliders. |

The modules are ES modules loaded with a dynamic `import` on first use; nothing was added to
`index.html`. Tests replace the three interfaces with fakes
(`tests/Jellyfin.Web.Tests/Playback/PlaybackTestSupport.cs`).

`player.js` also exports `debugState()` and `testCommand(name, args)`. The latter calls
`PlaybackTestHook` (C#) and is the entry of the browser test: no page of this workstream starts
playback by itself, so `e2e/playback.py` starts it through this hook and then uses the real UI.
Remove the hook once item pages have play buttons and the script can click them.

## 4. Extension points

- **Starting playback** (cards, details page, lists, context menus): inject `IPlaybackManager` and
  call `PlayAsync(new PlayOptions { Ids = [...] })` or one of the other entry points. Show the
  buttons according to `CanPlay(item)` / `CanQueue(item)`.
- **A non-local player** (remote session, cast, SyncPlay proxy): implement `IMediaPlayer` with
  `IsLocalPlayer = false` and register it with `[AppService(typeof(IMediaPlayer))]` (or call
  `PlaybackManager.RegisterPlayer`). `SetCurrentPlayer(player)` makes it the target and raises
  `playerchange`; `GetPlayer()` already prefers a non-local current player. What is still missing
  is the delegation itself: the original hands whole commands to such a player
  (`player.play(options)`, `player.queue(...)`, `getPlayerState()`, `sendCommand`), while the port's
  manager always does the work itself. The place is marked `PORT-TODO(playback)` at the top of
  `PlayAsync` in `PlaybackManager.Items.cs`; the transport methods in `PlaybackManager.Controls.cs`
  need the same `if (!player.IsLocalPlayer) return player.X()` branch, and `IMediaPlayer` needs the
  self-managing members (`PlayAsync(PlayOptions)`, `GetPlayerState`, `SendCommand`) added then.
  A target picker would list `Players` plus the sessions of `Sessions?ControllableByUserId=`.
- **Other local players** (photo, book, PDF, YouTube): implement `IMediaPlayer` with
  `IsLocalPlayer = true` and a `CanPlayMediaType` for their media type; `GetPlayer` picks by
  `Priority`. Photos and books are currently refused in `PlaybackManager.Items.cs`
  (`PORT-TODO(playback)`), remote trailers in `PlayTrailersAsync`.
- **Reacting to playback** (media segments, lyrics, SyncPlay): subscribe to `PlaybackManager.Event`
  and `PlaybackStopped`.
- **Queue page additions** (remote control sections, save as playlist, lyrics button) and the full
  item context menu are marked in `Queue.razor` and `NowPlayingBar.razor`.

## 5. What is supported

- Video: direct play, direct stream and transcode (HLS through hls.js or natively, or progressive),
  resume position, seeking (in the element, or by restarting the transcode), audio track switching
  (in the element when it has audio tracks, otherwise by a stream change), quality menu and
  automatic bitrate detection, playback speed, aspect ratio, repeat modes, fullscreen, picture in
  picture, chapters and trickplay previews on the position slider, "up next" for episodes, the
  playback info overlay, the original keyboard shortcuts of the OSD, auto-hide.
- Audio: queue, next/previous, shuffle and repeat, auto-advance, fade out on stop, normalisation
  gain, volume kept in `appSettings` (`volume`), Media Session metadata and actions, now-playing bar
  and queue page (jump, move, remove, clear).
- Subtitles:

  | Format | How |
  | --- | --- |
  | Text formats the server converts to WebVTT (SubRip, WebVTT, MOV text and the like), external files or embedded | The device profile advertises `vtt` / `External` when browser text tracks are available. The server delivers such streams as `External`; the player fetches them as track events (`….js` instead of `….vtt`) and shows them as cues of a native text track. Switching does not restart the stream. |
  | ASS/SSA | `@jellyfin/libass-wasm` 4.2.4 (`SubtitlesOctopus`) renders externally delivered tracks, including attached and server fallback fonts. Offset and renderer appearance settings are applied without restarting the video. A renderer failure reports `ASS_RENDER_ERROR` and retries server-side rendering. |
  | PGS | `libbitsub` 1.13.0 (`PgsRenderer`) renders externally delivered tracks when canvas support, `subtitlerenderpgs` and the burn-in policy allow it. Offset and contain/stretch/cover mapping are applied without restarting the video; `PGS_RENDER_ERROR` falls back to server rendering. |
  | VobSub, DVB and other image formats | Not advertised by this port and therefore burned in by the server (`Encode`). |
  | `subtitleburnin = all` | Everything is burned in, as in the original. |

- Reporting: `Sessions/Playing`, `Sessions/Playing/Progress`, `Sessions/Playing/Stopped` with the
  original payloads; active encodings are stopped on stream changes and at the end.
- Errors: the original keys (`PlaybackErrorNoCompatibleStream`, `PlaybackErrorPlaceHolder`,
  `PlaybackError.<code>`) under `HeaderPlaybackError`; after a media error the stream is
  negotiated again without direct play, as long as the server can still change something, like the
  original.
- Sign-out stops playback and clears the queue and the bitrate cache.

## 6. Known gaps

Marked in the code with `// PORT-TODO(playback): …`.

- Session remote control, synced lyrics and native AirPlay target pickers are now ported; see `15-playback-extras.md`. Cast, SyncPlay (also its OSD icon and statistics category),
  live TV specifics of the OSD (programme info, record button), photo/book/PDF/YouTube players and with them remote trailers and items that are plain URLs.
- VobSub/DVB client renderers and some advanced bitmap appearance controls remain unported.
- Brightness gestures, TV-layout key handling, iOS forced-fullscreen back navigation, iOS HLS live
  prefetch, flv.js, playback interceptors.
- Queue page: drag reordering (replaced by Up/Down buttons), remote backdrop. Remote control sections
  are ported.
- OSD: buttons are not focused when it appears; the slider bubble is not clamped to the track's
  edges.

## 7. Deviations from the original

- Reports are posted one after another, so that the server sees them in order; the original fires
  them independently.
- On auto-advance the original posts `Sessions/Playing/Stopped` twice and raises an extra
  `playbackstop`; the port sends one.
- `Sessions/Playing/Ping` is not sent.
- The progress rate limiter, `stopActiveEncodings` (`DELETE Videos/ActiveEncodings`) and the
  favourite request (`Users/{id}/FavoriteItems/{id}`) belong to `jellyfin-apiclient`, which is not
  in the repository. They were written from knowledge of that package and work against the test
  server, but were not compared with the package source.
- Item queries are `GET /Items` with comma-joined lists.
- Only one dialog is shown when a specific playback error was already reported; failures to load
  the items to play end in the same rejection handler as failures of the negotiation.
- A failed fetch of the album normalisation gain does not stop playback.
- `isRetry` of the device profile and `RequiredHttpHeaders` of a media source are ignored;
  direct play over the `File` protocol is never chosen (browsers cannot open paths).
- The signed-in user of `JellyfinApi` is used instead of fetching the user again for each play.
- Playback is stopped on sign-out (the original leaves that to the page reload). The stop report
  cannot be posted then, because the session's token is dropped before `LocalUserSignedOut` is
  raised.
- OSD: the header is drawn by the page; "hide" is applied by a timer instead of `transitionend`;
  ArrowLeft/ArrowRight on the focused position slider rewind and fast-forward like elsewhere on
  the page.
- The document title on `/video` is the series name, or the item name.
- The record describing a stop is called `PlaybackStoppedInfo`.

## 8. Tests

- Unit and bUnit tests: `tests/Jellyfin.Web.Tests/Playback/` — queue, item translation, device
  profile (faked capability probe), stream decision and URLs, the play flow, progress reports and
  their timing (injected clock), controls and settings, helpers, and the OSD, the now-playing bar
  and the queue page against a fake player.
- Browser test: `./dev e2e playback.py [chromium|firefox|webkit]` against the test server. It plays
  the VP9/Opus movie directly, checks time, decoded frames, pause, seeking, keys, the end of the
  video and the session the server reports; then resume position and back navigation; then the
  negotiation of the H.264 movie (HLS transcode, external subtitle track, audio track switch,
  quality change); then the audio queue with the bar and the queue page. Chromium and Firefox of
  the Playwright image decode all test media and pass completely. In the image's WebKit the media
  elements do not advance (the Linux build has no usable media pipeline there), so only the
  negotiation and UI checks pass.
- The test media are shorter than the server's minimum resume duration, so the server never keeps a
  resume position for them; the resume position is verified on the client's stop report.
- Subtitle renderer fixtures are private-server-only. Keep `seed-media.sh` unchanged because its
  inventory is shared by unrelated tests. To run the renderer test, use:
  `JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/test-server.sh up`, then
  `JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/seed-subtitles.sh`, and run
  `JELLYFIN_URL=http://localhost:18106 ./dev e2e subtitles.py`. The script generates the SUP file
  through the Jellyfin image's Perl runtime; no host Python is required. Finish with
  `JELLYFIN_INSTANCE=subtitles JELLYFIN_PORT=18106 scripts/test-server.sh reset`.
