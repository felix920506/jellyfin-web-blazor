// Only DOM operations for the tick-based C# lyrics controller.
const listeners = new WeakMap();

export function attach(container, reference) {
    detach(container);
    const manual = () => reference.invokeMethodAsync('OnManualScroll').catch(() => { /* page disposed */ });
    const key = e => {
        if (e.key === 'ArrowUp' || e.key === 'ArrowDown') manual();
    };
    document.addEventListener('wheel', manual, { passive: true });
    document.addEventListener('touchmove', manual, { passive: true });
    document.addEventListener('keydown', key);
    listeners.set(container, { manual, key });
}

export function scroll(container, index, smooth) {
    const line = container?.querySelector(`#lyricPosition${index}`);
    if (line) {
        line.scrollIntoView({ block: 'center', behavior: smooth ? 'smooth' : 'instant' });
        line.focus({ preventScroll: true });
    }
}

export function detach(container) {
    const state = listeners.get(container);
    if (!state) return;
    document.removeEventListener('wheel', state.manual);
    document.removeEventListener('touchmove', state.manual);
    document.removeEventListener('keydown', state.key);
    listeners.delete(container);
}
