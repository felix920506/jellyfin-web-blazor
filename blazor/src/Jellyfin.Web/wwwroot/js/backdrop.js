// The page backdrop (Services/Details/Backdrop.cs): port of the DOM part of components/backdrop/backdrop.js
// and components/Backdrop.tsx. Two image layers cross-fade behind the page; the layer above them
// carries the theme's background and becomes see-through while a backdrop shows.

let container;
let background;
let slot = 0;
let loading = null;

function ensureElements() {
    if (container?.isConnected && background?.isConnected) return;

    container = document.createElement('div');
    container.className = 'backdropContainer jf-backdrop-images';
    for (let i = 0; i < 2; i++) {
        const image = document.createElement('div');
        image.id = 'backdropImage' + i;
        image.className = 'backdropImage';
        container.appendChild(image);
    }

    background = document.createElement('div');
    background.className = 'backgroundContainer jf-backdrop-background';

    // First in the body, as in the original document: everything of the app paints above them.
    document.body.insertBefore(background, document.body.firstChild);
    document.body.insertBefore(container, background);
}

/** Shows an image as the backdrop once it has loaded, fading it in over the one showing. */
export function show(url) {
    ensureElements();

    const showing = container.querySelector('.displayingBackdropImage');
    if (showing && showing.getAttribute('data-url') === url) {
        loading = null;
        return;
    }

    const token = {};
    loading = token;
    const img = new Image();
    img.onload = () => {
        if (loading !== token) return;
        loading = null;

        const image = container.querySelector('#backdropImage' + slot);
        const other = container.querySelector('#backdropImage' + (1 - slot));
        slot = 1 - slot;

        image.style.backgroundImage = "url('" + url.replace(/'/g, '%27') + "')";
        image.setAttribute('data-url', url);
        image.classList.add('displayingBackdropImage', 'backdropImageFadeIn');
        other.classList.remove('displayingBackdropImage');
        image.addEventListener('animationend', () => image.classList.remove('backdropImageFadeIn'), { once: true });

        background.classList.add('withBackdrop');
    };
    img.src = url;
}

/** Removes the backdrop. */
export function clear() {
    loading = null;
    if (!container) return;

    for (const image of container.querySelectorAll('.backdropImage')) {
        image.classList.remove('displayingBackdropImage', 'backdropImageFadeIn');
        image.style.backgroundImage = 'none';
        image.removeAttribute('data-url');
    }

    background.classList.remove('withBackdrop');
}
