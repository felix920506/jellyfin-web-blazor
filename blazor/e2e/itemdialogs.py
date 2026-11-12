"""Item dialogs and editors, opened from the card menus of the home page.

Covers the action sheet position, media info, the refresh dialog, the identifier, the subtitle editor and its
uploader, the image editor with an upload and a delete, the image downloader, the metadata editor with a save,
the collection and playlist editors, the playlist's own editor, the lyrics editor and the download command, at
a desktop width and again at a phone width.

Changes on the shared test server, all undone at the end (also when the script fails):
  * "Third Film (2008)": name, year and a tag are edited and restored; a banner is uploaded and deleted.
  * A collection and a playlist are created and deleted.
  * The playlist and one song are favourites of the test user for a few seconds (to get their cards on the
    favourites tab, the only place of the home page where they have a menu).
"""
import struct
import sys
import zlib

from playwright.sync_api import TimeoutError as PlaywrightTimeout
from playwright.sync_api import sync_playwright

from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in

AUTH = 'MediaBrowser Client="e2e", Device="e2e", DeviceId="e2e-itemdialogs", Version="1"'
MOVIE_FILE = "Third Film (2008).mp4"
MOVIE_NAME = "Third Film (2008)"
MOVIE_YEAR = 2008
EDITED_NAME = "Third Film E2E"
COLLECTION = "E2E item dialogs collection"
PLAYLIST = "E2E item dialogs playlist"
RENAMED_PLAYLIST = "E2E item dialogs playlist (renamed)"
TAG = "e2e-itemdialogs"


class Api:
    """Direct calls to the test server, to look things up and to undo what the script changed."""

    def __init__(self, page):
        self.request = page.request
        body = self.request.post(
            f"{SERVER}/Users/AuthenticateByName",
            headers={"Authorization": AUTH},
            data={"Username": USER, "Pw": PASSWORD},
        ).json()
        self.user = body["User"]["Id"]
        self.headers = {"Authorization": f'{AUTH}, Token="{body["AccessToken"]}"'}

    def items(self, **query):
        query = {"userId": self.user, "Recursive": "true", **query}
        return self.request.get(f"{SERVER}/Items", headers=self.headers, params=query).json()["Items"]

    def item(self, item_id):
        return self.request.get(f"{SERVER}/Items/{item_id}", headers=self.headers, params={"userId": self.user}).json()

    def update(self, item):
        return self.request.post(f"{SERVER}/Items/{item['Id']}", headers=self.headers, data=item).status

    def delete(self, item_id):
        return self.request.delete(f"{SERVER}/Items/{item_id}", headers=self.headers).status

    def images(self, item_id):
        return self.request.get(f"{SERVER}/Items/{item_id}/Images", headers=self.headers).json()

    def delete_image(self, item_id, image_type):
        return self.request.delete(f"{SERVER}/Items/{item_id}/Images/{image_type}", headers=self.headers).status

    def set_favorite(self, item_id, favorite):
        call = self.request.post if favorite else self.request.delete
        call(f"{SERVER}/UserFavoriteItems/{item_id}", headers=self.headers, params={"userId": self.user})

    def named(self, item_type, *names):
        return [i for i in self.items(IncludeItemTypes=item_type) if i["Name"] in names]

    def sign_out(self):
        self.request.post(f"{SERVER}/Sessions/Logout", headers=self.headers)


def check(page, condition, message):
    if not condition:
        page.errors.append("check failed: " + message)


def png(width=400, height=74, color=(0x00, 0xa4, 0xdc)):
    """A one-colour PNG: the banner that is uploaded."""
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xffffffff)

    row = b"\x00" + bytes(color) * width
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(row * height)) + chunk(b"IEND", b""))


def dialogs(page):
    return page.locator(".dialogContainer")


def top(page):
    return dialogs(page).last


def wait_closed(page, count):
    page.wait_for_function("n => document.querySelectorAll('.dialogContainer').length < n", arg=count, timeout=10_000)


def close_top(page):
    count = dialogs(page).count()
    top(page).locator(".btnCancel:not(.hide)").first.click()
    wait_closed(page, count)
    page.wait_for_timeout(300)


def close_all(page):
    for _ in range(6):
        if dialogs(page).count() == 0:
            return
        try:
            close_top(page)
        except PlaywrightTimeout:
            page.keyboard.press("Escape")
            page.wait_for_timeout(500)
    check(page, dialogs(page).count() == 0, "all dialogs could be closed")


def card_of(page, tab, item_id):
    card = page.locator(f"{tab} .card[data-id='{item_id}']").first
    card.wait_for(timeout=30_000)
    return card


def open_menu(page, card):
    card.scroll_into_view_if_needed()
    card.hover()
    button = card.locator(".cardOverlayContainer button[data-action='menu']")
    box = button.bounding_box()
    button.click()
    page.wait_for_selector(".actionSheet button.actionSheetMenuItem", timeout=10_000)
    page.wait_for_timeout(400)
    return box


def menu_ids(page):
    return page.eval_on_selector_all(".actionSheet button.actionSheetMenuItem", "e => e.map(b => b.dataset.id)")


def run(page, card, command, ready, state="visible"):
    """Opens the card's menu, picks a command and waits for its dialog."""
    open_menu(page, card)
    page.click(f".actionSheet button.actionSheetMenuItem[data-id='{command}']")
    page.wait_for_selector(ready, state=state, timeout=20_000)
    page.wait_for_timeout(900)


def toast_shown(page, text):
    try:
        page.wait_for_function(
            "t => [...document.querySelectorAll('.toast')].some(e => e.textContent.includes(t))", arg=text, timeout=6_000)
        return True
    except PlaywrightTimeout:
        return False


def home(page, tab=0):
    go(page, "/home" if tab == 0 else f"/home?tab={tab}")
    page.wait_for_selector("#homeTab .section0 .card" if tab == 0 else "#favoritesTab", timeout=60_000)
    page.wait_for_timeout(800)


def menu_position(page, api, movie, suffix):
    card = card_of(page, "#homeTab", movie)
    button = open_menu(page, card)
    shot(page, f"itemdialogs-menu{suffix}")
    ids = menu_ids(page)
    print(f"menu{suffix}:", ids)
    for command in ("addtocollection", "addtoplaylist", "download", "edit", "editimages", "editsubtitles", "identify",
                    "moremediainfo", "refresh"):
        check(page, command in ids, f"the menu offers {command}")
    check(page, "share" not in ids, "share is not offered without navigator.share")

    sheet = page.locator(".dialog.actionSheet").last
    box = sheet.bounding_box()
    viewport = page.viewport_size
    print(f"menu{suffix}: button {button}, sheet {box}")
    check(page, sheet.evaluate("e => getComputedStyle(e).position") == "fixed", "the menu is positioned at its button")
    check(page, box["x"] >= 9 and box["y"] >= 9, "the menu keeps its distance from the left and top edge")
    check(page, box["x"] + box["width"] <= viewport["width"] and box["y"] + box["height"] <= viewport["height"],
          "the menu stays inside the window")
    # Centred on the button unless that would leave the window.
    centre = button["x"] + button["width"] / 2
    fits = centre - box["width"] / 2 >= 10 and centre + box["width"] / 2 <= viewport["width"]
    check(page, not fits or abs(box["x"] + box["width"] / 2 - centre) <= 2, "the menu is centred on its button")
    page.keyboard.press("Escape")
    page.wait_for_timeout(400)
    check(page, dialogs(page).count() == 0, "Escape closes the menu")


def media_info(page, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "moremediainfo", "#mediaInfoContent .mediaInfoStream")
    shot(page, f"itemdialogs-mediainfo{suffix}")
    text = top(page).inner_text()
    check(page, MOVIE_FILE in text, "media info shows the path to an administrator")
    check(page, "Container" in text and "Codec" in text, "media info lists the attributes")
    if not suffix:
        top(page).locator(".btnCopy").first.click()
        check(page, toast_shown(page, "Copied"), "copying media info is confirmed")
        copied = page.evaluate("navigator.clipboard.readText()")
        check(page, "Container" in copied, f"media info reaches the clipboard ({copied[:60]!r})")
    close_all(page)


def refresh(page, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "refresh", "#selectMetadataRefreshMode")
    page.select_option("#selectMetadataRefreshMode", "all")
    page.wait_for_timeout(300)
    shot(page, f"itemdialogs-refresh{suffix}")
    check(page, top(page).locator("label.fldReplaceExistingImages").is_visible(), "replacing images is offered for a full refresh")
    page.select_option("#selectMetadataRefreshMode", "scan")
    if suffix:
        close_all(page)
        return
    with page.expect_response(lambda r: f"/Items/{movie}/Refresh" in r.url and r.request.method == "POST") as response:
        top(page).locator("button[type=submit]").click()
    check(page, response.value.status == 204, f"the server queues the refresh ({response.value.status})")
    check(page, "MetadataRefreshMode=Default" in response.value.url and "Recursive=true" in response.value.url,
          "scanning sends a default refresh")
    check(page, toast_shown(page, "Refresh queued"), "the refresh is confirmed")
    close_all(page)


def identify(page, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "identify", "#txtLookupName")
    shot(page, f"itemdialogs-identify{suffix}")
    margin = top(page).locator("form.popupIdentifyForm > p").evaluate("p => getComputedStyle(p).marginTop")
    check(page, margin != "0px", "paragraphs of the dialogs keep the browser's margin")
    check(page, MOVIE_FILE in top(page).inner_text(), "the identifier shows the path")
    if not suffix:
        page.fill("#txtLookupName", "Third Film")
        page.fill("#txtLookupYear", "2008")
        with page.expect_response(lambda r: "/Items/RemoteSearch/Movie" in r.url) as response:
            top(page).locator("form.popupIdentifyForm button[type=submit]").click()
        check(page, response.value.status == 200, f"the server answers the search ({response.value.status})")
        body = response.value.request.post_data_json
        check(page, body["ItemId"] == movie and body["SearchInfo"]["Name"] == "Third Film" and body["SearchInfo"]["Year"] == 2008,
              f"the search sends the name and the year ({body})")
        page.wait_for_timeout(1000)
        shot(page, "itemdialogs-identify-results")
    close_all(page)


def subtitles(page, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "editsubtitles", ".subtitleSearchForm #selectLanguage")
    if not suffix:
        with page.expect_response(lambda r: f"/Items/{movie}/RemoteSearch/Subtitles/" in r.url) as response:
            top(page).locator(".btnSearchSubtitles").click()
        check(page, response.value.status == 200, f"the server answers the subtitle search ({response.value.status})")
        page.wait_for_timeout(800)
        check(page, top(page).locator(".noSearchResults").is_visible(), "a search without results says so")
    shot(page, f"itemdialogs-subtitles{suffix}")
    check(page, MOVIE_FILE in top(page).inner_text(), "the subtitle editor names the file")
    top(page).locator(".btnOpenUploadMenu").click()
    page.wait_for_selector(".uploadSubtitleForm", timeout=10_000)
    page.wait_for_timeout(600)
    shot(page, f"itemdialogs-subtitle-upload{suffix}")
    close_all(page)


def images(page, api, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "editimages", "#imagesContainer")
    shot(page, f"itemdialogs-images{suffix}")
    top(page).locator("#imagesContainer .btnOpenUploadMenu").click()
    page.wait_for_selector(".uploadItemImageForm", timeout=10_000)
    page.wait_for_timeout(500)
    if suffix:
        shot(page, f"itemdialogs-image-upload{suffix}")
        close_all(page)
        return

    page.set_input_files("#uploadImage", {"name": "banner.png", "mimeType": "image/png", "buffer": png()})
    page.wait_for_selector("#imageOutput img", timeout=10_000)
    page.select_option("#selectImageType", "Banner")
    page.wait_for_timeout(400)
    shot(page, "itemdialogs-image-upload")
    with page.expect_response(lambda r: f"/Items/{movie}/Images/Banner" in r.url and r.request.method == "POST") as response:
        top(page).locator("form.uploadItemImageForm button[type=submit]").click()
    check(page, response.value.status == 204, f"the server takes the uploaded image ({response.value.status})")
    check(page, response.value.request.headers.get("content-type", "").startswith("image/png"),
          "the upload carries the content type of the file")
    page.wait_for_selector(".btnDeleteImage[data-imagetype='Banner']", timeout=15_000)
    page.wait_for_timeout(1200)
    shot(page, "itemdialogs-images-uploaded")
    check(page, any(i["ImageType"] == "Banner" for i in api.images(movie)), "the item has the banner")

    top(page).locator(".btnDeleteImage[data-imagetype='Banner']").click()
    page.wait_for_selector("button.btnOption[data-id=ok]", timeout=10_000)
    page.wait_for_timeout(400)
    shot(page, "itemdialogs-image-delete")
    with page.expect_response(lambda r: f"/Items/{movie}/Images/Banner" in r.url and r.request.method == "DELETE") as response:
        page.click("button.btnOption[data-id=ok]")
    check(page, response.value.status == 204, f"the server deletes the image ({response.value.status})")
    page.wait_for_selector(".btnDeleteImage[data-imagetype='Banner']", state="detached", timeout=15_000)
    check(page, not any(i["ImageType"] == "Banner" for i in api.images(movie)), "the banner is gone")

    # Remote images: the test server has no image providers, so the search buttons stay hidden.
    browse = top(page).locator("#imagesContainer .btnBrowseAllImages")
    print("image providers offered:", browse.is_visible())
    if browse.is_visible():
        browse.click()
        page.wait_for_selector("#selectBrowsableImageType", timeout=10_000)
        page.wait_for_timeout(1500)
        shot(page, "itemdialogs-image-download")
    close_all(page)


def metadata(page, api, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "edit", "form.editItemMetadataForm #txtName")
    page.wait_for_function("document.querySelector('#txtName').value.length > 0", timeout=20_000)
    page.wait_for_timeout(600)
    shot(page, f"itemdialogs-metadata{suffix}")
    check(page, page.input_value("#txtName") == MOVIE_NAME, "the metadata editor shows the name")
    check(page, page.input_value("#txtProductionYear") == str(MOVIE_YEAR), "the metadata editor shows the year")
    check(page, MOVIE_FILE in page.input_value("#txtPath"), "the metadata editor shows the path")
    check(page, top(page).locator("#fldTagline").is_visible(), "a movie has a tagline")
    check(page, not top(page).locator("#fldAirDays").is_visible(), "a movie has no air days")

    top(page).locator("#metadataSettingsCollapsible").scroll_into_view_if_needed()
    page.wait_for_timeout(500)
    shot(page, f"itemdialogs-metadata-settings{suffix}")
    top(page).locator("#peopleCollapsible").scroll_into_view_if_needed()
    page.wait_for_timeout(300)
    shot(page, f"itemdialogs-metadata-lists{suffix}")

    top(page).locator(".btnMore").click()
    page.wait_for_selector(".actionSheet button.actionSheetMenuItem", timeout=10_000)
    page.wait_for_timeout(400)
    shot(page, f"itemdialogs-metadata-more{suffix}")
    ids = menu_ids(page)
    check(page, "editimages" in ids and "identify" in ids and "edit" not in ids, f"the editor's own menu ({ids})")
    page.keyboard.press("Escape")
    page.wait_for_timeout(400)

    top(page).locator("#btnAddPerson").click()
    page.wait_for_selector("form.popupEditPersonForm", timeout=10_000)
    page.wait_for_timeout(500)
    shot(page, f"itemdialogs-person{suffix}")
    close_top(page)

    if suffix:
        close_all(page)
        return

    page.fill("#txtName", EDITED_NAME)
    page.fill("#txtProductionYear", "2009")
    top(page).locator("#tagsCollapsible .btnAddTextItem").click()
    page.wait_for_selector("#txtInput", timeout=10_000)
    page.fill("#txtInput", TAG)
    top(page).locator("form button[type=submit]").click()
    page.wait_for_selector(f"#listTags :text('{TAG}')", timeout=10_000)
    with page.expect_response(lambda r: r.url.split("?")[0].endswith(f"/Items/{movie}") and r.request.method == "POST") as response:
        top(page).locator(".btnSave").click()
    check(page, response.value.status == 204, f"the server saves the item ({response.value.status})")
    check(page, toast_shown(page, "Item saved"), "saving is confirmed")
    page.wait_for_timeout(1000)
    close_all(page)
    saved = api.item(movie)
    check(page, saved["Name"] == EDITED_NAME and saved.get("ProductionYear") == 2009 and TAG in saved.get("Tags", []),
          f"the item was saved ({saved['Name']}, {saved.get('ProductionYear')}, {saved.get('Tags')})")


def collection(page, api, movie, suffix):
    run(page, card_of(page, "#homeTab", movie), "addtocollection", "#selectCollectionToAddTo")
    shot(page, f"itemdialogs-collection{suffix}")
    if suffix:
        close_all(page)
        return

    page.fill("#txtNewCollectionName", COLLECTION)
    with page.expect_response(lambda r: r.url.split("?")[0].endswith("/Collections") and r.request.method == "POST") as response:
        top(page).locator("form.newCollectionForm button[type=submit]").click()
    check(page, response.value.status == 200, f"the server creates the collection ({response.value.status})")
    created = response.value.json()["Id"]
    page.wait_for_function("id => location.hash.includes(id)", arg=created, timeout=10_000)
    check(page, dialogs(page).count() == 0, "the collection editor closes")
    children = api.items(ParentId=created)
    check(page, [c["Id"] for c in children] == [movie], "the new collection holds the movie")

    # The movie is offered the collection that now exists.
    home(page)
    # (An option of a closed select counts as hidden, so it is only waited for to exist.)
    run(page, card_of(page, "#homeTab", movie), "addtocollection", f"#selectCollectionToAddTo option[value='{created}']",
        state="attached")
    page.select_option("#selectCollectionToAddTo", created)
    page.wait_for_timeout(300)
    shot(page, "itemdialogs-collection-existing")
    with page.expect_response(lambda r: f"/Collections/{created}/Items" in r.url and r.request.method == "POST") as response:
        top(page).locator("form.newCollectionForm button[type=submit]").click()
    check(page, response.value.status == 204, f"the server adds to the collection ({response.value.status})")
    check(page, toast_shown(page, "Items added"), "adding to a collection is confirmed")
    close_all(page)


def playlist(page, api, movie, song, suffix):
    run(page, card_of(page, "#homeTab", movie), "addtoplaylist", "#selectPlaylistToAddTo")
    shot(page, f"itemdialogs-playlist{suffix}")
    if suffix:
        close_all(page)
        return None

    page.fill("#txtNewPlaylistName", PLAYLIST)
    with page.expect_response(lambda r: r.url.split("?")[0].endswith("/Playlists") and r.request.method == "POST") as response:
        top(page).locator("form button[type=submit]").click()
    check(page, response.value.status == 200, f"the server creates the playlist ({response.value.status})")
    created = response.value.json()["Id"]
    page.wait_for_function("id => location.hash.includes(id)", arg=created, timeout=10_000)
    check(page, [c["Id"] for c in api.items(ParentId=created)] == [movie], "the new playlist holds the movie")

    # The playlist and a song get their menus on the favourites tab.
    api.set_favorite(created, True)
    api.set_favorite(song, True)
    try:
        home(page, tab=1)
        playlist_card = card_of(page, "#favoritesTab", created)
        song_card = card_of(page, "#favoritesTab", song)
    finally:
        api.set_favorite(created, False)
        api.set_favorite(song, False)

    run(page, song_card, "editlyrics", ".lyricsSearchForm")
    with page.expect_response(lambda r: f"/Audio/{song}/RemoteSearch/Lyrics" in r.url) as response:
        top(page).locator(".btnSearchLyrics").click()
    check(page, response.value.status == 200, f"the server answers the lyrics search ({response.value.status})")
    page.wait_for_timeout(800)
    shot(page, "itemdialogs-lyrics")
    check(page, ".mp3" in top(page).inner_text(), "the lyrics editor names the file")
    top(page).locator(".btnOpenUploadMenu").click()
    page.wait_for_selector(".uploadLyricsForm, .lyricsUploaderDialog form", timeout=10_000)
    page.wait_for_timeout(600)
    shot(page, "itemdialogs-lyrics-upload")
    close_all(page)

    run(page, playlist_card, "editplaylist", "#txtNewPlaylistName")
    page.wait_for_function("document.querySelector('#txtNewPlaylistName').value.length > 0", timeout=10_000)
    shot(page, "itemdialogs-playlist-edit")
    check(page, page.input_value("#txtNewPlaylistName") == PLAYLIST, "the playlist editor shows the name")
    page.fill("#txtNewPlaylistName", RENAMED_PLAYLIST)
    with page.expect_response(lambda r: r.url.split("?")[0].endswith(f"/Playlists/{created}") and r.request.method == "POST") as response:
        top(page).locator("form button[type=submit]").click()
    check(page, response.value.status == 204, f"the server saves the playlist ({response.value.status})")
    page.wait_for_timeout(800)
    close_all(page)
    check(page, api.item(created)["Name"] == RENAMED_PLAYLIST, "the playlist was renamed")
    return created


def download(page, movie):
    card = card_of(page, "#homeTab", movie)
    open_menu(page, card)
    with page.expect_download(timeout=30_000) as download_info:
        page.click(".actionSheet button.actionSheetMenuItem[data-id='download']")
    started = download_info.value
    print("download:", started.url, started.suggested_filename)
    check(page, f"/Items/{movie}/Download" in started.url and "ApiKey=" in started.url, "the download uses the item's download URL")
    started.cancel()
    page.wait_for_timeout(500)
    check(page, dialogs(page).count() == 0, "the menu closes after a download")


def finish_dialogs(page) -> int:
    """finish(), without the failures that the original client meets as well.

    The optional config.json is not in the repository, and a song without lyrics answers the request for
    its lyrics with 404. The browser also reports each as a console error without a URL.
    """
    expected = [e for e in page.errors if e.startswith("HTTP 404 ") and (e.endswith("/config.json") or "/Lyrics" in e)]
    generic = "Failed to load resource: the server responded with a status of 404"
    for entry in expected:
        page.errors.remove(entry)
        for error in page.errors:
            if error.startswith(generic):
                page.errors.remove(error)
                break
    return finish(page, "itemdialogs")


def restore(api, movie):
    """Puts the test server back; safe to run after a script that failed half way."""
    try:
        item = api.item(movie)
        item["Name"] = MOVIE_NAME
        item["ProductionYear"] = MOVIE_YEAR
        item["Tags"] = [t for t in item.get("Tags", []) if t != TAG]
        print("restore movie:", api.update(item), api.item(movie)["Name"], api.item(movie).get("ProductionYear"))
        if any(i["ImageType"] == "Banner" for i in api.images(movie)):
            print("restore banner:", api.delete_image(movie, "Banner"))
        for leftover in api.named("BoxSet", COLLECTION) + api.named("Playlist", PLAYLIST, RENAMED_PLAYLIST):
            api.set_favorite(leftover["Id"], False)
            print("delete", leftover["Type"], leftover["Name"], api.delete(leftover["Id"]))
    finally:
        api.sign_out()


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        page.context.grant_permissions(["clipboard-read", "clipboard-write"])
        api = Api(page)
        movie = next(i["Id"] for i in api.items(IncludeItemTypes="Movie", Fields="Path") if i["Path"].endswith(MOVIE_FILE))
        song = api.items(IncludeItemTypes="Audio", SortBy="SortName")[0]["Id"]
        code = 1
        try:
            sign_in(page)
            home(page)
            page.wait_for_timeout(1500)

            menu_position(page, api, movie, "")
            media_info(page, movie, "")
            refresh(page, movie, "")
            identify(page, movie, "")
            subtitles(page, movie, "")
            images(page, api, movie, "")
            collection(page, api, movie, "")
            home(page)
            playlist(page, api, movie, song, "")
            home(page)
            download(page, movie)
            metadata(page, api, movie, "")
            restore_name = api.item(movie)
            restore_name["Name"], restore_name["ProductionYear"] = MOVIE_NAME, MOVIE_YEAR
            api.update(restore_name)

            # A phone-wide window. The layout stays the desktop one (it follows the user agent, as in the
            # original): in the mobile layout the cards of the home page have no menu to open the dialogs from.
            page.set_viewport_size({"width": 400, "height": 800})
            page.reload()
            page.wait_for_selector("#homeTab .section0 .card", timeout=120_000)
            page.wait_for_timeout(2500)
            menu_position(page, api, movie, "-narrow")
            media_info(page, movie, "-narrow")
            refresh(page, movie, "-narrow")
            identify(page, movie, "-narrow")
            subtitles(page, movie, "-narrow")
            images(page, api, movie, "-narrow")
            collection(page, api, movie, "-narrow")
            playlist(page, api, movie, song, "-narrow")
            metadata(page, api, movie, "-narrow")
            code = finish_dialogs(page)
        finally:
            restore(api, movie)
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
