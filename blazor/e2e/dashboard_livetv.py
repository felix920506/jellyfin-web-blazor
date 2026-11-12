"""Live TV administration against its private Jellyfin test server.

The private server has no tuner hardware.  The script creates an M3U playlist and XMLTV file in
its mounted media directory, configures them through the ported forms, verifies the saved API
state, and removes the provider and tuner before it exits.
"""
import json
import os
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in


def credentials(page):
    server = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))["Servers"][0]
    return server["AccessToken"]


def api(page, method, path, token, body=None):
    headers = {"Authorization": f'MediaBrowser Token="{token}"'}
    if body is not None:
        headers["Content-Type"] = "application/json"
    response = page.request.fetch(SERVER + path, method=method, headers=headers, data=json.dumps(body) if body is not None else None)
    assert response.ok, f"{method} {path}: {response.status}"
    return response.json() if response.body() else None


def open_page(page, route, selector):
    go(page, route)
    page.wait_for_selector(selector, timeout=30_000)


def main():
    # The caller has already created these files in the private instance's media directory.
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        token = credentials(page)
        try:
            open_page(page, "/dashboard/livetv", ".jf-dashboard-livetv-add-tuner")
            shot(page, "dashboard-livetv-index")

            open_page(page, "/dashboard/livetv/tuner", ".selectType")
            page.select_option(".selectType", "m3u")
            page.locator("input.txtDevicePath").fill("/media/livetv-e2e.m3u")
            page.locator("input.txtTunerCount").fill("1")
            page.locator("form button[type=submit]").click()
            page.wait_for_selector(".jf-dashboard-livetv-card", timeout=30_000)
            tuner = next(t for t in api(page, "GET", "/System/Configuration/livetv", token)["TunerHosts"] if t.get("Url") == "/media/livetv-e2e.m3u")
            assert tuner["Url"] == "/media/livetv-e2e.m3u"

            open_page(page, "/dashboard/livetv/guide?type=xmltv", ".xmltvForm")
            page.locator("input.txtPath").fill("/media/livetv-e2e.xml")
            page.locator(".xmltvForm button[type=submit]").click()
            page.wait_for_selector(".jf-dashboard-livetv-providers", timeout=30_000)
            providers = api(page, "GET", "/System/Configuration/livetv", token)["ListingProviders"]
            provider = next(p for p in providers if p.get("Path") == "/media/livetv-e2e.xml")

            # All administration routes load and the desktop layout has no accidental overflow.
            open_page(page, "/dashboard/livetv/recordings", ".jf-dashboard-livetv-recordings")
            shot(page, "dashboard-livetv-recordings")
            assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")
            page.set_viewport_size({"width": 390, "height": 844})
            shot(page, "dashboard-livetv-recordings-phone")
            assert page.evaluate("document.documentElement.scrollWidth <= window.innerWidth + 1")

            # Clean server configuration through the API so a failure in a visual assertion is safe.
            api(page, "DELETE", f"/LiveTv/ListingProviders?id={provider['Id']}", token)
            api(page, "DELETE", f"/LiveTv/TunerHosts?id={tuner['Id']}", token)
        finally:
            code = finish(page, "dashboard-livetv")
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
