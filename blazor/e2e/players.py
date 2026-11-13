"""Managed players: photo slideshow and EPUB/PDF/CBZ readers on desktop and phone widths.

YouTube is covered by unit tests because the private test server intentionally has no external
YouTube dependency. Start the server with JELLYFIN_SEED_PLAYERS=1 JELLYFIN_INSTANCE=players JELLYFIN_PORT=18105
scripts/test-server.sh up, then run with: JELLYFIN_URL=http://localhost:18105 ./dev e2e players.py
"""
import json
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in


def api_items(page, item_type):
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))["Servers"][0]
    response = page.request.get(
        f"{SERVER}/Items?IncludeItemTypes={item_type}&Recursive=true&Fields=Path&Limit=100",
        headers={"Authorization": f'MediaBrowser Token="{stored["AccessToken"]}"'},
    )
    assert response.ok
    return response.json().get("Items", [])


def assert_no_playback_error(page):
    error = page.get_by_text("Playback Error", exact=True)
    assert not error.is_visible(), "managed player displayed a playback error"


def player_root(page, selector):
    root = page.locator(selector)
    assert root.is_visible(), f"{selector} should be visible"
    box = root.bounding_box()
    viewport = page.evaluate("({width: innerWidth, height: innerHeight})")
    assert box and box["x"] == 0 and box["y"] == 0
    assert box["width"] == viewport["width"] and box["height"] == viewport["height"]
    return root


def play_item(page, item, selector, name, kind):
    page.set_viewport_size({"width": 1366, "height": 768})
    go(page, "/details?id=" + item["Id"])
    page.wait_for_selector("#itemDetailPage", timeout=30_000)
    page.wait_for_timeout(500)
    page.wait_for_selector(".btnPlay:not(.hide)", timeout=30_000)
    play = page.locator(".btnPlay:not(.hide):visible").first
    play.wait_for(timeout=10_000)
    play.evaluate("element => element.click()")
    page.wait_for_timeout(500)
    page.wait_for_selector(selector, state="attached", timeout=30_000)
    assert page.locator(selector).is_visible(), f"{selector} should be visible"
    page.wait_for_function(
        "selector => !document.querySelector('.dialogContainer') || "
        "getComputedStyle(document.querySelector(selector)).transform === 'none'",
        arg=selector,
        timeout=10_000,
    )
    assert_no_playback_error(page)

    if kind == "photo":
        image = page.locator(f"{selector} img").first
        page.wait_for_function("selector => { const image = document.querySelector(selector); return image?.complete && image.naturalWidth > 0; }", arg=f"{selector} img")
        player_root(page, selector)
        image_box = image.bounding_box()
        assert image_box and image_box["width"] == 1366 and image_box["height"] == 768
        page.wait_for_selector(f"{selector} .swiper-slide-active", state="attached", timeout=15_000)
        before = page.locator(f"{selector} .swiper-slide-active").get_attribute("data-index")
        page.locator(".btnSlideshowNext").click()
        page.wait_for_function("({selector, before}) => document.querySelector(selector + ' .swiper-slide-active')?.dataset.index !== before", arg={"selector": selector, "before": before})
        assert_no_playback_error(page)
        page.wait_for_function("selector => { const image = document.querySelector(selector + ' .swiper-slide-active img'); return image?.complete && image.naturalWidth > 0; }", arg=selector)
    elif kind == "epub":
        player_root(page, selector)
        frame = page.frame_locator(f"{selector} iframe").first
        frame.get_by_text("This EPUB is seeded for the Blazor reader smoke test.").wait_for(timeout=15_000)
        assert frame.get_by_text("Reader Sample", exact=True).is_visible()
    elif kind == "pdf":
        player_root(page, selector)
        canvas = page.locator(f"{selector} canvas")
        page.wait_for_function(
            "selector => { const canvas = document.querySelector(selector); return canvas?.width > 0 && canvas.height > 0 && "
            "[...canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height).data].some(v => v !== 0); }",
            arg=f"{selector} canvas",
            timeout=15_000,
        )
    else:
        player_root(page, selector)
        image = page.locator(f"{selector} img").first
        page.wait_for_function("selector => { const image = document.querySelector(selector); return image?.complete && image.naturalWidth > 0; }", arg=f"{selector} img", timeout=15_000)
        assert page.locator(f"{selector} .swiper-slide").count() >= 2
        before = page.locator(f"{selector} .swiper-wrapper").get_attribute("data-index")
        page.locator(f"{selector} .bookplayerButton[title='Next']").click(force=True)
        page.wait_for_function("({selector, before}) => document.querySelector(selector + ' .swiper-wrapper').dataset.index !== before", arg={"selector": selector, "before": before}, timeout=15_000)
        assert_no_playback_error(page)

    shot(page, f"players-{name}-desktop")
    page.set_viewport_size({"width": 400, "height": 800})
    page.wait_for_timeout(500)
    assert_no_playback_error(page)
    player_root(page, selector)
    shot(page, f"players-{name}-phone")
    page.locator(
        ".btnSlideshowExit:visible, #bookPlayer .bookplayerButton[title='Back']:visible, "
        "#pdfPlayer .bookplayerButton[title='Back']:visible, #comicsPlayer .bookplayerButton[title='Back']:visible"
    ).first.click(force=True)
    page.evaluate("""async () => {
        const module = await import('/js/players/players.js');
        await Promise.all(['photo', 'epub', 'pdf', 'comics'].map(kind => module.stop(kind, true)));
    }""")
    page.locator(".slideshowDialog, #bookPlayer, #pdfPlayer, #comicsPlayer").evaluate_all(
        "elements => elements.forEach(element => element.remove())"
    )
    page.wait_for_timeout(500)


def main():
    with sync_playwright() as playwright:
        browser = playwright.chromium.launch()
        api_page = new_page(browser)
        sign_in(api_page)
        photos = api_items(api_page, "Photo")
        books = api_items(api_page, "Book")
        assert photos and books, "seed-players.sh must provide Photos and Books items"
        by_extension = {item.get("Path", "").lower().rsplit(".", 1)[-1]: item for item in books}
        cases = [(photos[0], ".slideshowDialog", "photo", "photo"),
                 (by_extension["epub"], "#bookPlayer", "epub", "epub"),
                 (by_extension["pdf"], "#pdfPlayer", "pdf", "pdf"),
                 (by_extension["cbz"], "#comicsPlayer", "cbz", "cbz")]
        api_page.close()
        for item, selector, name, kind in cases:
            assert kind == "photo" or kind in by_extension, f"missing seeded .{kind} item"
            case_browser = playwright.chromium.launch()
            page = new_page(case_browser)
            sign_in(page)
            play_item(page, item, selector, name, kind)
            code = finish(page, f"players-{name}")
            case_browser.close()
            if code:
                browser.close()
                return code
        code = 0
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
