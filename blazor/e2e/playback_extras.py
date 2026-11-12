"""Two real devices: session targeting, remote playback/state, navigation, messages and text.

Uses only the public UI to select a target and start/control playback; no PlaybackTestHook.
    ./dev e2e playback_extras.py
"""
import json
import sys
import urllib.request

from playwright.sync_api import sync_playwright
from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


def media(page):
    return page.evaluate("""() => {
        const audio = document.querySelector('audio');
        return audio ? {time: audio.currentTime, paused: audio.paused, volume: audio.volume, muted: audio.muted} : null;
    }""")


def main():
    request = urllib.request.Request(SERVER + "/Users/AuthenticateByName",
        data=json.dumps({"Username": USER, "Pw": PASSWORD}).encode(),
        headers={"Content-Type": "application/json", "Authorization":
                 'MediaBrowser Client="e2e", Device="playback-extras", DeviceId="e2e-playback-extras", Version="1"'})
    auth = json.load(urllib.request.urlopen(request))
    token, user_id = auth["AccessToken"], auth["User"]["Id"]
    songs = json.load(urllib.request.urlopen(
        f"{SERVER}/Items?UserId={user_id}&Recursive=true&IncludeItemTypes=Audio&ApiKey={token}"))["Items"]
    song = next(s for s in songs if s.get("Album") == "First Album")
    with sync_playwright() as p:
        browser = p.chromium.launch(args=["--autoplay-policy=no-user-gesture-required"])
        controller = new_page(browser)
        target = new_page(browser)
        try:
            sign_in(target)
            sign_in(controller)
            controller.click(".jf-remote-play-button")
            controller.wait_for_selector(".jf-remote-play-target", timeout=15000)
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
            return max(finish(controller, "remote controller"), finish(target, "remote target"))
        finally:
            browser.close()


if __name__ == "__main__":
    sys.exit(main())
