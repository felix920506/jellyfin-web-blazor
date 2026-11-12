// Browser helpers of the item details page (Services/Details/DetailsInterop.cs).

let markdownReady;

// markdown-it and DOMPurify are the UMD builds of the versions the original client bundles; as
// modules they attach themselves to globalThis.
function loadMarkdown() {
    markdownReady ??= Promise.all([
        import('../generated/lib/markdown-it/dist/markdown-it.min.js'),
        import('../generated/lib/dompurify/dist/purify.min.js')
    ]).then(() => ({
        markdown: globalThis.markdownit({ html: true }),
        purify: globalThis.DOMPurify(window)
    }));
    return markdownReady;
}

/**
 * The overview of an item as HTML: markdown (raw HTML allowed) sanitised by DOMPurify, every link
 * opening in a new tab (renderOverview of the original details page).
 */
export async function renderOverview(markdown) {
    const { markdown: renderer, purify } = await loadMarkdown();
    const container = document.createElement('div');
    container.innerHTML = purify.sanitize(renderer.render(markdown || ''));
    for (const link of container.querySelectorAll('a')) {
        link.setAttribute('target', '_blank');
        link.setAttribute('rel', 'noopener noreferrer');
    }
    return container.innerHTML;
}

/** Whether a clamped text is cut off, so that "show more" has something to show. */
export function isClamped(element) {
    if (!element) return false;
    return Math.abs(element.scrollHeight - element.offsetHeight) > 2;
}

/**
 * Scrolls the scroller of a section so that the card before the given item's is at its start
 * (the current episode in "more from season").
 */
export function scrollToCard(section, itemId) {
    const scroller = section?.matches?.('.emby-scroller') ? section : section?.querySelector?.('.emby-scroller');
    const card = scroller?.querySelector('.card[data-id="' + itemId + '"]');
    if (!card) return;
    const target = card.previousElementSibling || card;
    const offset = target.getBoundingClientRect().left - scroller.getBoundingClientRect().left;
    scroller.scrollBy({ left: offset, behavior: 'instant' });
}

/**
 * Starts lazy loading for the images next to the given element: the logo and the header backdrop
 * are children of the page itself, which the page component owns.
 */
export async function loadSiblingImages(element, blurhash, fast) {
    const container = element?.parentElement;
    if (!container) return;
    const items = await import('./items.js');
    items.loadImages(container, blurhash, fast);
}

/** Saves a file of the server under the given name (scripts/fileDownloader.js). */
export function download(url, filename) {
    const link = document.createElement('a');
    link.href = url;
    link.download = filename || '';
    link.style.display = 'none';
    document.body.appendChild(link);
    link.click();
    link.remove();
}
