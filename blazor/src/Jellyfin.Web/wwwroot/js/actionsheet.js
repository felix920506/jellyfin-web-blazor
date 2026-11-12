// Anchored action sheets (IActionSheetPositioner): the DOM half of positionTo in components/actionSheet/actionSheet.ts.
// The arithmetic is in ActionSheetPosition.Compute.

let lastClicked = null;

// The original hands the clicked button to the menu (shortcuts.js); the item components call their
// actions without it, so the element is remembered here instead.
function remember(event) {
    const target = event.target;
    if (target instanceof Element) {
        lastClicked = target.closest('button, a, [data-action]') || target;
    }
}

document.addEventListener('click', remember, true);
document.addEventListener('contextmenu', remember, true);

/**
 * Measures the anchor (the given element, or the one clicked last), the dialog around `content` and the
 * window. Null when either is missing, the anchor has left the document or sits inside the sheet itself.
 */
export function measure(content, anchor) {
    const dialog = content?.closest?.('.dialog');
    const element = anchor || lastClicked;
    if (!dialog || !element?.isConnected || dialog.contains(element)) {
        return null;
    }

    const box = element.getBoundingClientRect();
    return {
        top: box.top,
        left: box.left,
        width: box.width,
        height: box.height,
        dialogWidth: dialog.offsetWidth,
        dialogHeight: dialog.offsetHeight,
        windowWidth: window.innerWidth,
        windowHeight: window.innerHeight
    };
}

/** Pins the dialog around `content` at the given window coordinates. */
export function place(content, left, top) {
    const dialog = content?.closest?.('.dialog');
    if (dialog) {
        dialog.style.position = 'fixed';
        dialog.style.margin = '0';
        dialog.style.left = left + 'px';
        dialog.style.top = top + 'px';
    }
}
