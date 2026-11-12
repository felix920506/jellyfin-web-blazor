"""Helpers of library.py and search.py: the test server's API, checks, a phone page, the player's state."""
import json
import time
import traceback

from lib import PASSWORD, SERVER, USER, finish, shot

AUTH = 'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-library", Version="1"'
PHONE = ("Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) "
         "Chrome/120.0.0.0 Mobile Safari/537.36")


class Api:
    """Direct calls to the test server, to look things up and to restore the user data the script changes."""

    def __init__(self, page):
        self.request = page.request
        response = self.request.post(
            f"{SERVER}/Users/AuthenticateByName",
            headers={"Authorization": AUTH},
            data={"Username": USER, "Pw": PASSWORD},
        )
        body = response.json()
        self.user = body["User"]["Id"]
        self.headers = {"Authorization": f'{AUTH}, Token="{body["AccessToken"]}"'}

    def get(self, path, **query):
        return self.request.get(f"{SERVER}/{path}", headers=self.headers, params=query).json()

    def views(self):
        """The libraries by collection type."""
        return {view.get("CollectionType"): view for view in self.get("UserViews", userId=self.user)["Items"]}

    def items(self, **query):
        return self.get("Items", **{"userId": self.user, "Recursive": "true", **query})["Items"]

    def item(self, item_id):
        return self.get(f"Items/{item_id}", userId=self.user)

    def user_data(self, item_ids):
        """The user data of items, to hand to restore() later."""
        return {item_id: self.item(item_id).get("UserData", {}) for item_id in item_ids}

    def restore(self, saved):
        for item_id, data in saved.items():
            body = {
                "Played": data.get("Played", False),
                "IsFavorite": data.get("IsFavorite", False),
                "PlaybackPositionTicks": data.get("PlaybackPositionTicks", 0),
                "PlayCount": data.get("PlayCount", 0),
            }
            if data.get("LastPlayedDate"):
                body["LastPlayedDate"] = data["LastPlayedDate"]
            self.request.post(f"{SERVER}/UserItems/{item_id}/UserData", headers=self.headers,
                              params={"userId": self.user}, data=body)

    def set_favorite(self, item_id, favorite):
        call = self.request.post if favorite else self.request.delete
        call(f"{SERVER}/UserFavoriteItems/{item_id}", headers=self.headers, params={"userId": self.user})

    def sign_out(self):
        self.request.post(f"{SERVER}/Sessions/Logout", headers=self.headers)


def check(page, condition, message):
    print(("ok   " if condition else "FAIL ") + message)
    if not condition:
        page.errors.append("check failed: " + message)
    return condition


def step(page, name, action):
    """Runs one part of a script; a failure is recorded with a screenshot and the next part still runs."""
    print("--", name)
    try:
        action()
    except Exception as error:  # noqa: BLE001 - a timeout or an assertion: both are failures of the step
        traceback.print_exc()
        page.errors.append(f"step '{name}' failed: {str(error).splitlines()[0]}")
        try:
            shot(page, "fail-" + name.replace(" ", "-"))
        except Exception:  # noqa: BLE001
            pass


def wait_until(probe, timeout=20.0, interval=0.25):
    """Polls probe() until it returns something truthy; returns the last value."""
    deadline = time.time() + timeout
    value = probe()
    while not value and time.time() < deadline:
        time.sleep(interval)
        value = probe()
    return value


def new_phone_page(browser):
    """lib.new_page for a phone: the app picks its layout from the user agent, not from the window width."""
    context = browser.new_context(viewport={"width": 400, "height": 800}, user_agent=PHONE, has_touch=True,
                                  is_mobile=True, device_scale_factor=2)
    page = context.new_page()
    page.errors = []
    page.on("console", lambda m: page.errors.append(m.text) if m.type == "error" else None)
    page.on("pageerror", lambda e: page.errors.append(str(e)))
    page.on("response", lambda r: page.errors.append(f"HTTP {r.status} {r.url}") if r.status >= 400 else None)
    return page


def finish_without_config(page, label) -> int:
    """finish(), without the failure of the optional config.json.

    wwwroot/config.json is not in the repository, so the app's request for it answers 404 and the app falls
    back to its defaults. The browser reports that as a failed response and as a console error without a
    URL; one such console error is dropped per missing config.json.
    """
    missing = [e for e in page.errors if e.startswith("HTTP 404 ") and e.endswith("/config.json")]
    generic = "Failed to load resource: the server responded with a status of 404"
    for entry in missing:
        page.errors.remove(entry)
        for error in page.errors:
            if error.startswith(generic):
                page.errors.remove(error)
                break
    return finish(page, label)


def user_id(page):
    """The signed-in user's id as the app stores it (the key prefix of its per-user settings)."""
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
    return stored["Servers"][0]["UserId"]


def set_page_size(page, size):
    """Sets the user's library page size (a browser-only display setting); takes effect on the next load."""
    key = f"{user_id(page)}-libraryPageSize"
    if size is None:
        page.evaluate("k => localStorage.removeItem(k)", key)
    else:
        page.evaluate("([k, v]) => localStorage.setItem(k, v)", [key, str(size)])


def card_ids(page, scope):
    return page.eval_on_selector_all(
        f"{scope} .itemsContainer .card[data-id], {scope} .itemsContainer .listItem[data-id]",
        "elements => elements.map(e => e.getAttribute('data-id'))",
    )


def player(page, name="state", args=None):
    """Returns observable media state; playback is controlled through public page controls."""
    kind = "audio" if page.locator("audio").count() else "video"
    media = page.locator(kind).first
    if not media.count():
        return {"isPlaying": False}
    state = media.evaluate("e => ({isPlaying: !e.paused, paused: e.paused, currentTime: e.currentTime, duration: e.duration})")
    state["isPlaying"] = not state["paused"]
    return state


def media_time(page, kind):
    """The current time in seconds of the video or audio element, or -1."""
    return page.locator(kind).first.evaluate("e => e.currentTime") if page.locator(kind).count() else -1


def stop_playback(page):
    # The now playing bar's button is .stopButton (as in the original); the video OSD's is .btnStop.
    for selector in (".nowPlayingBar .stopButton:visible", ".btnStop:visible"):
        if page.locator(selector).count():
            page.locator(selector).first.click()
            break
    return wait_until(lambda: not player(page).get("isPlaying"), timeout=15)


def public_queue_count(page):
    """Counts queue entries through the public now-playing page."""
    if page.locator(".nowPlayingBarText:visible").count():
        page.locator(".nowPlayingBarText:visible").first.click()
    page.wait_for_selector("#nowPlayingPage .playlist .listItem", timeout=10_000)
    count = page.locator("#nowPlayingPage .playlist .listItem").count()
    page.go_back()
    page.wait_for_timeout(500)
    return count


def close_menu(page):
    """Closes an open MudBlazor menu."""
    page.keyboard.press("Escape")
    page.wait_for_timeout(300)
    if page.locator(".mud-popover-open").count():
        page.mouse.click(5, 300)
        page.wait_for_timeout(300)
