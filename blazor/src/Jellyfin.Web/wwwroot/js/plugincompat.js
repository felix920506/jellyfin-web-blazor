// Administrator-installed plugins are trusted code, just as in the original web client.
// Each page gets a disposable realm: document selectors, globals, listeners and timers cannot
// accidentally mutate Blazor's render tree. This is lifecycle isolation, NOT a security sandbox.
export async function mount(host, html, options) {
    const frame = document.createElement('iframe');
    frame.title = options.name;
    frame.className = 'jf-plugin-configuration-frame';
    host.replaceChildren(frame);
    const w = frame.contentWindow;
    const doc = w.document;
    const cleanups = [];
    let disposed = false;
    let controller;
    let page;
    const blobs = [];
    const esmResources = new Map();
    const pending = new Set();
    const requests = new AbortController();
    const appBase = new URL('.', document.baseURI);
    const resourceUrl = name => getUrl('web/ConfigurationPage', { name: name.replace(/^__plugin\//, '') });
    const getUrl = (path, query) => {
        const url = new URL(path.replace(/^\//, ''), options.serverAddress.replace(/\/$/, '') + '/');
        for (const [key, value] of Object.entries(query || {})) {
            if (value !== undefined && value !== null) url.searchParams.set(key, Array.isArray(value) ? value.join(',') : String(value));
        }
        return url.href;
    };
    const status = doc.createElement('div');
    status.className = 'jf-plugin-status jf-plugin-message';
    status.setAttribute('role', 'status');
    const loading = doc.createElement('div');
    loading.className = 'jf-plugin-loading';
    loading.textContent = 'Loading…';
    loading.setAttribute('role', 'status');
    loading.hidden = true;
    const Loading = { show: () => { loading.hidden = false; }, hide: () => { loading.hidden = true; } };
    const report = error => {
        if (disposed || error?.name === 'AbortError') return;
        Loading.hide();
        status.classList.add('jf-plugin-error');
        status.setAttribute('role', 'alert');
        status.textContent = `${options.errorText}\n${error?.message || error?.statusText || String(error)}`;
        console.warn('[PluginConfiguration]', error);
    };
    // Track require callbacks (and nested requires) before dispatching lifecycle events.
    const track = promise => {
        pending.add(promise);
        promise.then(() => pending.delete(promise), error => { pending.delete(promise); report(error); });
        return promise;
    };
    const onError = event => { event.preventDefault(); report(event.error || new Error(event.message)); };
    const onRejection = event => { event.preventDefault(); report(event.reason); };
    w.addEventListener('error', onError);
    w.addEventListener('unhandledrejection', onRejection);

    async function request(input, init = {}) {
        const config = typeof input === 'string' ? { url: input, ...init } : input;
        const url = new URL(config.url, options.serverAddress + '/');
        const headers = new Headers(config.headers);
        // Never send the user's token to a third-party asset/service.
        if (url.origin === new URL(options.serverAddress).origin) headers.set('Authorization', options.authorization);
        const method = config.type || config.method || 'GET';
        let body = config.body;
        if (config.data !== undefined) {
            if (/^(GET|HEAD)$/i.test(method)) {
                for (const [key, value] of Object.entries(config.data)) url.searchParams.set(key, Array.isArray(value) ? value.join(',') : String(value));
            } else if (typeof config.data === 'string') body = config.data;
            else if (config.contentType?.includes('json')) body = JSON.stringify(config.data);
            else body = new URLSearchParams(config.data);
        }
        if (config.contentType) headers.set('Content-Type', config.contentType);
        const response = await w.fetch(url.href, { method, headers, body, signal: requests.signal });
        if (!response.ok) throw response;
        if (response.status === 204) return undefined;
        if (config.dataType === 'json') return response.json();
        return response.text();
    }
    const json = (path, query) => request({ url: getUrl(path, query), dataType: 'json' });
    const post = (path, data) => request({ url: getUrl(path), type: 'POST', contentType: 'application/json', data: JSON.stringify(data) });
    const ApiClient = {
        getUrl, ajax: request, fetch: request,
        get: url => request(url),
        getJSON: (url, data) => request({ url, data, dataType: 'json' }),
        getPluginConfiguration: id => json(`Plugins/${id}/Configuration`),
        updatePluginConfiguration: (id, config) => post(`Plugins/${id}/Configuration`, config),
        getNamedConfiguration: name => json(`System/Configuration/${name}`),
        updateNamedConfiguration: (name, config) => post(`System/Configuration/${name}`, config),
        getServerConfiguration: () => json('System/Configuration'),
        updateServerConfiguration: config => post('System/Configuration', config),
        getUsers: query => json('Users', query),
        getUser: id => json(`Users/${id || options.userId}`),
        getCurrentUser: () => json('Users/Me'),
        getCurrentUserId: () => options.userId,
        accessToken: () => options.accessToken,
        serverAddress: () => options.serverAddress,
        getVirtualFolders: () => json('Library/VirtualFolders'),
        getItems: (userId, query) => json('Items', typeof userId === 'object' ? userId : { UserId: userId, ...query }),
        getItem: (userId, id) => json(`Items/${id}`, { UserId: userId }),
        getPlugins: () => json('Plugins'),
        getScheduledTasks: query => json('ScheduledTasks', query),
        startScheduledTask: id => request({ url: getUrl(`ScheduledTasks/Running/${id}`), type: 'POST' }),
        getSystemInfo: () => json('System/Info'),
        getPublicSystemInfo: () => json('System/Info/Public'),
        getCultures: () => json('Localization/Cultures'),
        getCountries: () => json('Localization/Countries'),
        getParentalRatings: () => json('Localization/ParentalRatings'),
        getConfigurationPages: query => json('web/ConfigurationPages', query),
        getDeviceId: () => 'blazor-plugin-configuration',
    };
    // Put returned promises in the plugin realm as well. Otherwise an uncaught API callback
    // rejection could surface on the parent window instead of the plugin's error handler.
    for (const [name, method] of Object.entries(ApiClient)) {
        ApiClient[name] = (...args) => {
            const value = method(...args);
            return value?.then ? new w.Promise((resolve, reject) => value.then(resolve, reject)) : value;
        };
    }
    const navigate = url => {
        const route = url.replace(/^#!/, '').replace(/^#/, '').replace(/^\//, '');
        window.location.hash = '#/' + route;
        return Promise.resolve();
    };
    function message(text) {
        status.classList.remove('jf-plugin-error');
        status.setAttribute('role', 'status');
        status.textContent = typeof text === 'string' ? text : text?.text || text?.message || '';
    }
    function dialog(message, title, confirm) {
        return new Promise(resolve => {
            const element = doc.createElement('dialog');
            const heading = doc.createElement('h2');
            heading.textContent = title || '';
            const text = doc.createElement('p');
            text.textContent = message || '';
            element.append(heading, text);
            for (const [label, value] of confirm ? [['Cancel', false], ['OK', true]] : [['OK', true]]) {
                const button = doc.createElement('button');
                button.className = 'emby-button raised';
                button.textContent = label;
                button.onclick = () => { element.close(); element.remove(); resolve(value); };
                element.append(button);
            }
            element.addEventListener('cancel', () => { element.remove(); resolve(false); });
            doc.body.append(element);
            element.showModal();
        });
    }
    const saved = () => { Loading.hide(); message(options.savedText); };
    const Dashboard = {
        showLoadingMsg: Loading.show, hideLoadingMsg: Loading.hide,
        processPluginConfigurationUpdateResult: saved, processServerConfigurationUpdateResult: saved,
        alert: value => typeof value === 'string' ? Promise.resolve(message(value)) :
            dialog(value.message || value.text, value.title, false).then(() => value.callback?.()),
        confirm: (text, title, callback) => dialog(text, title, true).then(result => { callback?.(result); return result; }),
        navigate, getPluginUrl: name => 'configurationpage?name=' + encodeURIComponent(name),
        getConfigurationResourceUrl: resourceUrl,
        processErrorResponse: report, getCurrentUserId: ApiClient.getCurrentUserId,
        getCurrentUser: ApiClient.getCurrentUser, serverAddress: ApiClient.serverAddress,
    };
    const globalize = {
        translate: (key, ...args) => args.reduce((text, arg, i) => text.replaceAll(`{${i}}`, arg), translations[key.split('#').pop()] || key),
        translateHtml: html => html.replace(/\$\{([^}]+)\}/g, (_, key) => globalize.translate(key)),
        getCurrentLocale: () => options.culture,
    };
    let translations = {};
    const modules = new Map();
    let defining;
    function define(name, dependencies, factory) {
        if (typeof name !== 'string') { factory = dependencies; dependencies = name; name = defining; }
        if (!Array.isArray(dependencies)) { factory = dependencies; dependencies = []; }
        if (!name) throw new Error('Anonymous define outside a plugin module');
        const exports = {};
        const module = { exports };
        modules.set(name, Promise.all(dependencies.map(dep => dep === 'exports' ? exports : dep === 'module' ? module : load(dep)))
            .then(args => typeof factory === 'function' ? factory(...args) ?? module.exports : factory));
    }
    define.amd = {};
    function require(dependencies, callback, failure) {
        if (typeof dependencies === 'string') {
            if (builtins.has(dependencies)) return builtins.get(dependencies);
            throw new Error(`Use asynchronous require for ${dependencies}`);
        }
        return track(Promise.all(dependencies.map(load)).then(values => disposed ? undefined : callback?.(...values)).catch(error => {
            if (failure) failure(error); else throw error;
        }));
    }
    const builtins = new Map([
        ['require', require], ['loading', Loading], ['dashboard', Dashboard], ['globalize', globalize],
        ['appRouter', { show: navigate, navigate, back: () => window.history.back() }],
        ['connectionManager', { currentApiClient: () => ApiClient }],
        ['viewManager', { currentView: () => page }],
        ['toast', message], ['alert', value => dialog(value?.text || value?.message || value, value?.title, false)],
        ['confirm', (text, title) => dialog(text?.text || text, text?.title || title, true).then(ok => { if (!ok) throw new Error('cancelled'); })],
        ['dom', { addEventListener: (el, ...args) => el.addEventListener(...args), removeEventListener: (el, ...args) => el.removeEventListener(...args) }],
        ['events', {
            on: (el, name, fn) => el.addEventListener(name, fn),
            off: (el, name, fn) => el.removeEventListener(name, fn),
            trigger: (el, name, args) => el.dispatchEvent(new w.CustomEvent(name, { detail: args })),
        }],
    ]);
    async function evaluate(source, name, moduleScript = false) {
        if (disposed) return;
        if (moduleScript || /^\s*(?:export\s|import\s)/m.test(source)) {
            // Authenticate plugin imports too; Blob module URLs otherwise have no server token.
            const imports = [...source.matchAll(/(from\s*|import\s*)(['"])(__plugin\/[^'"]+)\2/g)];
            for (const match of imports) {
                const name = match[3];
                if (!esmResources.has(name)) {
                    const code = await request(resourceUrl(name));
                    const url = URL.createObjectURL(new Blob([code], { type: 'text/javascript' }));
                    blobs.push(url);
                    esmResources.set(name, url);
                }
                source = source.replace(match[0], match[1] + match[2] + esmResources.get(name) + match[2]);
            }
            const blob = URL.createObjectURL(new Blob([source], { type: 'text/javascript' }));
            blobs.push(blob);
            return w.Function('url', 'return import(url)')(blob);
        }
        const previous = modules.get(name);
        defining = name;
        try { w.eval(source + '\n//# sourceURL=' + name.replace(/[\r\n]/g, '')); }
        finally { defining = undefined; }
        return modules.get(name) !== previous ? modules.get(name) : undefined;
    }
    async function load(name) {
        if (builtins.has(name)) return builtins.get(name);
        if (/^emby-/.test(name)) return undefined; // Controls use native semantics plus port CSS.
        if (name.startsWith('css!__plugin/')) {
            const css = await request(resourceUrl(name.slice(4)));
            const style = doc.createElement('style');
            style.textContent = css;
            doc.head.append(style);
            return undefined;
        }
        // PORT-TODO(plugin-config): add further legacy UI modules as real plugins require them.
        if (!name.startsWith('__plugin/') && !/^(https?:|\.\.?\/)/.test(name)) {
            throw new Error(`Unsupported plugin module: ${name}`);
        }
        if (!modules.has(name)) {
            const promise = (async () => {
                const url = name.startsWith('__plugin/') ? resourceUrl(name) : new URL(name, resourceUrl(options.name)).href;
                const source = await request(url);
                const result = await evaluate(source, name);
                // define replaces the entry; otherwise this is an ESM/default or side-effect module.
                return modules.get(name) !== promise ? modules.get(name) : result;
            })();
            modules.set(name, promise);
        }
        return modules.get(name);
    }
    async function script(url) {
        return new Promise((resolve, reject) => {
            const element = doc.createElement('script');
            element.src = url;
            element.onload = resolve;
            element.onerror = () => reject(new Error(`Cannot load ${url}`));
            doc.head.append(element);
        });
    }
    function enhance(root) {
        for (const input of root.querySelectorAll('[is]')) {
            const type = input.getAttribute('is');
            if (type === 'emby-checkbox') continue;
            input.classList.add(type);
            if (['emby-input', 'emby-select', 'emby-textarea'].includes(type) && input.hasAttribute('label') && !input.dataset.jfLabel) {
                input.dataset.jfLabel = 'true';
                if (!input.id) input.id = 'jf-input-' + Math.random().toString(36).slice(2);
                const label = doc.createElement('label');
                label.htmlFor = input.id;
                label.className = type === 'emby-select' ? 'selectLabel' : 'inputLabel';
                label.textContent = input.getAttribute('label');
                input.before(label);
            }
        }
    }
    function dispatch(name) {
        page?.dispatchEvent(new w.CustomEvent(name, {
            bubbles: true, detail: { params: { name: options.name }, properties: [], isRestored: false, options: {} },
        }));
    }
    function dispose() {
        if (disposed) return;
        for (const event of ['viewbeforehide', 'pagebeforehide', 'viewhide', 'pagehide', 'viewdestroy', 'pagedestroy']) dispatch(event);
        try { controller?.destroy?.(); } catch (error) { report(error); }
        disposed = true;
        requests.abort();
        cleanups.forEach(cleanup => cleanup());
        w.removeEventListener('error', onError);
        w.removeEventListener('unhandledrejection', onRejection);
        blobs.forEach(url => URL.revokeObjectURL(url));
        frame.remove(); // All realm-owned globals, listeners, timers and DOM go away with it.
    }
    try {
        doc.documentElement.lang = options.culture;
        doc.documentElement.className = 'jf-plugin-document';
        doc.documentElement.dir = document.documentElement.dir;
        doc.body.className = document.body.className + ' jf-plugin-document';
        const base = doc.createElement('base');
        base.href = resourceUrl(options.name);
        doc.head.append(base);
        const styles = [...document.querySelectorAll('link[rel="stylesheet"]')].map(original => new Promise(resolve => {
            const link = doc.createElement('link');
            link.rel = 'stylesheet'; link.href = original.href;
            link.onload = resolve; link.onerror = () => { report(new Error(`Cannot load stylesheet ${link.href}`)); resolve(); };
            doc.head.append(link);
        }));
        // Use the active theme's inherited colours/font, but no shell positioning.
        doc.body.style.color = getComputedStyle(document.body).color;
        doc.body.style.fontFamily = getComputedStyle(document.body).fontFamily;
        Object.assign(w, { ApiClient, Dashboard, Loading, require, requirejs: require, define, globalize });
        const dictionaries = await Promise.all(['en-us', options.dictionaryCulture].map(async culture => {
            const response = await fetch(new URL(`generated/strings/${culture}.json`, appBase), { signal: requests.signal });
            if (!response.ok) throw new Error(`Cannot load translations: ${response.status}`);
            return response.json();
        }));
        translations = Object.assign({}, ...dictionaries);
        const parsed = new DOMParser().parseFromString(globalize.translateHtml(html)
            .replaceAll('\x3c!--<script', '<script').replaceAll('</script>--\x3e', '</script>'), 'text/html');
        const scripts = [...parsed.querySelectorAll('script')];
        scripts.forEach(element => element.remove());
        for (const element of parsed.head.querySelectorAll('style, link[rel="stylesheet"]')) {
            const imported = doc.importNode(element, true);
            if (imported.tagName === 'LINK') imported.href = new URL(imported.getAttribute('href'), resourceUrl(options.name)).href;
            doc.head.append(imported);
        }
        doc.body.append(status, loading);
        for (const node of [...parsed.body.childNodes]) doc.body.append(doc.importNode(node, true));
        page = doc.querySelector('[data-role="page"], .page') || doc.body;
        page.classList.add('page');
        enhance(doc);
        const observer = new w.MutationObserver(() => enhance(doc));
        observer.observe(doc.body, { childList: true, subtree: true });
        cleanups.push(() => observer.disconnect());
        const resize = new w.ResizeObserver(() => {
            if (!disposed) frame.style.height = Math.ceil(doc.body.getBoundingClientRect().height + 24) + 'px';
        });
        resize.observe(doc.body);
        cleanups.push(() => resize.disconnect());
        doc.addEventListener('click', event => {
            const link = event.target.closest('a[href]');
            if (!link || link.target === '_blank') return;
            const href = link.getAttribute('href');
            if (/^(?:#!?\/|\/?configurationpage\?)/i.test(href)) { event.preventDefault(); navigate(href); }
        });
        await Promise.all(styles);
        // jQuery is isolated too; legacy plugins frequently use delegated page events.
        await script(new URL('generated/lib/jquery/dist/jquery.min.js', appBase).href);
        builtins.set('jquery', w.jQuery);
        w.jQuery.fn.checked = function(value) { return value === undefined ? this.prop('checked') : this.prop('checked', value); };
        w.jQuery.fn.selectmenu = function() { return this; };
        for (const element of scripts) {
            try {
                if (!element.type || /^(?:text\/javascript|application\/javascript|module)$/.test(element.type)) {
                    const source = element.src ? await request(new URL(element.getAttribute('src'), resourceUrl(options.name)).href) : element.textContent;
                    await evaluate(source, element.src || options.name + '-inline.js', element.type === 'module');
                }
            } catch (error) { report(error); }
        }
        const required = page.getAttribute('data-require');
        if (required) await Promise.all(required.split(',').map(name => load(name.trim())));
        const controllerName = page.getAttribute('data-controller');
        if (controllerName) {
            const module = await load('__plugin/' + controllerName.replace(/^__plugin\//, ''));
            const Factory = module?.default || module;
            if (typeof Factory !== 'function') throw new Error(`No controller factory in ${controllerName}`);
            controller = new Factory(page, { name: options.name });
        }
        while (pending.size && !disposed) await Promise.allSettled([...pending]);
        if (!disposed) {
            for (const event of ['viewinit', 'pageinit', 'viewbeforeshow', 'pagebeforeshow', 'viewshow', 'pageshow']) dispatch(event);
            frame.dataset.ready = 'true';
        }
    } catch (error) { doc.body.append(status); report(error); }
    return { dispose };
}
