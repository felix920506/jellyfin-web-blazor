# 16. Managed playback players

The remaining non-HTML-media playback plugins are implemented under `Services/Players` and share
the existing `IMediaPlayer`/`PlaybackManager` lifecycle. `players.js` owns the DOM because EPUB.js,
PDF.js, libarchive.js and Swiper are browser libraries; the C# adapters provide capability checks,
download/image URLs, queue selection, synchronous playback state and progress reporting.

## Ported players

| Player | Selection | Browser implementation |
| --- | --- | --- |
| Photo slideshow | `MediaType=Photo` | Original slideshow classes and controls, with queue start index |
| EPUB | `.epub` book | EPUB.js paginated rendition, resume location, theme/font controls |
| PDF | `.pdf` book | PDF.js canvas renderer, page navigation and resume page |
| Comics | `.cbz`, `.cbr`, `.cbt`, `.cb7` book | libarchive.js image extraction and Swiper-style page navigation |
| Plain URL | HTTP(S) audio/video item without a server id | Native audio/video element |
| YouTube | YouTube URL without a server id | YouTube IFrame API; intentionally not used for server items |

Remote trailers are converted to URL-like playback items, so YouTube and other HTTP trailer links use
the same normal playback routing. Server-backed photos and books bypass `PlaybackInfo`; their download
or image URL is supplied directly to the managed player.

## Assets and compatibility

`scripts/prepare-assets.sh` vendors the pinned versions used by the original client: epubjs 0.3.93,
pdfjs-dist 3.11.174, libarchive.js 2.0.2 and swiper 12.2.0. The JS adapter imports them from
`wwwroot/generated/lib` and does not change the original `src/` client.

The bUnit/unit coverage in `tests/Jellyfin.Web.Tests/Players` verifies capability matching, queue
start index/position forwarding and stop events. `scripts/seed-players.sh` creates deterministic
photo, EPUB, PDF and CBZ fixtures and adds Photos/Books libraries to the throwaway test server.
Run the browser coverage with:

```sh
JELLYFIN_INSTANCE=players JELLYFIN_PORT=18105 ./scripts/test-server.sh up
JELLYFIN_URL=http://localhost:18105 ./dev e2e players.py
```

The private e2e intentionally does not contact YouTube. YouTube API loading and URL parsing are
covered by the adapter/unit boundary, while a real YouTube browser check remains environment- and
network-dependent.
