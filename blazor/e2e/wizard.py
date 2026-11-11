"""Startup wizard against a fresh server (JELLYFIN_URL=http://localhost:18097 ./dev e2e wizard.py).

The server must not have completed its wizard; it needs a readable /media folder.
"""
import base64
import sys

from playwright.sync_api import sync_playwright

import lib
from lib import SERVER, finish, go, new_page, open_app, shot
from sessionlib import Steps, close_alert, drop_console_noise, use_default_config

ADMIN = "wizadmin"
ADMIN_PASSWORD = "wizard-pass-1"
NEW_PASSWORD = "wizard-pass-2"
# A 1x1 PNG.
PNG = base64.b64decode("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==")

# A wrong current password is refused on purpose.
lib.IGNORED_FAILURES = ("/Users/Password",)


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        use_default_config(page)
        steps = Steps(page, "wizard")

        def start():
            open_app(page)
            if not page.locator("#wizardStartPage").count():
                go(page, "/addserver")
                page.wait_for_selector("#txtServerHost", timeout=30_000)
                page.fill("#txtServerHost", SERVER)
                page.click("form.addServerForm button[type=submit]")
            page.wait_for_selector("#wizardStartPage", timeout=30_000)
            page.wait_for_function("document.querySelectorAll('#selectLocalizationLanguage option').length > 10", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "wizard-1-start")
            assert page.input_value("#txtServerName") != "", "no server name"
            assert page.evaluate("location.hash").startswith("#/wizard/start"), page.evaluate("location.hash")
            page.fill("#txtServerName", "wizard-e2e")
            page.click("form.wizardStartForm button[type=submit]")
            page.wait_for_selector("#wizardUserPage", timeout=15_000)

        def user():
            page.wait_for_timeout(700)
            shot(page, "wizard-2-user")
            page.fill("#txtUsername", ADMIN)
            page.fill("#txtManualPassword", ADMIN_PASSWORD)
            page.fill("#txtPasswordConfirm", "something else")
            page.click("form.wizardUserForm button[type=submit]")
            page.wait_for_selector(".toast", timeout=5_000)
            assert "must match" in page.inner_text(".toast"), page.inner_text(".toast")
            assert page.locator("#wizardUserPage").count() == 1
            page.fill("#txtPasswordConfirm", ADMIN_PASSWORD)
            page.click("form.wizardUserForm button[type=submit]")
            page.wait_for_selector("#wizardLibraryPage", timeout=15_000)

        def library():
            page.wait_for_selector("#addLibrary", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "wizard-3-library-empty")
            assert page.locator("#divVirtualFolders .card").count() == 1

            page.click("#addLibrary")
            page.wait_for_selector(".dlg-librarycreator #selectCollectionType", timeout=10_000)

            # Without a folder the dialog refuses to create the library.
            page.select_option("#selectCollectionType", "movies")
            page.wait_for_function("document.querySelector('#txtValue').value !== ''", timeout=5_000)
            assert page.input_value("#txtValue") == "Movies", page.input_value("#txtValue")
            page.wait_for_selector(".libraryOptions:not(.hide)", timeout=5_000)
            page.wait_for_timeout(1000)
            shot(page, "wizard-3-library-dialog")
            page.click(".dlg-librarycreator .btnSubmit")
            page.wait_for_selector(".dialogContainer >> nth=1", timeout=5_000)
            assert "at least one folder" in page.inner_text(".dialogContainer >> nth=1"), page.inner_text(".dialogContainer >> nth=1")
            close_alert(page)

            page.click(".btnAddFolder")
            page.wait_for_selector("#txtDirectoryPickerPath", timeout=10_000)
            page.wait_for_selector(".results .lnkDirectory", timeout=10_000)
            shot(page, "wizard-3-directory-browser")
            page.click(".results .lnkDirectory[data-path='/media']")
            page.wait_for_function("document.querySelector('#txtDirectoryPickerPath').value === '/media'", timeout=10_000)
            page.click("#txtDirectoryPickerPath >> xpath=ancestor::form >> button[type=submit]")
            page.wait_for_selector("#txtDirectoryPickerPath", state="detached", timeout=10_000)
            page.wait_for_selector(".folderList .lnkPath", timeout=10_000)
            assert page.inner_text(".folderList .listItemBodyText").strip() == "/media"
            shot(page, "wizard-3-library-dialog-folder")

            page.click(".dlg-librarycreator .btnSubmit")
            page.wait_for_selector(".dlg-librarycreator", state="detached", timeout=30_000)
            page.wait_for_function("document.querySelectorAll('#divVirtualFolders .card').length === 2", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "wizard-3-library-added")
            text = page.inner_text("#divVirtualFolders .card >> nth=0")
            assert "Movies" in text and "/media" in text, text

            # Rename it through the card menu.
            page.click("#divVirtualFolders .btnCardMenu")
            page.wait_for_selector(".actionSheetMenuItem[data-id=rename]", timeout=5_000)
            shot(page, "wizard-3-library-menu")
            page.click(".actionSheetMenuItem[data-id=rename]")
            page.wait_for_selector("#txtInput", timeout=5_000)
            page.fill("#txtInput", "Films")
            page.click(".dialogContainer .btnSubmit")
            page.wait_for_function(
                "document.querySelector('#divVirtualFolders .card .cardText').textContent.trim() === 'Films'", timeout=20_000)

            # Remove it again.
            page.click("#divVirtualFolders .btnCardMenu")
            page.click(".actionSheetMenuItem[data-id=delete]")
            page.wait_for_selector(".dialogContainer button[data-id=ok]", timeout=5_000)
            shot(page, "wizard-3-library-remove-confirm")
            assert "/media" in page.inner_text(".dialogContainer"), page.inner_text(".dialogContainer")
            page.click(".dialogContainer button[data-id=ok]")
            page.wait_for_function("document.querySelectorAll('#divVirtualFolders .card').length === 1", timeout=20_000)

            page.click("#wizardLibraryPage .wizardNavigation .button-submit")
            page.wait_for_selector("#wizardSettingsPage", timeout=15_000)

        def settings():
            page.wait_for_function("document.querySelectorAll('#selectCountry option').length > 10", timeout=15_000)
            page.wait_for_timeout(500)
            shot(page, "wizard-4-settings")
            assert page.evaluate("location.hash").startswith("#/wizard/settings")
            page.select_option("#selectLanguage", "de")
            page.select_option("#selectCountry", "DE")
            page.click("form.wizardSettingsForm button[type=submit]")
            page.wait_for_selector("#chkRemoteAccess", timeout=15_000)

        def remote_access():
            page.wait_for_timeout(500)
            shot(page, "wizard-5-remoteaccess")
            assert page.evaluate("location.hash").startswith("#/wizard/remoteaccess")
            page.click("form.wizardSettingsForm button[type=submit]")
            page.wait_for_selector("#wizardFinishPage", timeout=15_000)

        def finish_wizard():
            page.wait_for_timeout(500)
            shot(page, "wizard-6-finish")
            page.click(".btnWizardNext")
            page.wait_for_selector("#loginPage", timeout=30_000)
            page.wait_for_timeout(1000)
            shot(page, "wizard-7-login")
            if page.locator(".btnManual").is_visible():
                page.click(".btnManual")
            page.fill("#txtManualName", ADMIN)
            page.fill("#txtManualPassword", ADMIN_PASSWORD)
            page.click("form.manualLoginForm button[type=submit]")
            page.wait_for_selector("#indexPage", timeout=30_000)
            # The wizard is closed for good.
            go(page, "/wizard/start")
            page.wait_for_selector("#indexPage", timeout=30_000)
            page.wait_for_timeout(500)
            assert "wizard" not in page.evaluate("location.hash"), page.evaluate("location.hash")

        def profile_image():
            go(page, "/userprofile")
            page.wait_for_selector("#userProfilePage", timeout=15_000)
            page.wait_for_selector("#btnAddImage", state="visible", timeout=10_000)
            assert page.inner_text(".username").strip() == ADMIN
            assert not page.locator("#btnDeleteImage").is_visible()
            page.set_input_files("#uploadImage", {"name": "avatar.png", "mimeType": "image/png", "buffer": PNG})
            page.wait_for_selector("#btnDeleteImage", state="visible", timeout=20_000)
            page.wait_for_function("document.querySelector('#image').style.backgroundImage.includes('/Images/Primary')", timeout=10_000)
            assert not page.locator("#btnAddImage").is_visible()
            page.wait_for_timeout(500)
            shot(page, "wizard-8-profile-image")
            page.click("#btnDeleteImage")
            page.wait_for_selector(".dialogContainer button[data-id=ok]", timeout=5_000)
            shot(page, "wizard-8-profile-delete-confirm")
            page.click(".dialogContainer button[data-id=ok]")
            page.wait_for_selector("#btnAddImage", state="visible", timeout=20_000)
            page.wait_for_function("document.querySelector('#image').style.backgroundImage.includes('avatar.png')", timeout=10_000)

        def password():
            page.fill("#txtCurrentPassword", "not the password")
            page.fill("#txtNewPassword", NEW_PASSWORD)
            page.fill("#txtNewPasswordConfirm", NEW_PASSWORD)
            page.click("form.updatePasswordForm button[type=submit]")
            page.wait_for_selector(".dialogContainer .formDialogFooter button", timeout=10_000)
            assert "Invalid username or password" in page.inner_text(".dialogContainer"), page.inner_text(".dialogContainer")
            shot(page, "wizard-9-password-refused")
            close_alert(page)

            page.fill("#txtCurrentPassword", ADMIN_PASSWORD)
            page.wait_for_selector(".toast", state="detached", timeout=10_000)
            page.click("form.updatePasswordForm button[type=submit]")
            page.wait_for_selector(".toast", timeout=10_000)
            assert page.inner_text(".toast").strip() == "Password saved.", page.inner_text(".toast")
            assert page.input_value("#txtNewPassword") == "", "the fields are not cleared"

            # Sign out and back in with the new password.
            go(page, "/mypreferencesmenu")
            # The modern layout hides this entry (the app bar's user menu signs out instead); the
            # page's own handler is exercised by a scripted click.
            page.wait_for_selector(".btnLogout", state="attached", timeout=10_000)
            page.locator(".btnLogout").evaluate("e => e.click()")
            page.wait_for_selector("#loginPage", timeout=30_000)
            page.wait_for_timeout(500)
            if page.locator(".btnManual").is_visible():
                page.click(".btnManual")
            page.fill("#txtManualName", ADMIN)
            page.fill("#txtManualPassword", NEW_PASSWORD)
            page.click("form.manualLoginForm button[type=submit]")
            page.wait_for_selector("#indexPage", timeout=30_000)

        for name, action in (("start", start), ("user", user), ("library", library), ("settings", settings),
                             ("remote access", remote_access), ("finish", finish_wizard),
                             ("profile image", profile_image), ("password", password)):
            if not steps.run(name, action):
                break

        drop_console_noise(page)
        code = steps.result(finish(page, "wizard"))
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
