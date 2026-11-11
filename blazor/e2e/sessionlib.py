"""Helpers of the session, wizard and user settings scripts."""
import json
import traceback

from lib import APP, shot

# config.json of the original client. The port's wwwroot/config.json is not in the repository
# (the root .gitignore excludes it), so the dev server answers 404 and the app falls back to a
# single theme and no plugins.
DEFAULT_CONFIG = {
    "includeCorsCredentials": False,
    "multiserver": False,
    "themes": [
        {"name": "Apple TV", "id": "appletv", "color": "#bcbcbc"},
        {"name": "Blue Radiance", "id": "blueradiance", "color": "#011432"},
        {"name": "Catppuccin - Latte", "id": "catppuccin-latte", "color": "#eff1f5"},
        {"name": "Catppuccin - Frappe", "id": "catppuccin-frappe", "color": "#303446"},
        {"name": "Catppuccin - Macchiato", "id": "catppuccin-macchiato", "color": "#24273a"},
        {"name": "Catppuccin - Mocha", "id": "catppuccin-mocha", "color": "#1e1e2e"},
        {"name": "Dark", "id": "dark", "color": "#202020", "default": True},
        {"name": "Light", "id": "light", "color": "#303030"},
        {"name": "Purple Haze", "id": "purplehaze", "color": "#000420"},
        {"name": "WMC", "id": "wmc", "color": "#0c2450"},
    ],
    "menuLinks": [],
    "servers": [],
    "plugins": [
        "playAccessValidation/plugin", "experimentalWarnings/plugin", "htmlAudioPlayer/plugin", "htmlVideoPlayer/plugin",
        "photoPlayer/plugin", "comicsPlayer/plugin", "bookPlayer/plugin", "youtubePlayer/plugin", "backdropScreensaver/plugin",
        "pdfPlayer/plugin", "logoScreensaver/plugin", "sessionPlayer/plugin", "chromecastPlayer/plugin", "stillWatching/plugin",
        "syncPlay/plugin",
    ],
}


def use_default_config(page, **overrides):
    """Serves the original client's config.json when the app has none (or when values are overridden)."""
    if not overrides and page.request.get(APP + "/config.json").ok:
        return
    body = json.dumps({**DEFAULT_CONFIG, **overrides})
    page.route("**/config.json", lambda route: route.fulfill(status=200, content_type="application/json", body=body))


class Steps:
    """Runs named steps, keeps going after a failure and takes a screenshot of it."""

    def __init__(self, page, prefix):
        self.page = page
        self.prefix = prefix
        self.failures = []

    def run(self, name, action):
        try:
            action()
            print("ok:", name)
            return True
        except Exception as error:  # noqa: BLE001 - report and continue
            self.failures.append(name)
            lines = str(error).splitlines() or [repr(error)]
            print(f"FAILED: {name}: {lines[0]}")
            for line in lines[1:4]:
                print("   ", line)
            if isinstance(error, AssertionError):
                frame = traceback.extract_tb(error.__traceback__)[-1]
                print(f"    at {frame.name} line {frame.lineno}: {frame.line}")
            try:
                shot(self.page, f"{self.prefix}-failed-{name.replace(' ', '-')}")
            except Exception:  # noqa: BLE001 - the page may be gone
                pass
            return False

    def result(self, code):
        if self.failures:
            print("failed steps:", ", ".join(self.failures))
        return 1 if self.failures else code


def drop_console_noise(page):
    """Failed requests are recorded with their URL by lib.new_page; the browser's own console line adds nothing."""
    page.errors[:] = [e for e in page.errors if not e.startswith("Failed to load resource")]


def close_alert(page):
    """Closes the topmost message dialog with its last button."""
    count = page.locator(".dialogContainer").count()
    page.click(".dialogContainer >> nth=-1 >> .formDialogFooter button >> nth=-1")
    page.wait_for_function("n => document.querySelectorAll('.dialogContainer').length < n", arg=count, timeout=5_000)
