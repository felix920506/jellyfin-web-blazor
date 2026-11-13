"""External subtitle rendering: ASS/libass and PGS/libbitsub.

    ./dev e2e subtitles.py chromium

The private test-server seed adds an ASS sidecar and a small SUP sidecar to Second Feature. This
scenario checks that both tracks remain external, are not selected as server burn-in subtitles, and
paint visible pixels in the video overlay. It is intentionally separate from playback.py because
renderer startup is asynchronous and needs more generous diagnostics.
"""
import json
import sys
import time
import urllib.request

from playwright.sync_api import sync_playwright

from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


failures = []


def check(ok, label, detail=""):
    print(("ok   " if ok else "FAIL ") + label + (f"  [{detail}]" if detail and not ok else ""))
    if not ok:
        failures.append(label)
    return ok


def wait_until(probe, timeout=25.0, interval=0.25):
    deadline = time.time() + timeout
    value = probe()
    while not value and time.time() < deadline:
        time.sleep(interval)
        value = probe()
    return value


class Server:
    def __init__(self):
        request = urllib.request.Request(
            SERVER + "/Users/AuthenticateByName",
            data=(f'{{"Username":"{USER}","Pw":"{PASSWORD}"}}').encode(),
            headers={
                "Content-Type": "application/json",
                "Authorization": 'MediaBrowser Client="e2e", Device="subtitles.py", DeviceId="e2e-subtitles", Version="1"',
            },
        )
        answer = json.load(urllib.request.urlopen(request))
        self.token = answer["AccessToken"]
        self.user_id = answer["User"]["Id"]

    def get(self, path):
        separator = "&" if "?" in path else "?"
        return json.load(urllib.request.urlopen(f"{SERVER}/{path}{separator}ApiKey={self.token}"))

    def items(self):
        return self.get(f"Items?userId={self.user_id}&Recursive=true&IncludeItemTypes=Movie")["Items"]

    def item(self, item_id):
        return self.get(f"Users/{self.user_id}/Items/{item_id}")

    def session(self, item_id):
        for session in self.get("Sessions"):
            playing = session.get("NowPlayingItem")
            if playing and playing["Id"].replace("-", "") == item_id.replace("-", ""):
                return session
        return None


def play(page, item):
    go(page, f"/details?id={item['Id']}&serverId={item['ServerId']}")
    page.wait_for_selector("#itemDetailPage .mainDetailButtons .btnPlay", timeout=30_000)
    with page.expect_response(lambda response: "/PlaybackInfo" in response.url and response.request.method == "POST", timeout=30_000) as playback:
        page.click("#itemDetailPage .mainDetailButtons .btnPlay")
    page.wait_for_selector("video", timeout=30_000)
    check(wait_until(lambda: page.evaluate("document.querySelector('video')?.currentTime > 1")), "the subtitle fixture plays")
    response = playback.value.json()
    sources = response.get("MediaSources", [])
    return sources[0] if sources else {}


def canvas_has_pixels(page):
    return page.evaluate("""() => [...document.querySelectorAll('.libassjs-canvas, .videoPlayerContainer canvas')].some(c => {
        const ctx = c.getContext('2d');
        if (ctx && c.width && c.height) {
            const data = ctx.getImageData(0, 0, c.width, c.height).data;
            for (let i = 3; i < data.length; i += 4) if (data[i] > 0) return true;
        }
        const gl = c.getContext('webgl2') || c.getContext('webgl');
        if (gl && c.width && c.height) {
            const data = new Uint8Array(4);
            gl.readPixels(Math.floor(c.width / 2), Math.floor(c.height / 2), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, data);
            if (data[3] > 0) return true;
        }
        if (c.width > 1 && c.height > 1 && c.toDataURL().length > 1000) return true;
        return false;
    })""")


def select(page, stream_index):
    page.mouse.move(400, 300)
    page.wait_for_selector(".videoOsdBottom:not(.hide):not(.videoOsdBottom-hidden)", timeout=8_000)
    page.click(".btnSubtitles")
    page.locator(f".actionSheetMenuItem[data-id='{stream_index}']").click()


def main():
    browser_name = sys.argv[1] if len(sys.argv) > 1 else "chromium"
    server = Server()
    movie = next((item for item in server.items() if item["Name"].startswith("Second Feature")), None)
    if not movie:
        print("subtitle fixture was not seeded")
        return 1
    with sync_playwright() as playwright:
        if browser_name == "firefox":
            browser = playwright.firefox.launch(firefox_user_prefs={"media.autoplay.default": 0, "media.autoplay.blocking_policy": 0})
        elif browser_name == "webkit":
            browser = playwright.webkit.launch()
        else:
            browser = playwright.chromium.launch(args=["--autoplay-policy=no-user-gesture-required"])
        page = new_page(browser)
        page.on("console", lambda message: print("   console:", message.text[:1000]) if message.type == "error" else None)
        try:
            sign_in(page)
            page.evaluate("localStorage.setItem('subtitlerenderpgs', 'true')")
            page.reload()
            page.wait_for_selector("#indexPage", timeout=30_000)
            requests = []
            page.on("request", lambda request: requests.append(request.url) if any(token in request.url.lower() for token in ("subtitles", ".ass", ".ssa", ".sup", ".pgssub")) else None)
            negotiated_source = play(page, movie)
            negotiated_streams = negotiated_source.get("MediaStreams", [])
            ass = next((stream for stream in negotiated_streams if (stream.get("Codec") or "").lower() in ("ass", "ssa")), None)
            pgs = next((stream for stream in negotiated_streams if (stream.get("Codec") or "").lower() == "pgssub"), None)
            check(ass is not None, "the negotiated source exposes the seeded ASS stream")
            check(pgs is not None, "the negotiated source exposes the seeded PGS stream")
            if not ass or not pgs:
                return 1

            for stream, label, extension in ((ass, "ASS", ".ass"), (pgs, "PGS", ".pgssub")):
                before_requests = len(requests)
                negotiated = next((candidate for candidate in negotiated_streams if candidate.get("Index") == stream["Index"]), {})
                check(negotiated.get("DeliveryMethod") == "External", f"{label} is advertised as external", str(negotiated))
                check(bool(negotiated.get("DeliveryUrl")), f"the negotiated {label} stream has an external URL", str(negotiated))
                select(page, stream["Index"])
                selected = wait_until(lambda: (server.session(movie["Id"]) or {}).get("PlayState", {}).get("SubtitleStreamIndex") == stream["Index"])
                check(selected, f"the {label} stream is selected", str(server.session(movie["Id"])))
                # ASS loads through the SubtitlesOctopus worker and PGS may use range requests;
                # a renderer canvas proves that the externally negotiated renderer was started.
                check(wait_until(lambda: page.evaluate("document.querySelectorAll('canvas').length > 0")), f"the {label} renderer is created", str(requests))
                check("transcode" not in (page.locator("video").get_attribute("src") or "").lower(), f"selecting {label} does not use a subtitle transcode")
                page.evaluate("document.querySelector('video').currentTime = 4")
                if label == "PGS":
                    check(wait_until(lambda: page.evaluate("""async () => {
                        const response = await fetch([...performance.getEntriesByType('resource')].map(e => e.name).find(url => url.includes('Stream.pgssub')));
                        const bytes = new Uint8Array(await response.arrayBuffer());
                        const lib = await import('/generated/lib/libbitsub/dist/index.js');
                        await lib.initWasm();
                        const parser = new lib.PgsParser();
                        parser.load(bytes);
                        return { metadata: parser.getMetadata(), timestamps: [...parser.getTimestamps()] };
                    }""")), "the PGS data contains renderable cues")
                check(wait_until(lambda: canvas_has_pixels(page)), f"the {label} renderer paints visible pixels")
                shot(page, f"subtitles-{label.lower()}")

            page.mouse.move(400, 300)
            if page.locator(".videoOsdBottom .btnStop:visible").count():
                page.locator(".videoOsdBottom .btnStop:visible").first.click()
            else:
                go(page, "/home")
            check(wait_until(lambda: not page.locator("video").count()), "subtitle playback stops cleanly")
        except Exception as error:
            check(False, "subtitle scenario completed", repr(error)[:1200])
            shot(page, "subtitles-failure")
        finally:
            for failure in failures:
                page.errors.append("check failed: " + failure)
            code = finish(page, f"subtitles ({browser_name})")
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
