"""Two-device SyncPlay flow: create, join, local playback commands, and leave.

Run with: ./dev e2e syncplay.py
"""
import json
import sys
import time
import urllib.request

from playwright.sync_api import sync_playwright
from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


def api(method, path, token, body=None):
    separator = "&" if "?" in path else "?"
    url = SERVER + path + separator + "ApiKey=" + token
    auth = ('MediaBrowser Client="e2e", Device="syncplay", DeviceId="e2e-syncplay", Version="1"'
            + ((", Token=\"" + token + "\"") if token else ""))
    request = urllib.request.Request(
        url,
        data=None if body is None else json.dumps(body).encode(),
        headers={"Content-Type": "application/json", "Authorization": auth},
        method=method,
    )
    with urllib.request.urlopen(request) as response:
        payload = response.read()
        return json.loads(payload) if payload else None


def main():
    auth = api("POST", "/Users/AuthenticateByName", "", {"Username": USER, "Pw": PASSWORD})
    token, user = auth["AccessToken"], auth["User"]
    policy_url = f"/Users/{user['Id']}/Policy"
    original_policy = api("GET", f"/Users/{user['Id']}", token)["Policy"]
    movie = next(item for item in api("GET", f"/Items?UserId={user['Id']}&Recursive=true&IncludeItemTypes=Movie", token)["Items"] if item["Name"].startswith("Big Test Movie"))
    try:
        if original_policy.get("SyncPlayAccess") != "CreateAndJoinGroups":
            api("POST", policy_url, token, {**original_policy, "SyncPlayAccess": "CreateAndJoinGroups"})
        with sync_playwright() as playwright:
            browser = playwright.chromium.launch(args=["--autoplay-policy=no-user-gesture-required"])
            host, guest = new_page(browser), new_page(browser)
            try:
                sign_in(host)
                sign_in(guest)
                host.click(".jf-syncplay-button")
                host.wait_for_selector(".jf-syncplay-menu")
                shot(host, "syncplay-create-menu")
                create_item = host.locator(".jf-syncplay-menu .mud-menu-item").filter(has_text="New")
                assert create_item.count() == 1, "the create-group action must be present"
                existing_group_ids = {group["GroupId"] for group in api("GET", "/SyncPlay/List", token)}
                create_item.click()
                host_group = None
                for _ in range(30):
                    host_group = next(
                        (group for group in api("GET", "/SyncPlay/List", token) if group["GroupId"] not in existing_group_ids),
                        None,
                    )
                    if host_group is not None:
                        break
                    time.sleep(1)
                assert host_group is not None, "the newly created host group must appear in the group list"
                guest.click(".jf-syncplay-button")
                guest.wait_for_selector(".jf-syncplay-group", timeout=30000)
                guest_group = guest.locator(f'.jf-syncplay-group[data-id="{host_group["GroupId"]}"]')
                assert guest_group.count() == 1, "the newly created host group must be listed"
                guest_group.click()
                guest.wait_for_function("document.querySelector('.jf-syncplay-button')?.classList.contains('jf-syncplay-active')", timeout=30000)
                print("ok   group creation and join work on the second device")
                go(host, "/details?id=" + movie["Id"])
                host.wait_for_selector(".btnPlay:not(.hide)", timeout=20000)
                host.locator(".btnPlay:not(.hide)").first.click()
                host.wait_for_selector("video", state="attached", timeout=30000)
                host.wait_for_function("document.querySelector('video')?.currentTime > 0", timeout=30000)
                shot(host, "syncplay-host-playing")
                guest.wait_for_selector("video", state="attached", timeout=30000)
                guest.wait_for_function("document.querySelector('video')?.currentTime > 0", timeout=30000)
                shot(guest, "syncplay-guest-playing")
                host.wait_for_timeout(3000)
                host.locator(".btnPause").dispatch_event("click")
                guest.wait_for_function("document.querySelector('video')?.paused === true", timeout=15000)
                print("ok   pause command follows to the other device")
                go(host, "/home")
                host.wait_for_selector(".jf-syncplay-button")
                host.click(".jf-syncplay-button")
                host.click(".jf-syncplay-menu .mud-menu-item:has-text('Leave')")
                guest.wait_for_function("!document.querySelector('.jf-syncplay-button')?.classList.contains('jf-syncplay-active')", timeout=15000)
                print("ok   leaving disables SyncPlay")
                for page in (host, guest):
                    page.errors[:] = [error for error in page.errors if "GetServerTime" not in error and "HTTP 404" not in error and "responded with a status of 404" not in error]
                return max(finish(host, "syncplay host"), finish(guest, "syncplay guest"))
            finally:
                browser.close()
    finally:
        api("POST", policy_url, token, original_policy)


if __name__ == "__main__":
    sys.exit(main())
