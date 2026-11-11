"""Smoke test: add server -> sign in -> home -> user menu -> mobile drawer, with screenshots."""
import sys

from playwright.sync_api import sync_playwright

from lib import finish, new_page, shot, sign_in


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        page.wait_for_timeout(1500)
        shot(page, "smoke-home")

        page.click(".jf-user-menu-button")
        page.wait_for_selector(".mud-popover-open .mud-menu-item", timeout=5_000)
        shot(page, "smoke-user-menu")
        page.keyboard.press("Escape")

        page.set_viewport_size({"width": 400, "height": 800})
        page.wait_for_timeout(500)
        page.click(".jf-drawer-button")
        page.wait_for_selector(".jf-drawer.mud-drawer--open", timeout=5_000)
        page.wait_for_timeout(400)
        shot(page, "smoke-mobile-drawer")
        code = finish(page, "smoke")
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
