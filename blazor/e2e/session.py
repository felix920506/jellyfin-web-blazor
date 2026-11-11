"""Session pages: add server -> login (errors, forgot password, pin) -> sign in -> select server."""
import sys

from playwright.sync_api import sync_playwright

import lib
from lib import PASSWORD, SERVER, USER, finish, go, new_page, open_app, shot
from sessionlib import Steps, close_alert, drop_console_noise, use_default_config

# Requests these steps make fail on purpose.
lib.IGNORED_FAILURES = ("/Users/AuthenticateByName", "/Users/ForgotPassword")


def show_manual_form(page):
    page.wait_for_selector("#loginPage", timeout=30_000)
    page.wait_for_timeout(500)
    if page.locator(".btnManual").is_visible():
        page.click(".btnManual")
    page.wait_for_selector("#txtManualName", state="visible", timeout=5_000)


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        use_default_config(page)
        steps = Steps(page, "session")

        def add_server():
            open_app(page)
            if not page.locator("#loginPage").count():
                go(page, "/addserver")
                page.wait_for_selector("#txtServerHost", timeout=30_000)
                shot(page, "session-addserver")
                page.fill("#txtServerHost", SERVER)
                page.click("form.addServerForm button[type=submit]")
            page.wait_for_selector("#loginPage", timeout=30_000)
            page.wait_for_timeout(1000)
            shot(page, "session-login")
            assert page.locator(".btnForgotPassword").is_visible(), "no forgot password button"
            assert "serverid=" in page.evaluate("location.hash"), page.evaluate("location.hash")

        def wrong_password():
            show_manual_form(page)
            page.fill("#txtManualName", USER)
            page.fill("#txtManualPassword", "definitely-wrong")
            page.click("form.manualLoginForm button[type=submit]")
            page.wait_for_selector(".toast", timeout=10_000)
            text = page.inner_text(".toast")
            assert "Invalid username or password" in text, text
            assert page.locator("#loginPage").count() == 1
            assert page.input_value("#txtManualPassword") == "", "the password field is not cleared"
            shot(page, "session-login-wrong-password")

        def forgot_password():
            page.click(".btnForgotPassword")
            page.wait_for_selector("#forgotPasswordPage", timeout=10_000)
            shot(page, "session-forgotpassword")
            page.fill("#txtName", "no-such-user-e2e")
            page.click("#btnSubmit")
            page.wait_for_selector(".dialogContainer .formDialogFooter button", timeout=10_000)
            shot(page, "session-forgotpassword-result")
            print("  forgot password dialog:", page.inner_text(".dialogContainer .formDialogContent").strip()[:160])
            close_alert(page)
            page.wait_for_timeout(500)
            print("  after forgot password:", page.evaluate("location.hash"))

        def forgot_password_cancel():
            go(page, "/forgotpassword")
            page.wait_for_selector("#forgotPasswordPage", timeout=10_000)
            page.click("#btnCancel")
            page.wait_for_selector("#forgotPasswordPage", state="detached", timeout=10_000)

        def forgot_password_pin():
            go(page, "/forgotpasswordpin")
            page.wait_for_selector("#forgotPasswordPinPage", timeout=10_000)
            page.fill("#txtPin", "00-00-00-00")
            with page.expect_response(lambda r: "/Users/ForgotPassword/Pin" in r.url, timeout=10_000) as info:
                page.click("form.forgotPasswordPinForm button[type=submit]")
            status = info.value.status
            print("  pin request:", status)
            if status == 200:
                page.wait_for_selector(".dialogContainer .formDialogFooter button", timeout=10_000)
                text = page.inner_text(".dialogContainer")
                assert "invalid or expired PIN" in text, text
                shot(page, "session-forgotpasswordpin-invalid")
                close_alert(page)
            else:
                # A server that refuses the request outright: like the original, the page shows nothing.
                page.wait_for_timeout(500)
                assert page.locator(".dialogContainer").count() == 0
                assert page.locator("#forgotPasswordPinPage").count() == 1
                shot(page, "session-forgotpasswordpin-refused")

        def sign_in():
            go(page, "/login")
            show_manual_form(page)
            page.fill("#txtManualName", USER)
            page.fill("#txtManualPassword", PASSWORD)
            page.click("form.manualLoginForm button[type=submit]")
            page.wait_for_selector("#indexPage", timeout=30_000)

        def select_server():
            go(page, "/selectserver")
            page.wait_for_selector("#selectServerPage .card", timeout=10_000)
            page.wait_for_timeout(500)
            shot(page, "session-selectserver")
            assert page.locator("#selectServerPage .card").count() >= 1
            page.click("#selectServerPage .card >> nth=0")
            page.wait_for_selector(".actionSheetMenuItem[data-id=connect]", timeout=5_000)
            shot(page, "session-selectserver-menu")
            page.click(".actionSheetMenuItem[data-id=connect]")
            page.wait_for_selector("#indexPage", timeout=30_000)

        def login_redirects_to_url():
            # A page asked for while signed out is opened after signing in.
            fresh = new_page(browser)
            use_default_config(fresh)
            try:
                open_app(fresh)
                go(fresh, "/addserver")
                fresh.wait_for_selector("#txtServerHost", timeout=30_000)
                fresh.fill("#txtServerHost", SERVER)
                fresh.click("form.addServerForm button[type=submit]")
                fresh.wait_for_selector("#loginPage", timeout=30_000)
                go(fresh, "/mypreferencesmenu")
                fresh.wait_for_selector("#loginPage", timeout=30_000)
                assert "url=" in fresh.evaluate("location.hash"), fresh.evaluate("location.hash")
                show_manual_form(fresh)
                fresh.fill("#txtManualName", USER)
                fresh.fill("#txtManualPassword", PASSWORD)
                fresh.click("form.manualLoginForm button[type=submit]")
                fresh.wait_for_selector("#myPreferencesMenuPage", timeout=30_000)
                page.errors.extend(fresh.errors)
            finally:
                fresh.close()

        steps.run("add server and login page", add_server)
        steps.run("wrong password", wrong_password)
        steps.run("forgot password", forgot_password)
        steps.run("forgot password cancel", forgot_password_cancel)
        steps.run("forgot password pin", forgot_password_pin)
        steps.run("sign in", sign_in)
        steps.run("select server", select_server)
        steps.run("login redirects to the page asked for", login_redirects_to_url)

        drop_console_noise(page)
        code = steps.result(finish(page, "session"))
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
