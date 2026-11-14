"""Two real devices: session targeting, remote playback/state, navigation, messages and text.

Uses only the public UI to select a target and start/control playback.
    ./dev e2e playback_extras.py
"""
import json
import sys
import urllib.request
import urllib.error
import urllib.parse

from playwright.sync_api import sync_playwright
from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


def media(page):
    return page.evaluate("""() => {
        const audio = document.querySelector('audio');
        return audio ? {time: audio.currentTime, paused: audio.paused, volume: audio.volume, muted: audio.muted} : null;
    }""")


def lyrics_scenario(page, token, user_id, song):
    """Temporarily add synced song lyrics, then restore the original (or absence)."""
    url = f"{SERVER}/Audio/{song['Id']}/Lyrics?ApiKey={token}"
    try:
        original = json.load(urllib.request.urlopen(url))
    except urllib.error.HTTPError as error:
        if error.code != 404:
            raise
        original = None
    original_lines = (original or {}).get("Lyrics", [])
    restore = "\n".join(
        (f"[{int(line['Start'] // 600000000):02d}:{(line['Start'] / 10000000) % 60:06.3f}]" if "Start" in line else "")
        + line.get("Text", "") for line in original_lines)
    test_lyrics = "\n".join(f"[00:{i:02d}.00]Line {i:02d}: playback lyrics" for i in range(30))

    def upload(text):
        filename = song["Path"].rsplit("/", 1)[-1].rsplit(".", 1)[0] + ".lrc"
        request = urllib.request.Request(url + "&fileName=" + urllib.parse.quote(filename), data=text.encode(),
                                         headers={"Content-Type": "text/plain"}, method="POST")
        urllib.request.urlopen(request).close()

    try:
        upload(test_lyrics)
        go(page, f"/details?id={song['Id']}")
        page.wait_for_selector(".btnPlay:not(.hide)")
        page.locator(".btnPlay:not(.hide)").first.click()
        page.wait_for_function("document.querySelector('audio')?.currentTime > 0.5")
        go(page, "/queue")
        page.wait_for_selector(".btnLyrics:not(.hide)")
        page.click(".btnLyrics")
        page.wait_for_selector("#lyricPage .dynamicLyric")
        page.wait_for_function("document.querySelectorAll('.pastLyric').length > 0")
        page.locator("#lyricPosition12").click()
        page.wait_for_function("document.querySelector('audio')?.currentTime >= 12")
        page.wait_for_function("document.activeElement?.id === 'lyricPosition12' || document.activeElement?.id === 'lyricPosition13'")
        shot(page, "playback-extras-lyrics-desktop")
        page.set_viewport_size({"width": 400, "height": 800})
        page.wait_for_timeout(500)
        shot(page, "playback-extras-lyrics-phone")
        page.mouse.wheel(0, -500)
        page.wait_for_timeout(400)
        # Seek using keyboard activation; manual scrolling is re-enabled by an explicit seek.
        page.locator("#lyricPosition5").focus()
        page.keyboard.press("Enter")
        page.wait_for_function("document.querySelector('audio')?.currentTime >= 5 && document.querySelector('audio')?.currentTime < 10")
        print("ok   synced lyrics, autoscroll, click and keyboard seek, phone layout")
        page.set_viewport_size({"width": 1366, "height": 768})
        go(page, "/queue")
        page.wait_for_selector(".btnLyrics:not(.hide)")
        page.click(".btnLyrics")
        page.wait_for_selector("#lyricPage")
        go(page, "/queue")
        page.wait_for_selector(".btnStop:visible")
        page.click(".btnStop")
        page.wait_for_function("!document.querySelector('audio')?.getAttribute('src')")
    finally:
        if original_lines:
            upload(restore)
        else:
            urllib.request.urlopen(urllib.request.Request(url, method="DELETE")).close()


def main():
    request = urllib.request.Request(SERVER + "/Users/AuthenticateByName",
        data=json.dumps({"Username": USER, "Pw": PASSWORD}).encode(),
        headers={"Content-Type": "application/json", "Authorization":
                 'MediaBrowser Client="e2e", Device="playback-extras", DeviceId="e2e-playback-extras", Version="1"'})
    auth = json.load(urllib.request.urlopen(request))
    token, user_id = auth["AccessToken"], auth["User"]["Id"]
    songs = json.load(urllib.request.urlopen(
        f"{SERVER}/Items?UserId={user_id}&Recursive=true&IncludeItemTypes=Audio&Fields=Path&ApiKey={token}"))["Items"]
    song = next(s for s in songs if s.get("Album") == "First Album")
    saved_data = {s["Id"]: json.load(urllib.request.urlopen(
        f"{SERVER}/Users/{user_id}/Items/{s['Id']}?ApiKey={token}"))["UserData"] for s in songs}
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--autoplay-policy=no-user-gesture-required"])
        controller = new_page(browser)
        target = new_page(browser)
        try:
            sign_in(target)
            sign_in(controller)
            # Google Cast: without a usable sender SDK the menu keeps the original's "unsupported"
            # entry next to the session targets, and nothing fails while it is loaded or skipped.
            cast = controller.evaluate("""() => ({
                chrome: !!window.chrome,
                script: !!document.querySelector('script[src*="cast_sender"]'),
                available: !!window.chrome?.cast?.isAvailable
            })""")
            print("info google cast in this browser:", cast)
            controller.click(".jf-remote-play-button")
            controller.wait_for_selector(".jf-remote-play-target", timeout=15000)
            if not cast["available"]:
                assert controller.locator(".jf-remote-play-nocast").is_visible(), "the unsupported Google Cast entry is missing"
                assert "Google Cast Unsupported" in controller.locator(".jf-remote-play-nocast").inner_text()
                assert controller.locator(".jf-remote-play-target[data-id='Google Cast']").count() == 0
            shot(controller, "playback-extras-cast-menu")
            print("ok   the cast menu opens; Google Cast entry matches the SDK's availability")
            controller.locator(".jf-remote-play-target").first.click()
            controller.wait_for_selector(".jf-remote-play-button-active")
            print("ok   another browser context is selected as target")
            go(controller, f"/details?id={song['Id']}")
            controller.wait_for_selector(".btnPlay:not(.hide)", timeout=20000)
            controller.locator(".btnPlay:not(.hide)").first.click()
            target.wait_for_selector("audio", state="attached", timeout=30000)
            target.wait_for_function("document.querySelector('audio')?.currentTime > 1", timeout=30000)
            assert media(controller) is None, "the controller must not play locally"
            print("ok   the detail page plays on the selected device only")
            go(controller, "/queue")
            controller.wait_for_selector("#nowPlayingPage .btnPlayPause:not(.hide)", timeout=20000)
            controller.click(".btnPlayPause")
            target.wait_for_function("document.querySelector('audio')?.paused === true")
            controller.wait_for_function("document.querySelector('.btnPlayPause .play_circle_filled') !== null")
            print("ok   remote pause and websocket state agree")
            controller.locator(".nowPlayingVolumeSlider:visible").fill("35")
            target.wait_for_function("document.querySelector('audio')?.volume < 0.4")
            controller.click(".buttonMute:visible")
            target.wait_for_function("document.querySelector('audio')?.muted === true")
            print("ok   remote volume and mute")
            shot(controller, "playback-extras-remote-desktop")
            # All remote-only sections retain the original collapsible markup.
            controller.locator(".navigationSection .emby-collapsible-button").click()
            controller.click(".btnShowSearch")
            target.wait_for_function("location.hash.startsWith('#/search')")
            search = target.locator("input[type=search], input[type=text]").first
            search.focus()
            controller.locator(".sendTextSection .emby-collapsible-button").click()
            controller.fill("#txtTypeText", "remote typing")
            controller.locator(".typeTextForm button[type=submit]").click()
            target.wait_for_function("Array.from(document.querySelectorAll('input')).some(i => i.value === 'remote typing')")
            print("ok   remote navigation and text input")
            controller.locator(".sendMessageSection .emby-collapsible-button").click()
            controller.fill("#txtMessageTitle", "Remote greeting")
            controller.fill("#txtMessageText", "Dinner is ready")
            controller.locator(".sendMessageForm button[type=submit]").click()
            target.wait_for_selector(".dialogContainer", timeout=10000)
            assert "Dinner is ready" in target.locator(".dialogContainer").inner_text()
            shot(target, "playback-extras-message-target")
            target.keyboard.press("Escape")
            print("ok   message arrives on the other device")
            controller.set_viewport_size({"width": 400, "height": 800})
            controller.wait_for_timeout(700)
            shot(controller, "playback-extras-remote-phone")
            controller.set_viewport_size({"width": 1366, "height": 768})
            controller.wait_for_timeout(400)
            controller.click(".btnStop")
            target.wait_for_function("!document.querySelector('audio')?.getAttribute('src')", timeout=15000)
            controller.click(".jf-remote-play-button-active")
            controller.click(".jf-remote-play-disconnect")
            controller.wait_for_selector(".jf-remote-play-button:not(.jf-remote-play-button-active)")
            print("ok   stop and disconnect")
            lyrics_scenario(controller, token, user_id, song)
            return max(finish(controller, "remote controller"), finish(target, "remote target"))
        finally:
            browser.close()
            for item_id, data in saved_data.items():
                body = {key: data.get(key, False if key in ("Played", "IsFavorite") else 0)
                        for key in ("Played", "IsFavorite", "PlaybackPositionTicks", "PlayCount")}
                if data.get("LastPlayedDate"):
                    body["LastPlayedDate"] = data["LastPlayedDate"]
                request = urllib.request.Request(
                    f"{SERVER}/UserItems/{item_id}/UserData?UserId={user_id}&ApiKey={token}",
                    data=json.dumps(body).encode(), headers={"Content-Type": "application/json"}, method="POST")
                urllib.request.urlopen(request).close()


if __name__ == "__main__":
    sys.exit(main())
