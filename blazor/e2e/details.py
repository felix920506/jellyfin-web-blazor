"""Item details page: every item type of the test libraries, track selection, playback, user data, menu.

    ./dev e2e details.py

Opens a movie, the series, a season, an episode, an album, an artist, a song and (when the server has
them) a person and a genre, at desktop and phone widths; checks buttons and sections; chooses other
tracks and starts playback with them; toggles played and favorite; opens the item menu.

Changes user data of the test user (played, favorite, resume position of two movies) and restores it.
The last part takes reference screenshots of the same items in the stock client the test server ships
(details-reference-*.png); failures there are reported but do not fail the run.
"""
import json
import sys
import time
from urllib.parse import parse_qs, urlsplit

from playwright.sync_api import TimeoutError as PlaywrightTimeout
from playwright.sync_api import sync_playwright

from home import Api, new_phone_page
from lib import OUT, PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


class DetailsApi(Api):
    def item(self, item_id):
        return self.request.get(f"{SERVER}/Items/{item_id}", headers=self.headers, params={"userId": self.user}).json()


def check(page, condition, message, detail=""):
    print(("ok   " if condition else "FAIL ") + message + (f"  [{detail}]" if detail and not condition else ""))
    if not condition:
        page.errors.append("check failed: " + message)
    return condition


def wait_until(probe, timeout=20.0, interval=0.25):
    deadline = time.time() + timeout
    value = probe()
    while not value and time.time() < deadline:
        time.sleep(interval)
        value = probe()
    return value


def playback_info(request):
    """What a PlaybackInfo request asks for; the server reads the choices from the query or the body."""
    asked = {key.lower(): values[0] for key, values in parse_qs(urlsplit(request.url).query).items()}
    try:
        body = json.loads((request.post_data_buffer or b"{}").decode("utf-8"))
    except ValueError:
        body = {}
    asked.update({key.lower(): str(value) for key, value in body.items() if not isinstance(value, (dict, list))})
    asked["url"] = request.url
    return asked


def play_from_page(page):
    """Presses the page's play button; returns what the PlaybackInfo request it causes asks for."""
    try:
        with page.expect_request(lambda r: "/PlaybackInfo" in r.url and r.method == "POST", timeout=30_000) as started:
            page.click("#itemDetailPage .mainDetailButtons .btnPlay")
        return playback_info(started.value)
    except PlaywrightTimeout:
        return {}


def back_to_details(page, item):
    """After playback: waits until the player has stopped and the details page of the item shows again."""
    for _ in range(3):
        if not count(page, ".dialogContainer"):
            break
        page.keyboard.press("Escape")
        page.wait_for_timeout(400)
    wait_until(lambda: not command(page, "state").get("isPlaying"), timeout=15)
    page.wait_for_timeout(1000)
    if not route(page).startswith("#/details"):
        go(page, details_route(item))
    wait_for_name(page, item["Name"])
    page.wait_for_timeout(1500)


def command(page, name, args=None):
    """Runs a command of the playback test hook; returns the playback manager's state."""
    raw = page.evaluate(
        "async ([name, args]) => (await import('/js/playback/player.js')).testCommand(name, args)",
        [name, args or {}],
    )
    return json.loads(raw) if raw else {}


def video_time(page):
    media = page.evaluate("async () => (await import('/js/playback/player.js')).debugState()")["video"]
    return media["currentTime"] if media else -1


def route(page):
    return page.evaluate("location.hash")


def details_route(item, context=None):
    return f"/details?id={item['Id']}" + (f"&context={context}" if context else "") + f"&serverId={item['ServerId']}"


def open_details(page, item, context=None, name=None):
    """Opens the details page of an item and waits until its name is shown."""
    go(page, details_route(item, context))
    wait_for_name(page, name or item["Name"])
    page.wait_for_timeout(2500)


def wait_for_name(page, name):
    page.wait_for_function(
        "name => { const e = document.querySelector('#itemDetailPage .nameContainer');"
        " return !!e && e.textContent.includes(name); }",
        arg=name, timeout=30_000)


def visible_buttons(page):
    return page.eval_on_selector_all(
        "#itemDetailPage .mainDetailButtons button",
        "buttons => buttons.filter(b => !b.classList.contains('hide') && b.offsetParent !== null)"
        ".map(b => [...b.classList].find(c => /^btn[A-Z]/.test(c)) || b.className)")


def shown(page, selector):
    return page.evaluate(
        "s => { const e = document.querySelector(s); return !!e && !e.classList.contains('hide') && e.offsetParent !== null; }",
        selector)


def count(page, selector):
    return page.locator(selector).count()


def section_titles(page):
    return page.eval_on_selector_all(
        "#itemDetailPage .verticalSection, #itemDetailPage .detailVerticalSection",
        "sections => [...new Set(sections.filter(s => !s.classList.contains('hide') && s.offsetParent !== null)"
        ".map(s => { const t = s.querySelector('.sectionTitle'); return t && !t.classList.contains('hide') ? t.textContent.trim() : ''; })"
        ".filter(t => t))]")


def options(page, select):
    return page.eval_on_selector_all(
        f"#itemDetailPage select.{select} option", "options => options.map(o => [o.value, o.textContent.trim()])")


def scroll_shot(page, name, pixels=700):
    page.mouse.wheel(0, pixels)
    page.wait_for_timeout(1500)
    shot(page, name)
    page.mouse.wheel(0, -pixels * 3)
    page.wait_for_timeout(400)


def finish_details(page) -> int:
    """finish(), without the failure of the optional config.json (see home.py)."""
    missing = [e for e in page.errors if e.startswith("HTTP 404 ") and e.endswith("/config.json")]
    generic = "Failed to load resource: the server responded with a status of 404"
    for entry in missing:
        page.errors.remove(entry)
        for error in page.errors:
            if error.startswith(generic):
                page.errors.remove(error)
                break
    return finish(page, "details")


def movie_page(page, api, movie):
    print("-- movie:", movie["Name"])
    open_details(page, movie)
    shot(page, "details-movie-desktop")
    buttons = visible_buttons(page)
    print("   buttons:", buttons)
    for button in ("btnPlay", "btnPlaystate", "btnUserRating", "btnMoreCommands"):
        check(page, button in buttons, f"the movie shows {button}", str(buttons))
    check(page, shown(page, "#itemDetailPage .detailImageContainer .card"), "the movie shows its poster")
    check(page, page.locator("#itemDetailPage .itemMiscInfo-primary").inner_text().strip() != "", "the media info line is filled")
    print("   sections:", section_titles(page))
    highlighted = wait_until(lambda: page.locator(".mud-button-text-primary").all_inner_texts(), timeout=10) or []
    check(page, any("movies" in text.lower() for text in highlighted), "the app bar highlights the movie's library", str(highlighted))
    backdrop = page.evaluate(
        "() => ({ images: document.querySelectorAll('.backdropContainer .backdropImage').length,"
        " shown: !!document.querySelector('.backgroundContainer.withBackdrop'),"
        " transparent: document.documentElement.className + ' | ' + document.body.className,"
        " body: getComputedStyle(document.body).backgroundColor })")
    print("   backdrop:", backdrop, "item backdrops:", movie.get("BackdropImageTags"))
    scroll_shot(page, "details-movie-desktop-scrolled")

    # Track selection.
    audio = options(page, "selectAudio")
    subtitles = options(page, "selectSubtitles")
    print("   audio:", audio, "subtitles:", subtitles)
    check(page, len(audio) == 2, "the audio select lists both tracks", str(audio))
    check(page, len(subtitles) >= 2 and subtitles[0][0] == "-1", "the subtitle select starts with Off", str(subtitles))
    check(page, not shown(page, "#itemDetailPage .selectSourceContainer"), "one version: the version select is hidden")
    check(page, page.locator("#itemDetailPage select.selectVideo").is_disabled(), "the video select is for information only")

    # The selects start with the defaults the server names for the version.
    source = api.item(movie["Id"])["MediaSources"][0]
    current = page.input_value("#itemDetailPage select.selectAudio")
    current_subtitle = page.input_value("#itemDetailPage select.selectSubtitles")
    print("   defaults:", source.get("DefaultAudioStreamIndex"), source.get("DefaultSubtitleStreamIndex"), "selected:", current, current_subtitle)
    check(page, current == str(source.get("DefaultAudioStreamIndex")), "the audio select starts with the default track", current)
    default_subtitle = source.get("DefaultSubtitleStreamIndex")
    check(page, current_subtitle == str(-1 if default_subtitle is None else default_subtitle),
          "the subtitle select starts with the default track or Off", current_subtitle)

    other_audio = [value for value, _ in audio if value != current][0]
    subtitle = [value for value, _ in subtitles if value != current_subtitle][-1]
    page.select_option("#itemDetailPage select.selectAudio", other_audio)
    page.select_option("#itemDetailPage select.selectSubtitles", subtitle)
    page.wait_for_timeout(300)
    shot(page, "details-movie-tracks")

    info = play_from_page(page)
    print("   playback info request:", {k: v for k, v in info.items() if k != "url"})
    check(page, bool(info), "the play button starts playback")
    if info:
        check(page, info.get("audiostreamindex") == other_audio, "playback gets the chosen audio track", str(info))
        check(page, info.get("subtitlestreamindex") == subtitle, "playback gets the chosen subtitle track", str(info))
        check(page, info.get("mediasourceid") == source["Id"], "playback gets the version", str(info))
    page.wait_for_timeout(4000)
    state = command(page, "state")
    print("   state:", {k: state.get(k) for k in ("isPlaying", "playMethod", "audioStreamIndex", "subtitleStreamIndex", "mediaSourceId")})
    shot(page, "details-movie-playing")
    if state.get("isPlaying"):
        check(page, str(state.get("audioStreamIndex")) == other_audio, "the player plays the chosen audio track", str(state))
        command(page, "stop")
    back_to_details(page, movie)

    # The server remembers the tracks last played for an item (they were the defaults above): play the
    # earlier ones again, from the page as well, until the server has them back, so the item is left
    # as it was found.
    def remembered():
        now = api.item(movie["Id"])["MediaSources"][0]
        subtitle_index = now.get("DefaultSubtitleStreamIndex")
        return str(now.get("DefaultAudioStreamIndex")), str(-1 if subtitle_index is None else subtitle_index)

    page.reload()
    wait_for_name(page, movie["Name"])
    page.wait_for_selector("#itemDetailPage select.selectAudio option", state="attached", timeout=15_000)
    page.wait_for_timeout(1500)
    print("   remembered after playing:", remembered(), "selected:", page.input_value("#itemDetailPage select.selectAudio"))
    page.select_option("#itemDetailPage select.selectAudio", current)
    page.select_option("#itemDetailPage select.selectSubtitles", current_subtitle)
    page.wait_for_timeout(300)
    play_from_page(page)
    restored = wait_until(lambda: remembered() == (current, current_subtitle), timeout=40, interval=1.0)
    command(page, "stop")
    check(page, restored, "the remembered tracks are as before", str(remembered()))
    back_to_details(page, movie)

    # Played and favorite.
    api.set_played(movie["Id"], False)
    api.set_favorite(movie["Id"], False)
    page.reload()
    wait_for_name(page, movie["Name"])
    page.wait_for_timeout(1500)
    with page.expect_response(lambda r: "/UserPlayedItems/" in r.url and r.request.method == "POST"):
        page.click("#itemDetailPage .mainDetailButtons .btnPlaystate")
    page.wait_for_selector("#itemDetailPage .btnPlaystate .playstatebutton-icon-played", timeout=5_000)
    check(page, api.item(movie["Id"])["UserData"]["Played"], "the server has the movie marked played")
    with page.expect_response(lambda r: "/UserFavoriteItems/" in r.url and r.request.method == "POST"):
        page.click("#itemDetailPage .mainDetailButtons .btnUserRating")
    page.wait_for_selector("#itemDetailPage .btnUserRating .ratingbutton-icon-withrating", timeout=5_000)
    check(page, api.item(movie["Id"])["UserData"]["IsFavorite"], "the server has the movie as favorite")
    page.wait_for_timeout(500)
    shot(page, "details-movie-played-favorite")
    with page.expect_response(lambda r: "/UserPlayedItems/" in r.url and r.request.method == "DELETE"):
        page.click("#itemDetailPage .mainDetailButtons .btnPlaystate")
    with page.expect_response(lambda r: "/UserFavoriteItems/" in r.url and r.request.method == "DELETE"):
        page.click("#itemDetailPage .mainDetailButtons .btnUserRating")
    check(page, wait_until(lambda: count(page, "#itemDetailPage .btnPlaystate .playstatebutton-icon-played") == 0, timeout=5),
          "the played mark is removed again")

    # The item menu.
    page.click("#itemDetailPage .mainDetailButtons .btnMoreCommands")
    page.wait_for_selector(".actionSheet", timeout=5_000)
    page.wait_for_timeout(500)
    shot(page, "details-movie-menu")
    menu = page.locator(".actionSheet").inner_text()
    check(page, "Add to collection" in menu and "Add to playlist" in menu, "the item menu lists its commands", menu[:300])
    page.keyboard.press("Escape")
    page.wait_for_timeout(500)
    check(page, count(page, ".actionSheet") == 0, "Escape closes the menu")


def direct_play(page, api, movie):
    print("-- playback from the details page:", movie["Name"])
    open_details(page, movie)
    page.click("#itemDetailPage .mainDetailButtons .btnPlay")
    check(page, wait_until(lambda: route(page).startswith("#/video"), timeout=20), "play opens the video page", route(page))
    check(page, wait_until(lambda: video_time(page) > 1.5, timeout=30), "the video's time advances", str(video_time(page)))
    state = command(page, "state")
    check(page, state.get("itemId") == movie["Id"].replace("-", ""), "the movie of the page is playing", str(state.get("itemName")))
    shot(page, "details-playing")
    command(page, "stop")
    check(page, wait_until(lambda: route(page).startswith("#/details"), timeout=15), "stopping returns to the details page", route(page))
    wait_for_name(page, movie["Name"])
    page.wait_for_timeout(1500)
    shot(page, "details-after-playback")


def series_pages(page, api, series, seasons, episodes):
    print("-- series:", series["Name"])
    open_details(page, series, context="tvshows")
    shot(page, "details-series-desktop")
    buttons = visible_buttons(page)
    titles = section_titles(page)
    print("   buttons:", buttons, "sections:", titles)
    check(page, count(page, "#itemDetailPage #childrenContent .card") == len(seasons), "the series lists its seasons",
          str(count(page, "#itemDetailPage #childrenContent .card")))
    check(page, "Seasons" in titles, "the seasons section has its title", str(titles))
    check(page, "btnPlay" in buttons and "btnShuffle" in buttons, "the series can be played and shuffled", str(buttons))
    check(page, not shown(page, "#itemDetailPage form.trackSelections"), "a series has no track selection")
    scroll_shot(page, "details-series-desktop-scrolled")

    # A season card leads to the season.
    season = seasons[0]
    # On desktop a card shows its hover overlay under the pointer; overlay and image both carry the link action.
    card = page.locator(f"#itemDetailPage #childrenContent .card[data-id='{season['Id']}'] .cardScalable")
    card.hover()
    card.click(position={"x": 20, "y": 20}, force=True)
    wait_for_name(page, season["Name"])
    check(page, f"id={season['Id']}" in route(page), "a season card opens the season", route(page))
    page.wait_for_timeout(2500)
    check(page, page.evaluate("window.scrollY") == 0, "the season opens at the top of the page", str(page.evaluate("window.scrollY")))
    shot(page, "details-season-desktop")
    in_season = [e for e in episodes if e.get("SeasonId") == season["Id"]]
    rows = count(page, "#itemDetailPage #childrenContent .listItem")
    check(page, rows == len(in_season), "the season lists its episodes", f"{rows} of {len(in_season)}")
    check(page, count(page, "#itemDetailPage .nameContainer a") >= 1, "the season links to its series")
    print("   season buttons:", visible_buttons(page), "sections:", section_titles(page))

    episode = in_season[0]
    print("-- episode:", episode["Name"])
    open_details(page, episode)
    shot(page, "details-episode-desktop")
    titles = section_titles(page)
    print("   buttons:", visible_buttons(page), "sections:", titles)
    check(page, count(page, "#itemDetailPage .nameContainer a") >= 2, "the episode links to its series and season")
    check(page, shown(page, "#itemDetailPage form.trackSelections"), "an episode has the track selection")
    if len(in_season) > 1:
        check(page, wait_until(lambda: count(page, "#itemDetailPage .moreFromSeasonSection .card") == len(in_season), timeout=10),
              "more from the season lists its episodes", str(count(page, "#itemDetailPage .moreFromSeasonSection .card")))
    scroll_shot(page, "details-episode-desktop-scrolled")


def music_pages(page, api, albums, artists, songs):
    album = albums[0]
    print("-- album:", album["Name"])
    open_details(page, album)
    shot(page, "details-album-desktop")
    tracks = [s for s in songs if s.get("AlbumId") == album["Id"]]
    rows = count(page, "#itemDetailPage #childrenContent .listItem")
    buttons = visible_buttons(page)
    print("   buttons:", buttons, "sections:", section_titles(page))
    check(page, rows == len(tracks), "the album lists its tracks", f"{rows} of {len(tracks)}")
    check(page, "btnPlay" in buttons and "btnInstantMix" in buttons and "btnShuffle" in buttons, "album buttons", str(buttons))
    check(page, "btnPlaystate" not in buttons, "an album has no played button", str(buttons))
    check(page, count(page, "#itemDetailPage .nameContainer .musicParentName a") >= 1, "the album links to its artist")
    scroll_shot(page, "details-album-desktop-scrolled")

    artist = artists[0]
    print("-- artist:", artist["Name"])
    open_details(page, artist)
    shot(page, "details-artist-desktop")
    titles = section_titles(page)
    print("   buttons:", visible_buttons(page), "sections:", titles)
    check(page, wait_until(lambda: count(page, "#itemDetailPage #childrenContent .card") >= 1, timeout=10), "the artist shows albums")
    scroll_shot(page, "details-artist-desktop-scrolled")

    song = tracks[0] if tracks else songs[0]
    print("-- song:", song["Name"])
    open_details(page, song)
    shot(page, "details-song-desktop")
    buttons = visible_buttons(page)
    print("   buttons:", buttons, "sections:", section_titles(page))
    check(page, "btnPlay" in buttons and "btnInstantMix" in buttons, "song buttons", str(buttons))
    check(page, not shown(page, "#itemDetailPage form.trackSelections"), "a song has no track selection")


def by_name_pages(page, api):
    people = api.request.get(f"{SERVER}/Persons", headers=api.headers, params={"userId": api.user, "Limit": "1"}).json()["Items"]
    if people:
        print("-- person:", people[0]["Name"])
        open_details(page, people[0])
        shot(page, "details-person-desktop")
        print("   sections:", section_titles(page))
    else:
        print("-- person: the test server has no people; not opened")

    genres = api.request.get(f"{SERVER}/Genres", headers=api.headers, params={"userId": api.user, "Limit": "1"}).json()["Items"]
    if genres:
        genre = genres[0]
        print("-- genre:", genre["Name"])
        go(page, f"/details?genre={genre['Name']}&serverId={genre['ServerId']}")
        wait_for_name(page, genre["Name"])
        page.wait_for_timeout(2500)
        shot(page, "details-genre-desktop")
        titles = section_titles(page)
        print("   sections:", titles)
        check(page, len(titles) >= 1, "a genre shows the items it has", str(titles))
    else:
        print("-- genre: the test server has no genres; not opened")


def phone_pages(browser, movie, series, season, album, artist):
    print("-- phone")
    phone = new_phone_page(browser)
    sign_in(phone)
    check(phone, phone.locator("html.layout-mobile").count() == 1, "the phone uses the mobile layout")
    for name, item in (("movie", movie), ("series", series), ("season", season), ("album", album), ("artist", artist)):
        open_details(phone, item)
        shot(phone, f"details-{name}-mobile")
        phone.mouse.wheel(0, 600)
        phone.wait_for_timeout(1500)
        shot(phone, f"details-{name}-mobile-scrolled")
        overflow = phone.evaluate("() => document.documentElement.scrollWidth - document.documentElement.clientWidth")
        check(phone, overflow <= 1, f"the {name} page does not scroll sideways on a phone", str(overflow))
    open_details(phone, movie)
    check(phone, shown(phone, "#itemDetailPage .infoWrapper .detailImageContainer"), "the phone shows the poster next to the title")
    check(phone, "btnPlay" in visible_buttons(phone), "the phone shows the play button", str(visible_buttons(phone)))
    if phone.locator("#blazor-error-ui").is_visible():
        phone.errors.append("the unhandled-error banner is visible on the phone")
    return phone.errors


def reference_shots(browser, items):
    """Screenshots of the same items in the stock client of the test server, to compare layouts with."""
    for label, make in (("desktop", lambda: browser.new_page(viewport={"width": 1366, "height": 768})),
                        ("mobile", lambda: new_phone_page(browser))):
        page = make()
        try:
            page.goto(SERVER + "/web/")
            page.wait_for_selector("#txtManualName", timeout=90_000)
            page.fill("#txtManualName", USER)
            page.fill("#txtManualPassword", PASSWORD)
            page.press("#txtManualPassword", "Enter")
            page.wait_for_selector(".homePage, #indexPage", timeout=60_000)
            for name, item in items:
                page.evaluate("r => { location.hash = '#' + r; }", details_route(item))
                page.wait_for_selector("#itemDetailPage:not(.hide) .nameContainer .itemName, #itemDetailPage:not(.hide) .nameContainer .parentName",
                                       timeout=30_000)
                page.wait_for_timeout(3500)
                page.screenshot(path=f"{OUT}/details-reference-{name}-{label}.png")
                if label == "mobile" or name in ("movie", "series"):
                    page.mouse.wheel(0, 650)
                    page.wait_for_timeout(1500)
                    page.screenshot(path=f"{OUT}/details-reference-{name}-{label}-scrolled.png")
        except Exception as error:  # the reference is a convenience, not part of the test
            print(f"reference ({label}) not taken:", str(error)[:300])
        finally:
            page.close()


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        api = DetailsApi(page)
        fields = {"Fields": "MediaSources,BackdropImageTags"}
        movies = api.items(IncludeItemTypes="Movie", SortBy="SortName", **fields)
        second = next(m for m in movies if m["Name"].startswith("Second Feature"))
        open_codec = next(m for m in movies if m["Name"].startswith("Open Codec Movie"))
        series = api.items(IncludeItemTypes="Series")[0]
        seasons = api.items(IncludeItemTypes="Season", SortBy="SortName")
        episodes = api.items(IncludeItemTypes="Episode", SortBy="ParentIndexNumber,IndexNumber")
        albums = api.items(IncludeItemTypes="MusicAlbum", SortBy="SortName")
        songs = api.items(IncludeItemTypes="Audio", SortBy="Album,IndexNumber")
        artists = api.request.get(f"{SERVER}/Artists/AlbumArtists", headers=api.headers, params={"userId": api.user}).json()["Items"]
        changed = {m["Id"]: api.item(m["Id"])["UserData"] for m in (second, open_codec)}
        code = 1
        try:
            sign_in(page)
            movie_page(page, api, second)
            direct_play(page, api, open_codec)
            series_pages(page, api, series, seasons, episodes)
            music_pages(page, api, albums, artists, songs)
            by_name_pages(page, api)
            page.errors.extend(phone_pages(browser, second, series, seasons[0], albums[0], artists[0]))
            code = finish_details(page)
            reference_shots(browser, [("movie", second), ("series", series), ("season", seasons[0]),
                                      ("episode", episodes[0]), ("album", albums[0]), ("artist", artists[0])])
        finally:
            for item_id, data in changed.items():
                api.set_favorite(item_id, data.get("IsFavorite", False))
                api.set_played(item_id, data.get("Played", False))
                api.set_user_data(item_id, PlaybackPositionTicks=data.get("PlaybackPositionTicks", 0), Played=data.get("Played", False))
                after = api.item(item_id)["UserData"]
                kept = ("IsFavorite", "Played", "PlaybackPositionTicks")
                print("user data restored:", {key: after.get(key) for key in kept},
                      "" if all(after.get(key) == data.get(key) for key in kept) else f"(was {({key: data.get(key) for key in kept})})")
            api.sign_out()
            browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
