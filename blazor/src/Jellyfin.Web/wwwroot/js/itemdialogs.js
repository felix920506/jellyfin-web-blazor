// Browser helpers of the item dialogs (IItemDialogsInterop).

/** Whether the browser can share links (appHost.supports('sharing') is `navigator.share`). */
export function canShare() {
    return typeof navigator.share === 'function';
}

/** `navigator.share`, as the "share" command of itemContextMenu.js calls it. False when the user cancelled or it failed. */
export async function share(data) {
    // A member that is null would be shared as the text "null".
    const payload = Object.fromEntries(Object.entries(data).filter(([, value]) => value != null));
    try {
        await navigator.share(payload);
        return true;
    } catch (error) {
        console.debug('[itemdialogs] sharing was cancelled or failed', error);
        return false;
    }
}

// Port of scripts/clipboard.js: the Clipboard API, else a hidden textarea and execCommand('copy').
function textAreaCopy(text) {
    const textArea = document.createElement('textarea');
    textArea.value = text;
    textArea.style.top = '0';
    textArea.style.left = '0';
    textArea.style.position = 'fixed';
    document.body.appendChild(textArea);
    textArea.focus();
    textArea.select();
    try {
        return document.execCommand('copy');
    } catch {
        return false;
    } finally {
        document.body.removeChild(textArea);
    }
}

/** Copies text to the clipboard; false when the browser refused. */
export async function copyText(text) {
    if (!navigator.clipboard?.writeText) {
        return textAreaCopy(text);
    }

    try {
        await navigator.clipboard.writeText(text);
        return true;
    } catch {
        return textAreaCopy(text);
    }
}

// multiDownload.js, fallback(): one hidden frame per URL, the next one once the previous started loading.
function downloadInFrames(urls) {
    let i = 0;

    (function createFrame() {
        const frame = document.createElement('iframe');
        frame.style.display = 'none';
        frame.src = urls[i++];
        document.documentElement.appendChild(frame);

        const interval = setInterval(() => {
            let state;
            try {
                state = frame.contentWindow.document.readyState;
            } catch {
                // A frame that already navigated to the server's origin: treat it as started.
                state = 'complete';
            }

            if (state === 'complete' || state === 'interactive') {
                clearInterval(interval);

                // Safari needs a timeout
                setTimeout(() => frame.remove(), 1000);

                if (i < urls.length) {
                    createFrame();
                }
            }
        }, 100);
    })();
}

/**
 * Port of scripts/multiDownload.js: one anchor click per URL, a tenth of a second apart so the browser
 * does not drop any; hidden frames where `a[download]` is missing and on iOS (`useFrames`).
 */
export function download(urls, useFrames) {
    if (!urls?.length) {
        return;
    }

    if (useFrames || typeof document.createElement('a').download === 'undefined') {
        downloadInFrames(urls);
        return;
    }

    urls.forEach((url, index) => {
        setTimeout(() => {
            const a = document.createElement('a');
            a.download = '';
            a.href = url;
            a.click();
        }, 100 * (index + 1));
    });
}

/** Clicks an element: the "Browse" buttons open the file picker of their hidden file input. */
export function click(element) {
    element?.click();
}
