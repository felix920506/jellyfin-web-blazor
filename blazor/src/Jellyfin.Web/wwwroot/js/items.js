// DOM helpers of the item components (Services/Items/ItemsInterop.cs): lazy images with blurhash
// placeholders, scroller measuring and paging, and the progress bars of live programmes.
// Loaded as a module on first use; keep logic in C# where possible.

const DIGITS = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz#$%*+,-.:;=?@[]^_{|}~';
const loadedSource = new WeakMap(); // element -> the data-src it shows or is loading
const drawnHash = new WeakMap(); // canvas -> the blurhash it shows
const scrollers = new WeakMap(); // scroller element -> { dotnet, update, observer, state }

// --- blurhash ------------------------------------------------------------------------------

function decode83(text) {
    let value = 0;
    for (let i = 0; i < text.length; i++) {
        value = value * 83 + DIGITS.indexOf(text[i]);
    }
    return value;
}

function sRgbToLinear(value) {
    const v = value / 255;
    return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

function linearToSRgb(value) {
    const v = Math.max(0, Math.min(1, value));
    return v <= 0.0031308 ? Math.round(v * 12.92 * 255 + 0.5) : Math.round((1.055 * Math.pow(v, 1 / 2.4) - 0.055) * 255 + 0.5);
}

function signPow(value, exponent) {
    return Math.sign(value) * Math.pow(Math.abs(value), exponent);
}

function decodeBlurhash(hash, width, height) {
    if (!hash || hash.length < 6) return null;
    const sizeFlag = decode83(hash[0]);
    const numY = Math.floor(sizeFlag / 9) + 1;
    const numX = (sizeFlag % 9) + 1;
    if (hash.length !== 4 + 2 * numX * numY) return null;

    const maximumValue = (decode83(hash[1]) + 1) / 166;
    const colors = new Array(numX * numY);
    for (let i = 0; i < colors.length; i++) {
        if (i === 0) {
            const value = decode83(hash.substring(2, 6));
            colors[i] = [sRgbToLinear(value >> 16), sRgbToLinear((value >> 8) & 255), sRgbToLinear(value & 255)];
        } else {
            const value = decode83(hash.substring(4 + i * 2, 6 + i * 2));
            colors[i] = [
                signPow((Math.floor(value / (19 * 19)) - 9) / 9, 2.0) * maximumValue,
                signPow((Math.floor(value / 19) % 19 - 9) / 9, 2.0) * maximumValue,
                signPow((value % 19 - 9) / 9, 2.0) * maximumValue
            ];
        }
    }

    const pixels = new Uint8ClampedArray(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            let r = 0;
            let g = 0;
            let b = 0;
            for (let j = 0; j < numY; j++) {
                for (let i = 0; i < numX; i++) {
                    const basis = Math.cos((Math.PI * x * i) / width) * Math.cos((Math.PI * y * j) / height);
                    const color = colors[i + j * numX];
                    r += color[0] * basis;
                    g += color[1] * basis;
                    b += color[2] * basis;
                }
            }
            const offset = 4 * (x + y * width);
            pixels[offset] = linearToSRgb(r);
            pixels[offset + 1] = linearToSRgb(g);
            pixels[offset + 2] = linearToSRgb(b);
            pixels[offset + 3] = 255;
        }
    }
    return pixels;
}

function drawBlurhashes(container) {
    for (const canvas of container.querySelectorAll('canvas.blurhash-canvas')) {
        const target = canvas.nextElementSibling;
        const hash = target ? target.getAttribute('data-blurhash') : null;
        if (!target || !hash || drawnHash.get(canvas) === hash) continue;
        drawnHash.set(canvas, hash);
        try {
            const pixels = decodeBlurhash(hash, canvas.width, canvas.height);
            const context = pixels ? canvas.getContext('2d') : null;
            if (!context) {
                target.classList.add('non-blurhashable');
                continue;
            }
            const data = context.createImageData(canvas.width, canvas.height);
            data.data.set(pixels);
            context.putImageData(data, 0, 0);
            canvas.classList.remove('lazy-hidden');
            target.classList.add('blurhashed');
        } catch (e) {
            console.warn('[items] blurhash failed', e);
            target.classList.add('non-blurhashable');
        }
    }
}

// --- lazy images ---------------------------------------------------------------------------

let fastFadein = false;

function onAnimationEnd(event) {
    const elem = event.target;
    elem.removeEventListener('animationend', onAnimationEnd);
    requestAnimationFrame(() => {
        const canvas = elem.previousElementSibling;
        if (elem.classList.contains('blurhashed') && canvas && canvas.tagName === 'CANVAS') {
            canvas.classList.add('lazy-hidden');
        }
        // As the original does: hide the placeholder icon behind the loaded image.
        const padder = elem.parentNode ? elem.parentNode.querySelector('.cardPadder') : null;
        if (padder) padder.classList.add('lazy-hidden-children');
    });
}

function fillImage(elem) {
    const url = elem.getAttribute('data-src');
    if (!url || loadedSource.get(elem) === url) return;
    loadedSource.set(elem, url);

    const preloader = new Image();
    elem.classList.remove('lazy-image-fadein', 'lazy-image-fadein-fast');
    elem.classList.add('lazy-hidden');
    elem.addEventListener('animationend', onAnimationEnd);
    const show = (loaded) => {
        requestAnimationFrame(() => {
            if (loadedSource.get(elem) !== url) return;
            if (loaded) {
                if (elem.tagName === 'IMG') elem.setAttribute('src', url);
                else elem.style.backgroundImage = "url('" + url + "')";
                elem.classList.add(fastFadein ? 'lazy-image-fadein-fast' : 'lazy-image-fadein');
            }
            elem.classList.remove('lazy-hidden');
        });
    };
    preloader.addEventListener('load', () => show(true));
    preloader.addEventListener('error', () => show(false));
    preloader.src = url;
}

// PORT-TODO(items): the original unloads images again when they leave the viewport (lazyLoader unveil/unload);
// here an image stays loaded once it was shown.
const imageObserver = typeof IntersectionObserver === 'undefined' ? null : new IntersectionObserver((entries) => {
    for (const entry of entries) {
        if (entry.isIntersecting) {
            imageObserver.unobserve(entry.target);
            fillImage(entry.target);
        }
    }
}, { rootMargin: '25%' });

export function loadImages(container, blurhash, fast) {
    if (!container || !container.querySelectorAll) return;
    fastFadein = !!fast;
    if (blurhash) drawBlurhashes(container);
    for (const elem of container.querySelectorAll('.lazy[data-src]')) {
        if (loadedSource.get(elem) === elem.getAttribute('data-src')) continue;
        if (imageObserver) imageObserver.observe(elem);
        else fillImage(elem);
    }
}

// --- scrollers -----------------------------------------------------------------------------

function isRtl() {
    return document.documentElement.getAttribute('dir') === 'rtl';
}

function scrollPosition(scroller) {
    // In RTL documents scrollLeft runs from 0 down to -(scrollWidth - clientWidth).
    return Math.abs(scroller.scrollLeft);
}

function scrollState(scroller) {
    const size = scroller.clientWidth;
    const width = scroller.scrollWidth;
    const position = scrollPosition(scroller);
    return {
        visible: width > size + 20,
        back: position > 0,
        forward: !(width > 0 && position + size >= width - 1)
    };
}

export function watchScroller(scroller, dotnet) {
    if (!scroller || !scroller.addEventListener) return;
    unwatchScroller(scroller);

    const entry = { dotnet, state: null, frame: 0 };
    entry.update = () => {
        if (entry.frame) return;
        entry.frame = requestAnimationFrame(() => {
            entry.frame = 0;
            if (scrollers.get(scroller) !== entry) return;
            const state = scrollState(scroller);
            const previous = entry.state;
            if (previous && previous.visible === state.visible && previous.back === state.back && previous.forward === state.forward) return;
            entry.state = state;
            dotnet.invokeMethodAsync('OnScrollState', state.visible, state.back, state.forward).catch(() => { /* the component is gone */ });
        });
    };

    scroller.addEventListener('scroll', entry.update, { passive: true });
    if (typeof ResizeObserver !== 'undefined') {
        entry.observer = new ResizeObserver(entry.update);
        entry.observer.observe(scroller);
        if (scroller.firstElementChild) entry.observer.observe(scroller.firstElementChild);
    }
    if (typeof MutationObserver !== 'undefined') {
        entry.mutations = new MutationObserver(entry.update);
        entry.mutations.observe(scroller, { childList: true, subtree: true });
    }
    scrollers.set(scroller, entry);
    entry.update();
}

export function unwatchScroller(scroller) {
    const entry = scroller ? scrollers.get(scroller) : null;
    if (!entry) return;
    scrollers.delete(scroller);
    scroller.removeEventListener('scroll', entry.update);
    if (entry.observer) entry.observer.disconnect();
    if (entry.mutations) entry.mutations.disconnect();
    if (entry.frame) cancelAnimationFrame(entry.frame);
}

// Port of scrollerItemSlideIntoView (elements/emby-scrollbuttons/utils.ts): one page of items.
export function scrollPage(scroller, forward) {
    if (!scroller) return;
    const slider = scroller.querySelector('.scrollSlider') || scroller.firstElementChild;
    const items = slider ? [...slider.querySelectorAll(':scope > *')] : [];
    const frameWidth = scroller.clientWidth;
    const position = scrollPosition(scroller);
    const itemWidth = items.length ? items[0].offsetWidth : 0;

    let target;
    if (itemWidth > 0) {
        const firstVisible = Math.max(Math.floor(position / itemWidth) - 1, 0);
        const lastVisible = Math.floor((position + frameWidth) / itemWidth);
        target = forward
            ? lastVisible * itemWidth
            : firstVisible * itemWidth - (Math.floor(frameWidth / itemWidth) - 1) * itemWidth;
    } else {
        target = forward ? position + frameWidth : position - frameWidth;
    }

    target = Math.max(0, Math.min(target, scroller.scrollWidth - frameWidth));
    scroller.scrollTo({ left: isRtl() ? -target : target, behavior: 'smooth' });
}

// --- live progress -------------------------------------------------------------------------

// Progress bars of programmes on air (data-automode="time") follow the clock, as emby-progressbar does.
function updateTimedProgress() {
    const now = Date.now();
    for (const bar of document.querySelectorAll('[data-automode="time"]')) {
        const start = parseInt(bar.getAttribute('data-starttime'), 10);
        const end = parseInt(bar.getAttribute('data-endtime'), 10);
        if (!(end > start)) continue;
        const percent = Math.max(0, Math.min(100, ((now - start) / (end - start)) * 100));
        const fill = bar.querySelector('.itemProgressBarForeground') || bar;
        fill.style.width = percent + '%';
    }
}

setInterval(updateTimedProgress, 60000);
