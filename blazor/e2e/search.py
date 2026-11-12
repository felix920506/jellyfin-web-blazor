"""Search: suggestions, typing with its debounce and ?query=, result rows per type, searching inside a library.

    ./dev e2e search.py

Reads only; no user data is changed.
"""
import sys

from playwright.sync_api import sync_playwright

from lib import go, new_page, shot, sign_in
from librarylib import Api, check, finish_without_config, new_phone_page, step, wait_until

SCOPE = "#searchPage"


def route(page):
    return page.evaluate("location.hash")


def sections(page):
    return page.eval_on_selector_all(f"{SCOPE} .searchResults h2.sectionTitle", "elements => elements.map(e => e.textContent.trim())")


def section_names(page, title):
    return page.eval_on_selector_all(
        f"{SCOPE} .searchResults [data-section='{title}'] .card .cardText-first",
        "elements => elements.map(e => e.textContent.trim())",
    )


def suggestions(page):
    return page.eval_on_selector_all(f"{SCOPE} .searchSuggestionsList a.button-link", "elements => elements.map(e => e.textContent.trim())")


def everywhere(page, api):
    def empty_field():
        page.click("a[aria-label='Search']")
        page.wait_for_selector(SCOPE, timeout=30_000)
        check(page, route(page) == "#/search", f"the search button of the home page opens the search ({route(page)})")
        check(page, wait_until(lambda: len(suggestions(page)) > 0, timeout=20), "an empty field shows suggestions")
        print("suggestions:", suggestions(page))
        check(page, page.inner_text(f"{SCOPE} .searchSuggestions h2.sectionTitle").strip() == "Suggestions", "the suggestions have their title")
        check(page, page.evaluate("document.activeElement && document.activeElement.id") == "searchTextInput", "the search field has the focus")
        page.wait_for_timeout(500)
        shot(page, "search-suggestions")

    def typing():
        page.click("#searchTextInput")
        page.keyboard.type("test", delay=60)
        check(page, wait_until(lambda: route(page) == "#/search?query=test", timeout=10), f"the address follows the field ({route(page)})")
        check(page, wait_until(lambda: "Movies" in sections(page) and "Shows" in sections(page), timeout=30), "the results arrive")
        page.wait_for_timeout(2500)
        found = sections(page)
        print("sections:", found)
        expected_order = [title for title in ("Movies", "Shows", "Episodes", "Videos", "Playlists", "Artists", "Albums", "Songs",
                                              "Photo albums", "Photos", "Collections", "Books", "People") if title in found]
        print("in the original's order:", expected_order == found)
        check(page, "Big Test Movie" in " ".join(section_names(page, "Movies")), f"the movies row holds the test movie ({section_names(page, 'Movies')})")
        check(page, section_names(page, "Shows") == ["Test Show"], f"the shows row holds the test show ({section_names(page, 'Shows')})")
        check(page, "Artists" in found and "Test Artist" in section_names(page, "Artists"), "the artists row holds the test artist")
        check(page, page.locator(f"{SCOPE} .searchSuggestions").count() == 0, "the suggestions are gone")
        shot(page, "search-results")
        page.evaluate("window.scrollTo(0, document.body.scrollHeight)")
        page.wait_for_timeout(800)
        shot(page, "search-results-bottom")
        page.evaluate("window.scrollTo(0, 0)")

    def reload_keeps_query():
        page.reload()
        page.wait_for_selector(SCOPE, timeout=120_000)
        check(page, wait_until(lambda: "Movies" in sections(page), timeout=30), "a reload searches for the query of the address")
        check(page, page.input_value("#searchTextInput") == "test", "the field shows the query")

    def result_opens_item():
        movie = api.items(IncludeItemTypes="Movie", SearchTerm="Second")[0]
        page.fill("#searchTextInput", "second")
        check(page, wait_until(lambda: section_names(page, "Movies") == [movie["Name"]], timeout=30),
              f"a narrower term narrows the results ({section_names(page, 'Movies')})")
        check(page, route(page) == "#/search?query=second", "the address has the new query")
        # The middle of a hovered card is its overlay with the play button; the corner opens the item.
        page.locator(f"{SCOPE} .searchResults [data-section='Movies'] .card").first.click(position={"x": 15, "y": 15})
        check(page, wait_until(lambda: route(page).startswith("#/details?id=" + movie["Id"]), timeout=15), f"a result opens the item ({route(page)})")
        page.go_back()
        page.wait_for_selector(SCOPE, timeout=30_000)
        check(page, wait_until(lambda: section_names(page, "Movies") == [movie["Name"]], timeout=30), "going back shows the results again")

    def nothing_found():
        page.fill("#searchTextInput", "zzzzqq")
        page.wait_for_selector(f"{SCOPE} .noItemsMessage", timeout=30_000)
        text = page.inner_text(f"{SCOPE} .noItemsMessage").strip()
        check(page, text == 'Sorry! No results found for "zzzzqq"', f"a search without results says so ({text})")
        check(page, page.locator(f"{SCOPE} .noItemsMessage a").count() == 0, "an unscoped search has no 'search everywhere' link")
        shot(page, "search-noresults")

    def clearing():
        page.fill("#searchTextInput", "")
        check(page, wait_until(lambda: route(page) == "#/search", timeout=10), f"an empty field clears the query ({route(page)})")
        check(page, wait_until(lambda: len(suggestions(page)) > 0, timeout=20), "the suggestions come back")
        check(page, page.locator(f"{SCOPE} .searchResults").count() == 0, "the results are gone")

    for name, action in (("search empty", empty_field), ("search typing", typing), ("search reload", reload_keeps_query),
                         ("search result", result_opens_item), ("search nothing", nothing_found), ("search clearing", clearing)):
        step(page, name, action)
        if not page.locator(SCOPE).count():
            go(page, "/search")
            page.wait_for_selector(SCOPE, timeout=30_000)


def in_a_library(page, api):
    library = api.views()["movies"]["Id"]

    def scoped():
        # The home screen's library tile links to the page with its collection type, which the search button passes on.
        go(page, f"/movies?topParentId={library}&collectionType=movies")
        page.wait_for_selector("#moviesPage .itemsContainer .card", timeout=30_000)
        href = page.get_attribute("a[aria-label='Search']", "href")
        print("search link on the movies page:", href)
        check(page, f"parentId={library}" in href and "collectionType=movies" in href, "the search button carries the library")
        page.click("a[aria-label='Search']")
        page.wait_for_selector(SCOPE, timeout=30_000)
        check(page, wait_until(lambda: len(suggestions(page)) > 0, timeout=20), "the library search shows suggestions")
        page.fill("#searchTextInput", "test")
        check(page, wait_until(lambda: "Movies" in sections(page), timeout=30), "the library search finds the movie")
        page.wait_for_timeout(2500)
        print("sections in the movies library:", sections(page))
        # The artists row stays: the original looks people up (usePeopleSearch) without the library in a movie search.
        check(page, not {"Shows", "Episodes", "Albums", "Songs"} & set(sections(page)),
              "a search in the movies library leaves shows and music out")
        check(page, f"parentId={library}" in route(page) and "query=test" in route(page), f"the address keeps the library ({route(page)})")
        shot(page, "search-library")

    def search_everywhere():
        page.fill("#searchTextInput", "show")
        page.wait_for_selector(f"{SCOPE} .noItemsMessage a.emby-button", timeout=30_000)
        check(page, page.get_attribute(f"{SCOPE} .noItemsMessage a.emby-button", "href") == "#/search?query=show",
              "nothing found in the library: a link searches everywhere")
        shot(page, "search-library-noresults")
        page.click(f"{SCOPE} .noItemsMessage a.emby-button")
        check(page, wait_until(lambda: section_names(page, "Shows") == ["Test Show"], timeout=30),
              f"the link finds the show outside the library ({sections(page)})")
        check(page, route(page) == "#/search?query=show", f"the address has no library any more ({route(page)})")

    step(page, "search in a library", scoped)
    step(page, "search everywhere", search_everywhere)


def phone_search(phone):
    sign_in(phone)
    phone.wait_for_selector("#homeTab .section0 .card", timeout=120_000)
    go(phone, "/search?query=test")
    phone.wait_for_selector(SCOPE, timeout=30_000)
    check(phone, wait_until(lambda: "Movies" in sections(phone), timeout=30), "the phone shows the results")
    phone.wait_for_timeout(2500)
    shot(phone, "search-results-mobile")
    overflow = phone.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
    check(phone, overflow <= 1, f"the search page does not scroll sideways on a phone ({overflow}px)")
    go(phone, "/search")
    check(phone, wait_until(lambda: len(suggestions(phone)) > 0, timeout=20), "the phone shows the suggestions")
    phone.wait_for_timeout(500)
    shot(phone, "search-suggestions-mobile")


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        api = Api(page)
        try:
            sign_in(page)
            page.wait_for_selector("#homeTab .section0 .card", timeout=30_000)
            everywhere(page, api)
            in_a_library(page, api)

            phone = new_phone_page(browser)
            step(phone, "phone", lambda: phone_search(phone))
            page.errors.extend("phone: " + error for error in phone.errors if not error.endswith("/config.json")
                               and not error.startswith("Failed to load resource: the server responded with a status of 404"))
            code = finish_without_config(page, "search")
        finally:
            api.sign_out()
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
