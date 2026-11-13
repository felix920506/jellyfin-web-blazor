let dotnet;
const players = new Map();
const scripts = new Map();

function loadScript(url) {
    if (scripts.has(url)) return scripts.get(url);
    const promise = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Unable to load ${url}`));
        document.head.append(script);
    });
    scripts.set(url, promise);
    return promise;
}

export function init(callback) {
    dotnet = callback;
}

function snapshot(state = {}) {
    return {
        currentTime: state.currentTime ?? 0,
        duration: state.duration ?? 1,
        paused: state.paused ?? false,
        volume: 1,
        muted: false,
        playbackRate: 1,
        buffered: [],
        seekable: true,
        fullscreen: !!document.fullscreenElement,
        pictureInPicture: false
    };
}

function emit(kind, name, state) {
    dotnet?.invokeMethodAsync('OnPlayerEvent', kind, name, snapshot(state));
}

function button(icon, className, title, handler) {
    const node = document.createElement('button');
    node.type = 'button';
    node.className = `autoSize paper-icon-button-light ${className}`;
    node.title = title;
    node.innerHTML = `<span class="material-icons slideshowButtonIcon">${icon}</span>`;
    node.addEventListener('click', handler);
    return node;
}

function itemUrl(item) {
    return item.imageUrl || item.url || item.path;
}

function openPhoto(state, request) {
    const dialog = document.createElement('div');
    dialog.className = 'slideshowDialog';
    dialog.style.height = '100vh';
    dialog.style.minHeight = '100vh';
    dialog.innerHTML = '<div class="slideshowSwiperContainer"><div class="swiper-wrapper"></div></div>';
    const wrapper = dialog.querySelector('.swiper-wrapper');
    request.items.forEach((item, index) => {
        const slide = document.createElement('div');
        slide.className = 'swiper-slide';
        slide.dataset.index = index;
        const zoom = document.createElement('div');
        zoom.className = 'swiper-zoom-container';
        const image = document.createElement('img');
        image.className = 'swiper-slide-img';
        image.src = itemUrl(item);
        image.alt = item.name || '';
        zoom.append(image);
        slide.append(zoom);
        wrapper.append(slide);
    });
    const controls = document.createElement('div');
    controls.className = 'topActionButtons';
    controls.append(
        button('keyboard_arrow_left', 'btnSlideshowPrevious slideshowButton', 'Previous', () => move(state, -1)),
        button('keyboard_arrow_right', 'btnSlideshowNext slideshowButton', 'Next', () => move(state, 1)),
        button('play_arrow', 'btnSlideshowPause slideshowButton', 'Play/Pause', () => togglePhoto(state)),
        ...(request.items[state.index]?.downloadUrl ? [button('file_download', 'btnDownload slideshowButton', 'Download', () => { const link = document.createElement('a'); link.href = request.items[state.index].downloadUrl; link.download = ''; link.click(); })] : []),
        button('fullscreen', 'btnFullscreen slideshowButton', 'Fullscreen', () => toggleFullscreen('photo')),
        button('close', 'slideshowButton btnSlideshowExit', 'Close', () => stop('photo', true)));
    dialog.append(controls);
    document.body.prepend(dialog);
    state.dialog = dialog;
    state.index = Math.max(0, Math.min(request.startIndex, request.items.length - 1));
    state.autoplay = request.slideshowIntervalSeconds > 0;
    state.interval = request.slideshowIntervalSeconds;
    showPhoto(state);
    if (state.autoplay) state.timer = setInterval(() => move(state, 1), state.interval * 1000);
    return snapshot({ duration: request.items.length, currentTime: state.index });
}

function showPhoto(state) {
    state.dialog.querySelectorAll('.swiper-slide').forEach((node, index) => node.classList.toggle('swiper-slide-active', index === state.index));
    const wrapper = state.dialog.querySelector('.swiper-wrapper');
    wrapper.dataset.index = state.index;
    wrapper.style.transform = 'none';
    emit('photo', 'timeupdate', { currentTime: state.index, duration: state.items.length, paused: !state.autoplay });
}

function move(state, amount) {
    state.index = Math.max(0, Math.min(state.items.length - 1, state.index + amount));
    showPhoto(state);
    if (state.index === state.items.length - 1 && amount > 0) {
        clearInterval(state.timer);
        state.autoplay = false;
        emit('photo', 'stopped', { currentTime: state.index, duration: state.items.length, paused: true });
    }
}

function togglePhoto(state) {
    state.autoplay = !state.autoplay;
    clearInterval(state.timer);
    if (state.autoplay) state.timer = setInterval(() => move(state, 1), state.interval * 1000);
    emit('photo', state.autoplay ? 'unpause' : 'pause', { currentTime: state.index, duration: state.items.length, paused: !state.autoplay });
}

async function openBook(state, request) {
    const item = request.items[0];
    const root = document.createElement('div');
    root.id = state.kind === 'pdf' ? 'pdfPlayer' : state.kind === 'comics' ? 'comicsPlayer' : 'bookPlayer';
    root.className = 'slideshowDialog';
    state.root = root;
    document.body.prepend(root);
    if (state.kind === 'epub') {
        root.innerHTML = '<div id="bookOsdMount"></div><div id="bookPlayerContainer" class="bookPlayerContainer"></div>';
         await loadScript('/generated/lib/jszip/dist/jszip.min.js');
         await loadScript('/generated/lib/epubjs/dist/epub.min.js');
         const book = window.ePub(item.downloadUrl || item.url, { openAs: 'epub' });
        const rendition = book.renderTo('bookPlayerContainer', { width: '100%', height: '100%', flow: 'paginated' });
         await rendition.display();
         await book.locations.generate(1024);
         if (request.startPositionTicks > 0 && book.locations.cfiFromPercentage) {
             await rendition.display(book.locations.cfiFromPercentage(request.startPositionTicks / 10000000));
         }
        state.book = book;
        state.rendition = rendition;
        state.pages = 1;
    } else if (state.kind === 'pdf') {
        root.innerHTML = '<div id="bookOsdMount"></div><div id="container"><canvas id="canvas"></canvas></div>';
         await loadScript('/generated/lib/pdfjs/build/pdf.min.js');
         window.pdfjsLib.GlobalWorkerOptions.workerSrc = '/generated/lib/pdfjs/build/pdf.worker.min.js';
         const pdf = await window.pdfjsLib.getDocument({ url: item.downloadUrl || item.url, isEvalSupported: false }).promise;
        state.pdf = pdf;
        state.pages = pdf.numPages;
         await renderPdfPage(state, Math.max(1, Math.floor(request.startPositionTicks / 10000) + 1));
    } else {
        root.innerHTML = '<div id="bookOsdMount"></div><div dir="ltr" class="slideshowSwiperContainer"><div class="swiper-wrapper"></div><div class="swiper-pagination"></div></div>';
        const response = await fetch(item.downloadUrl || item.url);
         const archive = await import('/generated/lib/libarchive/dist/libarchive.js');
         archive.Archive.init({ workerUrl: '/generated/lib/libarchive/dist/worker-bundle.js' });
        const opened = await archive.Archive.open(await response.blob());
         await opened.getFilesArray();
         await opened.extractFiles();
         const files = (await opened.getFilesArray()).filter(file => /\.(jpe?g|png|gif|webp|avif)$/i.test(file.file.name)).sort((a, b) => a.file.name.localeCompare(b.file.name));
        state.items = files.map(file => URL.createObjectURL(file.file));
        state.pages = state.items.length;
         state.index = Math.max(0, Math.floor(request.startPositionTicks / 10000));
         state.items.forEach((url, index) => {
            const slide = document.createElement('div');
            slide.className = 'swiper-slide';
            slide.dataset.index = index;
            const zoom = document.createElement('div');
            zoom.className = 'slider-zoom-container';
            const image = document.createElement('img');
            image.className = 'swiper-slide-img';
            image.src = url;
            image.alt = `${item.name || 'Comic'} page ${index + 1}`;
            zoom.append(image);
            slide.append(zoom);
             root.querySelector('.swiper-wrapper').append(slide);
         });
         root.querySelector('.swiper-slide')?.classList.add('swiper-slide-active');
         showBookPage(state);
    }
    addBookControls(state, request.items[0]);
    return snapshot({ duration: state.pages, currentTime: state.index || 0 });
}

function addBookControls(state, item) {
    const osd = document.createElement('div');
    osd.className = 'bookOsd';
    const top = document.createElement('div'); top.className = 'bookOsdRow';
    const bottom = document.createElement('div'); bottom.className = 'bookOsdRow';
    const title = document.createElement('span'); title.className = 'bookOsdTitle'; title.textContent = item.name;
    top.append(button('arrow_back', 'bookplayerButton', 'Back', () => stop(state.kind, true)), title);
    bottom.append(button('navigate_before', 'bookplayerButton', 'Previous', () => previous(state.kind)), button('navigate_next', 'bookplayerButton', 'Next', () => next(state.kind)));
    const spacer = document.createElement('div'); spacer.className = 'bookOsdSpacer'; bottom.append(spacer);
    if (state.kind === 'epub') {
        bottom.append(button('remove_red_eye', 'bookplayerButton', 'Theme', () => state.rendition?.themes?.update?.('default')));
        bottom.append(button('text_decrease', 'bookplayerButton', 'Smaller', () => state.rendition?.themes?.fontSize?.('90%')));
        bottom.append(button('text_increase', 'bookplayerButton', 'Larger', () => state.rendition?.themes?.fontSize?.('110%')));
    }
    bottom.append(button('fullscreen', 'bookplayerButton', 'Fullscreen', () => toggleFullscreen(state.kind)));
    osd.append(top, bottom);
    state.root.prepend(osd);
}

async function renderPdfPage(state, pageNumber) {
    state.index = Math.max(0, pageNumber - 1);
    const page = await state.pdf.getPage(pageNumber);
    const canvas = state.root?.querySelector('#canvas') || document.querySelector('#pdfPlayer #canvas');
    if (!canvas) return;
    const viewport = page.getViewport({ scale: Math.min(window.innerWidth / page.getViewport({ scale: 1 }).width, window.innerHeight / page.getViewport({ scale: 1 }).height) });
    canvas.width = viewport.width; canvas.height = viewport.height;
    await page.render({ canvasContext: canvas.getContext('2d'), viewport }).promise;
    emit(state.kind, 'timeupdate', { currentTime: state.index, duration: state.pages, paused: true });
}

function showBookPage(state) {
    state.root.querySelectorAll('.swiper-slide').forEach((slide, index) => slide.classList.toggle('swiper-slide-active', index === state.index));
    const wrapper = state.root.querySelector('.swiper-wrapper');
    wrapper.dataset.index = state.index;
    wrapper.style.transform = 'none';
    emit(state.kind, 'timeupdate', { currentTime: state.index, duration: state.pages, paused: true });
}

export async function open(request) {
    const state = { kind: request.kind, items: request.items, index: request.startIndex || 0 };
    players.set(request.kind, state);
    if (request.kind === 'photo') return openPhoto(state, request);
    if (request.kind === 'youtube') return openYoutube(state, request);
    if (request.kind === 'url') return openUrl(state, request);
    return openBook(state, request);
}

async function openYoutube(state, request) {
    const dialog = document.createElement('div'); dialog.className = 'youtubePlayerContainer onTop'; dialog.innerHTML = '<div id="player"></div>'; document.body.prepend(dialog); state.dialog = dialog;
    if (!window.YT?.Player) {
        await new Promise(resolve => {
            const previous = window.onYouTubeIframeAPIReady;
            window.onYouTubeIframeAPIReady = () => { previous?.(); resolve(); };
            if (!document.querySelector('script[src="https://www.youtube.com/iframe_api"]')) {
                const script = document.createElement('script'); script.src = 'https://www.youtube.com/iframe_api'; document.head.append(script);
            }
        });
    }
    const videoUrl = new URL(request.items[0].url);
    const id = videoUrl.searchParams.get('v') || videoUrl.pathname.split('/').filter(Boolean).pop();
    state.youtube = new YT.Player('player', { videoId: id, width: '100%', height: '100%', playerVars: { controls: 0, enablejsapi: 1, modestbranding: 1, rel: 0, fs: 0, playsinline: 1 }, events: { onReady: event => { event.target.seekTo(request.startPositionTicks / 10000000, true); event.target.playVideo(); state.timer = setInterval(() => { state.currentTime = state.youtube.getCurrentTime(); state.duration = state.youtube.getDuration(); emit('youtube', 'timeupdate', state); }, 500); }, onStateChange: event => { if (event.data === YT.PlayerState.PLAYING) emit('youtube', 'playing', state); if (event.data === YT.PlayerState.PAUSED) emit('youtube', 'pause', state); if (event.data === YT.PlayerState.ENDED) emit('youtube', 'stopped', state); } } });
    return snapshot();
}

async function openUrl(state, request) {
    const isAudio = request.items[0].mediaType?.toLowerCase() === 'audio';
    const media = document.createElement(isAudio ? 'audio' : 'video'); media.className = isAudio ? 'mediaPlayerAudio' : 'htmlvideoplayer'; media.controls = true; media.autoplay = true; media.src = request.items[0].url; media.currentTime = request.startPositionTicks / 10000000; document.body.prepend(media); state.media = media; media.addEventListener('timeupdate', () => emit('url', 'timeupdate', media)); media.addEventListener('ended', () => emit('url', 'stopped', media)); return new Promise(resolve => media.addEventListener('loadedmetadata', () => resolve(snapshot(media)), { once: true }));
}

export async function stop(kind, destroy = true) { const state = players.get(kind); if (!state) return; clearInterval(state.timer); state.currentTime = state.media?.currentTime ?? state.youtube?.getCurrentTime?.() ?? state.index ?? 0; state.duration = state.media?.duration ?? state.youtube?.getDuration?.() ?? state.pages ?? state.items?.length ?? 1; state.dialog?.remove(); state.root?.remove(); state.media?.remove(); state.youtube?.destroy(); players.delete(kind); emit(kind, 'stopped', state); }
export function pause(kind) { const state = players.get(kind); if (state?.media) state.media.pause(); else if (state?.youtube) state.youtube.pauseVideo(); else if (state) { state.autoplay = false; clearInterval(state.timer); emit(kind, 'pause', state); } }
export function unpause(kind) { const state = players.get(kind); if (state?.media) state.media.play(); else if (state?.youtube) state.youtube.playVideo(); else if (state) { state.autoplay = true; state.timer = setInterval(() => move(state, 1), state.interval * 1000); emit(kind, 'unpause', state); } }
export function next(kind) { const state = players.get(kind); if (!state) return; if (kind === 'pdf') renderPdfPage(state, state.index + 2); else if (kind === 'comics') { state.index = Math.min(state.pages - 1, state.index + 1); showBookPage(state); } else if (kind === 'photo') move(state, 1); else state.rendition?.next(); }
export function previous(kind) { const state = players.get(kind); if (!state) return; if (kind === 'pdf') renderPdfPage(state, state.index); else if (kind === 'comics') { state.index = Math.max(0, state.index - 1); showBookPage(state); } else if (kind === 'photo') move(state, -1); else state.rendition?.prev(); }
export function setCurrentTime(kind, seconds) { const state = players.get(kind); if (state?.media) state.media.currentTime = seconds; else if (state?.youtube) state.youtube.seekTo(seconds, true); else if (state?.kind === 'pdf') renderPdfPage(state, seconds + 1); else if (state) { state.index = Math.floor(seconds); showBookPage(state); } }
export function toggleFullscreen(kind) { const state = players.get(kind); (state?.dialog || state?.root || state?.media)?.requestFullscreen?.(); }
