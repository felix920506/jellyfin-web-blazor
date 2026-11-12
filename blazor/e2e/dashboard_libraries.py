"""Load and photograph every dashboard libraries/playback/networking route."""
import sys

from playwright.sync_api import sync_playwright

from lib import finish, go, new_page, shot, sign_in

PAGES = (
    ("libraries", "/dashboard/libraries", ".jf-dashboard-libraries"),
    ("library-display", "/dashboard/libraries/display", "#libraryDisplayPage"),
    ("library-metadata", "/dashboard/libraries/metadata", "#metadataImagesConfigurationPage"),
    ("library-nfo", "/dashboard/libraries/nfo", "#metadataNfoPage"),
    ("playback-transcoding", "/dashboard/playback/transcoding", "#encodingSettingsPage"),
    ("playback-resume", "/dashboard/playback/resume", "#playbackConfigurationPage"),
    ("playback-streaming", "/dashboard/playback/streaming", "#streamingSettingsPage"),
    ("playback-trickplay", "/dashboard/playback/trickplay", "#trickplayConfigurationPage"),
    ("networking", "/dashboard/networking", "#networkingPage"),
)


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        for name, route, selector in PAGES:
            go(page, route)
            page.wait_for_selector(selector, timeout=30_000)
            page.wait_for_timeout(300)
            shot(page, f"dashboard-libraries-{name}-desktop")
            page.set_viewport_size({"width": 390, "height": 844})
            page.wait_for_timeout(300)
            shot(page, f"dashboard-libraries-{name}-phone")
            width, window = page.evaluate("[document.documentElement.scrollWidth, window.innerWidth]")
            assert width <= window + 1, f"{name}: {width}px wide in {window}px window"
            page.set_viewport_size({"width": 1366, "height": 768})
        code = finish(page, "dashboard-libraries")
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
