"""The admin dashboard (part 1) against the shared test server.

Creates and deletes its own user, API key and task trigger. The three settings it saves (image
encoding limit, slow response threshold, login disclaimer) and the name it gives its own device
are put back at the end, also when a step fails. Restart, shutdown and "delete all devices" are only opened and cancelled.
"""
import json
import sys
import time

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in
from sessionlib import Steps, drop_console_noise, use_default_config

PREFIX = "e2e-dash-"
SAVED = "Settings saved."
DEVICE_NAME = "e2e dashboard browser"
HOUR_TICKS = 36_000_000_000

# Every page of part 1 with something that shows it has loaded.
PAGES = (
    ("index", "/dashboard", ".jf-dashboard-server-name"),
    ("settings", "/dashboard/settings", "#jf-dashboard-settings-name"),
    ("branding", "/dashboard/branding", "#jf-dashboard-branding-disclaimer"),
    ("users", "/dashboard/users", "#userProfilesPage .card"),
    ("users-add", "/dashboard/users/add", "#txtUsername"),
    ("activity", "/dashboard/activity", "#serverActivityPage tbody tr"),
    ("devices", "/dashboard/devices", "#devicesPage tbody tr"),
    ("keys", "/dashboard/keys", ".jf-dashboard-keys-add"),
    ("logs", "/dashboard/logs", ".jf-dashboard-log-item"),
    ("tasks", "/dashboard/tasks", ".jf-dashboard-task"),
)

# The drawer of the original: apps/dashboard/components/drawer/sections/*.tsx (groups closed).
DRAWER_LINKS = [
    "#/dashboard", "#/dashboard/settings", "#/dashboard/branding", "#/dashboard/users",
    "#/dashboard/devices", "#/dashboard/activity",
    "#/dashboard/livetv", "#/dashboard/livetv/recordings",
    "#/dashboard/plugins",
    "#/dashboard/networking", "#/dashboard/keys", "#/dashboard/backups", "#/dashboard/logs", "#/dashboard/tasks",
]


def credentials(page):
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
    server = stored["Servers"][0]
    return server["UserId"], server["AccessToken"]


def api(page, method, path, token, body=None):
    """A request to the test server outside the app."""
    # The token alone: naming a client or device here would rename the browser's device on the server.
    headers = {"Authorization": f'MediaBrowser Token="{token}"'}
    if body is not None:
        headers["Content-Type"] = "application/json"
        return page.request.fetch(SERVER + path, method=method, headers=headers, data=json.dumps(body))
    return page.request.fetch(SERVER + path, method=method, headers=headers)


def open_page(page, route, selector):
    go(page, route)
    page.wait_for_selector(selector, timeout=30_000)
    page.wait_for_timeout(600)


def title(page):
    return page.inner_text(".jf-dashboard-title").strip()


def check(page, selector, value):
    """Sets a checkbox of the legacy forms (the inputs are visually replaced by their labels)."""
    box = page.locator(selector).first
    if box.is_checked() != value:
        box.evaluate("e => e.click()")
    assert box.is_checked() == value, f"{selector} did not change"


def type_into(page, selector, text):
    """Fills a field and leaves it, which is when the app takes the value over."""
    page.fill(selector, text)
    page.locator(selector).blur()


def toast(page):
    page.wait_for_selector(".toast", timeout=15_000)
    return page.inner_text(".toast").strip()


def no_toast(page):
    page.wait_for_selector(".toast", state="detached", timeout=10_000)


def dialog_closed(page, selector=".jf-dashboard-dialog-form"):
    page.wait_for_selector(selector, state="detached", timeout=10_000)


def fits(page, name):
    """The page must not be wider than the window."""
    wide, window = page.evaluate("[document.documentElement.scrollWidth, window.innerWidth]")
    assert wide <= window + 1, f"{name}: the page is {wide}px wide in a {window}px window"


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        page.context.grant_permissions(["clipboard-read", "clipboard-write"])
        use_default_config(page)
        steps = Steps(page, "dashboard")
        sign_in(page)
        user_id, token = credentials(page)

        def get(path):
            response = api(page, "GET", path, token)
            assert response.ok, f"GET {path}: {response.status}"
            return response.json()

        configuration = get("/System/Configuration")
        branding = get("/System/Configuration/branding")
        tasks = get("/ScheduledTasks")
        task = next(t for t in tasks if t["Key"] == "CleanActivityLog")
        task_path = f"/ScheduledTasks/{task['Id']}"
        task_triggers = task.get("Triggers") or []
        user_name = PREFIX + str(int(time.time()))
        key_name = PREFIX + "key-" + str(int(time.time()))

        def created_user():
            return next((u for u in get("/Users") if u["Name"] == user_name), None)

        def cleanup():
            for user in get("/Users"):
                if user["Name"].startswith(PREFIX):
                    api(page, "DELETE", f"/Users/{user['Id']}", token)
            for key in get("/Auth/Keys")["Items"]:
                if (key.get("AppName") or "").startswith(PREFIX):
                    api(page, "DELETE", f"/Auth/Keys/{key['AccessToken']}", token)
            # Only what this script changes is put back, so that settings others changed meanwhile stay.
            current = get("/System/Configuration")
            changed = {name: configuration.get(name)
                       for name in ("ParallelImageEncodingLimit", "SlowResponseThresholdMs", "EnableSlowResponseWarning", "CachePath", "MetadataPath")
                       if current.get(name) != configuration.get(name)}
            if changed:
                api(page, "POST", "/System/Configuration", token, {**current, **changed})
            current = get("/System/Configuration/branding")
            if current.get("LoginDisclaimer") != branding.get("LoginDisclaimer"):
                api(page, "POST", "/System/Configuration/branding", token, {**current, "LoginDisclaimer": branding.get("LoginDisclaimer")})
            if (get(task_path).get("Triggers") or []) != task_triggers:
                api(page, "POST", task_path + "/Triggers", token, task_triggers)
            for device in get("/Devices")["Items"]:
                if device.get("CustomName") == DEVICE_NAME:
                    api(page, "POST", f"/Devices/Options?id={device['Id']}", token, {"CustomName": ""})

        def entry():
            # The user menu of the app leads to the dashboard ...
            page.click(".jf-user-menu-button")
            page.click("a[href='#/dashboard'] >> visible=true")
            page.wait_for_selector("#dashboardPage .jf-dashboard-server-name", timeout=30_000)
            assert page.evaluate("document.body.classList.contains('dashboardDocument')"), "no dashboardDocument class"
            assert title(page) == "Dashboard", title(page)
            # ... and so does the settings menu (hidden in the modern layout, as in the original).
            open_page(page, "/mypreferencesmenu", "#myPreferencesMenuPage")
            assert page.locator("#myPreferencesMenuPage a[href='#/dashboard']").count() == 1
            assert not page.evaluate("document.body.classList.contains('dashboardDocument')"), "dashboardDocument stays after leaving"
            assert page.locator(".jf-dashboard-drawer").count() == 0

        def index():
            open_page(page, "/dashboard", ".jf-dashboard-server-name")
            page.wait_for_selector(".jf-dashboard-device-card", timeout=20_000)
            page.wait_for_selector(".jf-dashboard-activity .jf-dashboard-activity-item, .jf-dashboard-activity:empty", timeout=20_000)
            page.wait_for_selector(".jf-dashboard-storage-size", timeout=20_000)
            page.wait_for_timeout(800)
            shot(page, "dashboard-index")
            info = get("/System/Info")
            assert page.inner_text(".jf-dashboard-server-name").strip() == info["ServerName"]
            assert page.inner_text(".jf-dashboard-server-version").strip() in info["Version"]
            assert page.locator(".jf-dashboard-paths .jf-dashboard-storage-item").count() == 7
            assert page.locator(".jf-dashboard-count-card").count() >= 1
            assert page.locator(".jf-dashboard-drawer-header-name").inner_text().strip() == info["ServerName"]
            fits(page, "index")
            # Restart and shutdown ask first. They are never confirmed here: the server is shared.
            for button in (".jf-dashboard-restart", ".jf-dashboard-shutdown"):
                page.click(button)
                page.wait_for_selector(".jf-dashboard-dialog-form", timeout=10_000)
                page.wait_for_timeout(300)
                shot(page, "dashboard-index-dialog" + button.replace(".jf-dashboard", ""))
                page.click(".jf-dashboard-dialog-form [data-id=cancel]")
                dialog_closed(page)
            assert get("/System/Info")["ServerName"] == info["ServerName"]

        def drawer():
            open_page(page, "/dashboard", ".jf-dashboard-server-name")
            links = page.locator(".jf-dashboard-drawer a.mud-nav-link:not(.jf-dashboard-drawer-header)")
            hrefs = [links.nth(i).get_attribute("href") for i in range(links.count())]
            assert [h for h in hrefs if h.startswith("#/dashboard")] == DRAWER_LINKS, hrefs
            sections = page.locator(".jf-dashboard-drawer .jf-drawer-subheader").all_inner_texts()
            assert len(sections) == 5, sections
            assert page.get_attribute(".jf-dashboard-drawer a.active", "href") == "#/dashboard"
            # The two groups open and show their links.
            groups = page.locator(".jf-dashboard-drawer .jf-dashboard-nav-group")
            assert groups.count() == 2
            groups.nth(0).click()
            page.wait_for_selector(".jf-dashboard-drawer a[href='#/dashboard/libraries/display']", timeout=5_000)
            groups.nth(1).click()
            page.wait_for_selector(".jf-dashboard-drawer a[href='#/dashboard/playback/trickplay']", timeout=5_000)
            assert page.locator(".jf-dashboard-drawer .jf-dashboard-nav-child").count() == 8
            shot(page, "dashboard-drawer-groups")
            groups.nth(0).click()
            groups.nth(1).click()
            # A link leads to its page and is highlighted there.
            page.click(".jf-dashboard-drawer a[href='#/dashboard/users']")
            page.wait_for_selector("#userProfilesPage .card", timeout=20_000)
            page.wait_for_function("document.querySelector('.jf-dashboard-drawer a.active').getAttribute('href') === '#/dashboard/users'", timeout=5_000)
            assert title(page) == "Users", title(page)
            # Back in the app bar returns to where one came from; the header leads to the app.
            page.click(".jf-dashboard-back")
            page.wait_for_selector("#dashboardPage .jf-dashboard-server-name", timeout=20_000)
            page.click(".jf-dashboard-drawer-header")
            page.wait_for_selector("#indexPage", timeout=30_000)
            assert not page.evaluate("document.body.classList.contains('dashboardDocument')")

        def settings():
            open_page(page, "/dashboard/settings", "#jf-dashboard-settings-name")
            shot(page, "dashboard-settings")
            fits(page, "settings")
            assert page.input_value("#jf-dashboard-settings-name") == (configuration.get("ServerName") or ""), page.input_value("#jf-dashboard-settings-name")
            assert page.input_value("#jf-dashboard-settings-cache") == (configuration.get("CachePath") or "")
            # The folder button opens the directory picker.
            page.click(".mud-input-adornment button >> nth=0")
            page.wait_for_selector("#txtDirectoryPickerPath", timeout=10_000)
            page.wait_for_timeout(500)
            shot(page, "dashboard-settings-directory-picker")
            page.click(".dialogContainer .btnCloseDialog >> nth=-1")
            page.wait_for_selector("#txtDirectoryPickerPath", state="detached", timeout=10_000)
            # The language list opens above the page.
            page.click(".jf-dashboard-settings-culture")
            page.wait_for_selector(".mud-popover-open .mud-list-item", timeout=10_000)
            assert page.locator(".mud-popover-open .mud-list-item").count() > 20
            page.wait_for_timeout(300)
            shot(page, "dashboard-settings-languages")
            page.keyboard.press("Escape")
            page.wait_for_selector(".mud-popover-open .mud-list-item", state="detached", timeout=10_000)

            limit = (configuration.get("ParallelImageEncodingLimit") or 0) + 3
            type_into(page, "#jf-dashboard-settings-encoding", str(limit))
            page.click(".jf-dashboard-submit")
            page.wait_for_selector(".jf-dashboard-saved", timeout=15_000)
            assert page.inner_text(".jf-dashboard-saved").strip() == SAVED
            saved = get("/System/Configuration")
            assert saved["ParallelImageEncodingLimit"] == limit, saved["ParallelImageEncodingLimit"]
            # Nothing else changed (like the original, the form sends an empty path as "" where the server had none).
            for name in ("ServerName", "UICulture", "QuickConnectAvailable", "LibraryScanFanoutConcurrency"):
                assert saved.get(name) == configuration.get(name), (name, saved.get(name), configuration.get(name))
            for name in ("CachePath", "MetadataPath"):
                assert (saved.get(name) or "") == (configuration.get(name) or ""), (name, saved.get(name), configuration.get(name))
            shot(page, "dashboard-settings-saved")
            # Put back through the page.
            original = configuration.get("ParallelImageEncodingLimit") or 0
            type_into(page, "#jf-dashboard-settings-encoding", str(original) if original else "")
            page.click(".jf-dashboard-submit")
            page.wait_for_timeout(1500)
            assert get("/System/Configuration")["ParallelImageEncodingLimit"] == original

        def branding_page():
            open_page(page, "/dashboard/branding", "#jf-dashboard-branding-disclaimer")
            shot(page, "dashboard-branding")
            fits(page, "branding")
            assert page.input_value("#jf-dashboard-branding-disclaimer") == (branding.get("LoginDisclaimer") or "")
            assert page.input_value("#jf-dashboard-branding-css") == (branding.get("CustomCss") or "")
            assert page.is_checked(".jf-dashboard-branding-enable input") == bool(branding.get("SplashscreenEnabled"))
            type_into(page, "#jf-dashboard-branding-disclaimer", "e2e dashboard disclaimer")
            page.click(".jf-dashboard-submit")
            page.wait_for_selector(".jf-dashboard-saved", timeout=15_000)
            saved = get("/System/Configuration/branding")
            assert saved.get("LoginDisclaimer") == "e2e dashboard disclaimer", saved
            assert (saved.get("CustomCss") or "") == (branding.get("CustomCss") or "")
            assert bool(saved.get("SplashscreenEnabled")) == bool(branding.get("SplashscreenEnabled"))
            # Put back through the page.
            type_into(page, "#jf-dashboard-branding-disclaimer", branding.get("LoginDisclaimer") or "")
            page.click(".jf-dashboard-submit")
            page.wait_for_timeout(1500)
            assert (get("/System/Configuration/branding").get("LoginDisclaimer") or "") == (branding.get("LoginDisclaimer") or "")

        def users():
            open_page(page, "/dashboard/users", "#userProfilesPage .card")
            shot(page, "dashboard-users")
            fits(page, "users")
            assert page.locator(".card[data-username='admin']").count() == 1
            page.click("#btnAddUser")
            page.wait_for_selector("#txtUsername", timeout=15_000)
            page.wait_for_selector(".folderAccessContainer .chkEnableAllFolders", state="attached", timeout=15_000)
            assert title(page) == "", title(page)
            page.fill("#txtUsername", user_name)
            page.fill("#txtPassword", "e2e-password")
            page.wait_for_timeout(300)
            shot(page, "dashboard-users-add")
            page.click("form.newUserProfileForm button[type=submit]")
            page.wait_for_selector(".jf-dashboard-user-name", timeout=20_000)
            created = created_user()
            assert created, "the user was not created"
            assert page.inner_text(".jf-dashboard-user-name").strip() == user_name
            assert page.evaluate("location.hash") == f"#/dashboard/users/{created['Id']}/profile", page.evaluate("location.hash")
            assert title(page) == user_name, title(page)

        def user_profile():
            created = created_user()
            open_page(page, f"/dashboard/users/{created['Id']}/profile", "form.editUserProfileForm")
            page.wait_for_selector(".deleteAccess input.chkFolder", state="attached", timeout=15_000)
            shot(page, "dashboard-user-profile")
            fits(page, "user profile")
            tabs = page.locator(".jf-dashboard-tabs .mud-tab").all_inner_texts()
            assert [t.strip().lower() for t in tabs] == ["profile", "access", "parental control", "password"], tabs
            assert page.input_value("#txtUserName") == user_name
            assert not page.is_checked("input.chkIsAdmin")
            check(page, "input.chkIsHidden", not created["Policy"]["IsHidden"])
            check(page, "input.chkEnableDeleteAllFolders", False)
            first = page.locator(".deleteAccess input.chkFolder").first.get_attribute("data-id")
            check(page, f".deleteAccess input.chkFolder[data-id='{first}']", True)
            page.fill("#txtMaxActiveSessions", "3")
            page.fill("#txtRemoteClientBitrateLimit", "2.5")
            no_toast(page)
            page.click("form.editUserProfileForm button[type=submit]")
            assert toast(page) == SAVED
            page.wait_for_selector(f"#userProfilesPage .card[data-username='{user_name}']", timeout=20_000)
            policy = created_user()["Policy"]
            assert policy["IsHidden"] == (not created["Policy"]["IsHidden"]), policy["IsHidden"]
            assert policy["MaxActiveSessions"] == 3, policy["MaxActiveSessions"]
            assert policy["RemoteClientBitrateLimit"] == 2_500_000, policy["RemoteClientBitrateLimit"]
            assert policy["EnableContentDeletionFromFolders"] == [first], policy["EnableContentDeletionFromFolders"]
            assert not policy["EnableContentDeletion"]

        def user_access():
            created = created_user()
            open_page(page, f"/dashboard/users/{created['Id']}/profile", "form.editUserProfileForm")
            page.click(".jf-dashboard-tabs .mud-tab >> nth=1")
            page.wait_for_selector("form.userLibraryAccessForm input.chkFolder", state="attached", timeout=15_000)
            assert page.evaluate("location.hash") == f"#/dashboard/users/{created['Id']}/access"
            check(page, "input.chkEnableAllFolders", False)
            folders = page.locator("form.userLibraryAccessForm input.chkFolder")
            for i in range(folders.count()):
                check(page, f"form.userLibraryAccessForm input.chkFolder >> nth={i}", i == 0)
            first = folders.first.get_attribute("data-id")
            page.wait_for_timeout(300)
            shot(page, "dashboard-user-access")
            fits(page, "user access")
            no_toast(page)
            page.click("form.userLibraryAccessForm button[type=submit]")
            assert toast(page) == SAVED
            policy = created_user()["Policy"]
            assert policy["EnableAllFolders"] is False
            assert [f.replace("-", "") for f in policy["EnabledFolders"]] == [first], policy["EnabledFolders"]
            # What the profile tab saved is still there.
            assert policy["MaxActiveSessions"] == 3

        def user_parental():
            created = created_user()
            open_page(page, f"/dashboard/users/{created['Id']}/parentalcontrol", "form.userParentalControlForm")
            page.wait_for_function("document.querySelectorAll('#selectMaxParentalRating option').length > 1", timeout=15_000)
            page.select_option("#selectMaxParentalRating", index=2)
            chosen = page.locator("#selectMaxParentalRating option:checked").inner_text().strip()
            check(page, "input.chkUnratedItem[data-itemtype=Movie]", True)
            page.click("#btnAddBlockedTag")
            page.wait_for_selector(".dialogContainer #txtInput", timeout=10_000)
            page.fill(".dialogContainer #txtInput", "e2e-tag")
            page.keyboard.press("Enter")
            page.wait_for_selector(".blockedTag.btnDeleteTag[data-tag='e2e-tag']", state="attached", timeout=10_000)
            page.click("#btnAddSchedule")
            page.wait_for_selector("form.scheduleForm", timeout=10_000)
            page.select_option("#selectDay", "Weekend")
            page.select_option("#selectStart", "8")
            page.select_option("#selectEnd", "20.5")
            page.wait_for_timeout(300)
            shot(page, "dashboard-user-schedule-dialog")
            page.click("form.scheduleForm button[type=submit]")
            page.wait_for_selector(".liSchedule", timeout=10_000)
            page.wait_for_timeout(300)
            shot(page, "dashboard-user-parental")
            fits(page, "user parental control")
            no_toast(page)
            page.click("form.userParentalControlForm button[type=submit]")
            assert toast(page) == SAVED
            policy = created_user()["Policy"]
            assert policy["BlockedTags"] == ["e2e-tag"], policy["BlockedTags"]
            assert policy["BlockUnratedItems"] == ["Movie"], policy["BlockUnratedItems"]
            assert policy["AccessSchedules"] and policy["AccessSchedules"][0]["DayOfWeek"] == "Weekend", policy["AccessSchedules"]
            assert policy["AccessSchedules"][0]["StartHour"] == 8 and policy["AccessSchedules"][0]["EndHour"] == 20.5
            assert policy.get("MaxParentalRating") is not None, chosen
            assert policy["EnableAllFolders"] is False

            # The password tab is the form of the user's own profile page.
            page.click(".jf-dashboard-tabs .mud-tab >> nth=3")
            page.wait_for_selector(".readOnlyContent form", timeout=15_000)
            assert page.evaluate("location.hash") == f"#/dashboard/users/{created['Id']}/password"
            page.wait_for_timeout(300)
            shot(page, "dashboard-user-password")

        def user_delete():
            open_page(page, "/dashboard/users", f"#userProfilesPage .card[data-username='{user_name}']")
            page.click(f".card[data-username='{user_name}'] .btnUserMenu")
            page.wait_for_selector(".actionSheetMenuItem[data-id=delete]", timeout=10_000)
            page.wait_for_timeout(300)
            shot(page, "dashboard-users-menu")
            page.click(".actionSheetMenuItem[data-id=delete]")
            page.wait_for_selector("button.btnOption[data-id=ok]", timeout=10_000)
            page.click("button.btnOption[data-id=ok]")
            page.wait_for_selector(f"#userProfilesPage .card[data-username='{user_name}']", state="detached", timeout=15_000)
            assert created_user() is None, "the user is still on the server"
            assert page.locator(".card[data-username='admin']").count() == 1

        def keys():
            open_page(page, "/dashboard/keys", ".jf-dashboard-keys-add")
            page.wait_for_selector(".mud-table-loading-progress", state="detached", timeout=15_000)
            page.click(".jf-dashboard-keys-add")
            page.wait_for_selector("#jf-dashboard-dialog-input", timeout=10_000)
            page.fill("#jf-dashboard-dialog-input", key_name)
            page.wait_for_timeout(300)
            shot(page, "dashboard-keys-dialog")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            dialog_closed(page)
            page.fill("#jf-dashboard-keys-search", key_name)
            page.wait_for_selector(f".jf-dashboard-table-actions[data-app='{key_name}']", timeout=15_000)
            assert page.locator("#apiKeysPage tbody tr").count() == 1
            created = [k for k in get("/Auth/Keys")["Items"] if k.get("AppName") == key_name]
            assert len(created) == 1, created
            assert page.inner_text(".jf-dashboard-key-token").strip() == created[0]["AccessToken"]
            page.fill("#jf-dashboard-keys-search", "")
            page.wait_for_timeout(500)
            shot(page, "dashboard-keys")
            fits(page, "keys")
            page.fill("#jf-dashboard-keys-search", key_name)
            page.wait_for_function("document.querySelectorAll('#apiKeysPage tbody tr').length === 1", timeout=10_000)
            # Revoking asks first.
            page.click(f".jf-dashboard-table-actions[data-app='{key_name}'] .jf-dashboard-key-revoke")
            page.wait_for_selector(".jf-dashboard-dialog-form", timeout=10_000)
            page.click(".jf-dashboard-dialog-form [data-id=cancel]")
            dialog_closed(page)
            assert page.locator(f".jf-dashboard-table-actions[data-app='{key_name}']").count() == 1
            page.click(f".jf-dashboard-table-actions[data-app='{key_name}'] .jf-dashboard-key-revoke")
            page.wait_for_selector(".jf-dashboard-dialog-form", timeout=10_000)
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            page.wait_for_selector(f".jf-dashboard-table-actions[data-app='{key_name}']", state="detached", timeout=15_000)
            assert not [k for k in get("/Auth/Keys")["Items"] if k.get("AppName") == key_name]

        def devices():
            open_page(page, "/dashboard/devices", "#devicesPage tbody tr")
            shot(page, "dashboard-devices")
            fits(page, "devices")
            # This browser is a device too: it can be renamed but not deleted.
            own = page.locator(".jf-dashboard-table-actions:has(.jf-dashboard-device-delete[disabled])")
            assert own.count() == 1, f"{own.count()} rows with a disabled delete button"
            device_id = own.get_attribute("data-deviceid")
            own.locator(".jf-dashboard-device-edit").click()
            page.wait_for_selector("#jf-dashboard-dialog-input", timeout=10_000)
            page.fill("#jf-dashboard-dialog-input", DEVICE_NAME)
            page.wait_for_timeout(300)
            shot(page, "dashboard-devices-rename")
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            dialog_closed(page)
            page.wait_for_function(
                "name => [...document.querySelectorAll('.jf-dashboard-device-cell-name')].some(e => e.textContent.trim() === name)",
                arg=DEVICE_NAME, timeout=15_000)
            stored = next(d for d in get("/Devices")["Items"] if d["Id"] == device_id)
            assert stored.get("CustomName") == DEVICE_NAME, stored
            # Searching narrows the list down.
            page.fill("#jf-dashboard-devices-search", DEVICE_NAME)
            page.wait_for_function("document.querySelectorAll('#devicesPage tbody tr').length === 1", timeout=10_000)
            page.fill("#jf-dashboard-devices-search", "")
            # Deleting every device would sign everybody else out: only open the question.
            page.click(".jf-dashboard-devices-delete-all")
            page.wait_for_selector(".jf-dashboard-dialog-form", timeout=10_000)
            page.click(".jf-dashboard-dialog-form [data-id=cancel]")
            dialog_closed(page)

        def activity():
            open_page(page, "/dashboard/activity", "#serverActivityPage tbody tr")
            shot(page, "dashboard-activity")
            fits(page, "activity")
            assert page.locator("#serverActivityPage thead th").count() == 7
            # System entries have no user column.
            page.click(".jf-dashboard-activity-views .mud-toggle-item >> nth=2")
            page.wait_for_function("document.querySelectorAll('#serverActivityPage thead th').length === 6", timeout=10_000)
            assert page.evaluate("location.hash").endswith("useractivity=false"), page.evaluate("location.hash")
            page.click(".jf-dashboard-activity-views .mud-toggle-item >> nth=1")
            page.wait_for_function("document.querySelectorAll('#serverActivityPage thead th').length === 7", timeout=10_000)
            page.wait_for_selector("#serverActivityPage tbody tr", timeout=15_000)
            # The filters of the columns.
            page.click(".jf-dashboard-table-filter-toggle")
            page.wait_for_selector(".jf-dashboard-activity-filter-name", timeout=5_000)
            page.fill(".jf-dashboard-activity-filter-name", "no such entry at all")
            page.keyboard.press("Enter")
            page.wait_for_function("document.querySelectorAll('#serverActivityPage tbody tr .jf-dashboard-table-date').length === 0", timeout=15_000)
            page.fill(".jf-dashboard-activity-filter-name", "")
            page.keyboard.press("Enter")
            page.wait_for_selector("#serverActivityPage tbody tr .jf-dashboard-table-date", timeout=15_000)
            page.wait_for_timeout(300)
            shot(page, "dashboard-activity-filters")

        def logs():
            open_page(page, "/dashboard/logs", ".jf-dashboard-log-item")
            shot(page, "dashboard-logs")
            fits(page, "logs")
            assert page.is_checked(".jf-dashboard-logs-warn input") == bool(configuration.get("EnableSlowResponseWarning"))
            threshold = (configuration.get("SlowResponseThresholdMs") or 500) + 77
            if not page.is_checked(".jf-dashboard-logs-warn input"):
                page.click(".jf-dashboard-logs-warn input")
            type_into(page, "#jf-dashboard-logs-threshold", str(threshold))
            page.click(".jf-dashboard-submit")
            page.wait_for_selector(".jf-dashboard-saved", timeout=15_000)
            saved = get("/System/Configuration")
            assert saved["SlowResponseThresholdMs"] == threshold, saved["SlowResponseThresholdMs"]
            assert saved["EnableSlowResponseWarning"] is True
            assert saved.get("ServerName") == configuration.get("ServerName")
            type_into(page, "#jf-dashboard-logs-threshold", str(configuration.get("SlowResponseThresholdMs") or 500))
            if not configuration.get("EnableSlowResponseWarning"):
                page.click(".jf-dashboard-logs-warn input")
            page.click(".jf-dashboard-submit")
            page.wait_for_timeout(1500)
            assert get("/System/Configuration")["SlowResponseThresholdMs"] == configuration.get("SlowResponseThresholdMs")

            # A log file as text.
            name = page.locator(".jf-dashboard-log-item h3").first.inner_text().strip()
            page.click(".jf-dashboard-log-item >> nth=0")
            page.wait_for_selector(".jf-dashboard-log-text", timeout=30_000)
            assert page.inner_text("#logPage h1").strip() == name
            assert title(page) == name, title(page)
            assert len(page.inner_text(".jf-dashboard-log-text")) > 100
            page.wait_for_timeout(300)
            shot(page, "dashboard-log-file")
            fits(page, "log file")
            no_toast(page)
            page.click(".jf-dashboard-log-copy >> nth=0")
            assert toast(page) == "Log contents copied successfully.", toast(page)
            copied = page.evaluate("navigator.clipboard.readText()")
            assert copied[:200] == page.inner_text(".jf-dashboard-log-text")[:200], copied[:80]
            with page.expect_download(timeout=15_000) as download:
                page.click(".jf-dashboard-log-download >> nth=0")
            assert download.value.suggested_filename == name, download.value.suggested_filename
            before = page.inner_text(".jf-dashboard-log-watch >> nth=0").strip()
            page.click(".jf-dashboard-log-watch >> nth=0")
            page.wait_for_function("t => document.querySelector('.jf-dashboard-log-watch').textContent.trim() !== t", arg=before, timeout=5_000)
            page.click(".jf-dashboard-log-watch >> nth=0")
            page.wait_for_function("t => document.querySelector('.jf-dashboard-log-watch').textContent.trim() === t", arg=before, timeout=5_000)

        def tasks_page():
            open_page(page, "/dashboard/tasks", ".jf-dashboard-task")
            shot(page, "dashboard-tasks")
            fits(page, "tasks")
            visible = [t for t in tasks if not t.get("IsHidden")]
            assert page.locator(".jf-dashboard-task").count() == len(visible), (page.locator(".jf-dashboard-task").count(), len(visible))
            assert page.locator(".jf-dashboard-task-category").count() == len({t["Category"] for t in visible})
            # Running a task: the button starts it, and the page follows it until it is done.
            before = (get(task_path).get("LastExecutionResult") or {}).get("EndTimeUtc")
            row = f".jf-dashboard-task[data-taskid='{task['Id']}']"
            page.click(row + " .jf-dashboard-task-action")
            deadline = time.time() + 60
            while time.time() < deadline:
                now = get(task_path)
                if now["State"] == "Idle" and (now.get("LastExecutionResult") or {}).get("EndTimeUtc") != before:
                    break
                page.wait_for_timeout(500)
            else:
                raise AssertionError("the task did not run")
            page.wait_for_selector(row + " .jf-dashboard-task-last-ran", timeout=20_000)
            page.wait_for_function(
                "s => /second|minute/.test(document.querySelector(s).textContent)", arg=row + " .jf-dashboard-task-last-ran", timeout=20_000)
            page.wait_for_timeout(300)
            shot(page, "dashboard-tasks-after-run")

        def task_edit():
            open_page(page, "/dashboard/tasks", ".jf-dashboard-task")
            page.click(f".jf-dashboard-task[data-taskid='{task['Id']}'] .jf-dashboard-task-link")
            page.wait_for_selector(".jf-dashboard-task-title", timeout=20_000)
            assert page.inner_text(".jf-dashboard-task-title").strip() == task["Name"]
            # As in the original, the page of a task has no title of its own.
            assert title(page) == "", title(page)
            assert page.locator(".jf-dashboard-trigger").count() == len(task_triggers)
            page.click(".jf-dashboard-trigger-add")
            page.wait_for_selector("form.jf-dashboard-trigger-dialog", timeout=10_000)
            page.click(".jf-dashboard-trigger-type")
            page.wait_for_selector(".mud-popover-open .mud-list-item", timeout=10_000)
            page.wait_for_timeout(300)
            shot(page, "dashboard-task-trigger-types")
            assert page.locator(".mud-popover-open .mud-list-item").count() == 4
            page.click(".mud-popover-open .mud-list-item >> nth=2")
            page.wait_for_selector(".jf-dashboard-trigger-interval", timeout=10_000)
            assert page.locator(".jf-dashboard-trigger-time").count() == 0
            type_into(page, "#jf-dashboard-trigger-limit", "2")
            page.wait_for_timeout(300)
            shot(page, "dashboard-task-trigger-dialog")
            page.click("form.jf-dashboard-trigger-dialog [data-id=ok]")
            dialog_closed(page, "form.jf-dashboard-trigger-dialog")
            page.wait_for_function("n => document.querySelectorAll('.jf-dashboard-trigger').length === n", arg=len(task_triggers) + 1, timeout=15_000)
            stored = get(task_path)["Triggers"]
            assert len(stored) == len(task_triggers) + 1, stored
            assert stored[-1]["Type"] == "IntervalTrigger" and stored[-1]["MaxRuntimeTicks"] == 2 * HOUR_TICKS, stored[-1]
            assert stored[:-1] == task_triggers, stored
            page.wait_for_timeout(300)
            shot(page, "dashboard-task")
            fits(page, "task")
            # Removing asks first.
            page.click(".jf-dashboard-trigger-remove >> nth=-1")
            page.wait_for_selector(".jf-dashboard-dialog-form", timeout=10_000)
            page.click(".jf-dashboard-dialog-form [data-id=ok]")
            page.wait_for_function("n => document.querySelectorAll('.jf-dashboard-trigger').length === n", arg=len(task_triggers), timeout=15_000)
            assert (get(task_path).get("Triggers") or []) == task_triggers
            # The address of the original's old links works as well.
            open_page(page, f"/dashboard/tasks/edit?id={task['Id']}", ".jf-dashboard-task-title")
            assert page.inner_text(".jf-dashboard-task-title").strip() == task["Name"]

        def phone():
            page.set_viewport_size({"width": 390, "height": 844})
            page.wait_for_timeout(500)
            for name, route, selector in PAGES:
                open_page(page, route, selector)
                page.wait_for_timeout(500)
                shot(page, f"dashboard-phone-{name}")
                fits(page, f"phone {name}")
                assert page.locator(".jf-dashboard-drawer").count() == 0, "the fixed drawer is shown on a phone"
            admin = get(f"/Users/{user_id}")
            for tab, selector in (("profile", "form.editUserProfileForm"), ("access", "form.userLibraryAccessForm"),
                                  ("parentalcontrol", "form.userParentalControlForm"), ("password", ".readOnlyContent form")):
                open_page(page, f"/dashboard/users/{tab}?userId={admin['Id']}", selector)
                page.wait_for_timeout(500)
                shot(page, f"dashboard-phone-user-{tab}")
                fits(page, f"phone user {tab}")
            open_page(page, f"/dashboard/tasks/{task['Id']}", ".jf-dashboard-task-title")
            page.click(".jf-dashboard-trigger-add")
            page.wait_for_selector("form.jf-dashboard-trigger-dialog", timeout=10_000)
            page.wait_for_timeout(400)
            shot(page, "dashboard-phone-task-trigger-dialog")
            fits(page, "phone trigger dialog")
            page.click("form.jf-dashboard-trigger-dialog [data-id=cancel]")
            dialog_closed(page, "form.jf-dashboard-trigger-dialog")
            shot(page, "dashboard-phone-task")
            open_page(page, "/dashboard/logs", ".jf-dashboard-log-item")
            page.click(".jf-dashboard-log-item >> nth=0")
            page.wait_for_selector(".jf-dashboard-log-text", timeout=30_000)
            page.wait_for_timeout(400)
            shot(page, "dashboard-phone-log-file")
            fits(page, "phone log file")

            # The drawer slides in from the menu button and closes when a link is followed.
            open_page(page, "/dashboard", ".jf-dashboard-server-name")
            page.click(".jf-drawer-button")
            link = ".jf-dashboard-drawer-temporary a[href='#/dashboard/keys']"
            page.wait_for_selector(link, timeout=10_000)
            page.wait_for_function("s => document.querySelector(s).getBoundingClientRect().left >= 0", arg=link, timeout=10_000)
            page.wait_for_timeout(500)
            shot(page, "dashboard-phone-drawer")
            assert page.get_attribute(".jf-dashboard-drawer-temporary a.active", "href") == "#/dashboard"
            page.click(link)
            page.wait_for_selector(".jf-dashboard-keys-add", timeout=20_000)
            page.wait_for_function(
                "() => { const d = document.querySelector('.jf-dashboard-drawer-temporary'); return !d || d.getBoundingClientRect().right <= 0; }",
                timeout=10_000)
            assert title(page) == "API Keys", title(page)
            page.set_viewport_size({"width": 1366, "height": 768})
            page.wait_for_selector(".jf-dashboard-drawer", timeout=10_000)

        try:
            cleanup()
            steps.run("entry from the app", entry)
            steps.run("index", index)
            steps.run("drawer", drawer)
            steps.run("settings", settings)
            steps.run("branding", branding_page)
            if steps.run("users and add user", users):
                steps.run("user profile", user_profile)
                steps.run("user access", user_access)
                steps.run("user parental control", user_parental)
                steps.run("user delete", user_delete)
            steps.run("api keys", keys)
            steps.run("devices", devices)
            steps.run("activity", activity)
            steps.run("logs", logs)
            steps.run("tasks", tasks_page)
            steps.run("task triggers", task_edit)
            steps.run("phone", phone)
        finally:
            cleanup()

        drop_console_noise(page)
        code = steps.result(finish(page, "dashboard"))
        browser.close()
        return code


if __name__ == "__main__":
    sys.exit(main())
