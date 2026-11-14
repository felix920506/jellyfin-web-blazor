"""Real bundled MusicBrainz settings on PRIVATE pluginconfig:18107 (admin/jellyfin).

Setup: JELLYFIN_INSTANCE=pluginconfig JELLYFIN_PORT=18107 scripts/test-server.sh reset
       JELLYFIN_INSTANCE=pluginconfig JELLYFIN_PORT=18107 scripts/test-server.sh up
Run:   JELLYFIN_URL=http://localhost:18107 e2e/run plugin_config.py
Stop:  JELLYFIN_INSTANCE=pluginconfig JELLYFIN_PORT=18107 scripts/test-server.sh down

Uses the server image's bundled MusicBrainz plugin, not a fixture or a downloaded build.
Additional intercepted resources exercise AMD/ESM controllers and error containment; they
are explicitly separate from the real plugin's persisted form and authenticated API checks.
"""
import json
import sys
from urllib.parse import quote

from playwright.sync_api import expect, sync_playwright

from lib import SERVER, finish, go, new_page, shot, sign_in
from sessionlib import use_default_config


def main():
    if SERVER.rstrip('/') != 'http://localhost:18107':
        raise RuntimeError('Use private pluginconfig:18107, never the shared server')
    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = new_page(browser)
        use_default_config(page)
        sign_in(page)
        stored = json.loads(page.evaluate("localStorage.getItem('jellyfin_credentials')"))
        token = stored['Servers'][0]['AccessToken']
        headers = {'Authorization': f'MediaBrowser Token="{token}"'}

        def get(path):
            response = page.request.get(SERVER + path, headers=headers)
            assert response.ok, f'{path}: {response.status}'
            return response.json()

        pages = get('/web/ConfigurationPages')
        print('Bundled configuration pages:', ', '.join(item['Name'] for item in pages))
        plugin = next(item for item in pages if item['Name'].lower() == 'musicbrainz')
        plugin_id = plugin['PluginId']
        path = f'/Plugins/{plugin_id}/Configuration'
        original = get(path)
        route = '/configurationpage?name=' + quote(plugin['Name'])
        frame = page.frame_locator('.jf-plugin-configuration-frame')

        def open_settings():
            go(page, route)
            page.wait_for_selector('.jf-plugin-configuration-frame[data-ready=true]')
            expect(frame.locator('#server')).to_have_value(original['Server'])
            expect(frame.locator('.jf-plugin-loading')).to_be_hidden()

        try:
            open_settings()
            expect(frame.locator('#rateLimit')).to_have_value(str(original['RateLimit']))
            expect(frame.locator('#replaceArtistName')).to_be_checked(checked=original['ReplaceArtistName'])
            expect(frame.get_by_label('Server', exact=True)).to_be_visible()
            shot(page, 'plugin-config-musicbrainz-before')
            changed = not original['ReplaceArtistName']
            frame.locator('#replaceArtistName').set_checked(changed)
            rate = '1.25' if float(original['RateLimit']) != 1.25 else '1.5'
            frame.get_by_label('Rate Limit', exact=True).fill(rate)
            with page.expect_response(lambda r: '/Configuration' in r.url and r.request.method == 'POST') as saved:
                frame.get_by_role('button', name='Save', exact=True).click()
            assert saved.value.ok, saved.value.status
            expect(frame.locator('.jf-plugin-status')).to_contain_text('Settings saved')
            persisted = get(path)
            assert persisted['ReplaceArtistName'] == changed
            assert float(persisted['RateLimit']) == float(rate)
            page.reload()
            page.wait_for_selector('.jf-plugin-configuration-frame[data-ready=true]')
            expect(frame.locator('#rateLimit')).to_have_value(rate)
            expect(frame.locator('#replaceArtistName')).to_be_checked(checked=changed)
            expect(frame.locator('.jf-plugin-loading')).to_be_hidden()
            shot(page, 'plugin-config-musicbrainz-persisted')
            page.set_viewport_size({'width': 390, 'height': 844})
            expect(frame.get_by_label('Server', exact=True)).to_be_visible()
            assert frame.locator('body').evaluate('(body) => body.scrollWidth <= innerWidth + 1')
            shot(page, 'plugin-config-musicbrainz-phone')
            # Query-only navigation must replace the old realm, not leave duplicate handlers.
            go(page, '/configurationpage?name=TMDb')
            page.wait_for_selector('.jf-plugin-configuration-frame[data-ready=true]')
            expect(frame.locator('h1')).to_have_text('TMDb')
            expect(frame.locator('#maxCastMembers')).not_to_have_value('')
            expect(frame.locator('.jf-plugin-loading')).to_be_hidden(timeout=60_000)
            shot(page, 'plugin-config-tmdb-phone')
            go(page, '/dashboard')
            page.wait_for_selector('#dashboardPage')
            expect(page.locator('.jf-plugin-configuration-frame')).to_have_count(0)
            assert page.evaluate("['ApiClient', 'Dashboard', 'Loading'].every(key => !(key in window))")
            compatibility_cases(page, pages, headers)
        finally:
            response = page.request.post(SERVER + path, headers={**headers, 'Content-Type': 'application/json'}, data=json.dumps(original))
            assert response.ok, f'restore MusicBrainz configuration: {response.status}'
        result = finish(page, 'plugin configuration')
        browser.close()
        return result


def compatibility_cases(page, pages, headers):
    """Exercise lifecycle/controller contracts not used by the bundled inline-script page."""
    listing = [*pages, {'Name': 'compat-e2e', 'DisplayName': 'Compatibility test', 'EnableInMainMenu': True}]
    page.route('**/web/ConfigurationPages*', lambda route: route.fulfill(json=listing))
    controller = """define(['loading', 'emby-input'], function(loading) {
        return function(view) {
            view.addEventListener('pageinit', () => view.dataset.init = 'yes');
            view.addEventListener('viewshow', () => {
                view.querySelector('input').value = view.dataset.init;
                loading.hide();
            });
        };
    });"""
    html = """<div class="page" data-role="page" data-controller="__plugin/compat-controller.js">
      <h1>Controller test</h1><input is="emby-input" label="Lifecycle" />
      <script>require(['loading'], loading => loading.show());</script>
      <button is="emby-button" id="fail" onclick="throw new Error('contained-plugin-test')">Fail safely</button>
    </div>"""

    def resource(route):
        route.fulfill(content_type='application/javascript' if 'controller.js' in route.request.url else 'text/html',
                      body=controller if 'controller.js' in route.request.url else html)

    page.route('**/web/ConfigurationPage?name=compat*', resource)
    # Recreate the dashboard layout so the real drawer consumes the extra main-menu page.
    go(page, '/home')
    page.wait_for_selector('#indexPage')
    go(page, '/dashboard')
    page.wait_for_selector('#dashboardPage')
    # At phone size the drawer must be opened first.
    page.set_viewport_size({'width': 1366, 'height': 900})
    drawer_link = page.locator('.jf-dashboard-nav a[href="#/configurationpage?name=compat-e2e"]')
    expect(drawer_link).to_be_visible()
    drawer_link.click()
    frame = page.frame_locator('.jf-plugin-configuration-frame')
    expect(frame.get_by_label('Lifecycle', exact=True)).to_have_value('yes')
    expect(frame.locator('.jf-plugin-loading')).to_be_hidden()
    frame.get_by_role('button', name='Fail safely').click()
    expect(frame.get_by_role('alert')).to_contain_text('contained-plugin-test')
    assert not page.locator('#blazor-error-ui').is_visible()
    shot(page, 'plugin-config-contained-error')
    go(page, '/dashboard')
    page.wait_for_selector('#dashboardPage')
    expect(page.locator('iframe.jf-plugin-configuration-frame')).to_have_count(0)
    # ESM data-controller, matching the original dynamic-import controller contract.
    controller = """export default function(view) {
        view.addEventListener('pageshow', () => { view.querySelector('input').value = 'esm-ready'; });
    }"""
    go(page, '/configurationpage?name=compat-e2e')
    expect(frame.get_by_label('Lifecycle', exact=True)).to_have_value('esm-ready')
    shot(page, 'plugin-config-esm-controller')
    page.unroute('**/web/ConfigurationPages*')
    page.unroute('**/web/ConfigurationPage?name=compat*')


if __name__ == '__main__':
    sys.exit(main())
