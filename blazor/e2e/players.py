"""Managed players: photo slideshow and EPUB/PDF/CBZ readers on desktop and phone widths.

YouTube is covered by unit tests because the private test server intentionally has no external
YouTube dependency. Run with: JELLYFIN_URL=http://localhost:18105 ./dev e2e players.py
"""
import json
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in


def api_items(page, item_type):
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))["Servers"][0]
    response = page.request.get(f"{SERVER}/Items?IncludeItemTypes={item_type}&Recursive=true&Fields=Path&Limit=100",
                                headers={"Authorization": f'MediaBrowser Token="{stored["AccessToken"]}"'})
    assert response.ok
    return response.json().get("Items", [])


def play_item(page, item, selector, name):
    go(page, "/details?id=" + item["Id"])
    page.wait_for_selector(".btnPlay:not(.hide)", timeout=30_000)
    page.locator(".btnPlay:not(.hide)").first.click()
    page.wait_for_selector(selector, state="attached", timeout=30_000)
    candidates = page.locator(selector)
    visible = any(candidates.nth(index).is_visible() for index in range(candidates.count()))
    if not visible:
        print("player elements:", candidates.evaluate_all("elements => elements.map(e => ({rect:e.getBoundingClientRect().toJSON(), display:getComputedStyle(e).display, visibility:getComputedStyle(e).visibility, outer:e.outerHTML.slice(0,200)}))"))
        print("page errors:", page.errors)
    assert visible, f"{selector} should be visible"
    page.wait_for_timeout(500)
    shot(page, f"players-{name}-desktop")
    page.set_viewport_size({"width": 400, "height": 800})
    page.wait_for_timeout(500)
    shot(page, f"players-{name}-phone")
    page.keyboard.press("Escape")
    page.wait_for_timeout(300)


def main():
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        photos = api_items(page, "Photo")
        books = api_items(page, "Book")
        assert photos and books, "seed-players.sh must provide Photos and Books items"
        play_item(page, photos[0], ".slideshowDialog", "photo")
        by_extension = {item.get("Path", "").lower().rsplit(".", 1)[-1]: item for item in books}
        for extension, selector in (("epub", "#bookPlayer"), ("pdf", "#pdfPlayer"), ("cbz", "#comicsPlayer")):
            assert extension in by_extension, f"missing seeded .{extension} item"
            play_item(page, by_extension[extension], selector, extension)
        page.set_viewport_size({"width": 1366, "height": 768})
        code = finish(page, "players")
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
