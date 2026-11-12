"""Metadata manager: load its tree and editor on the shared read-only media test server."""
import json
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in


def main():
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))["Servers"][0]
        token = stored["AccessToken"]
        response = page.request.fetch(SERVER + "/Items?IncludeItemTypes=Movie&Limit=1", headers={"Authorization": f'MediaBrowser Token="{token}"'})
        assert response.ok
        item = response.json()["Items"][0]
        try:
            go(page, "/metadata?id=" + item["Id"])
            page.wait_for_selector("#editItemMetadataPage .libraryTree", timeout=30_000)
            page.wait_for_selector("#editItemMetadataPage .editItemMetadataForm", timeout=30_000)
            assert "id=" + item["Id"] in page.url
            shot(page, "metadata-manager")
            assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")
            page.set_viewport_size({"width": 390, "height": 844})
            page.wait_for_timeout(300)
            shot(page, "metadata-manager-phone")
            assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")
        finally:
            code = finish(page, "metadata")
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
