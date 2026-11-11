// Input helpers of the playback UI that C# cannot do by itself:
//  - the document-level listeners of the video OSD (keys must be cancelled synchronously, and the
//    pointer has to be followed outside of the page's own elements);
//  - the hovered fraction of a slider (needs the element's geometry).
// Used through Services/Playback/PlaybackUiInterop.cs only.

let osd = null;

// The keys the OSD handles (controllers/playback/video/index.js, onKeyDown). They are cancelled here
// because the browser would otherwise act on them before the .NET handler runs.
const osdCodes = new Set([
    'Space', 'Enter', 'NumpadEnter', 'Escape', 'KeyK', 'KeyL', 'KeyJ', 'KeyF', 'KeyM', 'KeyP', 'KeyN',
    'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Comma', 'Period',
    'Home', 'End', 'PageUp', 'PageDown'
]);

function digitOf(code) {
    const match = /^(?:Digit|Numpad)([0-9])$/.exec(code || '');
    return match ? match[1] : null;
}

function isDialogOpen() {
    return !!document.querySelector('.dialogContainer');
}

function isTextInput(target) {
    if (!target || !target.tagName) {
        return false;
    }
    if (target.tagName === 'TEXTAREA' || target.tagName === 'SELECT' || target.isContentEditable) {
        return true;
    }
    return target.tagName === 'INPUT' && target.type !== 'range';
}

function send(name, ...args) {
    if (osd) {
        osd.ref.invokeMethodAsync(name, ...args).catch(() => { /* the page is gone */ });
    }
}

export function attachOsd(dotnetRef) {
    detachOsd();

    const state = { ref: dotnetRef, lastX: null, lastY: null, lastMove: 0 };

    state.onKeyDown = e => {
        if (isDialogOpen() || isTextInput(e.target)) {
            return;
        }

        // Any key counts as activity.
        send('OnActivity');

        if (e.ctrlKey || e.altKey || e.metaKey) {
            return;
        }

        const target = e.target;
        const classes = target && target.classList;
        const onVolumeSlider = !!classes && classes.contains('osdVolumeSlider');
        const onPositionSlider = !!classes && classes.contains('osdPositionSlider');
        const digit = digitOf(e.code);
        if (!digit && !osdCodes.has(e.code)) {
            return;
        }

        // The volume slider keeps its own arrow keys.
        if (onVolumeSlider && (e.code === 'ArrowLeft' || e.code === 'ArrowRight')) {
            return;
        }
        // Shift+P / Shift+N are the only meaning of these two keys.
        if ((e.code === 'KeyP' || e.code === 'KeyN') && !e.shiftKey) {
            return;
        }
        // Enter still activates the focused button; Escape is left to the browser (fullscreen).
        if (e.code !== 'Escape' && e.code !== 'Enter' && e.code !== 'NumpadEnter') {
            e.preventDefault();
        }

        send('OnKeyDown', e.code, e.shiftKey, onPositionSlider);
    };

    state.onPointerMove = e => {
        if (e.pointerType === 'touch') {
            return;
        }

        const x = e.screenX || 0;
        const y = e.screenY || 0;
        if (state.lastX === null) {
            // The first event is only the reference point.
            state.lastX = x;
            state.lastY = y;
            return;
        }
        if (Math.abs(x - state.lastX) < 10 && Math.abs(y - state.lastY) < 10) {
            return;
        }

        state.lastX = x;
        state.lastY = y;

        // No need to tell .NET about every pixel.
        const now = Date.now();
        if (now - state.lastMove < 200) {
            return;
        }
        state.lastMove = now;
        send('OnPointerMoved');
    };

    state.onPointerDown = () => send('OnMouseDownChanged', true);
    state.onPointerUp = () => send('OnMouseDownChanged', false);

    document.addEventListener('keydown', state.onKeyDown, true);
    document.addEventListener('pointermove', state.onPointerMove, { passive: true });
    window.addEventListener('pointerdown', state.onPointerDown, { passive: true });
    window.addEventListener('pointerup', state.onPointerUp, { passive: true });
    window.addEventListener('pointercancel', state.onPointerUp, { passive: true });
    window.addEventListener('dragend', state.onPointerUp, { passive: true });
    osd = state;
}

export function detachOsd() {
    const state = osd;
    if (!state) {
        return;
    }

    osd = null;
    document.removeEventListener('keydown', state.onKeyDown, true);
    document.removeEventListener('pointermove', state.onPointerMove);
    window.removeEventListener('pointerdown', state.onPointerDown);
    window.removeEventListener('pointerup', state.onPointerUp);
    window.removeEventListener('pointercancel', state.onPointerUp);
    window.removeEventListener('dragend', state.onPointerUp);
}

// --- sliders ---------------------------------------------------------------------------------------

const sliders = new WeakMap();

// Reports the hovered position of a slider as a fraction of its width (emby-slider's bubble).
export function attachSlider(input, dotnetRef) {
    if (!input || sliders.has(input)) {
        return;
    }

    let frame = 0;
    let last = null;
    const state = {
        onMove: e => {
            if (e.pointerType === 'touch') {
                return;
            }
            last = e.clientX;
            if (frame) {
                return;
            }
            frame = requestAnimationFrame(() => {
                frame = 0;
                const rect = input.getBoundingClientRect();
                if (!rect.width) {
                    return;
                }
                let fraction = (last - rect.left) / rect.width;
                if (getComputedStyle(input).direction === 'rtl') {
                    fraction = 1 - fraction;
                }
                dotnetRef.invokeMethodAsync('OnHover', Math.min(Math.max(fraction, 0), 1)).catch(() => { /* gone */ });
            });
        },
        onLeave: () => {
            if (frame) {
                cancelAnimationFrame(frame);
                frame = 0;
            }
            dotnetRef.invokeMethodAsync('OnHoverEnd').catch(() => { /* gone */ });
        }
    };

    input.addEventListener('pointermove', state.onMove, { passive: true });
    input.addEventListener('pointerleave', state.onLeave, { passive: true });
    sliders.set(input, state);
}

export function detachSlider(input) {
    const state = input && sliders.get(input);
    if (!state) {
        return;
    }

    sliders.delete(input);
    input.removeEventListener('pointermove', state.onMove);
    input.removeEventListener('pointerleave', state.onLeave);
}
