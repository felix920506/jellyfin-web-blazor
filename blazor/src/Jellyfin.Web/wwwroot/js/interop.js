// Browser glue for the Blazor port. Keep this file small: logic belongs in C#.
// Everything is exposed on window.jf and called through Jellyfin.Web.Interop.
(function () {
    'use strict';

    const jf = {};

    // --- storage -----------------------------------------------------------------------------
    function store(session) {
        try {
            return session ? window.sessionStorage : window.localStorage;
        } catch (e) {
            return null;
        }
    }

    jf.storageGet = (key, session) => {
        const s = store(session);
        return s ? s.getItem(key) : null;
    };
    jf.storageSet = (key, value, session) => {
        const s = store(session);
        if (!s) return;
        try {
            s.setItem(key, value);
        } catch (e) {
            console.warn('[storage] unable to save', key, e);
        }
    };
    jf.storageRemove = (key, session) => {
        const s = store(session);
        if (s) s.removeItem(key);
    };

    // --- document ----------------------------------------------------------------------------
    function target(name) {
        return name === 'body' ? document.body : document.documentElement;
    }

    jf.toggleClass = (where, className, on) => target(where).classList.toggle(className, !!on);
    jf.setAttribute = (where, name, value) => {
        if (value === null || value === undefined) target(where).removeAttribute(name);
        else target(where).setAttribute(name, value);
    };
    jf.setTitle = (title) => { document.title = title; };
    jf.setThemeColor = (color) => {
        const meta = document.getElementById('themeColor');
        if (meta) meta.setAttribute('content', color);
    };
    jf.setStylesheet = (id, href) => {
        const link = document.getElementById(id);
        if (link && link.getAttribute('href') !== href) link.setAttribute('href', href);
    };
    jf.enableStylesheet = (id, enabled) => {
        const link = document.getElementById(id);
        if (link) link.disabled = !enabled;
    };
    jf.setStyleText = (id, css) => {
        let style = document.getElementById(id);
        if (!style) {
            style = document.createElement('style');
            style.id = id;
            document.head.appendChild(style);
        }
        style.textContent = css || '';
    };

    // --- environment -------------------------------------------------------------------------
    jf.environment = () => ({
        userAgent: navigator.userAgent,
        languages: Array.from(navigator.languages || [navigator.language || 'en-US']),
        href: window.location.href,
        origin: window.location.origin,
        protocol: window.location.protocol,
        touch: ('ontouchstart' in window) || navigator.maxTouchPoints > 0,
        devicePixelRatio: window.devicePixelRatio || 1,
        screenWidth: window.screen ? window.screen.width : window.innerWidth,
        screenHeight: window.screen ? window.screen.height : window.innerHeight,
        innerWidth: window.innerWidth,
        innerHeight: window.innerHeight
    });
    jf.connectionDownlinkMax = () => {
        const connection = navigator.connection || navigator.mozConnection || navigator.webkitConnection;
        return typeof connection?.downlinkMax === 'number' ? connection.downlinkMax : null;
    };

    // --- location (hash routing) ---------------------------------------------------------------
    const currentHash = () => {
        const h = window.location.hash || '';
        return h.charAt(0) === '#' ? h.substring(1) : h;
    };
    jf.getHash = currentHash;
    jf.navigate = (hash, replace) => {
        const url = '#' + hash;
        if (replace) window.location.replace(url);
        else window.location.hash = hash;
    };
    jf.listenHash = (dotNetRef) => {
        window.addEventListener('hashchange', () => dotNetRef.invokeMethod('OnHashChanged', currentHash()));
    };
    jf.utcOffsetMinutes = (isoString) => -new Date(isoString).getTimezoneOffset();

    jf.historyBack = () => window.history.back();
    jf.historyLength = () => window.history.length;
    jf.reload = () => window.location.reload();
    jf.openUrl = (url, targetName) => { window.open(url, targetName || '_blank', 'noopener'); };
    jf.focus = (element) => { if (element && element.focus) element.focus(); };
    jf.scrollTo = (x, y) => window.scrollTo(x, y);
    jf.viewportWidth = () => window.innerWidth;
    jf.listenResize = (dotNetRef) => {
        let timer = 0;
        window.addEventListener('resize', () => {
            clearTimeout(timer);
            timer = setTimeout(() => dotNetRef.invokeMethod('OnResized'), 100);
        }, { passive: true });
    };
    // The app bar is transparent at the top of the page and solid once scrolled.
    const markScrolled = () => document.body.classList.toggle('jf-scrolled', window.scrollY > 0);
    window.addEventListener('scroll', markScrolled, { passive: true });
    markScrolled();

    // Locale-aware formatting: the browser has complete CLDR data, the WASM runtime does not.
    jf.formatDate = (isoString, locale, options) => {
        try {
            return new Date(isoString).toLocaleString(locale || undefined, options || undefined);
        } catch (e) {
            return new Date(isoString).toLocaleString(undefined, options || undefined);
        }
    };
    jf.formatNumber = (value, locale, options) => {
        try {
            return Number(value).toLocaleString(locale || undefined, options || undefined);
        } catch (e) {
            return String(value);
        }
    };

    // --- window events forwarded to .NET -----------------------------------------------------
    // subscribe(dotNetRef, method, eventName, target) -> id ; unsubscribe(id)
    const subscriptions = new Map();
    let nextSubscription = 1;
    jf.subscribe = (dotNetRef, method, eventName, where) => {
        const element = where === 'document' ? document : window;
        const handler = () => { dotNetRef.invokeMethodAsync(method); };
        element.addEventListener(eventName, handler, { passive: true });
        const id = nextSubscription++;
        subscriptions.set(id, () => element.removeEventListener(eventName, handler));
        return id;
    };
    jf.unsubscribe = (id) => {
        const remove = subscriptions.get(id);
        if (remove) {
            remove();
            subscriptions.delete(id);
        }
    };

    window.jf = jf;
})();
