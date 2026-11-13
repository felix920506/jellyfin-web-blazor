"""Screensaver activation, both built-in screensavers, and wake-on-input.

The original client permits a five-second minimum. The test uses that supported value rather than
adding a test-only query switch, and restores the user's local values in a finally block.
"""
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, new_page, open_app, sign_in, shot


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        code = 1
        try:
            sign_in(page)
            auth = page.request.post(
                f"{SERVER}/Users/AuthenticateByName",
                headers={"Authorization": 'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-screensaver", Version="1"'},
                data={"Username": "admin", "Pw": "jellyfin"},
            ).json()
            user_id = auth["User"]["Id"].replace("-", "")

            keys = [f"{user_id}-screensaver", f"{user_id}-screensaverTime", f"{user_id}-backdropScreensaverInterval"]
            saved = page.evaluate("keys => Object.fromEntries(keys.map(k => [k, localStorage.getItem(k)]))", keys)
            try:
                for saver, selector in (("logoscreensaver", ".logoScreenSaver"), ("backdropscreensaver", ".jf-screensaver-backdrop")):
                    page.evaluate("""([id, uid]) => {
                        localStorage.setItem(uid + '-screensaver', id);
                        localStorage.setItem(uid + '-screensaverTime', '5');
                        localStorage.setItem(uid + '-backdropScreensaverInterval', '1');
                    }""", [saver, user_id])
                    page.reload()
                    page.wait_for_selector("#indexPage", timeout=30_000)
                    page.wait_for_selector(selector, timeout=12_000)
                    shot(page, "screensaver-" + saver)
                    page.mouse.move(300, 300)
                    page.wait_for_selector(selector, state="detached", timeout=5_000)
            finally:
                page.evaluate("saved => Object.entries(saved).forEach(([k, v]) => v === null ? localStorage.removeItem(k) : localStorage.setItem(k, v))", saved)
                page.reload()
            code = finish(page, "screensaver")
        finally:
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
