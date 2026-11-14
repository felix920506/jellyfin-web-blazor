"""Exercise Live TV tabs/dialogs and the video OSD's real channel recording toggle.

Seed a private server with scripts/seed-livetv.sh first. Playback uses its HTTP M3U fixture;
the OSD is driven only through visible controls and timer changes are checked on the server.
Repeatable against the same seed: every timer created here is cancelled before playback stops.
"""
import re
import sys

from playwright.sync_api import TimeoutError as PlaywrightTimeout
from playwright.sync_api import expect, sync_playwright

from lib import PASSWORD, SERVER, USER, finish, go, new_page, shot, sign_in


def server_api(playwright):
    api = playwright.request.new_context(base_url=SERVER, extra_http_headers={
        "Authorization": 'MediaBrowser Client="e2e", Device="livetv.py", DeviceId="livetv-osd", Version="1"',
    })
    response = api.post("/Users/AuthenticateByName", data={"Username": USER, "Pw": PASSWORD})
    assert response.ok, response.text()
    token = response.json()["AccessToken"]
    api.dispose()
    return playwright.request.new_context(base_url=SERVER, extra_http_headers={
        "Authorization": f'MediaBrowser Client="e2e", Device="livetv.py", DeviceId="livetv-osd", Version="1", Token="{token}"',
    })


def get(api, path):
    response = api.get(path)
    assert response.ok, f"{path}: {response.status} {response.text()}"
    return response.json()


def show_osd(page):
    page.mouse.move(400, 300)
    page.mouse.move(420, 320)
    expect(page.locator(".videoOsdBottom:not(.videoOsdBottom-hidden)")).to_be_visible()


def record_playing_channel(page, api):
    channels = get(api, "/LiveTv/Channels?AddCurrentProgram=true")["Items"]
    assert len(channels) == 6, f"Expected six seeded channels, got {channels}"
    channel = next(c for c in channels if c.get("CurrentProgram"))
    program = channel["CurrentProgram"]
    assert not [t for t in get(api, "/LiveTv/Timers")["Items"] if t.get("ProgramId") == program["Id"]]
    go(page, f"/details?id={channel['Id']}&serverId={channel['ServerId']}")
    expect(page.locator("#itemDetailPage .nameContainer")).to_contain_text(channel["Name"])
    page.locator("#itemDetailPage .mainDetailButtons .btnPlay").click()
    page.wait_for_selector("#videoOsdPage", timeout=30_000)
    page.wait_for_function("() => { const v = document.querySelector('video'); return v && v.videoWidth > 0 && v.currentTime > 0; }", timeout=30_000)
    show_osd(page)
    record = page.locator(".btnRecord")
    expect(record).to_be_visible()
    expect(record).to_be_enabled()
    expect(record).to_have_attribute("title", "Record")
    expect(page.locator(".osdHeader .pageTitle")).to_contain_text(program["Name"])
    # Match legacy index.html: first in the left transport group, before rewind and pause.
    expect(page.locator(".buttons > div[dir=ltr] > button:first-child")).to_have_class(re.compile(r"\bbtnRecord\b"))
    box = record.bounding_box()
    rewind = page.locator(".btnRewind").bounding_box()
    assert box["x"] < rewind["x"] and abs(box["y"] - rewind["y"]) < 5
    shot(page, "livetv-osd-record")
    with page.expect_response(lambda r: "/LiveTv/Timers" in r.url and r.request.method == "POST") as created:
        record.click()
    assert created.value.ok, created.value.text()
    expect(record).to_have_class(re.compile(r"\brecordingIcon-active\b"))
    expect(record).to_have_attribute("title", re.compile(r"^(Do not record|Stop recording)$"))
    expect(record.locator(".material-icons")).to_have_css("color", "rgb(204, 51, 51)")
    timers = [t for t in get(api, "/LiveTv/Timers")["Items"] if t.get("ProgramId") == program["Id"]]
    assert len(timers) == 1, timers
    # RecordingHelper displays a toast over the bottom-left controls. Wait for its real lifetime
    # before moving the pointer to reopen the OSD; never force a click through an overlay.
    expect(page.locator(".toastVisible")).to_contain_text("Recording scheduled.")
    expect(page.locator(".toastVisible")).to_have_count(0)
    show_osd(page)
    shot(page, "livetv-osd-recording")
    with page.expect_response(lambda r: f"/LiveTv/Timers/{timers[0]['Id']}" in r.url and r.request.method == "DELETE") as cancelled:
        record.click()
    assert cancelled.value.ok, cancelled.value.text()
    expect(record).not_to_have_class(re.compile(r"\brecordingIcon-active\b"))
    expect(record).to_have_attribute("title", "Record")
    expect(record).to_be_enabled()
    assert not [t for t in get(api, "/LiveTv/Timers")["Items"] if t.get("Id") == timers[0]["Id"]]
    expect(page.locator(".toastVisible")).to_contain_text("Recording cancelled.")
    expect(page.locator(".toastVisible")).to_have_count(0)
    show_osd(page)
    shot(page, "livetv-osd-cancelled")
    with page.expect_response(lambda r: "/Sessions/Playing/Stopped" in r.url and r.request.method == "POST") as stopped:
        page.locator("#videoOsdPage .headerBackButton").click()
    assert stopped.value.ok, stopped.value.text()
    page.wait_for_selector("#videoOsdPage", state="detached")
    page.wait_for_selector("video", state="detached")
    assert not [s for s in get(api, "/Sessions") if s.get("NowPlayingItem", {}).get("Id") == channel["Id"]]
    print("livetv OSD: decoded channel, created timer, cancelled timer, stopped playback")


def wait_page(page, label):
    page.wait_for_selector("#liveTvPage", timeout=30_000)
    page.wait_for_function("label => document.querySelector('.jf-library-view-label')?.textContent.includes(label)", arg=label)


def choose_tab(page, index, label):
    page.click(".jf-library-view-button")
    page.click(f".jf-library-view-list [data-tab-index='{index}']")
    wait_page(page, label)
    shot(page, f"livetv-{index}-{label.lower()}")


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        try:
            sign_in(page)
            api = server_api(p)
            record_playing_channel(page, api)
        except Exception as error:
            page.errors.append(str(error))
            shot(page, "livetv-osd-failure")
            finish(page, "livetv")
            raise
        go(page, "/livetv")
        wait_page(page, "Programs")
        shot(page, "livetv-programs-desktop")

        choose_tab(page, 1, "Guide")
        page.wait_for_selector(".tvguide", timeout=30_000)
        page.wait_for_selector(".guide-date-tab-button")
        page.locator(".guide-date-tab-button").nth(1).click()
        page.click(".btnGuideViewSettings")
        page.wait_for_selector(".formDialog")
        shot(page, "livetv-guide-settings-desktop")
        page.locator(".formDialog .chkColorCodedBackgrounds").check(force=True)
        page.locator(".formDialog .btnCancel").click()
        page.locator(".programContainer").hover()
        page.mouse.wheel(0, 500)

        choose_tab(page, 2, "Channels")
        choose_tab(page, 3, "Recordings")
        choose_tab(page, 4, "Schedule")
        choose_tab(page, 5, "Series")

        # Return to the guide and open a programme's details/recording flow. The port follows the
        # modern client and routes a guide cell to details; the details page owns the same fields.
        choose_tab(page, 1, "Guide")
        # The seeded series programme is placed within the guide's current visible time window.
        page.locator(".guide-date-tab-button").nth(1).click()
        series_cell = page.locator(".programCell").filter(has_text="Jellyfin Test Series")
        series_cell.first.wait_for(state="visible", timeout=30_000)
        series_cell.first.click()
        stage = "opening programme details"
        try:
            page.wait_for_selector("#itemDetailPage", timeout=10_000)
            shot(page, "livetv-program-details-desktop")
            page.wait_for_selector("#itemDetailPage .recordingFields", timeout=10_000)
            shot(page, "livetv-recording-dialog-desktop")
            single = page.locator("#itemDetailPage .singleRecordingButton")
            manage_single = page.locator("#itemDetailPage .btnManageRecording")
            # The private server is reused between runs; normalize any timer left by a prior run.
            if "hide" not in manage_single.get_attribute("class"):
                stage = "cleaning existing single timer"
                single.click()
                page.wait_for_function(
                    "() => document.querySelector('#itemDetailPage .btnManageRecording')?.classList.contains('hide')"
                )
            stage = "creating single timer"
            single.click()
            shot(page, "livetv-after-record")
            stage = "waiting for single timer"
            page.wait_for_selector("#itemDetailPage .btnManageRecording:not(.hide)", timeout=15_000)
            stage = "opening single editor"
            page.locator("#itemDetailPage .btnManageRecording:not(.hide)").click()
            dialog = page.locator(".dialogContainer").last
            dialog.locator(".formDialogHeader .btnCancel").wait_for(state="visible", timeout=10_000)
            dialog.locator(".formDialogHeader .btnCancel").click()
            page.wait_for_selector(".dialogContainer", state="detached", timeout=10_000)
            # The record button is the cancellation action after the editor closes.
            stage = "cancelling single timer"
            single.click()
            page.wait_for_function(
                "() => document.querySelector('#itemDetailPage .btnManageRecording')?.classList.contains('hide')",
                timeout=15_000,
            )
            # The series button uses the recording creator's series-timer path, then its settings
            # link opens the full series editor. Cancel it through that dialog as well.
            stage = "creating series timer"
            series = page.locator("#itemDetailPage .seriesRecordingButton")
            manage_series = page.locator("#itemDetailPage .btnManageSeriesRecording")
            if "hide" not in manage_series.get_attribute("class"):
                stage = "cleaning existing series timer"
                series.click()
                page.wait_for_function(
                    "() => document.querySelector('#itemDetailPage .btnManageSeriesRecording')?.classList.contains('hide')"
                )
            series.click()
            page.wait_for_selector("#itemDetailPage .btnManageSeriesRecording:not(.hide)", timeout=15_000)
            page.locator("#itemDetailPage .btnManageSeriesRecording:not(.hide)").click()
            stage = "closing series editor"
            dialog = page.locator(".dialogContainer").last
            dialog.locator(".formDialogHeader .btnCancel").wait_for(state="visible", timeout=10_000)
            dialog.locator(".formDialogHeader .btnCancel").click()
            page.wait_for_selector(".dialogContainer", state="detached", timeout=10_000)
            stage = "cancelling series timer"
            series.click()
            page.wait_for_function(
                "() => document.querySelector('#itemDetailPage .btnManageSeriesRecording')?.classList.contains('hide')",
                timeout=15_000,
            )
        except PlaywrightTimeout:
            page.errors.append(
                f"recording flow failed at {stage}: "
                f"route={page.url} cells={page.locator('.programCell').count()} "
                f"details={page.locator('#itemDetailPage').count()} "
                f"record={page.locator('#itemDetailPage .recordingButton').count()} "
                f"manage={page.locator('#itemDetailPage .btnManageRecording').count()} "
                f"manageClass={page.locator('#itemDetailPage .btnManageRecording').get_attribute('class')} "
                f"singleText={page.locator('#itemDetailPage .singleRecordingButton').inner_text()} "
                f"seriesClass={page.locator('#itemDetailPage .btnManageSeriesRecording').get_attribute('class')} "
                f"seriesText={page.locator('#itemDetailPage .seriesRecordingButton').inner_text()} "
                f"dialogs={page.locator('.recordingDialog').count()}"
            )

        page.set_viewport_size({"width": 400, "height": 800})
        go(page, "/livetv?tab=1")
        wait_page(page, "Guide")
        page.wait_for_selector(".tvguide", timeout=30_000)
        shot(page, "livetv-guide-phone")
        code = finish(page, "livetv")
        api.dispose()
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
