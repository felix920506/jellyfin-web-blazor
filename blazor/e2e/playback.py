"""Playback: real playback of decodable media, the OSD, the now-playing bar and the queue page.

    ./dev e2e playback.py [chromium|firefox|webkit]

No page of this workstream starts playback, so the script starts it through the test hook of
wwwroot/js/playback/player.js (testCommand -> PlaybackTestHook.cs); everything after that goes
through the UI or is observed on the media element and in the server's /Sessions.

Real playback is checked with the VP9/Opus movie and the MP3 songs, which every engine decodes.
For the H.264 movie the script follows what the browser can do: where H.264 decodes (the
Playwright Chromium does), the negotiated stream is played and its tracks and quality are
switched; where it does not, the negotiation has to end in the original's playback error.
"""
import json
import sys
import time
import urllib.request

from playwright.sync_api import sync_playwright

from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in

TICKS = 10_000_000
failures = []
reports = []  # (time, path, body) of every playback report the page sends to the server


def record_report(request):
    path = request.url.split("?")[0]
    if "/Sessions/Playing" in path and request.method == "POST":
        try:
            body = json.loads(request.post_data or "{}")
        except ValueError:
            body = {}
        reports.append((time.time(), path[path.index("/Sessions/Playing"):], body))


def reports_to(path):
    return [body for _, sent_path, body in reports if sent_path == path]


def check(ok, label, detail=""):
    print(("ok   " if ok else "FAIL ") + label + (f"  [{detail}]" if detail and not ok else ""))
    if not ok:
        failures.append(label)
    return ok


def wait_until(probe, timeout=20.0, interval=0.25):
    """Polls probe() until it returns something truthy; returns the last value."""
    deadline = time.time() + timeout
    value = probe()
    while not value and time.time() < deadline:
        time.sleep(interval)
        value = probe()
    return value


class Server:
    """The test server's API, with a session of its own."""

    def __init__(self):
        request = urllib.request.Request(
            SERVER + "/Users/AuthenticateByName",
            data=json.dumps({"Username": USER, "Pw": PASSWORD}).encode(),
            headers={
                "Content-Type": "application/json",
                "Authorization": 'MediaBrowser Client="e2e", Device="playback.py", DeviceId="e2e-playback", Version="1"',
            },
        )
        answer = json.load(urllib.request.urlopen(request))
        self.token = answer["AccessToken"]
        self.user_id = answer["User"]["Id"]

    def get(self, path):
        separator = "&" if "?" in path else "?"
        return json.load(urllib.request.urlopen(f"{SERVER}/{path}{separator}ApiKey={self.token}"))

    def items(self, types):
        return self.get(
            f"Items?userId={self.user_id}&Recursive=true&IncludeItemTypes={types}&SortBy=Album,IndexNumber,SortName"
        )["Items"]

    def item(self, item_id):
        return self.get(f"Users/{self.user_id}/Items/{item_id}")

    def now_playing(self, item_id):
        """The session that plays the item, or None."""
        for session in self.get("Sessions"):
            playing = session.get("NowPlayingItem")
            if playing and playing["Id"].replace("-", "") == item_id.replace("-", ""):
                return session
        return None


def command(page, name, args=None):
    """Runs a test-hook command in the app; returns the manager's state."""
    raw = page.evaluate(
        "async ([name, args]) => (await import('/js/playback/player.js')).testCommand(name, args)",
        [name, args or {}],
    )
    state = json.loads(raw) if raw else {}
    if "error" in state:
        raise RuntimeError(state["error"])
    return state


def state(page):
    return command(page, "state")


def element(page, kind):
    """The media element's own state (currentTime in seconds), or None."""
    return page.evaluate("async () => (await import('/js/playback/player.js')).debugState()")[kind]


def current_time(page, kind):
    media = element(page, kind)
    return media["currentTime"] if media else -1


def route(page):
    return page.evaluate("location.hash")


def close_dialogs(page):
    for _ in range(3):
        if not page.locator(".dialogContainer").count():
            return
        page.keyboard.press("Escape")
        page.wait_for_timeout(400)


def show_osd(page):
    page.mouse.move(400, 300)
    page.mouse.move(420, 320)
    page.wait_for_selector(".videoOsdBottom:not(.hide):not(.videoOsdBottom-hidden)", timeout=5_000)


def video_direct_play(page, server, movie):
    print("-- video: direct play of", movie["Name"])
    started = command(page, "play", {"ids": [movie["Id"]]})
    check(started.get("playMethod") == "DirectPlay", "the open-codec movie is direct played", str(started))
    check(wait_until(lambda: route(page).startswith("#/video")), "playback opens the video page", route(page))
    check(wait_until(lambda: current_time(page, "video") > 1.5), "the video's time advances", str(element(page, "video")))
    media = element(page, "video") or {}
    check(media.get("videoWidth", 0) > 0, "the video has decoded frames", str(media))

    show_osd(page)
    page.wait_for_timeout(300)
    shot(page, "playback-video-osd")
    check(page.locator(".osdHeader .pageTitle").inner_text().startswith("Open Codec Movie"), "the OSD shows the title")
    check(page.locator(".osdTextContainer .osdPositionText, .osdPositionText").first.inner_text() != "", "the OSD shows the position")

    session = wait_until(lambda: (lambda s: s if s and s["PlayState"].get("PositionTicks", 0) > 0 else None)(
        server.now_playing(movie["Id"])), timeout=25)
    check(session is not None, "the server's session reports the position")
    if session:
        check(session["PlayState"].get("PlayMethod") == "DirectPlay", "the session reports direct play", str(session["PlayState"]))

    # Pause with the OSD's button, resume with the keyboard.
    show_osd(page)
    page.click(".videoOsdBottom .btnPause")
    check(wait_until(lambda: (element(page, "video") or {}).get("paused")), "the pause button pauses the video")
    check(wait_until(lambda: (server.now_playing(movie["Id"]) or {"PlayState": {}})["PlayState"].get("IsPaused"), timeout=10),
          "the pause is reported to the server")
    page.keyboard.press("Space")
    check(wait_until(lambda: (element(page, "video") or {}).get("paused") is False), "Space resumes playback")

    # Seeking: the hook, the keyboard (skip forward length) and the position slider.
    command(page, "seek", {"ticks": 10 * TICKS})
    check(wait_until(lambda: 9.5 <= current_time(page, "video") < 14), "seeking moves the video", str(current_time(page, "video")))
    before = current_time(page, "video")
    page.keyboard.press("ArrowRight")
    check(wait_until(lambda: current_time(page, "video") >= before + 25), "ArrowRight skips forward",
          f"{before} -> {current_time(page, 'video')}")
    show_osd(page)
    box = page.locator(".osdPositionSlider").bounding_box()
    page.mouse.click(box["x"] + box["width"] * 0.25, box["y"] + box["height"] / 2)
    check(wait_until(lambda: 12 <= current_time(page, "video") <= 20), "the position slider seeks",
          str(current_time(page, "video")))

    # Volume and mute keys.
    page.keyboard.press("ArrowDown")
    page.keyboard.press("m")
    now = wait_until(lambda: (lambda s: s if s["isMuted"] else None)(state(page)), timeout=5) or state(page)
    check(now["isMuted"] and now["volume"] < 100, "the volume keys change volume and mute", f"{now['volume']} {now['isMuted']}")
    page.keyboard.press("m")
    page.keyboard.press("ArrowUp")

    # Settings menu and the playback info overlay.
    show_osd(page)
    page.click(".btnVideoOsdSettings")
    page.wait_for_selector(".actionSheetMenuItem", timeout=5_000)
    page.wait_for_timeout(300)
    shot(page, "playback-video-settings")
    names = page.locator(".actionSheetMenuItem").all_inner_texts()
    check(len(names) >= 4, "the settings menu lists its entries", str(names))
    page.click(".actionSheetMenuItem[data-id=stats]")
    page.wait_for_selector(".playerStats:not(.hide) .playerStats-stat", timeout=10_000)
    page.wait_for_timeout(1200)
    shot(page, "playback-video-stats")
    check("DirectPlay" in page.locator(".playerStats").inner_text().replace(" ", "")
          or "Direct" in page.locator(".playerStats").inner_text(), "the playback info shows the play method",
          page.locator(".playerStats").inner_text())
    page.click(".playerStats-closeButton")

    # The OSD hides itself when the pointer rests.
    check(wait_until(lambda: page.locator(".videoOsdBottom.hide, .videoOsdBottom.videoOsdBottom-hidden").count() > 0, timeout=8),
          "the OSD hides after inactivity")
    shot(page, "playback-video-idle")

    # The end of the item ends playback, leaves the video page and marks the item played.
    duration = (state(page)["durationTicks"] or 0) / TICKS
    command(page, "seek", {"ticks": int((duration - 3) * TICKS)})
    check(wait_until(lambda: not state(page)["isPlaying"], timeout=20), "playback ends at the end of the video")
    check(wait_until(lambda: not route(page).startswith("#/video"), timeout=10), "the video page closes when playback ends", route(page))
    check(wait_until(lambda: server.now_playing(movie["Id"]) is None, timeout=10), "the server's session is idle after the end")
    check(wait_until(lambda: server.item(movie["Id"])["UserData"].get("Played"), timeout=10), "the server marks the movie played")
    check(element(page, "video") is None, "the video element is removed")


def video_back_stops(page, server, movie):
    print("-- video: resume position and back navigation")
    command(page, "play", {"ids": [movie["Id"]], "startPositionTicks": 20 * TICKS})
    check(wait_until(lambda: current_time(page, "video") >= 20.5), "playback starts at the requested position",
          str(current_time(page, "video")))
    show_osd(page)
    page.click(".osdHeader .headerBackButton")
    check(wait_until(lambda: not route(page).startswith("#/video"), timeout=10), "back leaves the video page", route(page))
    check(wait_until(lambda: not state(page)["isPlaying"], timeout=10), "back stops playback")
    # The test media is a minute long, shorter than the server's minimum resume duration: the server
    # stores no resume position for it (it marks the item played instead), so the position is
    # checked in the stop report the client sends.
    stopped = wait_until(lambda: [r for r in reports_to("/Sessions/Playing/Stopped") if r.get("ItemId") == movie["Id"]], timeout=10)
    check(bool(stopped) and stopped[-1].get("PositionTicks", 0) >= 20 * TICKS, "the stop report carries the position", str(stopped)[:600])
    data = wait_until(lambda: (lambda d: d if d.get("PlaybackPositionTicks", 0) > 0 or d.get("Played") else None)(
        server.item(movie["Id"])["UserData"]), timeout=10)
    check(data is not None, "the server records the stop", str(server.item(movie["Id"])["UserData"]))
    page.wait_for_timeout(500)
    shot(page, "playback-after-video")


def video_negotiation(page, server, movie, can_play_h264):
    print("-- video: negotiation for", movie["Name"], "(H.264 decodable:", can_play_h264, ")")
    try:
        started = command(page, "play", {"ids": [movie["Id"]]})
    except RuntimeError as error:
        check(False, "starting the H.264 movie", str(error)[:600])
        return
    print("   play method:", started.get("playMethod"), "url:", (started.get("url") or "")[:160])
    print("   audio stream:", started.get("audioStreamIndex"), "subtitle stream:", started.get("subtitleStreamIndex"),
          "text tracks:", started.get("textTracks"))
    page.wait_for_timeout(4000)
    media = element(page, "video")
    dialog = page.locator(".dialogContainer").inner_text() if page.locator(".dialogContainer").count() else None
    print("   element:", media, "dialog:", dialog)
    shot(page, "playback-video-h264")
    if started.get("isPlaying") or (media and media["currentTime"] > 0):
        check(wait_until(lambda: current_time(page, "video") > 1, timeout=30), "the negotiated stream plays", str(element(page, "video")))
        if started.get("playMethod") == "Transcode":
            check("m3u8" in (started.get("url") or ""), "the transcode is an HLS stream", started.get("url"))

        # External SRT subtitles are delivered as a WebVTT text track.
        command(page, "setSubtitleStreamIndex", {"index": 0})
        with_subtitles = wait_until(lambda: (lambda s: s if s["subtitleStreamIndex"] == 0 else None)(state(page)), timeout=20)
        check(with_subtitles is not None, "the external subtitle stream is selected", str(state(page)))
        page.wait_for_timeout(2500)
        shot(page, "playback-video-subtitles")

        # The second audio track needs a new stream (browsers cannot switch tracks in a file).
        other = [s["Index"] for s in server.item(movie["Id"])["MediaStreams"] if s["Type"] == "Audio"][-1]
        command(page, "setAudioStreamIndex", {"index": other})
        switched = wait_until(lambda: (lambda s: s if s["audioStreamIndex"] == other and s["isPlaying"] else None)(state(page)), timeout=30)
        check(switched is not None, "the audio stream is switched", str(state(page)))
        check(wait_until(lambda: current_time(page, "video") > 0, timeout=30), "playback continues after the switch")

        command(page, "setMaxStreamingBitrate", {"bitrate": 1_000_000})
        limited = wait_until(lambda: (lambda s: s if s["playMethod"] == "Transcode" and s["isPlaying"] else None)(state(page)), timeout=30)
        check(limited is not None, "a lower quality switches to a transcode", str(state(page)))
        page.wait_for_timeout(3000)
        shot(page, "playback-video-transcode")
        command(page, "stop")
    else:
        # Nothing this browser can decode: the original shows its playback error.
        check(not can_play_h264, "a browser with H.264 plays the H.264 movie", str(started))
        check(dialog is not None, "an undecodable stream ends in the playback error dialog", str(started))
        close_dialogs(page)
        if state(page)["isPlaying"]:
            command(page, "stop")

    check(wait_until(lambda: not state(page)["isPlaying"], timeout=15), "playback is stopped")
    close_dialogs(page)
    if route(page).startswith("#/video"):
        go(page, "/home")
    page.wait_for_selector("#indexPage", timeout=15_000)


def audio(page, server, songs):
    print("-- audio:", [s["Name"] for s in songs])
    ids = [s["Id"] for s in songs]
    started = command(page, "play", {"ids": ids})
    check(started.get("mediaType") == "Audio" and len(started.get("queue", [])) == 3, "three songs are queued", str(started))
    check(wait_until(lambda: current_time(page, "audio") > 1.5), "the song's time advances", str(element(page, "audio")))
    check(not route(page).startswith("#/video"), "audio does not open the video page", route(page))
    page.wait_for_selector(".nowPlayingBar:not(.hide):not(.nowPlayingBar-hidden)", timeout=10_000)
    page.wait_for_timeout(600)
    shot(page, "playback-nowplaying-bar")
    check(songs[0]["Name"] in page.locator(".nowPlayingBarText").inner_text(), "the bar shows the song",
          page.locator(".nowPlayingBarText").inner_text())
    check(wait_until(lambda: server.now_playing(ids[0]) is not None, timeout=10), "the server's session shows the song")

    page.click(".nowPlayingBarCenter .playPauseButton")
    check(wait_until(lambda: (element(page, "audio") or {}).get("paused")), "the bar's pause button pauses")
    page.click(".nowPlayingBarCenter .playPauseButton")
    check(wait_until(lambda: (element(page, "audio") or {}).get("paused") is False), "the bar's play button resumes")

    page.click(".nowPlayingBarCenter .nextTrackButton")
    check(wait_until(lambda: state(page)["queueIndex"] == 1 and current_time(page, "audio") > 0.5), "next track plays the second song",
          str(state(page)))

    # The end of a track starts the next one.
    command(page, "seek", {"ticks": 27 * TICKS})
    advanced = wait_until(lambda: (lambda s: s if s["queueIndex"] == 2 and s["isPlaying"] else None)(state(page)), timeout=20)
    check(advanced is not None and advanced["itemName"] == songs[2]["Name"], "the end of a track starts the next one", str(state(page)))
    check(wait_until(lambda: current_time(page, "audio") > 0.5, timeout=10), "the next song's time advances")
    check(wait_until(lambda: server.now_playing(ids[2]) is not None, timeout=10), "the server's session follows to the next song")
    check(wait_until(lambda: server.item(ids[1])["UserData"].get("Played"), timeout=10), "the finished song is marked played")

    # The queue page.
    page.click(".nowPlayingBarText")
    page.wait_for_selector("#nowPlayingPage .playlist .listItem", timeout=10_000)
    page.wait_for_timeout(800)
    shot(page, "playback-queue")
    check(route(page).startswith("#/queue"), "the bar opens the queue page", route(page))
    check(page.locator("#nowPlayingPage .playlist .listItem").count() == 3, "the queue lists three songs")
    check(page.locator("#nowPlayingPage .listItem").nth(2).locator(".playlistIndexIndicatorImage").count() == 1,
          "the playing song is marked in the queue")
    check(songs[2]["Name"] in page.locator(".nowPlayingSongName").inner_text(), "the page shows the playing song")

    page.locator("#nowPlayingPage .listItem").nth(0).locator(".listItemBody").click()
    check(wait_until(lambda: state(page)["queueIndex"] == 0 and current_time(page, "audio") > 0.3), "a queue entry jumps to its song",
          str(state(page)))

    page.locator("#nowPlayingPage .listItem").nth(0).locator(".jf-playlistMoveDown").click()
    moved = wait_until(lambda: (lambda s: s if s["queue"][1] == songs[0]["Name"] else None)(state(page)), timeout=5)
    check(moved is not None and moved["queueIndex"] == 1, "a queue entry is moved down", str(state(page)))
    page.locator("#nowPlayingPage .listItem").nth(2).locator("button[data-customaction=remove]").click()
    check(wait_until(lambda: len(state(page)["queue"]) == 2, timeout=5), "a queue entry is removed", str(state(page)))
    check(page.locator("#nowPlayingPage .playlist .listItem").count() == 2, "the list follows the queue")

    # The page has two repeat buttons; the stylesheet shows one of them per layout.
    page.locator("#nowPlayingPage .btnRepeat:visible").first.click()
    check(wait_until(lambda: state(page)["repeatMode"] == "RepeatAll", timeout=5), "the repeat button switches the repeat mode")
    page.locator(".nowPlayingVolumeSlider").fill("40")
    check(wait_until(lambda: state(page)["volume"] == 40, timeout=5), "the volume slider sets the volume", str(state(page)["volume"]))
    check(abs((element(page, "audio") or {}).get("volume", 0) - 0.4 ** 3) < 0.02 or abs((element(page, "audio") or {}).get("volume", 0) - 0.4) < 0.02,
          "the element's volume follows", str(element(page, "audio")))
    page.wait_for_timeout(500)
    shot(page, "playback-queue-changed")

    page.set_viewport_size({"width": 400, "height": 800})
    page.wait_for_timeout(800)
    shot(page, "playback-queue-mobile")
    page.set_viewport_size({"width": 1366, "height": 768})
    page.wait_for_timeout(500)

    page.click("#nowPlayingPage .btnStop")
    check(wait_until(lambda: not state(page)["isPlaying"], timeout=10), "stop ends playback")
    check(wait_until(lambda: not route(page).startswith("#/queue"), timeout=10), "the queue page closes when playback stops", route(page))
    check(wait_until(lambda: page.locator(".nowPlayingBar:not(.hide)").count() == 0, timeout=5), "the bar is hidden when nothing plays")
    check(wait_until(lambda: all(server.now_playing(i) is None for i in ids), timeout=10), "the server's session is idle")

    # The volume is kept for the next start.
    page.reload()
    page.wait_for_selector("#indexPage", timeout=120_000)
    restarted = command(page, "play", {"ids": ids[:1]})
    check(restarted["volume"] == 40, "the volume is remembered", str(restarted["volume"]))
    command(page, "stop")
    command(page, "play", {"ids": ids[:1]})
    page.wait_for_selector(".nowPlayingBar:not(.hide)", timeout=10_000)
    page.locator(".nowPlayingBarVolumeSlider").fill("100")
    command(page, "stop")


def main() -> int:
    name = sys.argv[1] if len(sys.argv) > 1 else "chromium"
    server = Server()
    # The server names movies after their folders ("Open Codec Movie (2015)").
    movies = {m["Name"].split(" (")[0]: m for m in server.items("Movie")}
    songs = [s for s in server.items("Audio") if s.get("Album") == "First Album"]
    with sync_playwright() as p:
        if name == "chromium":
            browser = p.chromium.launch(args=["--autoplay-policy=no-user-gesture-required"])
        elif name == "firefox":
            browser = p.firefox.launch(firefox_user_prefs={"media.autoplay.default": 0, "media.autoplay.blocking_policy": 0})
        else:
            browser = p.webkit.launch()
        page = new_page(browser)
        # The app's warnings (AppLog.Warn) explain a failed start; show them with the results.
        page.on("console", lambda m: print("   console:", m.text[:1500]) if "[warn]" in m.text else None)
        page.on("request", record_report)
        sign_in(page)
        can_play = page.evaluate("""() => {
            const v = document.createElement('video');
            return {
                h264: v.canPlayType('video/mp4; codecs="avc1.42E01E, mp4a.40.2"'),
                vp9: v.canPlayType('video/webm; codecs="vp9, opus"'),
                hls: v.canPlayType('application/vnd.apple.mpegurl'),
                mp3: document.createElement('audio').canPlayType('audio/mpeg'),
                mse: !!window.MediaSource,
            };
        }""")
        print(name, "capabilities:", can_play)

        sections = []
        if can_play["vp9"]:
            sections += [
                lambda: video_direct_play(page, server, movies["Open Codec Movie"]),
                lambda: video_back_stops(page, server, movies["Open Codec Movie"]),
            ]
        sections.append(lambda: video_negotiation(page, server, movies["Second Feature"], bool(can_play["h264"])))
        if can_play["mp3"]:
            sections.append(lambda: audio(page, server, songs))
        for section in sections:
            try:
                section()
            except Exception as error:  # keep going: one broken section must not hide the others
                check(False, "section ran to its end", repr(error)[:1500])
                shot(page, f"playback-failure-{len(failures)}")
                try:
                    close_dialogs(page)
                    command(page, "stop")
                    go(page, "/home")
                    page.wait_for_timeout(1000)
                except Exception as cleanup_error:
                    print("cleanup failed:", repr(cleanup_error)[:300])

        for failure in failures:
            page.errors.append("check failed: " + failure)
        code = finish(page, f"playback ({name})")
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
