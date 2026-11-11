"""Home screen: sections, card actions (favorite, played, menu), favorites tab, scroll buttons, mobile layout.

Changes user data of the test user (resume position, favorite, played) and restores it at the end.
"""
import sys

from playwright.sync_api import sync_playwright

from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in

AUTH = 'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-home", Version="1"'


class Api:
    """Direct calls to the test server, to prepare and restore user data."""

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

    def items(self, **query):
        query = {"userId": self.user, "Recursive": "true", **query}
        return self.request.get(f"{SERVER}/Items", headers=self.headers, params=query).json()["Items"]

    def set_user_data(self, item_id, **data):
        self.request.post(f"{SERVER}/UserItems/{item_id}/UserData", headers=self.headers,
                          params={"userId": self.user}, data=data)

    def set_favorite(self, item_id, favorite):
        call = self.request.post if favorite else self.request.delete
        call(f"{SERVER}/UserFavoriteItems/{item_id}", headers=self.headers, params={"userId": self.user})

    def set_played(self, item_id, played):
        call = self.request.post if played else self.request.delete
        call(f"{SERVER}/UserPlayedItems/{item_id}", headers=self.headers, params={"userId": self.user})

    def sign_out(self):
        self.request.post(f"{SERVER}/Sessions/Logout", headers=self.headers)


def check(page, condition, message):
    if not condition:
        page.errors.append("check failed: " + message)


def section_titles(page, tab):
    return page.eval_on_selector_all(
        f"{tab} .verticalSection:not(.hide) .sectionTitle",
        "elements => elements.map(e => e.textContent.trim())",
    )


def finish_home(page) -> int:
    """finish(), without the failure of the optional config.json.

    wwwroot/config.json is not in the repository (the root .gitignore excludes it), so the app's request for it
    answers 404 and WebConfig falls back to its defaults. The browser reports that as a failed response and as a
    console error without a URL; one such console error is dropped per missing config.json.
    """
    missing = [e for e in page.errors if e.startswith("HTTP 404 ") and e.endswith("/config.json")]
    generic = "Failed to load resource: the server responded with a status of 404"
    for entry in missing:
        page.errors.remove(entry)
        for error in page.errors:
            if error.startswith(generic):
                page.errors.remove(error)
                break
    return finish(page, "home")


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        api = Api(page)
        movies = api.items(IncludeItemTypes="Movie", SortBy="SortName")
        movie, other = movies[0], movies[1]
        try:
            # A resume position makes "Continue Watching" appear.
            api.set_favorite(other["Id"], False)
            api.set_played(other["Id"], False)
            api.set_user_data(movie["Id"], PlaybackPositionTicks=movie["RunTimeTicks"] // 3, Played=False)

            sign_in(page)
            page.wait_for_selector("#homeTab .section0 .card", timeout=30_000)
            page.wait_for_selector("#homeTab .card .cardImageContainer.lazy-image-fadein-fast, "
                                   "#homeTab .card .cardImageContainer.lazy-image-fadein", timeout=30_000)
            page.wait_for_timeout(2500)
            shot(page, "home-desktop")

            titles = section_titles(page, "#homeTab")
            print("home sections:", titles)
            check(page, "My Media" in titles, "the library tiles section is shown")
            check(page, "Continue Watching" in titles, "the resume section is shown")
            check(page, any(t.startswith("Recently Added in") for t in titles), "the latest sections are shown")
            check(page, page.locator("#homeTab .section0 .card").count() >= 3, "one tile per library")
            check(page, page.locator("#favoritesTab .card").count() == 0, "the favorites tab is not loaded yet")
            check(page, page.locator(".jf-appbar-tabs").count() == 0, "the home page shows no tab bar")
            resume = page.locator("#homeTab .section1 .card").first
            check(page, resume.get_attribute("data-id") == movie["Id"], "the resume card is the movie in progress")
            check(page, resume.locator(".itemProgressBar, .itemProgressBarForeground").count() > 0,
                  "the resume card shows progress")

            page.mouse.wheel(0, 600)
            page.wait_for_timeout(1500)
            shot(page, "home-desktop-scrolled")
            page.mouse.wheel(0, -600)
            page.wait_for_timeout(500)

            # Hover overlay of a latest-movies card: favorite, played, menu.
            card = page.locator(f"#homeTab .card[data-id='{other['Id']}']").first
            card.scroll_into_view_if_needed()
            card.hover()
            page.wait_for_timeout(600)
            shot(page, "home-card-hover")
            with page.expect_response(lambda r: "/UserFavoriteItems/" in r.url and r.request.method == "POST"):
                card.locator("button[is='emby-ratingbutton']").click()
            page.wait_for_selector(f"#homeTab .card[data-id='{other['Id']}'] .ratingbutton-icon-withrating", timeout=5_000)
            with page.expect_response(lambda r: "/UserPlayedItems/" in r.url and r.request.method == "POST"):
                card.locator("button[is='emby-playstatebutton']").click()
            page.wait_for_selector(f"#homeTab .card[data-id='{other['Id']}'] .playstatebutton-icon-played", timeout=5_000)
            page.wait_for_selector(f"#homeTab .card[data-id='{other['Id']}'] .playedIndicator", timeout=5_000)

            card.hover()
            card.locator(".cardOverlayContainer button[data-action='menu']").click()
            page.wait_for_selector(".actionSheet, .mud-dialog, .mud-popover-open", timeout=5_000)
            page.wait_for_timeout(500)
            shot(page, "home-card-menu")
            menu = page.inner_text("body")
            check(page, "Add to collection" in menu and "Add to playlist" in menu, "the item menu lists its commands")
            page.keyboard.press("Escape")
            page.wait_for_timeout(400)

            # Scroll buttons of a row (desktop only).
            buttons = page.locator("#homeTab .emby-scrollbuttons-button")
            print("scroll buttons:", buttons.count())
            check(page, buttons.count() > 0, "rows have scroll buttons on desktop")

            # A card links to the item.
            href = card.locator("a.cardImageContainer").get_attribute("href")
            check(page, href is not None and href.startswith("#/details?id=" + other["Id"]), f"card link ({href})")

            # Favorites tab.
            go(page, "/home?tab=1")
            page.wait_for_selector(f"#favoritesTab .card[data-id='{other['Id']}']", timeout=30_000)
            page.wait_for_timeout(2000)
            shot(page, "home-favorites")
            titles = section_titles(page, "#favoritesTab")
            print("favorite sections:", titles)
            check(page, titles == ["Movies"], "only the movies section has favorites")
            check(page, not page.locator("#homeTab").is_visible(), "the home tab is hidden")

            # Removing the favorite empties the tab (data-monitor="markfavorite").
            fav = page.locator(f"#favoritesTab .card[data-id='{other['Id']}']")
            fav.hover()
            with page.expect_response(lambda r: "/UserFavoriteItems/" in r.url and r.request.method == "DELETE"):
                fav.locator("button[is='emby-ratingbutton']").click()
            page.wait_for_selector(f"#favoritesTab .card[data-id='{other['Id']}']", state="detached", timeout=30_000)
            check(page, section_titles(page, "#favoritesTab") == [], "the favorites sections are hidden again")

            # Back on the home tab the played state is still shown.
            go(page, "/home")
            page.wait_for_selector(f"#homeTab .card[data-id='{other['Id']}'] .playedIndicator", timeout=30_000)

            # Mobile layout.
            api.set_favorite(other["Id"], True)
            page.set_viewport_size({"width": 400, "height": 800})
            page.reload()
            page.wait_for_selector("#homeTab .section0 .card", timeout=120_000)
            page.wait_for_timeout(3000)
            shot(page, "home-mobile")
            check(page, page.locator("#homeTab .emby-scrollbuttons-button").count() == 0, "no scroll buttons on mobile")
            page.mouse.wheel(0, 500)
            page.wait_for_timeout(1500)
            shot(page, "home-mobile-scrolled")
            go(page, "/home?tab=1")
            page.wait_for_selector("#favoritesTab .card", timeout=30_000)
            page.wait_for_timeout(2000)
            shot(page, "home-favorites-mobile")
            code = finish_home(page)
        finally:
            api.set_favorite(other["Id"], False)
            api.set_played(other["Id"], False)
            api.set_user_data(movie["Id"], PlaybackPositionTicks=0, Played=False)
            api.sign_out()
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
