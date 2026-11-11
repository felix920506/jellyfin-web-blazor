"""Helpers shared by the Playwright scripts in this directory (`from lib import ...`)."""
import os

APP = os.environ.get("APP_URL", "http://localhost:5080")
SERVER = os.environ.get("JELLYFIN_URL", "http://localhost:18096")
USER = os.environ.get("TEST_USER", "admin")
PASSWORD = os.environ.get("TEST_PASSWORD", "jellyfin")
OUT = os.environ.get("OUT_DIR", "/out")

# Requests that are expected to fail and must not count as errors.
IGNORED_FAILURES = ()


def new_page(browser, width=1366, height=768):
    """A page that collects console errors, page errors and failed HTTP responses in page.errors."""
    page = browser.new_page(viewport={"width": width, "height": height})
    page.errors = []
    page.on("console", lambda m: page.errors.append(m.text) if m.type == "error" else None)
    page.on("pageerror", lambda e: page.errors.append(str(e)))
    page.on(
        "response",
        lambda r: page.errors.append(f"HTTP {r.status} {r.url}")
        if r.status >= 400 and not any(s in r.url for s in IGNORED_FAILURES) else None,
    )
    return page


def open_app(page):
    """Loads the app and waits for its first page."""
    page.goto(APP + "/")
    page.wait_for_selector(".page", timeout=120_000)


def go(page, route):
    """Navigates within the loaded app, e.g. go(page, '/home?tab=1')."""
    page.evaluate("r => { location.hash = '#' + r; }", route)


def sign_in(page, user=USER, password=PASSWORD):
    """From a fresh browser context: add the test server, sign in, wait for the home page."""
    open_app(page)
    if not page.locator("#loginPage").count():
        go(page, "/addserver")
        page.wait_for_selector("#txtServerHost", timeout=30_000)
        page.fill("#txtServerHost", SERVER)
        page.click("form.addServerForm button[type=submit]")
    page.wait_for_selector("#loginPage", timeout=30_000)
    page.fill("#txtManualName", user)
    page.fill("#txtManualPassword", password)
    page.click("form.manualLoginForm button[type=submit]")
    page.wait_for_selector("#indexPage", timeout=30_000)


def shot(page, name):
    page.screenshot(path=f"{OUT}/{name}.png")


def finish(page, label):
    """Prints collected errors; returns the process exit code."""
    if page.locator("#blazor-error-ui").is_visible():
        page.errors.append("the unhandled-error banner is visible")
    for error in page.errors:
        print("error:", error)
    print(f"{label}: {'ok' if not page.errors else 'FAILED'}")
    return 1 if page.errors else 0
