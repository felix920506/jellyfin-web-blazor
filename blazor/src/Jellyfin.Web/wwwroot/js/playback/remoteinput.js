// What a remote control does to the page itself: moving the focus in a direction, pressing the
// focused element and typing into it. Port of the parts of components/focusManager.js (nav,
// sendText) and scripts/inputManager.js (select) that the "GeneralCommand" messages use.
// Used through Services/Playback/RemoteInput.cs only.

const focusableQuery = [
    'input:not([type="range"]):not([type="file"]):not([tabindex="-1"]):not(:disabled)',
    'textarea:not([tabindex="-1"]):not(:disabled)',
    'select:not([tabindex="-1"]):not(:disabled)',
    'button:not([tabindex="-1"]):not(:disabled)',
    'a:not([tabindex="-1"]):not(:disabled)',
    '.focusable'
].join(',');

// The original scopes the search with "focuscontainer" classes; the port's pages do not carry
// them everywhere, so the scope is the topmost open dialog, or the whole page.
function scope() {
    const dialogs = document.querySelectorAll('.dialogContainer .dialog.opened, .mud-dialog, .mud-popover-open');
    return dialogs.length ? dialogs[dialogs.length - 1] : document.body;
}

function isVisible(element) {
    const rect = element.getBoundingClientRect();
    if (!rect.width && !rect.height) {
        return false;
    }
    const style = getComputedStyle(element);
    return style.visibility !== 'hidden' && style.display !== 'none';
}

function intersects(a1, a2, b1, b2) {
    return (b1 >= a1 && b1 <= a2) || (b2 >= a1 && b2 <= a2) || (a1 >= b1 && a1 <= b2);
}

function focusFirst(container) {
    for (const element of container.querySelectorAll(focusableQuery)) {
        if (isVisible(element)) {
            element.focus({ preventScroll: false });
            return;
        }
    }
}

// direction: 0 left, 1 right, 2 up, 3 down (as in the original).
function nav(direction) {
    const container = scope();
    let active = document.activeElement;
    if (!active || active === document.body || !container.contains(active)) {
        focusFirst(container);
        return;
    }

    const rect = active.getBoundingClientRect();
    const point1x = rect.left;
    const point1y = rect.top;
    const point2x = point1x + rect.width - 1;
    const point2y = point1y + rect.height - 1;
    const sourceMidX = rect.left + (rect.width / 2);
    const sourceMidY = rect.top + (rect.height / 2);

    let minDistance = Infinity;
    let nearest = null;
    for (const current of container.querySelectorAll(focusableQuery)) {
        if (current === active || !isVisible(current)) {
            continue;
        }

        const elementRect = current.getBoundingClientRect();
        if (direction === 0 && (elementRect.left >= rect.left || elementRect.right === rect.right)) {
            continue;
        }
        if (direction === 1 && (elementRect.right <= rect.right || elementRect.left === rect.left)) {
            continue;
        }
        if (direction === 2 && (elementRect.top >= rect.top || elementRect.bottom >= rect.bottom)) {
            continue;
        }
        if (direction === 3 && (elementRect.bottom <= rect.bottom || elementRect.top <= rect.top)) {
            continue;
        }

        const x = elementRect.left;
        const y = elementRect.top;
        const x2 = x + elementRect.width - 1;
        const y2 = y + elementRect.height - 1;
        const intersectX = intersects(point1x, point2x, x, x2);
        const intersectY = intersects(point1y, point2y, y, y2);
        const midX = elementRect.left + (elementRect.width / 2);
        const midY = elementRect.top + (elementRect.height / 2);

        let distX;
        let distY;
        switch (direction) {
            case 0:
                distX = Math.abs(point1x - Math.min(point1x, x2));
                distY = intersectY ? 0 : Math.abs(sourceMidY - midY);
                break;
            case 1:
                distX = Math.abs(point2x - Math.max(point2x, x));
                distY = intersectY ? 0 : Math.abs(sourceMidY - midY);
                break;
            case 2:
                distY = Math.abs(point1y - Math.min(point1y, y2));
                distX = intersectX ? 0 : Math.abs(sourceMidX - midX);
                break;
            default:
                distY = Math.abs(point2y - Math.max(point2y, y));
                distX = intersectX ? 0 : Math.abs(sourceMidX - midX);
                break;
        }

        const distance = Math.sqrt((distX * distX) + (distY * distY));
        if (distance < minDistance) {
            nearest = current;
            minDistance = distance;
        }
    }

    if (nearest) {
        nearest.focus({ preventScroll: false });
        nearest.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    }
}

export function moveFocus(direction) {
    nav({ left: 0, right: 1, up: 2, down: 3 }[direction]);
}

export function select() {
    const element = document.activeElement;
    if (element && element !== document.body && typeof element.click === 'function') {
        element.click();
    }
}

// The original only assigns the value; the events let the page's bindings see the text.
export function sendText(text) {
    const element = document.activeElement;
    if (!element || !('value' in element)) {
        return;
    }
    element.value = text;
    element.dispatchEvent(new Event('input', { bubbles: true }));
    element.dispatchEvent(new Event('change', { bubbles: true }));
}
