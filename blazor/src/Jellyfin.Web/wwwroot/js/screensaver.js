let target;

const events = ['click', 'keydown', 'mousedown', 'mousemove', 'pointerdown', 'pointermove', 'touchstart', 'wheel'];

function onInput() {
    target?.invokeMethodAsync('OnInput').catch(() => { /* the root component was disposed */ });
}

export function attach(dotnet) {
    detach();
    target = dotnet;
    for (const event of events) window.addEventListener(event, onInput, { capture: true, passive: true });
}

export function detach() {
    for (const event of events) window.removeEventListener(event, onInput, true);
    target = null;
}
