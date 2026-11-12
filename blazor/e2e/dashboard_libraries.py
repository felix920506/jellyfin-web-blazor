"""Exercise dashboard libraries/playback/networking against a private server."""
import json
import sys

from playwright.sync_api import sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in

PAGES = (
    ("libraries", "/dashboard/libraries", ".jf-dashboard-libraries"),
    ("library-display", "/dashboard/libraries/display", "#libraryDisplayPage"),
    ("library-metadata", "/dashboard/libraries/metadata", "#metadataImagesConfigurationPage"),
    ("library-nfo", "/dashboard/libraries/nfo", "#metadataNfoPage"),
    ("playback-transcoding", "/dashboard/playback/transcoding", "#encodingSettingsPage"),
    ("playback-resume", "/dashboard/playback/resume", "#playbackConfigurationPage"),
    ("playback-streaming", "/dashboard/playback/streaming", "#streamingSettingsPage"),
    ("playback-trickplay", "/dashboard/playback/trickplay", "#trickplayConfigurationPage"),
    ("networking", "/dashboard/networking", "#networkingPage"),
)


def main() -> int:
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        page.on("request", capture_encoding_request)
        sign_in(page)
        user_id, token = credentials(page)

        originals = {}
        for key in ("network", "encoding", "metadata", "xbmcmetadata"):
            originals[key] = api(page, "GET", f"/System/Configuration/{key}", token).json()
        originals["configuration"] = api(page, "GET", "/System/Configuration", token).json()

        for name, route, selector in PAGES:
            go(page, route)
            page.wait_for_selector(selector, timeout=30_000)
            page.wait_for_timeout(300)
            mutate_and_restore(page, name, token, originals)
            shot(page, f"dashboard-libraries-{name}-desktop")
            page.set_viewport_size({"width": 390, "height": 844})
            page.wait_for_timeout(300)
            shot(page, f"dashboard-libraries-{name}-phone")
            width, window = page.evaluate("[document.documentElement.scrollWidth, window.innerWidth]")
            assert width <= window + 1, f"{name}: {width}px wide in {window}px window"
            page.set_viewport_size({"width": 1366, "height": 768})

        library_lifecycle(page, token)
        code = finish(page, "dashboard-libraries")
        browser.close()
    return code


def credentials(page):
    stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
    server = stored["Servers"][0]
    return server["UserId"], server["AccessToken"]


def api(page, method, path, token, body=None):
    headers = {"Authorization": f'MediaBrowser Client="dashboard-e2e", Device="dashboard-e2e", DeviceId="dashboard-e2e", Version="1", Token="{token}"'}
    if body is not None:
        headers["Content-Type"] = "application/json"
        return page.request.fetch(SERVER + path, method=method, headers=headers, data=json.dumps(body))
    return page.request.fetch(SERVER + path, method=method, headers=headers)


def save(page):
    with page.expect_response(
        lambda response: response.request.method == "POST" and "/System/Configuration" in response.url,
        timeout=30_000,
    ) as saved:
        page.locator(".jf-dashboard-submit").click()
    if page.locator(".jf-dashboard-dialog-form").count():
        page.locator(".jf-dashboard-dialog-form [data-id=ok]").click()
    if saved.value.ok:
        page.wait_for_selector(".jf-dashboard-saved", timeout=15_000)
    # These alerts are opened asynchronously by some pages after the POST starts, so checking
    # immediately after expect_response can miss them and leave them over the next page.
    page.wait_for_timeout(500)
    dismiss_message_dialogs(page)
    return saved.value


def dismiss_message_dialogs(page):
    """Dismiss any message dialogs left by a settings page, without touching form dialogs."""
    for _ in range(10):
        buttons = page.locator(".dialogContainer .btnOption")
        if buttons.count():
            buttons.last.click(force=True)
            page.wait_for_timeout(100)
            continue
        page.wait_for_timeout(500)


def capture_encoding_request(request):
    if request.method == "POST" and "/System/Configuration/encoding" in request.url:
        body = request.post_data or "<no body>"
        print("ENCODING_PAGE_POST_BODY=" + body, flush=True)


def mutate_and_restore(page, name, token, originals):
    """Change one field, verify the wire value through the API, then restore the original object."""
    if name == "networking":
        # Changing BaseUrl changes the server's routing prefix immediately, which would make the
        # following root-relative API assertion redirect. Toggle a boolean instead so the save can
        # be verified without changing how the test server addresses subsequent requests.
        box = page.locator(".jf-dashboard-networking-ipv6 input[type=checkbox]")
        box.evaluate("e => e.click()")
        save(page)
        assert api(page, "GET", "/System/Configuration/network", token).json()["EnableIPv6"] == box.is_checked()
        api(page, "POST", "/System/Configuration/network", token, originals["network"])
    elif name == "library-display":
        box = page.locator(".jf-dashboard-display-folderview input[type=checkbox]")
        box.evaluate("e => e.click()")
        save(page)
        assert api(page, "GET", "/System/Configuration", token).json()["EnableFolderView"] == box.is_checked()
        api(page, "POST", "/System/Configuration", token, originals["configuration"])
        api(page, "POST", "/System/Configuration/metadata", token, originals["metadata"])
    elif name == "library-metadata":
        field = page.locator("#jf-dashboard-metadata-chapterduration")
        original = field.input_value()
        field.fill("17" if original != "17" else "18")
        save(page)
        assert api(page, "GET", "/System/Configuration", token).json()["DummyChapterDuration"] in (17, 18)
        api(page, "POST", "/System/Configuration", token, originals["configuration"])
    elif name == "library-nfo":
        box = page.locator(".jf-dashboard-nfo-imagepaths input[type=checkbox]")
        box.evaluate("e => e.click()")
        save(page)
        assert api(page, "GET", "/System/Configuration/xbmcmetadata", token).json()["SaveImagePathsInNfo"] == box.is_checked()
        api(page, "POST", "/System/Configuration/xbmcmetadata", token, originals["xbmcmetadata"])
    elif name == "playback-transcoding":
        field = page.locator("#jf-dashboard-transcoding-transcodingtemppath")
        # The server validates this directory while handling the POST.  Use a path that exists
        # in the private-server container; a synthetic path makes the server correctly return 404.
        field.fill("/tmp")
        response = save(page)
        assert response.ok, f"page encoding save failed: {response.status} {response.url}"
        assert api(page, "GET", "/System/Configuration/encoding", token).json()["TranscodingTempPath"] == "/tmp"
        api(page, "POST", "/System/Configuration/encoding", token, originals["encoding"])
    elif name == "playback-streaming":
        field = page.locator("#jf-dashboard-streaming-bitrate")
        original = field.input_value()
        field.fill("1.25" if original != "1.25" else "1.5")
        save(page)
        assert api(page, "GET", "/System/Configuration", token).json()["RemoteClientBitrateLimit"] in (1250000, 1500000)
        api(page, "POST", "/System/Configuration", token, originals["configuration"])
    elif name == "playback-resume":
        field = page.locator("#jf-dashboard-resume-minpct")
        original = field.input_value()
        field.fill("11" if original != "11" else "12")
        save(page)
        assert api(page, "GET", "/System/Configuration", token).json()["MinResumePct"] in (11, 12)
        api(page, "POST", "/System/Configuration", token, originals["configuration"])
    elif name == "playback-trickplay":
        field = page.locator("#jf-dashboard-trickplay-widths")
        original = field.input_value()
        field.fill("320,640" if original != "320,640" else "320,720")
        save(page)
        assert api(page, "GET", "/System/Configuration", token).json()["TrickplayOptions"]["WidthResolutions"] in ([320, 640], [320, 720])
        api(page, "POST", "/System/Configuration", token, originals["configuration"])


def library_lifecycle(page, token):
    go(page, "/dashboard/libraries")
    page.wait_for_selector(".jf-dashboard-libraries", timeout=30_000)
    page.click(".jf-dashboard-libraries-add")
    page.wait_for_selector("#selectCollectionType", timeout=15_000)
    page.select_option("#selectCollectionType", "movies")
    page.fill("#txtValue", "E2E Library")
    page.click(".btnAddFolder")
    page.wait_for_selector("#txtDirectoryPickerPath", timeout=15_000)
    page.fill("#txtDirectoryPickerPath", "/media/movies")
    page.click(".directoryPicker form button[type=submit]")
    page.wait_for_selector(".directoryPicker", state="detached", timeout=15_000)
    page.click(".dlg-librarycreator form button[type=submit]")
    page.wait_for_function("document.querySelector('[data-name=\"E2E Library\"]')")
    assert any(item.get("Name") == "E2E Library" for item in api(page, "GET", "/Library/VirtualFolders", token).json())

    card = page.locator('[data-name="E2E Library"]')
    card.locator(".jf-dashboard-library-menu button").click()
    page.locator(".jf-dashboard-library-rename").click()
    page.fill("#jf-dashboard-dialog-input", "E2E Renamed")
    page.locator(".jf-dashboard-dialog-form button[type=submit]").click()
    page.wait_for_selector(".jf-dashboard-dialog-form", state="detached")
    page.wait_for_selector('[data-name="E2E Renamed"]')
    assert any(item.get("Name") == "E2E Renamed" for item in api(page, "GET", "/Library/VirtualFolders", token).json())

    card = page.locator('[data-name="E2E Renamed"]')
    card.wait_for(state="visible")
    page.wait_for_function("""name => {
        const card = document.querySelector(`[data-name="${name}"]`);
        return card && card.querySelector('.jf-dashboard-library-menu button') &&
            !card.querySelector('.mud-progress-circular');
    }""", arg="E2E Renamed")
    card.locator(".jf-dashboard-library-menu button").click()
    page.locator(".jf-dashboard-library-manage").click()
    page.wait_for_selector(".dlg-libraryeditor")
    option = page.locator(".dlg-libraryeditor .chkEnableRealtimeMonitor input[type=checkbox]")
    if option.count():
        option.evaluate("e => e.click()")
    page.click(".dlg-libraryeditor .btnSubmit")
    page.wait_for_selector(".dlg-libraryeditor", state="detached")

    card = page.locator('[data-name="E2E Renamed"]')
    card.locator(".jf-dashboard-library-menu button").click()
    page.locator(".jf-dashboard-library-scan").click()
    page.wait_for_selector("#selectMetadataRefreshMode")
    page.locator(".formDialogFooter button[type=submit]").click()
    page.wait_for_selector("#selectMetadataRefreshMode", state="detached")
    page.wait_for_timeout(500)

    card.locator(".jf-dashboard-library-menu button").click()
    page.locator(".jf-dashboard-library-remove").click()
    page.click("[data-id=ok]")
    page.wait_for_selector('[data-name="E2E Renamed"]', state="detached", timeout=20_000)
    assert not any(item.get("Name") == "E2E Renamed" for item in api(page, "GET", "/Library/VirtualFolders", token).json())


if __name__ == "__main__":
    sys.exit(main())
