"""Library pages: tabs, sorting, alphabet picker, filters, view settings, paging, play buttons, multi-select,
the generic list page and the phone layout.

    ./dev e2e library.py

Changes user data of the test user (favorite, played, playback positions) and restores it at the end.
Playback is started with media every browser decodes: "Open Codec Movie" and the MP3 songs.
"""
import sys

from playwright.sync_api import sync_playwright

from lib import go, new_page, shot, sign_in
from librarylib import (Api, card_ids, check, close_menu, finish_without_config, media_time, new_phone_page,
                        player, public_queue_count, set_page_size, step, stop_playback, wait_until)

ITEMS = ".itemsContainer .card[data-id], .itemsContainer .listItem[data-id]"
BY_YEAR = "[data-sort='ProductionYear,PremiereDate,SortName']"


def route(page):
    return page.evaluate("location.hash")


def wait_items(page, scope, count, timeout=30_000):
    """Waits until the page shows exactly `count` cards or list rows."""
    selector = ", ".join(f"{scope} {part.strip()}" for part in ITEMS.split(","))
    page.wait_for_function("([s, n]) => document.querySelectorAll(s).length === n", arg=[selector, count], timeout=timeout)


def shown(page, scope, names):
    return [names.get(item_id, item_id) for item_id in card_ids(page, scope)]


def expect_order(page, scope, names, expected, message):
    wait_until(lambda: shown(page, scope, names) == expected, timeout=15)
    check(page, shown(page, scope, names) == expected, f"{message} ({shown(page, scope, names)})")


def count_text(page, scope):
    return page.inner_text(f"{scope} .jf-library-count").strip()


def expect_count(page, scope, expected):
    wait_until(lambda: count_text(page, scope) == expected, timeout=15)
    check(page, count_text(page, scope) == expected, f"the item count reads '{expected}' ({count_text(page, scope)})")


def settle(page, milliseconds=1500):
    """Lets images load before a screenshot."""
    page.wait_for_timeout(milliseconds)


class Media:
    """What the test server holds, looked up once."""

    def __init__(self, api):
        views = api.views()
        self.movies_library = views["movies"]
        self.shows_library = views["tvshows"]
        self.music_library = views["music"]
        self.server_id = self.movies_library["ServerId"]
        self.movies = api.items(IncludeItemTypes="Movie", SortBy="SortName", ParentId=self.movies_library["Id"])
        self.songs = api.items(IncludeItemTypes="Audio", SortBy="SortName", ParentId=self.music_library["Id"])
        self.albums = api.items(IncludeItemTypes="MusicAlbum", SortBy="SortName", ParentId=self.music_library["Id"])
        self.episodes = api.items(IncludeItemTypes="Episode", ParentId=self.shows_library["Id"])
        self.names = {item["Id"]: item["Name"] for item in self.movies + self.songs + self.albums + self.episodes}
        self.movie_names = [movie["Name"] for movie in self.movies]
        self.by_year = [movie["Name"] for movie in sorted(self.movies, key=lambda m: (m.get("ProductionYear", 0), m["Name"]))]
        self.open_codec = next(movie for movie in self.movies if movie["Name"].startswith("Open Codec"))
        self.favorite = next(movie for movie in self.movies if movie["Name"].startswith("Second"))
        self.touched = [item["Id"] for item in self.movies + self.songs]


def movies_page(page, api, media):
    scope = "#moviesPage"
    library = media.movies_library["Id"]
    names = media.names

    def open_from_home():
        page.click(f"#homeTab .section0 .card[data-id='{library}'] a.cardImageContainer")
        page.wait_for_selector(scope, timeout=30_000)
        wait_items(page, scope, len(media.movies))
        print("movies route:", route(page))
        check(page, route(page).startswith("#/movies?topParentId=" + library), "the library tile opens the movies page")
        expect_order(page, scope, names, media.movie_names, "the movies are listed by name")
        expect_count(page, scope, str(len(media.movies)))
        check(page, page.inner_text(f"{scope} .jf-library-view-label").strip() == "Movies", "the view switch shows the current view")
        check(page, page.locator(f"{scope} .jf-library-alpha").count() == 27, "the alphabet picker has # and A-Z")
        settle(page, 2500)
        shot(page, "library-movies")

    def sorting():
        page.click(f"{scope} .jf-library-btn-sort")
        page.wait_for_selector(BY_YEAR, timeout=5_000)
        page.wait_for_timeout(300)
        shot(page, "library-movies-sortmenu")
        page.click(BY_YEAR)
        expect_order(page, scope, names, media.by_year, "sorting by release date")
        page.click(BY_YEAR)
        expect_order(page, scope, names, list(reversed(media.by_year)), "the same entry again reverses the order")
        page.click("[data-sort='SortName']")
        expect_order(page, scope, names, media.movie_names, "sorting by name again")
        close_menu(page)
        check(page, page.locator(BY_YEAR).count() == 0 or not page.locator(BY_YEAR).first.is_visible(), "the sort menu closes")

    def alphabet():
        page.click(f"{scope} button.jf-library-alpha[value='S']")
        expected = [name for name in media.movie_names if name.startswith("S")]
        expect_order(page, scope, names, expected, "the alphabet picker keeps the movies starting with S")
        expect_count(page, scope, str(len(expected)))
        shot(page, "library-movies-alphabet")
        page.click(f"{scope} .jf-library-btn-resetfilters")
        expect_order(page, scope, names, media.movie_names, "reset filters shows everything again")
        check(page, page.locator(f"{scope} .jf-library-btn-resetfilters").count() == 0, "the reset button goes away")

    def filters():
        year = media.movies[0].get("ProductionYear")
        page.click(f"{scope} .jf-library-btn-filter")
        page.wait_for_selector(".jf-library-filters", timeout=5_000)
        page.wait_for_selector("#filtersYears", timeout=15_000)
        page.wait_for_timeout(300)
        shot(page, "library-movies-filtermenu")
        sections = page.eval_on_selector_all(".jf-library-filter-section", "elements => elements.map(e => e.id)")
        print("filter sections:", sections)
        check(page, sections[0] == "filtersStatus" and "filtersVideoTypes" in sections, "the filter menu has the original's groups")
        page.click("#filtersYears .mud-expand-panel-header")
        page.wait_for_timeout(500)
        page.locator(f"label:has(input[data-filter='Years:{year}'])").click()
        expected = [movie["Name"] for movie in media.movies if movie.get("ProductionYear") == year]
        expect_order(page, scope, names, expected, f"the year filter keeps the movies of {year}")
        shot(page, "library-movies-filtered")
        close_menu(page)
        check(page, page.locator(f"{scope} .jf-library-filter-badge .mud-badge").count() > 0, "the filter button shows its badge")
        page.click(f"{scope} .jf-library-btn-resetfilters")
        expect_order(page, scope, names, media.movie_names, "reset filters removes the year filter")

    def view_settings():
        page.click(f"{scope} .jf-library-btn-viewsettings")
        page.wait_for_selector(".mud-menu-item.jf-library-view-list", timeout=5_000)
        page.wait_for_timeout(300)
        shot(page, "library-movies-viewmenu")
        page.click(".mud-menu-item.jf-library-view-list")
        page.wait_for_selector(f"{scope} .itemsContainer.vertical-list .listItem", timeout=15_000)
        close_menu(page)
        expect_order(page, scope, names, media.movie_names, "the list view shows the same movies")
        settle(page)
        shot(page, "library-movies-list")

        page.click(f"{scope} .jf-library-btn-viewsettings")
        page.wait_for_selector(".mud-menu-item.jf-library-view-grid", timeout=5_000)
        page.locator(".mud-menu-item.jf-library-view-grid .jf-menu-text").click()
        page.wait_for_selector(f"{scope} .itemsContainer.vertical-wrap .card", timeout=15_000)
        page.click(".jf-library-view-gridsettings")
        page.wait_for_selector("[data-imagetype='Thumb']", timeout=5_000)
        page.wait_for_timeout(400)
        shot(page, "library-movies-gridsettings")
        page.click("[data-imagetype='Thumb']")
        page.wait_for_selector(f"{scope} .itemsContainer .card.backdropCard", timeout=15_000)
        close_menu(page)
        settle(page)
        shot(page, "library-movies-thumb")
        stored = page.evaluate("Object.keys(localStorage).filter(k => k.includes('libraryViewSettings') || k.includes(' - '))")
        print("stored view settings keys:", stored)

        page.reload()
        page.wait_for_selector(f"{scope} .itemsContainer .card.backdropCard", timeout=120_000)
        check(page, True, "the image type survives a reload")
        page.click(f"{scope} .jf-library-btn-viewsettings")
        page.wait_for_selector(".jf-library-view-gridsettings", timeout=5_000)
        page.click(".jf-library-view-gridsettings")
        page.wait_for_selector("[data-imagetype='Primary']", timeout=5_000)
        page.click("[data-imagetype='Primary']")
        page.wait_for_selector(f"{scope} .itemsContainer .card.portraitCard", timeout=15_000)
        close_menu(page)

    def paging():
        set_page_size(page, 2)
        page.reload()
        page.wait_for_selector(scope, timeout=120_000)
        wait_items(page, scope, 2)
        expect_order(page, scope, names, media.movie_names[:2], "the first page holds two movies")
        expect_count(page, scope, f"1-2 of {len(media.movies)}")
        check(page, page.locator(f"{scope} .jf-library-btn-previous").is_disabled(), "there is no page before the first")
        page.click(f"{scope} .jf-library-btn-next")
        expect_order(page, scope, names, media.movie_names[2:4], "the next page holds the next two")
        expect_count(page, scope, f"3-4 of {len(media.movies)}")
        settle(page)
        shot(page, "library-movies-paging")
        page.click(f"{scope} .jf-library-btn-previous")
        expect_order(page, scope, names, media.movie_names[:2], "back on the first page")
        set_page_size(page, None)
        page.reload()
        page.wait_for_selector(scope, timeout=120_000)
        wait_items(page, scope, len(media.movies))
        check(page, page.locator(f"{scope} .jf-library-pagination button:not([disabled])").count() == 0,
              "one page needs no paging buttons")

    def tabs():
        page.click(f"{scope} .jf-library-view-button")
        page.wait_for_selector("[data-tab-index='2']", timeout=5_000)
        page.wait_for_timeout(300)
        labels = page.eval_on_selector_all("[data-tab-index]", "elements => elements.map(e => e.textContent.trim())")
        print("movie views:", labels)
        check(page, labels == ["Movies", "Suggestions", "Favorites", "Collections", "Genres", "Studios", "Playlists", "People"]
              or labels[:5] == ["Movies", "Suggestions", "Favorites", "Collections", "Genres"], "the view menu lists the movie views")
        shot(page, "library-movies-tabmenu")
        page.click("[data-tab-index='2']")
        wait_until(lambda: "tab=2" in route(page), timeout=10)
        check(page, "tab=2" in route(page), "choosing a view sets ?tab=")
        page.wait_for_selector(f"{scope} .itemsContainer .card[data-id='{media.favorite['Id']}']", timeout=30_000)
        check(page, page.inner_text(f"{scope} .jf-library-view-label").strip() == "Favorites", "the favorites view is shown")
        settle(page)
        shot(page, "library-movies-favorites")

        base = route(page)[1:].replace("&tab=2", "").replace("tab=2", "")
        for index, name, wait_for in ((1, "suggestions", ".jf-library-suggestions"), (3, "collections", ".jf-library-toolbar"),
                                      (4, "genres", ".jf-library-toolbar"), (5, "studios", ".jf-library-toolbar"),
                                      (7, "people", ".jf-library-toolbar")):
            go(page, f"{base}&tab={index}")
            wait_until(lambda: f"tab={index}" in route(page), timeout=10)
            page.wait_for_selector(f"{scope} {wait_for}", timeout=30_000)
            settle(page, 2500)
            shot(page, f"library-movies-{name}")
            print(f"tab {index} ({name}): view label '{page.inner_text(f'{scope} .jf-library-view-label').strip()}',",
                  page.locator(f"{scope} .card").count(), "cards,", page.locator(f"{scope} .noItemsMessage").count(), "empty messages")
            if index == 3:
                check(page, page.locator(f"{scope} .jf-library-btn-newcollection").count() == 1,
                      "the collections view offers a new collection to an administrator")
        go(page, base)
        wait_items(page, scope, len(media.movies))

    def play():
        page.click(f"{scope} button.jf-library-alpha[value='O']")
        wait_items(page, scope, 1)
        check(page, card_ids(page, scope) == [media.open_codec["Id"]], "the letter O leaves the open-codec movie")
        page.click(f"{scope} .jf-library-btn-play")
        check(page, wait_until(lambda: route(page).startswith("#/video"), timeout=30), "play all opens the video player")
        check(page, wait_until(lambda: media_time(page, "video") > 1.0, timeout=40), "the movie really plays")
        state = player(page)
        print("player:", {key: state.get(key) for key in ("isPlaying", "currentTime", "duration")})
        shot(page, "library-movies-playing")
        check(page, stop_playback(page), "playback stops")
        if not wait_until(lambda: route(page).startswith("#/movies"), timeout=15):
            go(page, f"/movies?topParentId={library}")
        page.wait_for_selector(scope, timeout=30_000)
        if page.locator(f"{scope} .jf-library-btn-resetfilters").count():
            page.click(f"{scope} .jf-library-btn-resetfilters")
        wait_items(page, scope, len(media.movies))

    def multi_select():
        saved = api.user_data([movie["Id"] for movie in media.movies[:2]])
        selected_ids = [movie["Id"] for movie in media.movies[:2]]
        try:
            cards = page.locator(f"{scope} .itemsContainer .card")
            cards.nth(0).click(modifiers=["Control"])
            page.wait_for_selector(".selectionCommandsPanel", timeout=10_000)
            check(page, page.inner_text(".itemSelectionCount").strip() == "1", "ctrl-click starts a selection with the card")
            check(page, route(page).startswith("#/movies"), "the click did not open the item")
            cards.nth(1).click()
            wait_until(lambda: page.inner_text(".itemSelectionCount").strip() == "2", timeout=10)
            check(page, page.inner_text(".itemSelectionCount").strip() == "2", "a click on another card adds it")
            settle(page, 500)
            shot(page, "library-multiselect")
            page.click(".btnSelectionPanelOptions")
            page.wait_for_selector(".actionSheetMenuItem", timeout=10_000)
            page.wait_for_timeout(500)
            entries = page.eval_on_selector_all(".actionSheetMenuItem", "elements => elements.map(e => e.textContent.trim())")
            print("selection commands:", entries)
            for expected in ("Add to collection", "Add to playlist", "Mark played", "Mark unplayed", "Refresh metadata"):
                check(page, any(expected in entry for entry in entries), f"the selection menu offers '{expected}'")
            shot(page, "library-multiselect-menu")

            # The three item-dialog commands must retain the complete selection, not just the
            # first card.  The hidden field is the same itemIds payload sent by each editor.
            for command, dialog_selector, label in (
                ("addtocollection", ".fldSelectedItemIds", "collection"),
                ("playlist", ".fldSelectedItemIds", "playlist"),
                ("refresh", ".fldSelectedItemIds", "refresh"),
            ):
                if command != "addtocollection":
                    cards = page.locator(f"{scope} .itemsContainer .card")
                    cards.nth(0).click(modifiers=["Control"])
                    cards.nth(1).click()
                    page.click(".btnSelectionPanelOptions")
                    page.wait_for_selector(".actionSheetMenuItem", timeout=10_000)
                page.click(f".actionSheetMenuItem[data-id='{command}']")
                page.wait_for_selector(dialog_selector, state="attached", timeout=10_000)
                ids = page.input_value(dialog_selector).split(",")
                check(page, ids == selected_ids, f"multi-select forwards both items to {label}")
                dialog_count = page.locator(".dialogContainer").count()
                page.locator(".dialogContainer").last.locator(".btnCancel").first.click()
                page.wait_for_function("n => document.querySelectorAll('.dialogContainer').length < n", arg=dialog_count,
                                       timeout=10_000)

            cards = page.locator(f"{scope} .itemsContainer .card")
            cards.nth(0).click(modifiers=["Control"])
            cards.nth(1).click()
            page.click(".btnSelectionPanelOptions")
            page.wait_for_selector(".actionSheetMenuItem", timeout=10_000)

            page.locator(".actionSheetMenuItem", has_text="Mark played").first.click()
            page.wait_for_selector(".selectionCommandsPanel", state="detached", timeout=15_000)
            played = wait_until(lambda: all(api.item(item_id)["UserData"].get("Played") for item_id in saved), timeout=15)
            check(page, played, "mark played reaches both selected movies")
            page.wait_for_selector(f"{scope} .card[data-id='{media.movies[0]['Id']}'] .playedIndicator", timeout=15_000)
            settle(page, 500)
            shot(page, "library-multiselect-played")
        finally:
            api.restore(saved)

    for name, action in (("movies from home", open_from_home), ("movies sorting", sorting), ("movies alphabet", alphabet),
                         ("movies filters", filters), ("movies view settings", view_settings), ("movies paging", paging),
                         ("movies tabs", tabs), ("movies play", play), ("movies multi-select", multi_select)):
        step(page, name, action)
        close_menu(page)
        if not page.locator(scope).count():
            go(page, f"/movies?topParentId={library}")
            page.wait_for_selector(scope, timeout=30_000)


def shows_page(page, media):
    scope = "#tvshowsPage"
    library = media.shows_library["Id"]
    go(page, f"/tv?topParentId={library}")
    page.wait_for_selector(scope, timeout=30_000)
    wait_items(page, scope, 1)
    check(page, page.inner_text(f"{scope} .jf-library-view-label").strip() == "Shows", "the TV page starts with the shows")
    expect_count(page, scope, "1")
    settle(page)
    shot(page, "library-tv")

    go(page, f"/tv?topParentId={library}&tab=5")
    wait_items(page, scope, len(media.episodes))
    expect_count(page, scope, str(len(media.episodes)))
    check(page, page.locator(f"{scope} .jf-library-alpha").count() == 0, "the episodes have no alphabet picker")
    settle(page)
    shot(page, "library-tv-episodes")

    for index, name in ((1, "suggestions"), (2, "upcoming"), (4, "networks")):
        go(page, f"/tv?topParentId={library}&tab={index}")
        wait_until(lambda: f"tab={index}" in route(page), timeout=10)
        settle(page, 3000)
        shot(page, f"library-tv-{name}")
        print(f"tv tab {index} ({name}):", page.locator(f"{scope} .card").count(), "cards,",
              page.locator(f"{scope} .noItemsMessage").count(), "empty messages")


def music_page(page, media):
    scope = "#musicPage"
    library = media.music_library["Id"]
    go(page, f"/music?topParentId={library}")
    page.wait_for_selector(scope, timeout=30_000)
    wait_items(page, scope, len(media.albums))
    check(page, page.inner_text(f"{scope} .jf-library-view-label").strip() == "Albums", "the music page starts with the albums")
    settle(page)
    shot(page, "library-music")

    page.click(f"{scope} .jf-library-btn-play")
    check(page, wait_until(lambda: player(page).get("isPlaying") and media_time(page, "audio") > 0.5, timeout=40),
          "play all on the albums plays music")
    settle(page, 500)
    shot(page, "library-music-playing")
    check(page, route(page).startswith("#/music"), "music plays without leaving the page")
    check(page, stop_playback(page), "the music stops")

    go(page, f"/music?topParentId={library}&tab=2")
    wait_until(lambda: "tab=2" in route(page), timeout=10)
    page.wait_for_selector(f"{scope} .itemsContainer .card", timeout=30_000)
    settle(page)
    shot(page, "library-music-albumartists")
    print("album artists:", page.locator(f"{scope} .itemsContainer .card").count())

    go(page, f"/music?topParentId={library}&tab=5")
    wait_items(page, scope, len(media.songs))
    expect_count(page, scope, str(len(media.songs)))
    check(page, page.locator(f"{scope} .jf-library-btn-play").count() == 0 and page.locator(f"{scope} .jf-library-btn-shuffle").count() == 1,
          "the songs offer shuffle only")
    settle(page)
    shot(page, "library-music-songs")
    page.click(f"{scope} .jf-library-btn-shuffle")
    check(page, wait_until(lambda: player(page).get("isPlaying") and media_time(page, "audio") > 0.5, timeout=40),
          "shuffle on the songs plays music")
    check(page, public_queue_count(page) == len(media.songs), f"all songs are queued ({len(media.songs)})")
    # No view of the original enables the queue button (isBtnQueueEnabled is false in its defaults).
    check(page, page.locator(f"{scope} .jf-library-btn-queue").count() == 0, "the songs have no queue button, as in the original")
    check(page, stop_playback(page), "the music stops again")


def list_page(page, api, media):
    scope = "#listPage"
    library = media.movies_library["Id"]
    names = media.names
    address = f"/list?parentId={library}&serverId={media.server_id}"

    def paging_text():
        return page.locator(f"{scope} .listPaging span").first.inner_text().strip()

    def folder():
        go(page, address)
        page.wait_for_selector(scope, timeout=30_000)
        wait_items(page, scope, len(media.movies))
        expect_order(page, scope, names, media.movie_names, "the list page shows the folder by name")
        check(page, paging_text() == f"1-{len(media.movies)} of {len(media.movies)}", f"the paging text ({paging_text()})")
        check(page, page.locator(f"{scope} .btnNextPage").count() == 0, "one page has no paging buttons")
        # list.js hides both for a library folder other than home videos and music videos.
        check(page, page.locator(f"{scope} .btnPlay:visible").count() == 0, "a movie library has no play button")
        check(page, page.locator(f"{scope} .btnShuffle:visible").count() == 0, "a movie library has no shuffle button")
        check(page, page.locator(f"{scope} .btnSort:visible").count() == 1, "one sort button is visible")
        settle(page, 2500)
        shot(page, "library-list")

    def alphabet():
        page.click(f"{scope} .alphaPickerButton[data-value='T']")
        expected = [name for name in media.movie_names if name.startswith("T")]
        expect_order(page, scope, names, expected, "the alphabet picker keeps the movies starting with T")
        check(page, "alphaPickerButton-selected" in page.get_attribute(f"{scope} .alphaPickerButton[data-value='T']", "class"),
              "the letter is marked")
        page.click(f"{scope} .alphaPickerButton[data-value='T']")
        expect_order(page, scope, names, media.movie_names, "the same letter again removes the filter")

    def sorting():
        page.locator(f"{scope} .btnSort:visible").first.click()
        page.wait_for_selector("select.selectSortBy", timeout=10_000)
        page.wait_for_timeout(500)
        shot(page, "library-list-sortdialog")
        page.select_option("select.selectSortBy", "ProductionYear,PremiereDate,SortName")
        page.select_option("select.selectSortOrder", "Descending")
        page.click(".formDialogHeader .btnCancel")
        expect_order(page, scope, names, list(reversed(media.by_year)), "the sort dialog sorts by release date, newest first")
        check(page, "arrow_downward" in page.get_attribute(f"{scope} .btnSortIcon", "class"), "the sort button shows the direction")
        page.locator(f"{scope} .btnSort:visible").first.click()
        page.wait_for_selector("select.selectSortBy", timeout=10_000)
        page.select_option("select.selectSortBy", "SortName")
        page.select_option("select.selectSortOrder", "Ascending")
        page.click(".formDialogHeader .btnCancel")
        expect_order(page, scope, names, media.movie_names, "and by name again")

    def filtering():
        page.locator(f"{scope} .btnFilter:visible").first.click()
        page.wait_for_selector("input.chkFavorite", state="attached", timeout=10_000)
        page.wait_for_timeout(500)
        shot(page, "library-list-filterdialog")
        page.locator("label:has(input.chkFavorite)").click()
        page.click(".formDialogHeader .btnCancel")
        wait_until(lambda: media.favorite["Name"] in shown(page, scope, names) and len(card_ids(page, scope)) < len(media.movies), timeout=15)
        check(page, media.favorite["Name"] in shown(page, scope, names) and len(card_ids(page, scope)) < len(media.movies),
              f"the favorite filter keeps the favorites ({shown(page, scope, names)})")
        check(page, page.locator(f"{scope} .filterButtonBubble:visible").count() == 1, "the filter button shows its bubble")
        settle(page, 500)
        shot(page, "library-list-filtered")
        page.locator(f"{scope} .btnFilter:visible").first.click()
        page.wait_for_selector("input.chkFavorite", state="attached", timeout=10_000)
        check(page, page.is_checked("input.chkFavorite"), "the dialog shows the stored filter")
        page.locator("label:has(input.chkFavorite)").click()
        page.click(".formDialogHeader .btnCancel")
        expect_order(page, scope, names, media.movie_names, "the filter is removed again")

    def view_settings():
        page.locator(f"{scope} .btnViewSettings:visible").first.click()
        page.wait_for_selector("select.selectImageType", timeout=10_000)
        page.wait_for_timeout(500)
        shot(page, "library-list-viewdialog")
        page.select_option("select.selectImageType", "list")
        page.click(".formDialogHeader .btnCancel")
        page.wait_for_selector(f"{scope} .itemsContainer.vertical-list .listItem", timeout=15_000)
        expect_order(page, scope, names, media.movie_names, "the list view shows the same movies")
        settle(page)
        shot(page, "library-list-listview")
        page.locator(f"{scope} .btnViewSettings:visible").first.click()
        page.wait_for_selector("select.selectImageType", timeout=10_000)
        page.select_option("select.selectImageType", "primary")
        page.click(".formDialogHeader .btnCancel")
        page.wait_for_selector(f"{scope} .itemsContainer.vertical-wrap .card", timeout=15_000)

    def paging():
        set_page_size(page, 2)
        page.reload()
        page.wait_for_selector(scope, timeout=120_000)
        wait_items(page, scope, 2)
        check(page, paging_text() == f"1-2 of {len(media.movies)}", f"the first page ({paging_text()})")
        page.locator(f"{scope} .btnNextPage").first.click()
        expect_order(page, scope, names, media.movie_names[2:4], "the next page holds the next two")
        check(page, paging_text() == f"3-4 of {len(media.movies)}", f"the second page ({paging_text()})")
        settle(page)
        shot(page, "library-list-paging")
        page.locator(f"{scope} .btnPreviousPage").first.click()
        expect_order(page, scope, names, media.movie_names[:2], "back on the first page")
        set_page_size(page, None)
        page.reload()
        page.wait_for_selector(scope, timeout=120_000)
        wait_items(page, scope, len(media.movies))

    def album():
        album_item = next(item for item in media.albums if item["Name"] == "First Album")
        tracks = [song for song in media.songs if song.get("AlbumId") == album_item["Id"]]
        go(page, f"/list?parentId={album_item['Id']}&serverId={media.server_id}")
        wait_until(lambda: album_item["Id"] in route(page), timeout=10)
        wait_items(page, scope, len(tracks))
        settle(page)
        shot(page, "library-list-album")
        check(page, page.locator(f"{scope} .btnPlay:visible").count() == 1 and page.locator(f"{scope} .btnShuffle:visible").count() == 1,
              "an album has one visible play and one visible shuffle button")
        page.locator(f"{scope} .btnPlay:visible").first.click()
        check(page, wait_until(lambda: player(page).get("isPlaying") and media_time(page, "audio") > 0.5, timeout=40),
              "play all on the list page plays the album")
        check(page, public_queue_count(page) == len(tracks), f"the album is queued ({len(tracks)})")
        check(page, wait_until(lambda: page.locator(f"{scope} .btnQueue:visible").count() == 1, timeout=10),
              "the queue button appears while something plays")
        check(page, stop_playback(page), "the album stops")

    def other_lists():
        go(page, f"/list?type=nextup&serverId={media.server_id}")
        wait_until(lambda: "type=nextup" in route(page), timeout=10)
        settle(page, 3000)
        shot(page, "library-list-nextup")
        print("next up:", len(card_ids(page, scope)), "items; paging text:",
              page.locator(f"{scope} .listPaging span").first.inner_text().strip() if page.locator(f"{scope} .listPaging span").count() else None)
        check(page, page.locator(f"{scope} .btnSort.hide").count() == 2, "next up cannot be sorted")
        go(page, f"/list?type=Movie&IsFavorite=true&serverId={media.server_id}")
        page.wait_for_selector(f"{scope} .itemsContainer .card[data-id='{media.favorite['Id']}']", timeout=30_000)
        settle(page)
        shot(page, "library-list-favoritemovies")

    for name, action in (("list folder", folder), ("list alphabet", alphabet), ("list sorting", sorting), ("list filter", filtering),
                         ("list view settings", view_settings), ("list paging", paging), ("list album", album),
                         ("list other", other_lists)):
        step(page, name, action)
        if page.locator(".formDialogHeader .btnCancel").count():
            page.click(".formDialogHeader .btnCancel")
            page.wait_for_timeout(500)
        if name not in ("list paging", "list album", "list other") and address[1:] not in route(page):
            go(page, address)
            page.wait_for_selector(scope, timeout=30_000)


def phone_pages(phone, media):
    library = media.movies_library["Id"]
    sign_in(phone)
    phone.wait_for_selector("#homeTab .section0 .card", timeout=120_000)
    check(phone, phone.locator("html.layout-mobile").count() == 1, "the phone uses the mobile layout")

    go(phone, f"/movies?topParentId={library}")
    phone.wait_for_selector("#moviesPage", timeout=30_000)
    wait_items(phone, "#moviesPage", len(media.movies))
    settle(phone, 2500)
    shot(phone, "library-movies-mobile")
    overflow = phone.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
    check(phone, overflow <= 1, f"the movies page does not scroll sideways on a phone ({overflow}px)")

    phone.click("#moviesPage .jf-library-view-button")
    phone.wait_for_selector("[data-tab-index='2']", timeout=5_000)
    phone.wait_for_timeout(300)
    shot(phone, "library-movies-mobile-tabmenu")
    close_menu(phone)

    # A long press starts multi-select (touch browsers report it as a context-menu event).
    phone.locator("#moviesPage .itemsContainer .card").first.dispatch_event("contextmenu")
    phone.wait_for_selector(".selectionCommandsPanel", timeout=10_000)
    check(phone, phone.inner_text(".itemSelectionCount").strip() == "1", "a long press selects the card")
    settle(phone, 500)
    shot(phone, "library-multiselect-mobile")
    phone.click(".btnCloseSelectionPanel")
    phone.wait_for_selector(".selectionCommandsPanel", state="detached", timeout=10_000)
    check(phone, phone.locator("#moviesPage .itemSelectionPanel").count() == 0, "closing the toolbar ends the selection")

    phone.click("#moviesPage .jf-library-btn-viewsettings")
    phone.wait_for_selector(".mud-menu-item.jf-library-view-list", timeout=5_000)
    phone.click(".mud-menu-item.jf-library-view-list")
    phone.wait_for_selector("#moviesPage .itemsContainer.vertical-list .listItem", timeout=15_000)
    close_menu(phone)
    settle(phone)
    shot(phone, "library-movies-mobile-list")

    go(phone, f"/music?topParentId={media.music_library['Id']}&tab=5")
    phone.wait_for_selector("#musicPage", timeout=30_000)
    wait_items(phone, "#musicPage", len(media.songs))
    settle(phone)
    shot(phone, "library-music-songs-mobile")

    go(phone, f"/tv?topParentId={media.shows_library['Id']}&tab=5")
    phone.wait_for_selector("#tvshowsPage", timeout=30_000)
    wait_items(phone, "#tvshowsPage", len(media.episodes))
    settle(phone)
    shot(phone, "library-tv-episodes-mobile")

    go(phone, f"/list?parentId={library}&serverId={media.server_id}")
    phone.wait_for_selector("#listPage", timeout=30_000)
    wait_items(phone, "#listPage", len(media.movies))
    settle(phone, 2500)
    shot(phone, "library-list-mobile")
    overflow = phone.evaluate("document.documentElement.scrollWidth - document.documentElement.clientWidth")
    check(phone, overflow <= 1, f"the list page does not scroll sideways on a phone ({overflow}px)")
    check(phone, phone.locator("#listPage button.btnSort:visible").count() == 1, "the phone shows one sort button")
    if phone.locator("#blazor-error-ui").is_visible():
        phone.errors.append("the unhandled-error banner is visible on the phone")


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        api = Api(page)
        media = Media(api)
        saved = api.user_data(media.touched)
        print("movies:", media.movie_names, "| songs:", len(media.songs), "| albums:", len(media.albums), "| episodes:", len(media.episodes))
        try:
            api.set_favorite(media.favorite["Id"], True)
            sign_in(page)
            page.wait_for_selector("#homeTab .section0 .card", timeout=30_000)

            movies_page(page, api, media)
            step(page, "tv", lambda: shows_page(page, media))
            step(page, "music", lambda: music_page(page, media))
            stop_playback(page)
            list_page(page, api, media)
            stop_playback(page)

            phone = new_phone_page(browser)
            step(phone, "phone", lambda: phone_pages(phone, media))
            page.errors.extend("phone: " + error for error in phone.errors if not error.endswith("/config.json")
                               and not error.startswith("Failed to load resource: the server responded with a status of 404"))
            code = finish_without_config(page, "library")
        finally:
            api.restore(saved)
            api.sign_out()
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
