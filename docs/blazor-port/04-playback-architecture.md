I've written the spec below. It is based on the source in `/home/user/jellyfin-web` (HEAD `b71bb29ed6`). One gap up front: `node_modules` is not installed and the `jellyfin-apiclient` 1.11.0 package isn't vendored. So for the old `apiClient.*` wrapper methods (playback reporting, `stopActiveEncodings`, `sendPlayCommand`, etc.) only the call sites and the type declarations in `src/apiclient.d.ts` can be checked. Where I give the HTTP route behind one of those methods, it's marked "unverified".

---

## 1. PlaybackManager — `src/components/playback/playbackmanager.js`

**Singleton:** `export const playbackManager = new PlaybackManager()`. On creation it binds `bindMediaSegmentManager`, `bindMediaSessionSubscriber` and `bindSkipSegment`. A `beforeunload` handler calls `onAppClose()`, which sends a final Stopped report.

**Public API (grouped)**
- Playback: `play(options)`, `stop`, `playPause`, `pause`, `unpause`, `seek(ticks)`, `seekRelative`, `seekPercent`, `seekMs`, `seekFrames(n)` (only when paused; uses `ReferenceFrameRate`, default 24), `fastForward` / `rewind` (use `userSettings.skipForwardLength` / `skipBackLength`), `nextTrack`, `previousTrack`, `nextChapter`, `previousChapter` (goes back 10 s, then to the previous chapter), `channelUp/Down`, `playTrailers`, `instantMix` (`getInstantMixFromItem`, `Limit:200`), `shuffle` (`play({items:[x], shuffle:true})`).
- Queue: `queue`, `queueNext`, `getPlaylist`, `setCurrentPlaylistItem`, `removeFromPlaylist`, `movePlaylistItem`, `clearQueue`, `getCurrentPlaylistIndex`, `getCurrentPlaylistItemId`, `nextItem`, `getNextItem`, `canQueue`, `setRepeatMode`, `getRepeatMode`, `setQueueShuffleMode`, `toggleQueueShuffleMode`.
- Tracks: `audioTracks`, `subtitleTracks`, `get/setAudioStreamIndex`, `changeAudioStream` (cycles), `get/setSubtitleStreamIndex`, `changeSubtitleStream`, `get/setSecondarySubtitleStreamIndex`, `setSubtitleOffset`, `getPlayerSubtitleOffset`, `canHandleOffsetOnCurrentSubtitle` (external subtitles only), `getSubtitleUrl`.
- Quality: `getMaxStreamingBitrate`, `setMaxStreamingBitrate({enableAutomaticBitrateDetection, maxBitrate})`, `enableAutomaticBitrateDetection`.
- Video and UI: `toggleFullscreen`, `isFullscreen` (Screenfull, with `webkitEnterFullscreen` fallback for iOS), `togglePictureInPicture`, `toggleAirPlay`, `set/getAspectRatio`, `getSupportedAspectRatios`, `set/getPlaybackRate` (stored in `sessionStorage.playbackRateSpeed`), `increase/decreasePlaybackRate`, `setBrightness`, volume and mute methods.
- State: `getPlayerState`, `currentItem`, `currentMediaSource`, `playMethod`, `playSessionId`, `duration` (from `mediaSource.RunTimeTicks`, else player duration), `currentTime` (ms), `getCurrentTicks` (adds `transcodingOffsetTicks`), `getBufferedRanges`, `isPlaying*`, `getPlaybackInfo`, `getPlaybackMediaSources`, `translateItemsForPlayback`, `getItemsForPlayback`.
- Targets: `getTargets`, `setActivePlayer`, `trySetActivePlayer`, `getSupportedCommands`, `sendCommand(cmd)`.

**Events**
- Raised on the manager: `playerchange`, `playbackstart [player,state]`, `playbackstop [{player,state,nextItem,nextMediaType}]`, `playbackcancelled`, `playbackerror [errorType]`, `reportplayback [bool]`, `pairing` / `paired` / `pairerror`.
- Raised on the player: `playbackstart`, `playbackstop`, `fullscreenchange`, `repeatmodechange`, `shufflequeuemodechange`, `playlistitemadd/remove/move`, `mediastreamschange`, `PlayerEvent.PromptSkip`.
- Consumed from local players: `error`, `timeupdate`, `pause`, `unpause`, `volumechange`, `stopped`. Self-managing players emit `itemstarted` / `itemstopped` instead.

**How a player is picked (`getPlayer`)**
- Players are plugins of `PluginType.MediaPlayer`, sorted by `priority` (ascending).
- Candidates are the local players. If a remote player is active, it is used.
- The first player that passes all of these wins: `canPlayMediaType(item.MediaType)`, then `canPlayItem(item, opts)` for server items, or `canPlayUrl(item.Url)` for URL items.
- A player that has its own `getPlaylist` manages its own queue and bypasses the local queue (`enableLocalPlaylistManagement`).

**`play(options)` flow**
1. If the active player is remote, call `player.play(options)` and stop here.
2. Load the items. One id uses `getItem(userId,id)`. Several ids use `/Items` with `fields=[Chapters,MediaSources,Trickplay]`, `excludeLocationTypes=[Virtual]`, `limit` 300 by default.
3. `translateItemsForPlayback` expands the first item by type. Sort is `Random` when `options.shuffle`, else `SortName`. `IsNotFolder` is always added.
   - Program → its channel.
   - Playlist → `parentId`.
   - MusicArtist → `artistIds`, sorted Album / ParentIndexNumber / IndexNumber / SortName, Audio only.
   - MusicGenre, Genre, Studio, Person → filter by that id.
   - PhotoAlbum, or a Photo (siblings, `startIndex` set to the photo).
   - Series / Season: if not shuffling and no single season was picked, call `getNextUpEpisodes` to get a start item. Then `getEpisodes(SeriesId, {IsVirtualUnaired:false, IsMissing:false, SeasonId, limit:100 unless a season is set, startItemId, SortBy Random if shuffle})`. The start index is the first unplayed episode.
   - Single Episode → `getEpisodes(..., startItemId: Id, limit:100)`. This is how series auto-advance works.
   - Folders: `homevideos` → photos, `musicvideos` → video, any other folder → Audio and Video recursive (music folders get the album sort).
4. `getAdditionalParts`: Movies and Episodes with `PartCount>1` are expanded with `getAdditionalVideoParts`, and `startIndex` is re-based.
5. `playWithIntros`:
   - Photo and Book items go straight to their player (`playOther`).
   - Otherwise `getIntros(firstItem.Id)` runs only when all of these hold: no `startPositionTicks`, no `startIndex`, `fullscreen` true, item is a server Video that is not a TvChannel or an in-progress recording, and `userSettings.enableCinemaMode()` (default true).
   - Intros are prepended to the list. Play options are cut down to `{aspectRatio, fullscreen, mediaSourceId, audioStreamIndex, subtitleStreamIndex, startPositionTicks}` and stored on `item.playOptions`.
6. `playInternal`:
   - Reject placeholders.
   - Run the PreplayIntercept plugins in priority order (§9).
   - `detectBitrate`: if `appSettings.enableAutomaticBitrateDetection(IsInNetwork, mediaType)`, run `bitrateTest.detectBitrate` and save the result. Otherwise use the saved value.
7. `playAfterBitrateDetect`:
   - Stop the current player (`onPlaybackChanging` → Stopped report).
   - In parallel: `player.getDeviceProfile(item)`, `getCurrentUser`, `getItem(mediaSourceId||item.Id)`.
   - Choose an alternate version by matching the previous source's `Name`.
   - `autoSetNextTracks` scores the previous track against each new track: +1 same codec, +1 same relative index, +2 same DisplayTitle, +2 same Language (not `und`); the best score ≥3 wins. Only runs if `user.Configuration.RememberAudioSelections` / `RememberSubtitleSelections` are set.
   - Then `getPlaybackMediaSource`, `createStreamInfo`, `player.play(streamInfo)`, `onPlaybackStarted`.
   - Note: if `player.play` rejects, it still reports start and then calls `onPlaybackError` after 100 ms (there's a TODO about this in the code).

**PlaybackInfo request** — SDK `getMediaInfoApi(api).getPostedPlaybackInfo({itemId, playbackInfoDto})` = `POST /Items/{id}/PlaybackInfo`. Body fields:
- Always: `UserId`, `StartTimeTicks` (default 0), `IsPlayback`, `AutoOpenLiveStream` (both true for real playback), `AlwaysBurnInSubtitleWhenTranscoding` (appSetting), `DeviceProfile`.
- Optional: `AudioStreamIndex`, `SubtitleStreamIndex`, `SecondarySubtitleStreamIndex`, `EnableDirectPlay`, `EnableDirectStream`, `AllowVideoStreamCopy`, `AllowAudioStreamCopy`, `MediaSourceId`, `LiveStreamId`, `MaxStreamingBitrate`.
- `EnableMediaProbe=false` if the player's `enableMediaProbe(item)` returns false.
- `EnableDirectStream=false` if the player's `supportsPlayMethod('DirectStream')` returns false.
- `DirectPlayProtocols` if the player provides `getDirectPlayProtocols`.
- If the response has `ErrorCode`, show an alert (`PlaybackErrorNoCompatibleStream` or `PlaybackError.{code}`).

**Audio shortcut:** server Audio items do not call PlaybackInfo unless the player sets `useServerPlaybackInfoForAudio`, and no player in the repo sets it. Instead the client builds a `StreamUrl`: `Audio/{id}/universal?UserId&DeviceId&MaxStreamingBitrate&Container=<audio DirectPlay containers joined, with "|codec">&TranscodingContainer&TranscodingProtocol&AudioCodec&MaxAudioSampleRate&MaxAudioBitDepth&ApiKey&PlaySessionId=<counter>&StartTimeTicks&EnableRedirection=true&EnableRemoteMedia&EnableAudioVbrEncoding`. These values come from the first `Audio/Streaming` TranscodingProfile and the Audio CodecProfile `LessThanEqual` conditions. Queued audio gets the same URL as `item.PresetMediaSource`.

**Media source choice (`getOptimalMediaSource`)**
- For each source, `supportsDirectPlay` is true only when the source has `SupportsDirectPlay` (or is a BluRay/DVD rip), `Protocol=Http`, and no `RequiredHttpHeaders`. If DirectStream or Transcode are also possible, the host must be reachable (`getEndpointInfo().IsInNetwork`, and the path is not localhost unless `IsLocal`). This is the client-side "true direct play" of a raw `Path` URL.
- Order of preference: the item's own source if it is playable at all, then any direct-playable source, then `SupportsDirectStream`, then `SupportsTranscoding`, then the first source.
- If `RequiresOpening && !LiveStreamId`: `POST LiveStreams/Open?UserId&StartTimeTicks&ItemId&PlaySessionId[&MaxStreamingBitrate]` with body `{DeviceProfile, OpenToken}`, then use the returned `MediaSource`.
- If the item has `AlbumId`, fetch the album and store `mediaSource.albumNormalizationGain`.

**Stream URL (`createStreamInfo`)**
- `enableDirectPlay` → `mediaSource.Path`, `DirectPlay`.
- `StreamUrl` (audio universal) → that URL; `playMethod` stays `Transcode`.
- `SupportsDirectPlay || SupportsDirectStream` → `Videos|Audio/{itemId}/stream.{container}?Static=true&mediaSourceId&deviceId&ApiKey[&Tag=ETag][&LiveStreamId]`. `playMethod` = `DirectPlay` if `SupportsDirectPlay`, else `DirectStream`.
- `SupportsTranscoding` → `getUrl(mediaSource.TranscodingUrl)`. MIME is `application/x-mpegURL` when `TranscodingSubProtocol==='hls'`. Otherwise it is progressive: `transcodingOffsetTicks = startPosition` unless the URL contains `copytimestamps=true`.
- `playSessionId` is parsed from the URL. `textTracks` = External subtitle streams (`DeliveryUrl`). `backdropUrl` becomes the poster.

**Resume:** the caller passes `startPositionTicks`. It is sent as `StartTimeTicks` and as `streamInfo.playerStartPositionTicks`. The player seeks to it after start (§5).

**Seek and stream changes (`changeStream`)**
- If `canPlayerSeek` and no params: set `player.currentTime(ms)` directly. `canPlayerSeek` is true when the URL contains `.m3u8`, or `player.seekable()` is true, or the method is not Transcode and there is a duration.
- Otherwise (progressive transcode, or audio / subtitle / bitrate change): new PlaybackInfo with the current `MediaSourceId`, `LiveStreamId`, and new `StartTimeTicks`. Then `stopActiveEncodings(playSessionId)` (unverified: `DELETE /Videos/ActiveEncodings?deviceId&playSessionId`), `player.play(newStreamInfo)`, and `stopActiveEncodings` again. `isChangingStream` suppresses the `stopped` handling during the switch.

**Error fallback (`onPlaybackError`)**
- If `mediaSource.SupportsTranscoding` and stream copy is not already disabled for both video and audio: `changeStream(currentTicks, {EnableDirectPlay:false, EnableDirectStream:tryCopy, AllowVideoStreamCopy:tryCopy, AllowAudioStreamCopy: false if copy was already blocked, else null})`. `tryCopy` is true only for `LocationType==='Remote'` sources on the first try (URL does not contain `transcodereasons`).
- The device profile is rebuilt with `isRetry:true`, which removes ASS/SSA/PGS from the subtitle profiles (§4).
- Otherwise: `playbackerror` event, stop, and an alert.

**Live streams:** `TvChannel` / `LiveTvChannel` skip `getItem`. `CanSeek = RunTimeTicks>0 || canPlayerSeek`. On progress, if `liveStreamId` is set and more than 600 s have passed since the last query, call `getLiveStreamMediaInfo(liveStreamId)` (unverified: `LiveStreams/MediaInfo`) and fire `mediastreamschange`.

**Auto-advance (`onPlaybackStopped`):** `nextItem = queue.getNextItemInfo()`, unless there was an error or the user stopped. It plays when `user.Configuration.EnableNextEpisodeAutoPlay` is true or the next item is not Video. If no player can handle the next item, the current one is destroyed.

**Implementation notes for the port**
- `getPlayerData(player)` returns the player object itself, so per-player state (`streamInfo`, `audioStreamIndex`, …) lives on the plugin instance.
- `seekRelative` uses `ticks` before it is declared on the remote-player branch (L1676), so it would throw.
- `sendCommand('SetMaxStreamingBitrate')` passes an int to `setMaxStreamingBitrate`, which expects an options object (L4434).

## 2. Playback reporting

- `reportPlayback` calls `apiClient.reportPlaybackStart`, `reportPlaybackProgress` or `reportPlaybackStopped` with `info = state.PlayState + {ItemId, EventName?, NowPlayingQueue?}`. Unverified: these map to `POST /Sessions/Playing`, `/Sessions/Playing/Progress` and `/Sessions/Playing/Stopped`. Any rate limiting inside the apiclient package can't be checked here.
- **PlayState fields** (`getPlayerState`): `VolumeLevel`, `IsMuted`, `IsPaused`, `RepeatMode`, `ShuffleMode`, `PlaybackOrder` (`Shuffle`/`Default`), `MaxStreamingBitrate`, `PositionTicks` (includes the transcoding offset), `PlaybackStartTimeTicks` (wall clock ×10000 at start), `PlaybackRate`, `SubtitleStreamIndex`, `SecondarySubtitleStreamIndex`, `AudioStreamIndex`, `BufferedRanges[{start,end}]` (ticks), `PlayMethod`, `LiveStreamId`, `PlaySessionId`, `PlaylistItemId`, `MediaSourceId`, `CanSeek`.
- `NowPlayingQueue` = `[{Id, PlaylistItemId, ServerId?}]`. Sent with Start, Stopped and the playlist events.
- **When each is sent**
  - Start: after `player.play` resolves.
  - Progress:
    - `EventName:'timeupdate'` on every player `timeupdate` event, plus a 10 s `setInterval`, plus once after a stream change.
    - `pause`, `unpause`, `volumechange`, `repeatmodechange`, `shufflequeuemodechange`.
    - `playlistitemadd/remove/move`, with the queue.
    - Only sent between `streamInfo.started` and `ended`.
  - Stopped: on the player's `stopped` event, when switching items, and on `beforeunload`. For self-managing players `PositionTicks = positionMs*10000`.
- **Ping:** there are no calls to `/Sessions/Playing/Ping` in the repo. The only "ping" is SyncPlay's `sendSyncPlayPing({Ping})`.
- Non-server items (no `ServerId`) are not reported.
- Capabilities (`utils/dashboard.js` `capabilities()`): `PlayableMediaTypes ['Audio','Video']`, a `SupportedCommands` list, `SupportsMediaControl:true`.

## 3. Play queue — `playqueuemanager.js`

- State: `_playlist`, `_sortedPlaylist` (backup taken when shuffling), `_currentPlaylistItemId`, `_repeatMode` (`RepeatNone|RepeatAll|RepeatOne`), `_shuffleMode` (`Sorted|Shuffle`).
- `PlaylistItemId` = `"playlistItem"+counter`, assigned once per item object.
- `setPlaylist` resets the repeat mode to `RepeatNone` but does not reset shuffle. `reset()` clears everything.
- `getNextItemInfo`:
  - `RepeatOne` → the same index. This also applies to a manual `nextTrack`.
  - `RepeatAll` → wraps to 0.
  - Otherwise index+1, or null at the end.
- `previousTrack` = index−1 with `startPositionTicks=0`. No wrap.
- Shuffle:
  - Copy to `_sortedPlaylist`, remove the current item, Fisher–Yates using `randomInt(0, i-1)` (a Sattolo-style variant that never leaves an element in place), then put the current item back at index 0.
  - Un-shuffle restores the sorted copy.
- `queueNext` inserts after the current item. `removeFromPlaylist` returns `'empty'` if removing everything. `clearPlaylist(keepCurrent)`.

## 4. Device profile — `src/scripts/browserDeviceProfile.js`

**Wrapping (`components/apphost.js` `getDeviceProfile`)**
- `getBaseProfileOptions`: when hls.js will be used, `disableHlsVideoAudioCodecs` gets `ac3`/`eac3` (except Edge Chromium) and `opus` (except Chrome, Edge Chromium and Firefox).
- After the profile is built:
  - `appSettings.maxVideoWidth` adds a `Width` condition to video TranscodingProfiles. It is also added as a CodecProfile when `limitSupportedVideoResolution` is on.
  - `preferredTranscodeVideoCodec` and `preferredTranscodeVideoAudioCodec` move the chosen codec to the front of the transcode codec lists.
- `window.NativeShell` can replace the whole profile.

**Top level**
- `MaxStreamingBitrate:120000000`, `MaxStaticBitrate:100000000`, `MusicStreamingTranscodingBitrate:384000`.
- `ResponseProfiles:[{Type:'Video',Container:'m4v',MimeType:'video/mp4'}]`.

**Probes**
- All capability checks use `canPlayType` on a `<video>` or `<audio>` element. There are no `MediaSource.isTypeSupported` calls. MSE is checked only as `window.MediaSource != null` (`canPlayHlsWithMSE`).
- Probe strings:
  - H.264: `avc1.42E01E, mp4a.40.2`. Level 5.1 via `avc1.640833`, level 5.2 via `avc1.640834`, High 10 via `avc1.6e0033` (not Safari or mobile).
  - HEVC: `hvc1/hev1.1.L120` (4.0). Main10 at levels 123/153/183/186 via `hvc1.2.4.L1xx`.
  - AV1: `av01.0.15M.08` and `.10`, up to `19M` (level 6.3).
  - VP8 / VP9: `video/webm; codecs=...`.
  - Audio: AC-3 `audio/mp4; codecs="ac-3"`, E-AC-3 `ec-3`, DTS `dts-`/`dts+`, HE-AAC `mp4a.40.5`, MP3-in-MP4 `mp4a.69|6B|mp3`, AC-3-in-HLS / MP3-in-HLS via `application/x-mpegurl; codecs=...`.
  - Dolby Vision: `dvh1.05.06`, `dvh1.08.06`, `dav1.10.06`.
  - Audio containers: `audio/<fmt>`; opus via `audio/ogg; codecs="opus"`; Safari opus via `audio/x-caf`.
- `getPhysicalAudioChannels`: `userSettings.allowedAudioChannels` if >0. Otherwise, if AC-3/E-AC-3 is supported on a "surround browser" (Chrome, Firefox, Safari, Edge Chromium), max(6, `AudioContext.destination.maxChannelCount`). Otherwise speaker count or 2.

**Structure**
- **DirectPlayProfiles (Video)**
  - `webm`: vp8, vp9, av1 + vorbis/opus.
  - `mp4,m4v`: h264, hevc, vp9, av1 + `videoAudioCodecs`.
  - `mkv`: same codec lists, if MKV is playable.
  - `mov`: h264 (Chrome, Safari, Edge Chromium).
  - `hls`: the fMP4 codec set and the TS codec set.
- **DirectPlayProfiles (Audio):** per playable format in `opus, mp3, mp2, aac, flac, alac, webma, wma, wav, ogg, oga`. Extras: `ts|mp3`, `webm|opus`, `m4a|aac/alac`, `m4b|aac/alac`, `mp4|flac` when `alwaysRemuxFlac`.
- **TranscodingProfiles**
  - Audio HLS: `{Container: mp4 or ts by enableFmp4Hls, AudioCodec:'aac', Protocol:'hls', Context:'Streaming', MaxAudioChannels, MinSegments: 2 on iOS/macOS else 1, BreakOnNonKeyFrames, EnableAudioVbrEncoding}`.
  - Audio over http: aac, mp3, opus, wav (Streaming). opus, mp3, aac, wav (Static).
  - Video HLS fMP4 (`Container:'mp4'`), only if `userSettings.preferFmp4HlsContainer()`. Defaults on for Safari, Firefox, Chrome and Edge Chromium. Forced off on Safari without native fMP4 HLS, and on Firefox 149.
  - Video HLS TS (`Container:'ts'`). `SegmentLength:1` when `limitSegmentLength` is on. `BreakOnNonKeyFrames` = iOS, macOS, legacy Edge, or no native HLS.
- **ContainerProfiles:** only for Tizen < 6.5 (`NumStreams<=32`).
- **CodecProfiles**
  - h264: `VideoProfile EqualsAny high|main|baseline|constrained baseline[|high 10]`, `VideoRangeType SDR`, `VideoLevel <= 42/51/52`, `IsInterlaced!=true`, optional `IsAnamorphic`, `Width`, `VideoBitrate`.
  - hevc: profile `main` or `main|main 10`, level, `VideoRangeType`. Safari adds `VideoCodecTag hvc1|dvh1` and `VideoFramerate<=60`.
  - vp9: range only. av1: `main`, range, level.
  - VideoAudio `aac`: `AudioProfile!=HE-AAC` if unsupported, `IsSecondaryAudio=false` if there is no `audioTracks` API (always for Firefox).
  - `AudioChannels<=N` when the user forces a channel count.
  - Safari opus: ≤2 channels, with the transcode profiles split accordingly.
- **Video range types**
  - HDR10, HDR10+ and HLG: Safari on macOS/iOS 11+, desktop Chrome, Edge Chromium 121+, Firefox 100+ on macOS.
  - Dolby Vision: Safari only (profiles 5 and 8, plus AV1 DV).
- **SubtitleProfiles** (`appSettings.get('subtitleburnin')`, `subtitlerenderpgs`)
  - `vtt External` when `textTracks` exists.
  - `ass` and `ssa External` unless `isRetry` or burn-in is `allcomplexformats`.
  - `pgssub` and `vobsub (Container mks) External` only when `subtitlerenderpgs==='true'`, a 2D canvas exists, it's not a retry, and burn-in is not `allcomplexformats` or `onlyimageformats`.
  - Burn-in `all` → no subtitle profiles, so everything is burned in by the transcoder.

**Per-browser summary**
- **Chromium (desktop):**
  - MKV direct play when `canPlayType('video/x-matroska')` reports support (always on Windows Edge Chromium).
  - HEVC and AV1 per probe, plus HDR10/HLG.
  - AC-3/E-AC-3 only if the probe passes (and stripped from HLS when hls.js is used, except on Edge Chromium); MP2 and FLAC are allowed.
  - Always uses hls.js, even where native HLS exists (Chromium 141+ note in the code).
- **Firefox:** no MKV direct play (`testCanPlayMkv` returns false). No secondary audio. Native text tracks are disabled for HLS. HEVC in HLS only on 134+. HDR only on macOS.
- **Safari:** native HLS (hls.js only for VP9 on desktop). fMP4 HLS on macOS/iOS 11+. Dolby Vision. ALAC. MP3 only in TS-HLS. WebM VP9/AV1 disabled on Safari 17+. HEVC needs `hvc1`/`dvh1` tags.

## 5. HTML video player — `src/plugins/htmlVideoPlayer/plugin.js`, `src/components/htmlMediaHelper.js`

**Identity:** `id 'htmlvideoplayer'`, name `Html Video Player`, `priority 1`, `canPlayMediaType('video')`. `getDeviceProfile` → `appHost.getDeviceProfile`.

**DOM**
- `div.videoPlayerContainer[dir=ltr]` is inserted as the first child of `body`. It gets `videoPlayerContainer-onTop` (z-index 1000) until the OSD route is shown.
- Inside: `<video class="htmlvideoplayer" preload="metadata" autoplay playsinline webkit-playsinline>`. `controls` is added only when autoplay is unsupported.
- Custom subtitles use `div.videoSubtitles > div.videoSubtitlesInner` and `.videoSecondarySubtitlesInner`. libass adds `.libassjs-canvas-parent`.
- Cue CSS goes into a `<style id="htmlvideoplayer-cuestyle">` using `.htmlvideoplayer::cue`.
- There is a zoom-in animation on open, `body.hide-scroll`, and backdrop transparency.
- The volume is restored from `appSettings 'volume'`.

**`play(options)`**
1. `createMediaElement`, apply the aspect ratio.
2. `updateVideoUrl`: on iOS/macOS, for HLS transcodes with no runtime, prefetch `live.m3u8` instead of `master.m3u8`.
3. `setCurrentSrc`:
   - Destroy any previous hls/flv player.
   - Append `#t=<seconds>`.
   - Set `crossOrigin='anonymous'` unless the source is remote, or `use-credentials` when `config.json includeCorsCredentials` is set.
   - Record the default subtitle index (−1 if `DeliveryMethod==='Encode'`) and the audio index (null when transcoding). These are applied when `playing` fires.
   - Choose the path:
     - hls.js: `enableHlsJsPlayerForCodecs && isHls(mediaSource)`, where `isHls` = `TranscodingSubProtocol` or `Container` is `HLS`.
     - flv.js: FLV container and not transcoding.
     - Otherwise native: `elem.src = url`, then `play()`. `NotAllowedError` and `AbortError` are swallowed.

**hls.js**
- Dynamic `import('hls.js/dist/hls.js')`. Defaults: `lowLatencyMode=false`, `backBufferLength=Infinity`, `liveBackBufferLength=90`.
- Instance config: `{startPosition: ticks/1e7, manifestLoadingTimeOut:20000, maxBufferLength and maxMaxBufferLength: 30, or 6 when Chrome/Firefox and max bitrate >= 25 Mbps, videoPreference:{preferHDR:true}, xhrSetup: withCredentials}`.
- `loadSource`, then `attachMedia`. Resolve on `MANIFEST_PARSED` → `play()`.
- `enableHlsJsPlayer` is true when MSE exists, and the platform is not iOS, Tizen or webOS, and either there is no native HLS, or the browser is Chromium/Opera, or Android, or there is no runtime (live).

**Errors**
- hls.js:
  - Network error with HTTP ≥400 → `SERVER_ERROR`.
  - Fatal network error with code 0 → `NETWORK_ERROR`. Other fatal network errors → `startLoad()`.
  - Fatal media error → `recoverMediaError`; a second one within 3 s → `swapAudioCodec`; a third → `FATAL_HLS_ERROR`.
- `<video>.error` codes: 1 ignored, 2 `NETWORK_ERROR`, 3 `MEDIA_DECODE_ERROR` (or hls recovery), 4 `MEDIA_NOT_SUPPORTED`.
- `ensureValidVideo`: `videoWidth==0` on the first timeupdate → `NO_MEDIA_ERROR`.
- All of these become the player `error` event, and PlaybackManager falls back to transcoding (§1).

**Start and seek**
- On the first `playing` event: `seekOnPlaybackStart` sets `currentTime` once the duration is at least the start position (it waits for durationchange, loadeddata, play or loadedmetadata).
- Then it either navigates to the OSD (`appRouter.showVideoOsd()`) or goes inline.
- `currentTime(ms)` setter is used for native and HLS seeks. Progressive transcodes restart the stream (see `changeStream`, §1).

**Subtitles (`setCurrentTrackElement`)**
- If not true DirectPlay and `alwaysBurnInSubtitleWhenTranscoding`: call `getSessions({deviceId})`. If the session is transcoding video, mark every track `DeliveryMethod='Encode'`.
- Otherwise:
  - `ass`/`ssa` → `@jellyfin/libass-wasm` (SubtitlesOctopus). Settings: `renderMode:'wasm-blend'`, workers at `/libraries/subtitles-octopus-worker(-legacy).js`, fonts from `MediaAttachments` `DeliveryUrl` plus `/FallbackFont/Fonts` (when the encoding config has `EnableFallbackFont`), `timeOffset` = transcode offset.
  - `pgssub` → `libbitsub.PgsRenderer`. `dvdsub`/`vobsub` → `libbitsub.VobSubRenderer` (with deband).
  - Text with custom styling (`useCustomSubtitles`): fetch `<DeliveryUrl with .vtt replaced by .js>` (JSON `TrackEvents`) and render into a div on each `timeupdate`.
  - Native: `addTextTrack('subtitles','manualTrack')`, then add `VTTCue` objects built from the same JSON. `<track src>` is not used.
  - Native rendering is turned off for Firefox+HLS, legacy Edge, webOS and PS4.
- Embedded streams in DirectPlay use the same JSON endpoint.
- Secondary subtitles go into track index 1.
- Offset: debounced 100 ms. Shifts cue times, TrackEvents, or the renderer's `timeOffset`.

**Audio tracks**
- In-player switching only if `canPlaySecondaryAudio` (`elem.audioTracks` exists, not Firefox) and the codec is in the Video DirectPlay profile for the container. It toggles `audioTracks[i].enabled`.
- Otherwise `changeStream({AudioStreamIndex})`.

**Other features**
- Picture-in-picture: `requestPictureInPicture` or `webkitSetPresentationMode`.
- AirPlay: `webkitShowPlaybackTargetPicker`.
- Aspect ratio: `object-fit` `auto|cover|fill`, saved in `appSettings 'aspectRatio'`.
- Playback rate: 0.5–4×.
- Volume: cubic curve, `vol = (v/100)^3`.
- Brightness: CSS `filter: brightness()`.
- `getStats()` reports stream type, player size and video size, dropped frames, and `sinkId`.

## 6. HTML audio player — `src/plugins/htmlAudioPlayer/plugin.js`

- One hidden `<audio class="mediaPlayerAudio hide">` appended to `body` and reused.
- Plays the universal URL from §1:
  - If the URL contains `.m3u8`, or a HEAD request returns an HLS content type, and `enableHlsJsPlayer` → hls.js (`manifestLoadingTimeOut:20000` only).
  - Otherwise native `src`.
- `#t=` start position plus `seekOnPlaybackStart`.
- **Normalisation:** `userSettings.selectAudioNormalization()`, default `TrackGain`.
  - `TrackGain`: `item.NormalizationGain ?? albumNormalizationGain`.
  - `AlbumGain`: the reverse.
  - Gain = `10^(dB/20)` through a Web Audio `GainNode` (`createMediaElementSource`). On Safari it is multiplied by the volume. Skipped on iOS.
  - Sets `navigator.audioSession.type='playback'`.
- **Crossfade / gapless:** neither exists. The only effect is a fade-out on `stop(destroy)`: −0.15 volume every 100 ms (not on TVs).

## 7. Video OSD — `src/apps/legacy/controllers/playback/video/index.{html,js}`, `src/styles/videoosd.scss`

**DOM** (`#videoOsdPage`)
- `.syncPlayContainer #syncPlayIcon`, `.upNextContainer`.
- `.videoOsdBottom.videoOsdBottom-maincontrols` contains:
  - Text: `.osdTitle`, `.osdMediaStatus` ("FetchingData"), `.osdSecondaryMediaInfo`.
  - Position row: `.osdPositionText`, `.sliderMarkerContainer` with `input.osdPositionSlider[is=emby-slider]`, `.osdDurationText`.
  - Buttons: `btnRecord`, `btnPreviousTrack`, `btnPreviousChapter`, `btnRewind`, `btnPause`, `btnFastForward`, `btnNextChapter`, `btnNextTrack`, `.endsAtText`, `btnUserRating`, `btnSubtitles`, `btnAudio`, `.buttonMute` + `.osdVolumeSlider`, `btnVideoOsdSettings`, `btnAirPlay`, `btnPip`, `btnFullscreen`.
- The page header `.skinHeader` gets `osdHeader` / `osdHeader-hidden`.

**Auto-hide**
- Hides after 3 s (`startOsdHideTimer`). The timer restarts on pointer moves over 10 px, keydown (capture), mouse or touch down and up. It pauses while the mouse is held or a dialog is open.
- Hiding uses CSS transitions, then the `hide` class.
- Fires `EventType.SHOW_VIDEO_OSD`, which the skip button listens to.

**Pointer**
- Mouse click: play/pause after 300 ms (a double click cancels it and toggles fullscreen).
- Touch: toggles the OSD.
- Wheel: volume.

**Keys**
- Space or K: play/pause.
- Left/J and Right/L: rewind and fast-forward.
- Up/Down: volume. M: mute. F: fullscreen.
- `,` / `.`: step a frame back/forward. Shift+`,` / Shift+`.`: slower/faster.
- Shift+P / Shift+N: previous/next track.
- Home/End: 0% / 100%. Digits 0–9: seek to that tenth.
- PageUp/PageDown: next/previous chapter.
- G/H: subtitle offset −/+.
- Esc/Back: hide the OSD.
- Separate TV-layout behaviour, and `inputManager` commands (`togglestats`, `record`, …).

**Seek bar**
- `change` → `seekPercent`. For live TV with a time-of-day bar, it uses program start and end.
- `getMarkerInfo` returns chapter markers (`StartPositionTicks/RunTimeTicks`).
- The hover bubble shows trickplay if there is a matching resolution.
  - `item.Trickplay[mediaSourceId][width]`; the chosen width is the largest at or below 20% of the screen width.
  - Tile = `floor(ms/Interval)`; sheet = `tile / (TileWidth*TileHeight)`.
  - Image `Videos/{id}/Trickplay/{Width}/{index}.jpg?ApiKey&MediaSourceId`, positioned with background offsets.
- Without trickplay, the bubble shows a chapter image (`getScaledImageUrl(type:'Chapter', tag, index)`) and the chapter name.

**Time display**
- Updated at most every 700 ms. Shows ends-at time. Clicking the duration toggles remaining vs total (`enableVideoRemainingTime`).

**Skip intro / segments**
- `apps/legacy/features/playback/utils/mediaSegmentManager.ts`:
  - On playback start, if `MediaSource.HasSegments`, fetch `getMediaSegmentApi.getItemSegments({itemId, includeSegmentTypes})`.
  - Per-type action comes from user setting `segmentTypeAction__{Type}` (default `AskToSkip` for Intro and Outro, otherwise None).
  - `Skip` → seek to `EndTicks`, or `nextTrack` when there is no end. Segments under 1 s are ignored.
  - `AskToSkip` (≥3 s) → `promptToSkip`.
- `components/playback/skipsegment.ts`: `.skip-button-container > button.skip-button` appended to `body`. Auto-hides after 8 s. Stays visible while the OSD is open. Suppressed when the segment runs to the end and up-next will appear.

**Up next** (`components/upnextdialog`)
- Episodes only, with `enableNextVideoInfoOverlay` on. Shown 30 s before the end (35 s if runtime ≥40 min, 40 s if ≥50 min), only for runtime ≥10 min and ≥20 s remaining. Also shown when an Outro segment reaches the end.
- Countdown text; "Start now" calls `nextTrack`.

**Settings menu** (`components/playback/playersettingsmenu.js`)
- Items: aspect ratio, playback rate, quality (`qualityOptions.js` ladder 120 Mbps…420 kbps plus Auto → `setMaxStreamingBitrate`), repeat mode, subtitle offset, playback data (stats).
- Audio and subtitle menus: action sheets from `audioTracks()` / `subtitleTracks()`, plus a secondary subtitle submenu.
- Subtitle sync: `components/subtitlesync/subtitlesync.js` (slider plus text field).
- Stats overlay: `components/playerstats/playerstats` (uses `player.getStats`).
- Still-watching: see §9.
- Playback rate is restored from `sessionStorage`.
- When the page hides, playback stops. When playback stops and the next item is not Video, the page navigates back.

## 8. Now playing bar and queue page

**`components/nowPlayingBar/nowPlayingBar.js`**
- DOM: `.nowPlayingBar` → `.nowPlayingBarTop` (`.nowPlayingBarPositionSlider`), `.nowPlayingBarInfoContainer` (`.nowPlayingImage`, `.nowPlayingBarText`), `.nowPlayingBarCenter` (prev, play/pause, stop, next, `.nowPlayingBarCurrentTime`), `.nowPlayingBarRight` (mute, volume slider, AirPlay, `.openLyricsButton`, repeat, shuffle, user-data rating, context menu).
- Hidden for local video playback.
- Previous: if more than 5 s in, or at queue index 0, restart the track; a double click goes to the previous track.
- Repeat cycles All → One → None.
- Clicking the bar opens the queue page.

**`components/remotecontrol/remotecontrol.js`** (route `apps/legacy/controllers/playback/queue`)
- Image, title, slider, repeat, rewind, prev, play/pause, stop, next, ff, shuffle, audio and subtitle pickers, fullscreen, lyrics.
- Remote nav pad, sending `GeneralCommand`s (`MoveUp`, `Select`, `GoHome`, …).
- Send message and send text sections.
- `#playlist` drag-reorderable list → `movePlaylistItem` / `removeFromPlaylist`. Save-as-playlist button.
- Space bar = play/pause.

**Lyrics** (`apps/legacy/controllers/lyrics.js`)
- `getLyricApi(api).getLyrics({itemId})`. Timed when lines have a `Start` (ticks).
- Highlights `findLastIndex(Start <= time)` on `timeupdate`. Clicking a line seeks there.
- The button is shown when `item.HasLyrics`.

## 9. Other players and plugins

- **sessionPlayer:** controls another session.
  - Targets come from `getSessions({ControllableByUserId})`.
  - Play: `sendPlayCommand(sessionId, {ItemIds, PlayCommand: PlayNow|PlayNext|PlayLast|PlayShuffle|PlayInstantMix, StartPositionTicks, MediaSourceId, AudioStreamIndex, SubtitleStreamIndex, StartIndex})`.
  - Playstate: `sendPlayStateCommand(sessionId, stop|nextTrack|previousTrack|seek|Pause|Unpause|PlayPause)`.
  - General: `sendCommand(sessionId, {Name, Arguments})` for Mute, Unmute, ToggleMute, SetVolume, VolumeUp/Down, ToggleFullscreen, PlayTrailers, SetAudioStreamIndex, SetSubtitleStreamIndex, SetRepeatMode, SetShuffleQueue, DisplayContent.
  - Unverified routes: `/Sessions/{id}/Playing`, `/Playing/{cmd}`, `/Command`.
  - State arrives over the websocket `Sessions` subscription (`PeriodicListenerInterval(100,800)`).
- **Inbound remote control** (`scripts/serverNotifications.js`): websocket `Play`, `Playstate` and `GeneralCommand` messages are mapped onto playbackManager.
- **chromecastPlayer:** Cast SDK. Receiver app id = `user.Configuration.CastReceiverId`. Namespace `urn:x-cast:com.connectsdk`. JSON `{command, options, userId, deviceId, accessToken, serverAddress, serverId, maxBitrate (appSettings chromecastBitrate1), subtitleAppearance, subtitleBurnIn}`. The receiver sends back `playbackstart`, `playbackprogress` and `playstatechange`.
- **youtubePlayer:** for remote YouTube trailer URLs (`canPlayUrl`).
- **photoPlayer:** wraps `components/slideshow`.
- **bookPlayer:** `epubjs` loaded from `getLibraryApi.getDownloadUrl` (`/Items/{id}/Download`), with an OSD and table of contents.
- **pdfPlayer:** `pdfjs-dist` from the same download URL.
- **comicsPlayer:** `libarchive.js` to unpack cbz/cbr, displayed with Swiper.
- **Screensavers** (`PluginType.Screensaver`): `backdropScreensaver` (backdrop slideshow, `backdropScreensaverInterval`) and `logoScreensaver`. Selected by `userSettings.screensaver` / `screensaverTime`.
- **PreplayIntercept chain**, run before every item:
  - `playAccessValidation` (priority −2): rejects when `user.Policy.EnableMediaPlayback` is false.
  - `experimentalWarnings`: once-per-week alert for `VideoType` Iso, BluRay or Dvd.
  - `stillWatching` (priority 10): Episodes only. `userSettings.stillWatchingPrompt` = disabled, short (2 items / 1 h), default (3 / 1.5 h), long (5 / 2.5 h), or veryLong (8 / 4 h). Shows a confirm when session time ≥ that duration and (input idle ≥ duration or items played ≥ count).
- **SyncPlay** (`src/plugins/syncPlay/core`)
  - Manager, Controller, PlaybackCore, QueueCore, TimeSyncCore, with player wrappers for the HTML players.
  - Inbound websocket: `SyncPlayCommand` (`{Command: Unpause|Pause|Stop|Seek, When, PositionTicks, PlaylistItemId}`), and `SyncPlayGroupUpdate` types `PlayQueue`, `UserJoined`, `UserLeft`, `GroupJoined`, `GroupLeft`, `NotInGroup`, `GroupUpdate`, `StateUpdate`, `SyncPlayIsDisabled`, `GroupDoesNotExist`, `CreateGroupDenied`, `JoinGroupDenied`, `LibraryAccessDenied`.
  - Outbound: `requestSyncPlay{Pause,Unpause,Seek,Buffering,Ready,NextItem,PreviousItem,SetNewQueue,Queue,SetPlaylistItem,RemoveFromPlaylist,MovePlaylistItem,SetRepeatMode,SetShuffleMode,SetIgnoreWait}` and `sendSyncPlayPing({Ping})`.
  - Time sync: `getServerTime()` NTP-style 4 timestamps, keeps 8 measurements, polls every 1 s for 3 pings then every 60 s, offset = remote − local.
  - Drift correction: SpeedToSync between 60 and 3000 ms of delay (rate change for 1000 ms), SkipToSync above 400 ms. Tunable through `appSettings` with prefix `syncPlay`.
  - The OSD shows the `#syncPlayIcon` state.

## 10. Settings consumed

- **appSettings** (localStorage):
  - `maxbitrate-{mediaType}-{isInNetwork}`: default 1.5 Mbps; Audio on the LAN is forced to 150 Mbps.
  - `enableautobitratebitrate-{mediaType}-{isInNetwork}`: default true.
  - `maxVideoWidth`, `limitSupportedVideoResolution`, `preferredTranscodeVideoCodec`, `preferredTranscodeVideoAudioCodec` (read in apphost).
  - `alwaysBurnInSubtitleWhenTranscoding` (PlaybackInfo body and video player).
  - `enableDts`, `enableTrueHd`, `enableHi10p`, `disableVbrAudio`, `alwaysRemuxFlac`, `alwaysRemuxMp3` (profile).
  - `subtitleburnin` (`all|allcomplexformats|onlyimageformats|…`), `subtitlerenderpgs`.
  - `aspectRatio`, `volume`, `displaymirror`, `chromecastBitrate1`, `syncPlay*`.
- **userSettings** (some synced to server DisplayPreferences):
  - `skipBackLength` (10000 ms), `skipForwardLength` (30000 ms).
  - `enableNextVideoInfoOverlay` (true), `enableVideoRemainingTime`, `enableCinemaMode` (true).
  - `preferFmp4HlsContainer`, `limitSegmentLength`, `allowedAudioChannels` (−1).
  - `selectAudioNormalization` (`TrackGain`), `stillWatchingPrompt`, `segmentTypeAction__*`.
  - `localplayersubtitleappearance3` (subtitle styling, vertical position, `subtitleStyling` Native/Custom).
  - `screensaver*`, `slideshowInterval`.
- **Server user configuration** (read in the client): `RememberAudioSelections`, `RememberSubtitleSelections`, `EnableNextEpisodeAutoPlay`, `CastReceiverId`, `Policy.EnableMediaPlayback`.
- Audio and subtitle language preferences (`AudioLanguagePreference`, `SubtitleMode`, …) are not read on the client. The server applies them when it picks `DefaultAudioStreamIndex` / `DefaultSubtitleStreamIndex` in PlaybackInfo.

---

## Minimum viable slice (modern desktop Chromium, Firefox, Safari)

**Must have**
1. A device-profile builder covering the H.264/HEVC/AV1/VP9 probes, mp4/webm/mkv (not mkv on Firefox), audio formats, both HLS transcode profiles with the fMP4 preference, `vtt External`, and the h264/hevc CodecProfiles.
2. `POST /Items/{id}/PlaybackInfo`, `getOptimalMediaSource`, and the `createStreamInfo` URL rules: static stream, `TranscodingUrl`, and the `Audio/{id}/universal` path.
3. JS interop: create `<video>`/`<audio>`; native `src` vs hls.js (config as in §5); events `timeupdate`, `playing`, `pause`, `play`, `ended`, `error`, `waiting`, `canplay`, `volumechange`; set `currentTime`, `volume`, `muted`, `playbackRate`.
4. Resume via `playerStartPositionTicks` plus seek-on-start. `changeStream` restart for progressive transcodes and for audio/subtitle/bitrate changes, including `stopActiveEncodings`.
5. Error → transcode fallback (`EnableDirectPlay:false`, retry profile).
6. Reporting: Start, Progress (10 s timer plus pause/unpause/seek), Stopped, and the stop on unload.
7. Queue: series/season/episode/album/playlist expansion, repeat and shuffle, auto-advance gated by `EnableNextEpisodeAutoPlay`.
8. Subtitles: External VTT via `addTextTrack`/VTTCue from the `.js` JSON endpoint, plus server burn-in for everything else.
9. A minimal OSD: play/pause, seek bar, time, volume, fullscreen, audio and subtitle pickers, quality menu, 3 s auto-hide, core keys. A now-playing bar for music.

**Can come later**
- libass ASS rendering, PGS/VobSub (libbitsub), secondary subtitles, subtitle offset.
- In-element audio track switching (Chromium only).
- Trickplay, chapter markers and images.
- Media segments and the skip button, up-next, still-watching, cinema-mode intros.
- Auto bitrate test, normalisation gain, live TV (`LiveStreams/Open`, media info refresh, time-of-day bar), multi-part items, alternate-version matching, track auto-carryover scoring.
- Picture-in-picture, AirPlay, brightness, aspect ratio, stats overlay, lyrics.
- sessionPlayer, Chromecast, SyncPlay.
- Book, PDF, comics and photo players, screensavers, the flv.js path, and the TV/console profile branches.
