"""User settings pages against the shared test server.

Changes display preferences of the signed-in user only and puts language and theme back at the end.
The profile image and the password are exercised by wizard.py on a throwaway server instead.
"""
import json
import sys

from playwright.sync_api import sync_playwright

import lib
from lib import SERVER, finish, go, new_page, shot, sign_in
from sessionlib import DEFAULT_CONFIG, Steps, drop_console_noise, use_default_config

# The unknown Quick Connect code is refused on purpose.
lib.IGNORED_FAILURES = ("/QuickConnect/Authorize",)
SAVED = "Settings saved."


def credentials(page):
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
    server = stored["Servers"][0]
    return server["UserId"], server["AccessToken"]


def api(page, method, path, token, body=None):
    """A request to the test server outside the app."""
    headers = {"Authorization": f'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-usersettings", Version="1", Token="{token}"'}
    if body is not None:
        headers["Content-Type"] = "application/json"
        return page.request.fetch(SERVER + path, method=method, headers=headers, data=json.dumps(body))
    return page.request.fetch(SERVER + path, method=method, headers=headers)


def local(page, user_id, name):
    """A per-user setting kept in the browser only (most display settings never reach the server)."""
    return page.evaluate("k => localStorage.getItem(k)", f"{user_id}-{name}")


def open_page(page, route, selector):
    go(page, route)
    page.wait_for_selector(selector, timeout=20_000)
    page.wait_for_selector(".docspinner.mdlSpinnerActive", state="detached", timeout=20_000)
    page.wait_for_timeout(700)


def reload_page(page, route, selector):
    page.reload()
    page.wait_for_selector(".page", timeout=120_000)
    open_page(page, route, selector)


def save(page, button=".btnSave"):
    page.wait_for_selector(".toast", state="detached", timeout=10_000)
    page.click(button)
    page.wait_for_selector(".toast", timeout=15_000)
    text = page.inner_text(".toast")
    return text


def check(page, selector, value):
    """Sets a checkbox (the inputs are visually replaced by their labels)."""
    box = page.locator(selector).first
    if box.is_checked() != value:
        box.evaluate("e => e.click()")
    assert box.is_checked() == value, f"{selector} did not change"


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        use_default_config(page)
        steps = Steps(page, "usersettings")
        sign_in(page)
        user_id, token = credentials(page)
        prefs_path = f"/DisplayPreferences/usersettings?userId={user_id}&client=emby"
        original_prefs = api(page, "GET", prefs_path, token).json()
        original_custom = dict(original_prefs.get("CustomPrefs") or {})
        original_config = api(page, "GET", f"/Users/{user_id}", token).json()["Configuration"]
        local_keys = ("language", "appTheme", "libraryPageSize", "detailsBanner")
        original_local = {name: local(page, user_id, name) for name in local_keys}
        default_theme = next(theme["id"] for theme in DEFAULT_CONFIG["themes"] if theme.get("default"))
        print("original language/theme:", original_local["language"], original_local["appTheme"])

        def menu():
            open_page(page, "/mypreferencesmenu", "#myPreferencesMenuPage")
            shot(page, "usersettings-menu")
            assert page.inner_text(".headerUsername").strip() == "admin", page.inner_text(".headerUsername")
            for name in ("lnkUserProfile", "lnkDisplayPreferences", "lnkHomePreferences",
                         "lnkPlaybackPreferences", "lnkSubtitlePreferences", "lnkControlsPreferences"):
                link = page.locator("." + name)
                assert link.count() == 1 and link.is_visible(), name + " missing"
                assert f"userId={user_id}" in link.get_attribute("href"), link.get_attribute("href")
            # As in the original's modern layout these entries are rendered but hidden: the user menu
            # of the app bar offers them (apps/modern/AppOverrides.scss).
            for name in ("lnkQuickConnectPreferences", "adminSection", "userSection", "btnLogout"):
                entry = page.locator("#myPreferencesMenuPage ." + name)
                assert entry.count() == 1, name + " missing"
                assert not entry.is_visible(), name + " is visible in the modern layout"
            assert f"userId={user_id}" in page.locator(".lnkQuickConnectPreferences").get_attribute("href")
            page.click(".lnkDisplayPreferences")
            page.wait_for_selector("#displayPreferencesPage", timeout=10_000)

        def display():
            open_page(page, "/mypreferencesdisplay", "#displayPreferencesPage")
            shot(page, "usersettings-display")
            assert page.locator("#selectLanguage option").count() > 50
            assert page.locator("#selectTheme option").count() >= 5
            theme_before = page.input_value("#selectTheme")
            new_theme = "light" if theme_before != "light" else "purplehaze"
            page.select_option("#selectTheme", new_theme)
            page.select_option("#selectLanguage", "de")
            page.fill("#txtLibraryPageSize", "60")
            check(page, "#chkDetailsBanner", False)
            text = save(page)
            # The language applies at once, toast included.
            assert "gespeichert" in text.lower() or text == SAVED, text
            page.wait_for_function(
                "t => (document.querySelector('link#cssTheme') || {href: ''}).href.includes('/themes/' + t + '/')", arg=new_theme, timeout=10_000)
            page.wait_for_function("document.querySelector('.btnSave').textContent.trim() === 'Speichern'", timeout=10_000)
            page.wait_for_timeout(500)
            shot(page, "usersettings-display-german-" + new_theme)

            # These four are browser settings of the user (userSettings.set(..., false) in the original).
            stored = {name: local(page, user_id, name) for name in local_keys}
            assert stored == {"language": "de", "appTheme": new_theme, "libraryPageSize": "60", "detailsBanner": "false"}, stored
            on_server = api(page, "GET", prefs_path, token).json()["CustomPrefs"]
            assert "language" not in on_server and "appTheme" not in on_server, on_server

            reload_page(page, "/mypreferencesdisplay", "#displayPreferencesPage")
            assert page.input_value("#selectLanguage") == "de"
            assert page.input_value("#selectTheme") == new_theme
            assert page.input_value("#txtLibraryPageSize") == "60"
            assert not page.is_checked("#chkDetailsBanner")
            assert page.inner_text(".btnSave").strip() == "Speichern"
            assert f"/themes/{new_theme}/" in page.get_attribute("link#cssTheme", "href")

        def restore_display():
            open_page(page, "/mypreferencesdisplay", "#displayPreferencesPage")
            page.select_option("#selectLanguage", original_local["language"] or "")
            page.select_option("#selectTheme", original_local["appTheme"] or default_theme)
            page.fill("#txtLibraryPageSize", original_local["libraryPageSize"] or "100")
            check(page, "#chkDetailsBanner", original_local["detailsBanner"] != "false")
            save(page)
            page.wait_for_function("document.querySelector('.btnSave').textContent.trim() === 'Save'", timeout=10_000)
            page.wait_for_function(
                "t => (document.querySelector('link#cssTheme') || {href: ''}).href.includes('/themes/' + t + '/')",
                arg=original_local["appTheme"] or default_theme, timeout=10_000)
            page.wait_for_timeout(300)
            shot(page, "usersettings-display-restored")

        def home():
            open_page(page, "/mypreferenceshome", "#homeScreenPreferencesPage")
            page.wait_for_selector(".viewOrderList .viewItem", timeout=15_000)
            page.wait_for_selector(".folderGroupList .chkGroupFolder", state="attached", timeout=15_000)
            shot(page, "usersettings-home")
            assert page.locator("select[id^=selectHomeSection]").count() == 10
            # The default of a section is the option with the empty value, as in the original.
            assert page.input_value("#selectHomeSection1") == "", page.input_value("#selectHomeSection1")
            assert page.locator("#selectHomeSection1 option:checked").inner_text().strip() == "My Media"
            assert page.locator("#selectHomeSection1 option[value=smalllibrarytiles]").count() == 0
            assert page.locator(".perLibrarySettings .verticalSection").count() >= 2

            # Library order: moving the second library up swaps the first two (nothing is saved).
            names = page.locator(".viewOrderList .viewItem .listItemBody").all_inner_texts()
            page.click(".viewOrderList .viewItem >> nth=1 >> .btnViewItemUp")
            moved = page.locator(".viewOrderList .viewItem .listItemBody").all_inner_texts()
            assert moved[:2] == [names[1], names[0]], (names, moved)

            # Leaving "My Media" hides the "other sections" checkbox of that library.
            # (Only channels, collections and playlists have that checkbox; the test server may have none.)
            if page.locator(".chkIncludeInMyMedia").count():
                first = page.locator(".chkIncludeInMyMedia").first
                folder = first.get_attribute("data-folderid")
                latest = page.locator(f".chkIncludeInLatest[data-folderid='{folder}']")
                if latest.count():
                    label = latest.locator("xpath=ancestor::label")
                    first.evaluate("e => e.click()")
                    assert "hide" not in (label.get_attribute("class") or "").split(), "fldIncludeInLatest stays hidden"
                    first.evaluate("e => e.click()")
            assert page.locator(".chkIncludeInLatest").count() >= 1
            assert page.locator(".selectLanding[data-folderid]").count() >= 1

            before = page.input_value("#selectHomeSection10")
            page.select_option("#selectHomeSection10", "livetv")
            reload_and_check = "livetv"
            reload_page(page, "/mypreferenceshome", "#homeScreenPreferencesPage")
            assert page.input_value("#selectHomeSection10") == before, "an unsaved change survived a reload"
            page.select_option("#selectHomeSection10", reload_and_check)
            assert save(page) == SAVED
            stored = api(page, "GET", prefs_path, token).json()["CustomPrefs"]
            assert stored.get("homesection9") == "livetv", stored
            reload_page(page, "/mypreferenceshome", "#homeScreenPreferencesPage")
            assert page.input_value("#selectHomeSection10") == "livetv"
            page.select_option("#selectHomeSection10", before)
            assert save(page) == SAVED

        def playback():
            open_page(page, "/mypreferencesplayback", "#languagePreferencesPage")
            page.wait_for_function("document.querySelectorAll('#selectAudioLanguage option').length > 20", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "usersettings-playback")
            assert page.locator(".selectVideoInNetworkQuality option").count() > 10
            assert page.locator(".segmentTypeAction").count() == 5
            visible = [name for name in ("fldVideoInNetworkQuality", "fldVideoInternetQuality") if page.locator("." + name).is_visible()]
            assert len(visible) == 1, visible
            before = page.input_value(".selectSkipForwardLength")
            new_value = "15000" if before != "15000" else "20000"
            page.select_option(".selectSkipForwardLength", new_value)
            page.select_option("#segmentTypeAction__Recap", "Skip")
            page.select_option("#selectAudioLanguage", "deu")
            assert save(page) == SAVED
            stored = api(page, "GET", prefs_path, token).json()["CustomPrefs"]
            assert stored.get("skipForwardLength") == new_value, stored
            config = api(page, "GET", f"/Users/{user_id}", token).json()["Configuration"]
            assert config["AudioLanguagePreference"] == "deu", config
            reload_page(page, "/mypreferencesplayback", "#languagePreferencesPage")
            page.wait_for_function("document.querySelectorAll('#selectAudioLanguage option').length > 20", timeout=15_000)
            assert page.input_value(".selectSkipForwardLength") == new_value
            assert page.input_value("#segmentTypeAction__Recap") == "Skip"
            assert page.input_value("#selectAudioLanguage") == "deu"
            page.select_option(".selectSkipForwardLength", before)
            page.select_option("#segmentTypeAction__Recap", "None")
            page.select_option("#selectAudioLanguage", original_config.get("AudioLanguagePreference") or "")
            assert save(page) == SAVED

        def subtitles():
            open_page(page, "/mypreferencessubtitles", ".subtitlesettings")
            page.wait_for_function("document.querySelectorAll('#selectSubtitleLanguage option').length > 20", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "usersettings-subtitles")
            mode = page.input_value("#selectSubtitlePlaybackMode")
            assert page.locator(f".subtitles{mode or 'Default'}Help").is_visible(), mode
            page.select_option("#selectSubtitlePlaybackMode", "Always")
            assert page.locator(".subtitlesAlwaysHelp").is_visible()
            assert not page.locator(f".subtitles{mode or 'Default'}Help").is_visible() or mode == "Always"

            size_before = page.input_value("#selectTextSize")
            page.select_option("#selectTextSize", "larger")
            style = page.get_attribute(".subtitleappearance-preview-text", "style")
            assert "2em" in style, style
            page.select_option("#selectDropShadow", "raised")
            page.select_option("#selectSubtitleStyling", "Custom")

            # Dragging the position slider shows the full-size preview for a moment.
            assert page.locator(".subtitleappearance-fullpreview-hide").count() == 1
            page.locator("#sliderVerticalPosition").evaluate(
                "e => { e.value = '-6'; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }")
            page.wait_for_selector(".subtitleappearance-fullpreview:not(.subtitleappearance-fullpreview-hide)", timeout=5_000)
            shot(page, "usersettings-subtitles-fullpreview")
            page.wait_for_selector(".subtitleappearance-fullpreview.subtitleappearance-fullpreview-hide", state="attached", timeout=5_000)

            assert save(page) == SAVED
            config = api(page, "GET", f"/Users/{user_id}", token).json()["Configuration"]
            assert config["SubtitleMode"] == "Always", config
            reload_page(page, "/mypreferencessubtitles", ".subtitlesettings")
            assert page.input_value("#selectSubtitlePlaybackMode") == "Always"
            assert page.input_value("#selectTextSize") == "larger"
            assert page.input_value("#sliderVerticalPosition") == "-6"
            assert "2em" in page.get_attribute(".subtitleappearance-preview-text", "style")
            shot(page, "usersettings-subtitles-saved")

            page.select_option("#selectSubtitlePlaybackMode", original_config.get("SubtitleMode") or "Default")
            page.select_option("#selectTextSize", size_before)
            page.select_option("#selectDropShadow", "")
            page.select_option("#selectSubtitleStyling", "Auto")
            page.locator("#sliderVerticalPosition").evaluate(
                "e => { e.value = '-3'; e.dispatchEvent(new Event('input', {bubbles: true})); e.dispatchEvent(new Event('change', {bubbles: true})); }")
            assert save(page) == SAVED

        def controls():
            open_page(page, "/mypreferencescontrols", "#controlsPreferencesPage")
            shot(page, "usersettings-controls")
            before = page.is_checked(".chkEnableGamepad")
            check(page, ".chkEnableGamepad", not before)
            assert save(page) == SAVED
            reload_page(page, "/mypreferencescontrols", "#controlsPreferencesPage")
            assert page.is_checked(".chkEnableGamepad") == (not before)
            check(page, ".chkEnableGamepad", before)
            assert save(page) == SAVED

        def profile():
            open_page(page, "/userprofile", "#userProfilePage")
            shot(page, "usersettings-profile")
            assert page.inner_text(".username").strip() == "admin"
            assert page.locator("#btnAddImage").is_visible() != page.locator("#btnDeleteImage").is_visible()
            assert page.locator("form.updatePasswordForm").is_visible()
            assert not page.locator("#btnResetPassword").is_visible(), "administrators cannot reset their password"
            # A mismatch never reaches the server (the admin password must stay as it is).
            requests = []
            page.on("request", lambda r: requests.append(r.url) if "/Users/Password" in r.url else None)
            page.fill("#txtCurrentPassword", "x")
            page.fill("#txtNewPassword", "one")
            page.fill("#txtNewPasswordConfirm", "two")
            page.wait_for_selector(".toast", state="detached", timeout=10_000)
            page.click("form.updatePasswordForm button[type=submit]")
            page.wait_for_selector(".toast", timeout=5_000)
            assert "must match" in page.inner_text(".toast"), page.inner_text(".toast")
            assert not requests, requests

        def quick_connect():
            open_page(page, "/quickconnect", "#quickConnectPreferencesPage")
            shot(page, "usersettings-quickconnect")
            assert page.input_value("#userId").replace("-", "") == user_id.replace("-", "")
            page.fill("#txtQuickConnectCode", "000000")
            page.click("#quickConnectPreferencesPage button[type=submit]")
            page.wait_for_selector(".quickConnectError", timeout=10_000)
            assert page.inner_text(".quickConnectError").strip() == "Unknown Quick Connect code"

            initiated = page.request.post(
                SERVER + "/QuickConnect/Initiate",
                headers={"Authorization": 'MediaBrowser Client="e2e", Device="e2e-device", DeviceId="e2e-quickconnect", Version="1"'})
            assert initiated.ok, initiated.status
            code = initiated.json()["Code"]
            secret = initiated.json()["Secret"]
            page.fill("#txtQuickConnectCode", code[:3] + " " + code[3:])
            page.click("#quickConnectPreferencesPage button[type=submit]")
            page.wait_for_selector("#quickConnectPreferencesPage a[href='#/home']", timeout=10_000)
            assert "successfully authenticated" in page.inner_text("#quickConnectPreferencesPage")
            shot(page, "usersettings-quickconnect-authorized")
            state = page.request.get(SERVER + "/QuickConnect/Connect?secret=" + secret).json()
            assert state["Authenticated"] is True, state

        def menu_for_other_user():
            users = api(page, "GET", "/Users", token).json()
            others = [u for u in users if u["Id"] != user_id]
            if not others:
                print("  (no second user on the server; skipped)")
                return
            other = others[0]
            open_page(page, "/mypreferencesmenu?userId=" + other["Id"], "#myPreferencesMenuPage")
            page.wait_for_function("n => document.querySelector('.headerUsername').textContent.trim() === n", arg=other["Name"], timeout=10_000)
            assert other["Id"] in page.locator(".lnkPlaybackPreferences").get_attribute("href")
            shot(page, "usersettings-menu-other-user")

        try:
            steps.run("menu", menu)
            steps.run("display", display)
        finally:
            steps.run("restore display", restore_display)
        steps.run("home", home)
        steps.run("playback", playback)
        steps.run("subtitles", subtitles)
        steps.run("controls", controls)
        steps.run("profile", profile)
        steps.run("quick connect", quick_connect)
        steps.run("menu for another user", menu_for_other_user)

        # Whatever happened above: language, theme and the other display preferences are as they were.
        final = api(page, "GET", prefs_path, token).json()
        final_custom = final.get("CustomPrefs") or {}
        keys = ("skipForwardLength", "homesection9")

        def stored(prefs, key):
            # The server keeps a home section once it was saved; "none" is the default of the last one.
            value = prefs.get(key)
            return None if key == "homesection9" and value in ("", "none") else value

        if any(stored(final_custom, k) != stored(original_custom, k) for k in keys):
            print("restoring display preferences through the API:",
                  {k: (final_custom.get(k), original_custom.get(k)) for k in keys if stored(final_custom, k) != stored(original_custom, k)})
            for k in keys:
                if k in original_custom:
                    final_custom[k] = original_custom[k]
                else:
                    final_custom.pop(k, None)
            final["CustomPrefs"] = final_custom
            response = api(page, "POST", prefs_path, token, final)
            assert response.ok, response.status
        config = api(page, "GET", f"/Users/{user_id}", token).json()["Configuration"]
        changed = {k: (config.get(k), v) for k, v in original_config.items() if config.get(k) != v}
        if changed:
            print("restoring the user configuration through the API:", changed)
            response = api(page, "POST", f"/Users/Configuration?userId={user_id}", token, original_config)
            assert response.ok, response.status

        final_local = {name: local(page, user_id, name) for name in local_keys}
        for name in local_keys:
            # Unset and the default are the same thing for these; anything else must be as it was.
            if final_local[name] != original_local[name] and original_local[name] is not None:
                steps.failures.append(f"local setting {name} is {final_local[name]!r}, was {original_local[name]!r}")

        drop_console_noise(page)
        code = steps.result(finish(page, "usersettings"))
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
