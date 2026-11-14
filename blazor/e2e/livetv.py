"""Exercise the six Live TV tabs, guide controls, and recording dialogs."""
import sys

from playwright.sync_api import TimeoutError as PlaywrightTimeout
from playwright.sync_api import sync_playwright

from lib import finish, go, new_page, shot, sign_in


def wait_page(page, label):
    page.wait_for_selector("#liveTvPage", timeout=30_000)
    page.wait_for_function("label => document.querySelector('.jf-library-view-label')?.textContent.includes(label)", arg=label)
    page.wait_for_timeout(500)


def choose_tab(page, index, label):
    page.click(".jf-library-view-button")
    page.click(f".jf-library-view-list [data-tab-index='{index}']")
    wait_page(page, label)
    shot(page, f"livetv-{index}-{label.lower()}")


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        sign_in(page)
        go(page, "/livetv")
        wait_page(page, "Programs")
        shot(page, "livetv-programs-desktop")

        choose_tab(page, 1, "Guide")
        page.wait_for_selector(".tvguide", timeout=30_000)
        page.wait_for_selector(".guide-date-tab-button")
        page.locator(".guide-date-tab-button").nth(1).click()
        page.wait_for_timeout(700)
        page.click(".btnGuideViewSettings")
        page.wait_for_selector(".formDialog")
        shot(page, "livetv-guide-settings-desktop")
        page.locator(".formDialog .chkColorCodedBackgrounds").check(force=True)
        page.locator(".formDialog .btnCancel").click()
        page.wait_for_timeout(400)
        page.locator(".programContainer").hover()
        page.mouse.wheel(0, 500)
        page.wait_for_timeout(400)

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
            page.wait_for_timeout(1500)
            shot(page, "livetv-after-record")
            stage = "waiting for single timer"
            page.wait_for_selector("#itemDetailPage .btnManageRecording:not(.hide)", timeout=15_000)
            stage = "opening single editor"
            page.locator("#itemDetailPage .btnManageRecording:not(.hide)").click()
            page.wait_for_timeout(500)
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
            page.wait_for_timeout(500)
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
        browser.close()
    return code


if __name__ == "__main__":
    sys.exit(main())
