// DOM helpers of the library pages (Services/Library/LibraryInterop.cs): the gestures that start
// multi-select on a card (components/multiSelect/multiSelect.js), the "load more" sentinel of endless
// lists, and focusing the search field. Loaded as a module on first use; the logic lives in C#.

const watches = new Map(); // registration id -> function that stops the watch

const LONG_PRESS_MS = 550;
const MOVE_TOLERANCE = 5;

function selectableCard(target) {
    if (!(target instanceof Element)) {
        return null;
    }
    const card = target.closest('.card[data-id]');
    if (!card || !card.closest('.itemsContainer') || card.closest('[data-multiselect="false"]')) {
        return null;
    }
    return card;
}

// Like the original: touch browsers other than Safari report a long press as a context-menu event;
// everything else gets a timer on touchstart/mousedown.
function usesContextMenu() {
    const touch = 'ontouchstart' in window || navigator.maxTouchPoints > 0;
    const safari = /^((?!chrome|android|crios|fxios|edg).)*safari/i.test(navigator.userAgent);
    return touch && !safari;
}

export function watchSelectionGestures(id, dotnet) {
    let timer = null;
    let startX = 0;
    let startY = 0;
    let swallowClickUntil = 0;
    const options = { capture: true };
    const passive = { capture: true, passive: true };

    const start = (card) => dotnet.invokeMethodAsync('OnSelectionGesture', card.getAttribute('data-id'));

    const cancel = () => {
        if (timer !== null) {
            clearTimeout(timer);
            timer = null;
        }
    };

    const point = (event) => (event.touches && event.touches.length ? event.touches[0] : event);

    const onDown = (event) => {
        cancel();
        swallowClickUntil = 0;
        if (event.type === 'mousedown' && event.button !== 0) {
            return;
        }
        const card = selectableCard(event.target);
        if (!card || card.querySelector('.itemSelectionPanel')) {
            return;
        }
        const position = point(event);
        startX = position.clientX;
        startY = position.clientY;
        timer = setTimeout(() => {
            timer = null;
            // The click that follows the release must not open the item or untick the card again.
            swallowClickUntil = Date.now() + 1500;
            start(card);
        }, LONG_PRESS_MS);
    };

    const onMove = (event) => {
        if (timer === null) {
            return;
        }
        const position = point(event);
        if (Math.abs(position.clientX - startX) >= MOVE_TOLERANCE || Math.abs(position.clientY - startY) >= MOVE_TOLERANCE) {
            cancel();
        }
    };

    const onContextMenu = (event) => {
        const card = selectableCard(event.target);
        if (card && !card.querySelector('.itemSelectionPanel')) {
            event.preventDefault();
            event.stopPropagation();
            start(card);
        }
    };

    const onClick = (event) => {
        const card = selectableCard(event.target);
        if (!card) {
            return;
        }
        if (Date.now() < swallowClickUntil) {
            swallowClickUntil = 0;
            event.preventDefault();
            event.stopImmediatePropagation();
            return;
        }
        if ((event.ctrlKey || event.metaKey) && !card.querySelector('.itemSelectionPanel')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            start(card);
        }
    };

    const contextMenu = usesContextMenu();
    window.addEventListener('click', onClick, options);
    if (contextMenu) {
        window.addEventListener('contextmenu', onContextMenu, options);
    } else {
        window.addEventListener('touchstart', onDown, passive);
        window.addEventListener('touchmove', onMove, passive);
        window.addEventListener('touchend', cancel, passive);
        window.addEventListener('touchcancel', cancel, passive);
        window.addEventListener('mousedown', onDown, options);
        window.addEventListener('mousemove', onMove, passive);
        window.addEventListener('mouseup', cancel, options);
        window.addEventListener('dragstart', cancel, options);
        window.addEventListener('scroll', cancel, passive);
    }

    watches.set(id, () => {
        cancel();
        window.removeEventListener('click', onClick, options);
        window.removeEventListener('contextmenu', onContextMenu, options);
        window.removeEventListener('touchstart', onDown, passive);
        window.removeEventListener('touchmove', onMove, passive);
        window.removeEventListener('touchend', cancel, passive);
        window.removeEventListener('touchcancel', cancel, passive);
        window.removeEventListener('mousedown', onDown, options);
        window.removeEventListener('mousemove', onMove, passive);
        window.removeEventListener('mouseup', cancel, options);
        window.removeEventListener('dragstart', cancel, options);
        window.removeEventListener('scroll', cancel, passive);
    });
}

// Reports while the element is within rootMargin of the viewport: once when it gets there and then
// twice a second, because loading more items does not necessarily move it out again.
export function watchVisibility(id, dotnet, element, rootMargin) {
    if (!element || typeof IntersectionObserver === 'undefined') {
        return;
    }
    let interval = null;
    const notify = () => dotnet.invokeMethodAsync('OnVisible').catch(() => { });
    const stopInterval = () => {
        if (interval !== null) {
            clearInterval(interval);
            interval = null;
        }
    };
    const observer = new IntersectionObserver((entries) => {
        const visible = entries.some(entry => entry.isIntersecting);
        stopInterval();
        if (visible) {
            notify();
            interval = setInterval(notify, 500);
        }
    }, { rootMargin: rootMargin || '0px' });
    observer.observe(element);
    watches.set(id, () => {
        stopInterval();
        observer.disconnect();
    });
}

export function unwatch(id) {
    const stop = watches.get(id);
    if (stop) {
        watches.delete(id);
        stop();
    }
}

export function focus(element) {
    if (element && typeof element.focus === 'function') {
        element.focus();
    }
}
