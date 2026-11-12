// Browser helpers of the session, wizard and user settings pages (ISessionInterop).

let markdownReady;

// markdown-it and DOMPurify are the UMD builds of the versions the original client bundles; as
// modules they attach themselves to globalThis.
function loadMarkdown() {
    markdownReady ??= Promise.all([
        import('../generated/lib/markdown-it/dist/markdown-it.min.js'),
        import('../generated/lib/dompurify/dist/purify.min.js')
    ]).then(() => {
        const purify = globalThis.DOMPurify(window);
        purify.setConfig({
            // Same customised URI allow-list as controllers/session/login/index.js.
            ALLOWED_URI_REGEXP: /^(?:(?:https?|mailto|tel|callto|cid|xmpp|matrix|tg|whatsapp|signal|ircs?):|[^a-z]|[a-z+.-]+(?:[^-a-z+.:]|$))/i
        });
        return { markdown: globalThis.markdownit({ html: true }), purify };
    });
    return markdownReady;
}

/**
 * Renders the login disclaimer: markdown (raw HTML allowed) sanitised by DOMPurify, every link
 * opening in a new tab. Port of the branding handling in controllers/session/login/index.js.
 */
export async function renderDisclaimer(markdown) {
    const { markdown: renderer, purify } = await loadMarkdown();
    const container = document.createElement('div');
    container.innerHTML = purify.sanitize(renderer.render(markdown || ''));
    for (const link of container.querySelectorAll('a')) {
        link.rel = 'noopener noreferrer';
        link.target = '_blank';
        link.classList.add('button-link');
    }
    return container.innerHTML;
}

export async function renderMarkdown(markdown) {
    const { markdown: renderer, purify } = await loadMarkdown();
    return purify.sanitize(renderer.render(markdown || ''));
}

/** Clicks an element; a file input is emptied first so that picking the same file again is noticed. */
export function click(element) {
    if (element?.type === 'file') {
        element.value = '';
    }
    element?.click();
}
