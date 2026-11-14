"""Plugins, repositories and backups on a PRIVATE Jellyfin instance.

Never run against the shared server. Adds/removes one real repository and creates a real
database-only backup. Restore is opened/cancelled; the destructive restart flow is mocked in bUnit.
"""
import json
import sys
import time

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in
from sessionlib import use_default_config


def main():
    if ":18102" not in SERVER:
        raise RuntimeError("Use the private dashplugins server on port 18102, never the shared server")
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        page.context.grant_permissions(["clipboard-read", "clipboard-write"])
        use_default_config(page)
        sign_in(page)
        stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
        token = stored["Servers"][0]["AccessToken"]
        headers = {"Authorization": f'MediaBrowser Token="{token}"'}

        def get(path):
            response = page.request.get(SERVER + path, headers=headers)
            assert response.ok, f"{path}: {response.status}"
            return response.json()

        def open_page(route, selector):
            go(page, route)
            page.wait_for_selector(selector, timeout=90_000)
            page.wait_for_timeout(350)

        def fits(name):
            wide, width = page.evaluate("[document.documentElement.scrollWidth, innerWidth]")
            assert wide <= width + 1, f"{name}: horizontal overflow {wide} > {width}"

        def closed():
            page.wait_for_selector(".jf-dashboard-dialog-form", state="detached", timeout=60_000)

        repository_name = "e2e-dashplugins-" + str(int(time.time()))
        original_repositories = get("/Repositories")
        detail_route = None
        config_route = None
        catalog_available = True
        try:
            open_page("/dashboard/plugins/catalog", "#pluginsPage")
            page.wait_for_function("!location.hash.includes('/catalog')")
            page.wait_for_selector(".jf-dashboard-plugin-card, #pluginsPage .mud-alert", timeout=90_000)
            if page.locator("#pluginsPage .mud-alert").count():
                catalog_available = False
                print("Catalog unavailable from server; catalog details/actions are covered by bUnit")
                shot(page, "dashboard-plugins-catalog-error")
            else:
                page.click("[data-status=All]")
                page.wait_for_selector(".jf-dashboard-plugin-card")
                shot(page, "dashboard-plugins-all")
                page.click("[data-category=Music]")
                page.wait_for_timeout(300)
                shot(page, "dashboard-plugins-category")
                page.click("[data-category='']")
                page.fill("#jf-dashboard-plugin-search", "no-such-plugin-123")
                page.wait_for_selector(".jf-dashboard-plugin-empty")
                assert page.locator(".jf-dashboard-plugin-card").count() == 0
                page.fill("#jf-dashboard-plugin-search", "")
                page.click("[data-status=Installed]")
                page.wait_for_selector(".jf-dashboard-plugin-card")
                shot(page, "dashboard-plugins-installed")
                detail_route = page.locator(".jf-dashboard-plugin-card").first.get_attribute("href")[1:]

            # Details must still work for bundled plugins when internet/catalog is unavailable.
            installed = get("/Plugins")
            if detail_route is None and installed:
                from urllib.parse import quote
                detail_route = f"/dashboard/plugins/{installed[0]['Id']}?name={quote(installed[0]['Name'])}"
            if detail_route:
                open_page(detail_route, ".jf-dashboard-plugin-info")
                assert page.locator(".jf-dashboard-plugin-info tbody tr").count() == 4
                shot(page, "dashboard-plugins-details")
                fits("plugin details")
                if page.locator(".jf-dashboard-plugin-settings").count():
                    config_route = page.locator(".jf-dashboard-plugin-settings").get_attribute("href")[1:]
                    open_page(config_route, ".jf-plugin-configuration-frame[data-ready=true]")
                    assert page.frame_locator('.jf-plugin-configuration-frame').locator('form').count() > 0
                    shot(page, "dashboard-plugins-settings")
            if not config_route:
                config_route = "/configurationpage?name=missing-plugin"
                open_page(config_route, "#pluginConfigurationPage .mud-alert")
                shot(page, "dashboard-plugins-settings-missing")

            open_page("/dashboard/plugins/repositories", ".jf-dashboard-repository-add")
            shot(page, "dashboard-plugins-repositories")
            page.click(".jf-dashboard-repository-add")
            page.wait_for_selector("#jf-dashboard-repository-name")
            page.fill("#jf-dashboard-repository-name", repository_name)
            # A real repository manifest, not a fake local URL. No plugin is installed.
            page.fill("#jf-dashboard-repository-url", "https://repo.jellyfin.org/releases/plugin/manifest.json")
            shot(page, "dashboard-plugins-repository-add")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            closed()
            page.wait_for_selector(f"[data-repository='{repository_name}']")
            assert any(r["Name"] == repository_name and r["Enabled"] for r in get("/Repositories"))
            page.click(f"[data-repository='{repository_name}'] .jf-dashboard-repository-remove")
            page.wait_for_selector(".jf-dashboard-dialog-form")
            shot(page, "dashboard-plugins-repository-remove")
            page.click(".jf-dashboard-dialog-form [data-id=cancel]")
            closed()
            assert any(r["Name"] == repository_name for r in get("/Repositories"))
            page.click(f"[data-repository='{repository_name}'] .jf-dashboard-repository-remove")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            closed()
            page.wait_for_selector(f"[data-repository='{repository_name}']", state="detached")
            assert not any(r["Name"] == repository_name for r in get("/Repositories"))

            open_page("/dashboard/backups", ".jf-dashboard-backup-create")
            before = len(get("/Backup"))
            page.click(".jf-dashboard-backup-create")
            page.wait_for_selector(".jf-dashboard-backup-database input")
            assert page.locator(".jf-dashboard-backup-database input").is_checked()
            assert page.locator(".jf-dashboard-backup-database input").is_disabled()
            shot(page, "dashboard-plugins-backup-create")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            page.wait_for_timeout(300)
            # Seeded metadata scans may still be running: explicitly confirm the task warning.
            if page.locator(".jf-dashboard-dialog-form [data-id=ok]").count():
                page.click(".jf-dashboard-dialog-form [data-id=ok]")
            closed()
            page.wait_for_selector("[data-backup]", timeout=90_000)
            assert len(get("/Backup")) == before + 1
            shot(page, "dashboard-plugins-backups")
            page.locator(".jf-dashboard-backup-info").first.click()
            page.wait_for_selector(".jf-dashboard-backup-copy")
            shot(page, "dashboard-plugins-backup-details")
            page.click(".jf-dashboard-backup-copy")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            closed()
            page.locator(".jf-dashboard-backup-restore").first.click()
            page.wait_for_selector(".jf-dashboard-dialog-form")
            shot(page, "dashboard-plugins-backup-restore")
            page.click(".jf-dashboard-dialog-form [data-id=cancel]")
            closed()

            page.set_viewport_size({"width": 390, "height": 844})
            routes = [
                ("installed", "/dashboard/plugins", ".jf-dashboard-plugin-card, #pluginsPage .mud-alert"),
                ("repositories", "/dashboard/plugins/repositories", ".jf-dashboard-repository-add"),
                ("backups", "/dashboard/backups", "[data-backup]"),
                ("settings", config_route, ".jf-plugin-configuration-frame[data-ready=true], #pluginConfigurationPage .mud-alert"),
            ]
            if detail_route:
                routes.append(("details", detail_route, ".jf-dashboard-plugin-info"))
            for name, route, selector in routes:
                open_page(route, selector)
                fits(name)
                shot(page, "dashboard-plugins-phone-" + name)
            # A phone-sized options dialog is also part of the visual coverage.
            open_page("/dashboard/backups", ".jf-dashboard-backup-create")
            page.click(".jf-dashboard-backup-create")
            page.wait_for_selector(".jf-dashboard-backup-database")
            shot(page, "dashboard-plugins-phone-backup-dialog")
            page.click(".jf-dashboard-dialog-form [data-id=cancel]")
            closed()
        finally:
            # Private instance is reset by the caller, but restore repositories even on a failure.
            page.request.post(SERVER + "/Repositories", headers={**headers, "Content-Type": "application/json"}, data=json.dumps(original_repositories))
        result = finish(page, "dashboard plugins/backups")
        browser.close()
        return result


if __name__ == "__main__":
    sys.exit(main())
