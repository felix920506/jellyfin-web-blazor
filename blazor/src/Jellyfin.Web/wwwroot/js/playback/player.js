// The local media players (HTML video and HTML audio), driven from C#
// (Services/Playback/HtmlMediaPlayer.cs through JsPlayerInterop). Port of the element handling in
// plugins/htmlVideoPlayer/plugin.js, plugins/htmlAudioPlayer/plugin.js and components/htmlMediaHelper.js:
// same element markup and classes, same hls.js configuration and error handling. Decisions (what to
// play, which tracks, when to report) stay in C#; this module only owns the elements.

let dotnet = null;
let hlsPromise = null;
let recoverDecodingErrorDate;
let recoverSwapAudioCodecDate;
let fadeTimeout = null;
let mediaSessionBound = false;

const players = { video: null, audio: null };

function newPlayer(kind) {
    return { kind, elem: null, container: null, hls: null, src: null, started: false, fading: false, options: null, onError: null, gainNode: null, normalizationGain: 1, subtitleRenderer: null, secondarySubtitleRenderer: null, subtitleRendererGeneration: 0, secondarySubtitleRendererGeneration: 0 };
}

function getPlayer(kind) {
    if (!players[kind]) {
        players[kind] = newPlayer(kind);
    }
    return players[kind];
}

function isValidDuration(duration) {
    return duration && !isNaN(duration) && duration !== Number.POSITIVE_INFINITY && duration !== Number.NEGATIVE_INFINITY;
}

function isFullscreen() {
    return !!(document.fullscreenElement || document.webkitFullscreenElement || document.webkitIsFullScreen);
}

function isPictureInPicture(elem) {
    if (document.pictureInPictureEnabled) {
        return !!document.pictureInPictureElement;
    }
    return !!elem && elem.webkitPresentationMode === 'picture-in-picture';
}

function snapshot(player) {
    const elem = player && player.elem;
    const state = {
        currentTime: null,
        duration: null,
        paused: true,
        volume: 1,
        muted: false,
        playbackRate: 1,
        buffered: [],
        seekable: false,
        fullscreen: isFullscreen(),
        pictureInPicture: false,
        airPlay: false
    };
    if (!elem) {
        return state;
    }

    state.currentTime = isValidDuration(elem.currentTime) ? elem.currentTime : 0;
    state.duration = isValidDuration(elem.duration) ? elem.duration : null;
    state.paused = elem.paused;
    state.volume = elem.volume;
    state.muted = elem.muted;
    state.playbackRate = elem.playbackRate;
    state.pictureInPicture = player.kind === 'video' && isPictureInPicture(elem);
    state.airPlay = !!(elem.webkitCurrentPlaybackTargetIsWireless || (document.AirPlayEnabled && document.AirplayElement === elem));

    const buffered = elem.buffered || [];
    for (let i = 0; i < buffered.length; i++) {
        const start = isValidDuration(buffered.start(i)) ? buffered.start(i) : 0;
        const end = buffered.end(i);
        if (isValidDuration(end)) {
            state.buffered.push(start, end);
        }
    }

    const seekable = elem.seekable;
    if (seekable && seekable.length) {
        const start = isValidDuration(seekable.start(0)) ? seekable.start(0) : 0;
        const end = isValidDuration(seekable.end(0)) ? seekable.end(0) : 0;
        state.seekable = (end - start) > 0;
    }

    return state;
}

function emit(player, name, errorType) {
    if (dotnet) {
        dotnet.invokeMethodAsync('OnPlayerEvent', player.kind, name, snapshot(player), errorType || null);
    }
}

function requireHls() {
    if (window.Hls) {
        return Promise.resolve(window.Hls);
    }
    if (!hlsPromise) {
        hlsPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = 'generated/lib/hls.js/dist/hls.min.js';
            script.onload = () => {
                const Hls = window.Hls;
                Hls.DefaultConfig.lowLatencyMode = false;
                Hls.DefaultConfig.backBufferLength = Infinity;
                Hls.DefaultConfig.liveBackBufferLength = 90;
                resolve(Hls);
            };
            script.onerror = () => {
                hlsPromise = null;
                reject(new Error('hls.js could not be loaded'));
            };
            document.head.appendChild(script);
        });
    }
    return hlsPromise;
}

function destroyHls(player) {
    if (player.hls) {
        try {
            player.hls.destroy();
        } catch (err) {
            console.warn('[player] hls destroy failed', err);
        }
        player.hls = null;
    }
}

function resetSrc(elem) {
    elem.src = '';
    elem.innerHTML = '';
    elem.removeAttribute('src');
}

function onErrorInternal(player, type) {
    setSubtitleCues(null);
    disposeSubtitleRenderer(player, false);
    disposeSubtitleRenderer(player, true);
    emit(player, 'error', type);
}

function disposeSubtitleRenderer(player, secondary) {
    const property = secondary ? 'secondarySubtitleRenderer' : 'subtitleRenderer';
    const renderer = player[property];
    if (renderer) {
        if (renderer.dispose) {
            renderer.dispose();
        } else if (renderer.destroy) {
            renderer.destroy();
        }
    }
    player[property] = null;
}

let octopusPromise = null;
function requireOctopus() {
    if (window.SubtitlesOctopus) {
        return Promise.resolve(window.SubtitlesOctopus);
    }
    if (!octopusPromise) {
        octopusPromise = new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = assetUrl('generated/lib/subtitles-octopus/dist/js/subtitles-octopus.js');
            script.onload = () => window.SubtitlesOctopus ? resolve(window.SubtitlesOctopus) : reject(new Error('libass did not expose SubtitlesOctopus'));
            script.onerror = () => reject(new Error('libass could not be loaded'));
            document.head.appendChild(script);
        });
    }
    return octopusPromise;
}

function assetUrl(path) {
    return new URL(path, document.baseURI).href;
}

function rendererDisplaySettings(request) {
    return { aspectMode: request.aspectMode || 'contain' };
}

async function fallbackFonts(request) {
    const configResponse = await fetch(request.fallbackConfigUrl);
    if (!configResponse.ok) {
        throw new Error('fallback font configuration could not be loaded');
    }
    const config = await configResponse.json();
    if (!config.EnableFallbackFont) {
        return [];
    }
    const filesResponse = await fetch(request.fallbackFontsUrl);
    if (!filesResponse.ok) {
        throw new Error('fallback font list could not be loaded');
    }
    const files = await filesResponse.json();
    return files.map(font => {
        const url = new URL(request.fallbackFontUrl);
        url.pathname = url.pathname.replace(/\/$/, '') + '/' + encodeURIComponent(font.Name);
        return url.href;
    });
}

export async function setSubtitleRenderer(request, secondary) {
    const player = players.video;
    if (!player || !player.elem) {
        return;
    }
    const property = secondary ? 'secondarySubtitleRenderer' : 'subtitleRenderer';
    const generationProperty = secondary ? 'secondarySubtitleRendererGeneration' : 'subtitleRendererGeneration';
    const generation = ++player[generationProperty];
    disposeSubtitleRenderer(player, secondary);
    setTrackCues(secondary ? 'manualSecondaryTrack' : 'manualTrack', null);
    if (!request) {
        return;
    }

    const error = () => {
        if (player[generationProperty] === generation) {
            disposeSubtitleRenderer(player, secondary);
            emit(player, 'error', request.codec === 'pgssub' ? 'PGS_RENDER_ERROR' : 'ASS_RENDER_ERROR');
        }
    };
    try {
        if (request.codec === 'ass' || request.codec === 'ssa') {
            const SubtitlesOctopus = await requireOctopus();
            // Keep the bundled fallback available under a visible filename as well as the
            // worker's hidden .fallback-* name. Fontconfig ignores hidden files while scanning
            // /fonts, which otherwise makes libass report a failed font scan even though its
            // direct fallback font works.
            const fonts = [
                assetUrl('generated/lib/subtitles-octopus/dist/js/default.woff2'),
                ...(request.attachments || []),
                ...(await fallbackFonts(request))
            ];
            if (player[generationProperty] !== generation) {
                return;
            }
            player[property] = new SubtitlesOctopus({
                video: player.elem,
                subUrl: request.url,
                fonts,
                workerUrl: assetUrl('generated/lib/subtitles-octopus/dist/js/subtitles-octopus-worker.js'),
                legacyWorkerUrl: assetUrl('generated/lib/subtitles-octopus/dist/js/subtitles-octopus-worker-legacy.js'),
                timeOffset: request.timeOffset || 0,
                renderMode: 'wasm-blend',
                dropAllAnimations: false,
                libassMemoryLimit: 40,
                libassGlyphLimit: 40,
                targetFps: request.targetFps || 24,
                prescaleFactor: 0.8,
                prescaleHeightLimit: 1080,
                maxRenderHeight: 2160,
                resizeVariation: 0.2,
                renderAhead: 90,
                onError: error
            });
        } else if (request.codec === 'pgssub') {
            const libbitsub = await import(assetUrl('generated/lib/libbitsub/dist/index.js'));
            if (player[generationProperty] !== generation) {
                return;
            }
            player[property] = new libbitsub.PgsRenderer({
                video: player.elem,
                container: player.container,
                subUrl: request.url,
                timeOffset: request.timeOffset || 0,
                streamingLoad: true,
                rangeRequests: true,
                prefetchWindow: { before: 1, after: 2 },
                displaySettings: {
                    scale: 1,
                    aspectMode: request.aspectMode || 'contain',
                    verticalOffset: 0,
                    horizontalOffset: 0,
                    horizontalAlign: 'center',
                    bottomPadding: 0,
                    safeArea: 0,
                    opacity: 1
                },
                onError: error
            });
        }
    } catch (err) {
        error();
    }
}

export function setSubtitleRendererOffset(seconds, secondary) {
    const player = players.video;
    const renderer = player && player[secondary ? 'secondarySubtitleRenderer' : 'subtitleRenderer'];
    if (renderer) {
        renderer.timeOffset = (player.options && player.options.transcodingOffsetSeconds || 0) + seconds;
    }
}

function handleHlsJsMediaError(player, reject) {
    const hls = player.hls;
    if (!hls) {
        return;
    }

    const now = performance.now();
    if (!recoverDecodingErrorDate || (now - recoverDecodingErrorDate) > 3000) {
        recoverDecodingErrorDate = now;
        hls.recoverMediaError();
    } else if (!recoverSwapAudioCodecDate || (now - recoverSwapAudioCodecDate) > 3000) {
        recoverSwapAudioCodecDate = now;
        hls.swapAudioCodec();
        hls.recoverMediaError();
    } else if (reject) {
        reject();
    } else {
        onErrorInternal(player, 'FATAL_HLS_ERROR');
    }
}

function createErrorHandler(player) {
    return function () {
        const elem = player.elem;
        const errorCode = elem && elem.error ? (elem.error.code || 0) : 0;
        let type;
        switch (errorCode) {
            case 2:
                type = 'NETWORK_ERROR';
                break;
            case 3:
                if (player.hls) {
                    handleHlsJsMediaError(player);
                    return;
                }
                type = 'MEDIA_DECODE_ERROR';
                break;
            case 4:
                type = 'MEDIA_NOT_SUPPORTED';
                break;
            default:
                return;
        }
        onErrorInternal(player, type);
    };
}

function playWithPromise(player) {
    const elem = player.elem;
    try {
        return elem.play()
            .catch(e => {
                const errorName = (e.name || '').toLowerCase();
                if (errorName === 'notallowederror' || errorName === 'aborterror') {
                    return Promise.resolve();
                }
                return Promise.reject(e);
            })
            .then(() => {
                elem.addEventListener('error', player.onError);
            });
    } catch (err) {
        return Promise.reject(err);
    }
}

function bindEventsToHls(player, hls, resolve, reject) {
    const Hls = window.Hls;
    hls.on(Hls.Events.MANIFEST_PARSED, () => {
        playWithPromise(player).then(resolve, () => {
            if (reject) {
                reject();
                reject = null;
            }
        });
    });

    hls.on(Hls.Events.ERROR, (event, data) => {
        console.warn('[player] hls error', data.type, data.details, 'fatal:', data.fatal);
        if (data.type === Hls.ErrorTypes.NETWORK_ERROR && data.response && data.response.code && data.response.code >= 400) {
            hls.destroy();
            if (reject) {
                reject('SERVER_ERROR');
                reject = null;
            } else {
                onErrorInternal(player, 'SERVER_ERROR');
            }
            return;
        }

        if (!data.fatal) {
            return;
        }

        switch (data.type) {
            case Hls.ErrorTypes.NETWORK_ERROR:
                if (data.response && data.response.code === 0) {
                    hls.destroy();
                    if (reject) {
                        reject('NETWORK_ERROR');
                        reject = null;
                    } else {
                        onErrorInternal(player, 'NETWORK_ERROR');
                    }
                } else {
                    hls.startLoad();
                }
                break;
            case Hls.ErrorTypes.MEDIA_ERROR:
                handleHlsJsMediaError(player, reject);
                reject = null;
                break;
            default:
                hls.destroy();
                if (reject) {
                    reject();
                    reject = null;
                } else {
                    onErrorInternal(player, 'FATAL_HLS_ERROR');
                }
                break;
        }
    });
}

function setCurrentTimeIfNeeded(elem, seconds) {
    if (Math.abs((elem.currentTime || 0) - seconds) >= 1) {
        elem.currentTime = seconds;
    }
}

function seekOnPlaybackStart(elem, seconds) {
    if (!seconds) {
        return;
    }
    if (elem.duration >= seconds) {
        setCurrentTimeIfNeeded(elem, seconds);
        return;
    }

    const events = ['durationchange', 'loadeddata', 'play', 'loadedmetadata'];
    const onMediaChange = () => {
        if (elem.currentTime === 0 && elem.duration >= seconds) {
            setCurrentTimeIfNeeded(elem, seconds);
            events.forEach(name => elem.removeEventListener(name, onMediaChange));
        }
    };
    events.forEach(name => elem.addEventListener(name, onMediaChange));
}

function onEndedInternal(player) {
    const elem = player.elem;
    elem.removeEventListener('error', player.onError);
    resetSrc(elem);
    destroyHls(player);
    // The event carries the last state before the source is dropped on the C# side.
    emit(player, 'stopped');
    player.src = null;
    player.options = null;
}

function setTransparency(on) {
    document.documentElement.classList.toggle('transparentDocument', on);
}

// --- element creation ---------------------------------------------------------------------------

function bindElementEvents(player, elem) {
    const on = (name, handler) => elem.addEventListener(name, handler);
    on('timeupdate', () => {
        if (!player.fading) {
            if (player.kind === 'video' && elem.currentTime && !player.timeUpdated) {
                player.timeUpdated = true;
                // A video without a picture cannot be shown: report it like the original.
                if (elem.videoWidth === 0 && elem.videoHeight === 0 && player.options && player.options.hasRunTime) {
                    onErrorInternal(player, 'NO_MEDIA_ERROR');
                    return;
                }
            }
            emit(player, 'timeupdate');
        }
    });
    on('ended', () => {
        if (player.kind === 'video') {
            setSubtitleCues(null);
        }
        onEndedInternal(player);
    });
    on('volumechange', () => {
        if (!player.fading) {
            emit(player, 'volumechange');
        }
    });
    on('pause', () => emit(player, 'pause'));
    on('playing', () => {
        if (!player.started) {
            player.started = true;
            elem.removeAttribute('controls');
            seekOnPlaybackStart(elem, player.options ? player.options.startSeconds : 0);
            if (player.kind === 'video') {
                if (player.options && player.options.fullscreen) {
                    // C# navigates to the video OSD and then calls navigatedToOsd().
                    emit(player, 'firstplaying');
                } else {
                    setTransparency(true);
                    player.container.classList.remove('videoPlayerContainer-onTop');
                    emit(player, 'firstplaying');
                }
            }
        }
        emit(player, 'playing');
    });
    on('play', () => emit(player, 'unpause'));
    on('waiting', () => emit(player, 'waiting'));
    on('ratechange', () => emit(player, 'ratechange'));
    on('durationchange', () => emit(player, 'durationchange'));
    if (player.kind === 'video') {
        on('click', () => emit(player, 'click'));
        on('dblclick', () => emit(player, 'dblclick'));
        on('canplay', () => emit(player, 'canplay'));
        on('enterpictureinpicture', () => emit(player, 'pictureinpicturechange'));
        on('leavepictureinpicture', () => emit(player, 'pictureinpicturechange'));
        on('webkitpresentationmodechanged', () => emit(player, 'pictureinpicturechange'));
        on('webkitplaybacktargetavailabilitychanged', () => emit(player, 'airplayavailabilitychange'));
        on('webkitcurrentplaybacktargetiswirelesschanged', () => emit(player, 'airplaychange'));
    }
}

function createVideoElement(player, options) {
    if (player.elem) {
        if (options.fullscreen) {
            document.body.classList.add('hide-scroll');
        }
        return player.elem;
    }

    const container = document.createElement('div');
    container.setAttribute('dir', 'ltr');
    container.classList.add('videoPlayerContainer');
    if (options.fullscreen) {
        container.classList.add('videoPlayerContainer-onTop');
    }
    container.innerHTML = '<video class="htmlvideoplayer" preload="metadata" autoplay="autoplay" webkit-playsinline playsinline></video>';
    const elem = container.querySelector('video');
    if (options.volume != null) {
        elem.volume = options.volume;
    }

    player.elem = elem;
    player.container = container;
    player.onError = createErrorHandler(player);
    bindElementEvents(player, elem);
    document.body.insertBefore(container, document.body.firstChild);
    if (options.fullscreen) {
        document.body.classList.add('hide-scroll');
    }
    return elem;
}

function createAudioElement(player, options) {
    if (player.elem) {
        return player.elem;
    }

    let elem = document.querySelector('.mediaPlayerAudio');
    if (!elem) {
        elem = document.createElement('audio');
        elem.classList.add('mediaPlayerAudio');
        elem.classList.add('hide');
        document.body.appendChild(elem);
    }
    if (options.volume != null) {
        elem.volume = options.volume;
    }

    player.elem = elem;
    player.onError = createErrorHandler(player);
    bindElementEvents(player, elem);
    return elem;
}

function applyNormalization(player, elem, gain) {
    if (gain == null) {
        return;
    }
    if (!player.gainNode) {
        try {
            const AudioContext = window.AudioContext || window.webkitAudioContext;
            const context = new AudioContext();
            const source = context.createMediaElementSource(elem);
            const gainNode = context.createGain();
            source.connect(gainNode);
            gainNode.connect(context.destination);
            player.gainNode = gainNode;
            player.audioContext = context;
        } catch (err) {
            console.warn('[player] Web Audio is not available', err);
            return;
        }
    }
    if (player.audioContext && player.audioContext.state === 'suspended') {
        player.audioContext.resume().catch(() => { /* resumed by the next user gesture */ });
    }
    player.normalizationGain = gain ? Math.pow(10, gain / 20) : 1;
    player.gainNode.gain.value = player.normalizationGain;
}

function applyAspectRatio(elem, value) {
    if (!elem) {
        return;
    }
    if (!value || value === 'auto') {
        elem.style.removeProperty('object-fit');
    } else {
        elem.style['object-fit'] = value;
    }
}

// --- commands -----------------------------------------------------------------------------------

export function init(dotnetRef) {
    dotnet = dotnetRef;
    const onFullscreenChange = () => {
        const player = players.video || players.audio;
        if (player) {
            emit(player, 'fullscreenchange');
        }
    };
    document.addEventListener('fullscreenchange', onFullscreenChange);
    document.addEventListener('webkitfullscreenchange', onFullscreenChange);
}

export async function load(kind, options) {
    const player = getPlayer(kind);
    player.started = false;
    player.timeUpdated = false;
    player.fading = false;

    const elem = kind === 'video' ? createVideoElement(player, options) : createAudioElement(player, options);
    if (kind === 'video') {
        applyAspectRatio(elem, options.aspectRatio);
    } else {
        applyNormalization(player, elem, options.normalizationGain);
        if ('audioSession' in navigator) {
            navigator.audioSession.type = 'playback';
        }
    }

    elem.removeEventListener('error', player.onError);
    let url = options.url;
    if (options.startSeconds) {
        url += '#t=' + options.startSeconds;
    }

    destroyHls(player);
    player.options = options;
    if (options.crossOrigin) {
        elem.crossOrigin = options.crossOrigin;
    }

    try {
        if (options.useHlsJs) {
            const Hls = await requireHls();
            await new Promise((resolve, reject) => {
                const config = {
                    manifestLoadingTimeOut: 20000,
                    xhrSetup(xhr) {
                        xhr.withCredentials = false;
                    }
                };
                if (kind === 'video') {
                    config.startPosition = options.startSeconds;
                    config.maxBufferLength = options.maxBufferLength;
                    config.maxMaxBufferLength = options.maxBufferLength;
                    config.videoPreference = { preferHDR: true };
                }
                const hls = new Hls(config);
                hls.loadSource(url);
                hls.attachMedia(elem);
                bindEventsToHls(player, hls, resolve, reject);
                player.hls = hls;
                player.src = url;
            });
        } else {
            elem.autoplay = true;
            elem.src = url;
            player.src = url;
            await playWithPromise(player);
        }
        return { ok: true };
    } catch (err) {
        if (typeof err === 'string') {
            return { ok: false, errorType: err };
        }
        return { ok: false, errorName: err && err.name ? err.name : null };
    }
}

function fade(player, elem, startingVolume) {
    player.fading = true;
    const newVolume = Math.max(0, startingVolume - 0.15);
    elem.volume = newVolume;
    if (newVolume <= 0) {
        player.fading = false;
        return Promise.resolve();
    }
    return new Promise(resolve => {
        clearTimeout(fadeTimeout);
        fadeTimeout = setTimeout(() => fade(player, elem, newVolume).then(resolve), 100);
    });
}

export async function stop(kind, destroyPlayer, useFade) {
    const player = players[kind];
    if (!player || !player.elem) {
        return;
    }

    clearTimeout(fadeTimeout);
    const elem = player.elem;
    if (kind === 'audio') {
        if (!player.src) {
            return;
        }
        if (destroyPlayer && useFade) {
            const originalVolume = elem.volume;
            await fade(player, elem, elem.volume);
            elem.pause();
            elem.volume = originalVolume;
        } else {
            elem.pause();
        }
        onEndedInternal(player);
    } else {
        if (player.src) {
            elem.pause();
        }
        onEndedInternal(player);
        setSubtitleCues(null);
    }

    if (destroyPlayer) {
        destroy(kind);
    }
}

export function destroy(kind) {
    const player = players[kind];
    if (!player) {
        return;
    }

    destroyHls(player);
    disposeSubtitleRenderer(player, false);
    disposeSubtitleRenderer(player, true);
    if (kind === 'video') {
        setTransparency(false);
        document.body.classList.remove('hide-scroll');
        if (player.elem) {
            resetSrc(player.elem);
        }
        if (player.container && player.container.parentNode) {
            player.container.parentNode.removeChild(player.container);
        }
        if (isFullscreen()) {
            const exit = document.exitFullscreen || document.webkitExitFullscreen || document.webkitCancelFullScreen;
            if (exit) {
                Promise.resolve(exit.call(document)).catch(() => { /* not in fullscreen any more */ });
            }
        }
        players.video = null;
    } else if (player.elem) {
        // The audio element (and its Web Audio graph) is kept for the next track, as in the original.
        resetSrc(player.elem);
        player.src = null;
    }
}

export function pause(kind) {
    const player = players[kind];
    if (player && player.elem) {
        player.elem.pause();
    }
}

export function unpause(kind) {
    const player = players[kind];
    if (player && player.elem) {
        const promise = player.elem.play();
        if (promise && promise.catch) {
            promise.catch(err => console.warn('[player] play() was rejected', err));
        }
    }
}

export function setCurrentTime(kind, seconds) {
    const player = players[kind];
    if (player && player.elem) {
        player.elem.currentTime = seconds;
    }
}

export function setVolume(kind, value) {
    const player = players[kind];
    if (player && player.elem) {
        player.elem.volume = Math.min(Math.max(value, 0), 1);
    }
}

export function setMute(kind, mute) {
    const player = players[kind];
    if (player && player.elem) {
        player.elem.muted = mute;
    }
}

export function setPlaybackRate(kind, rate) {
    const player = players[kind];
    if (player && player.elem) {
        player.elem.playbackRate = rate;
    }
}

export function setAspectRatio(value) {
    applyAspectRatio(players.video && players.video.elem, value);
}

export function setAudioTrack(position) {
    const elem = players.video && players.video.elem;
    if (!elem) {
        return;
    }
    const tracks = elem.audioTracks || [];
    for (let i = 0; i < tracks.length; i++) {
        tracks[i].enabled = i === position;
    }
}

// Text subtitles are fetched by C# as TrackEvents and shown through a native text track, as the
// original does with its "manualTrack".
export function setSubtitleCues(cues) {
    setTrackCues('manualTrack', cues);
}

export function setSecondarySubtitleCues(cues) {
    setTrackCues('manualSecondaryTrack', cues);
}

function setTrackCues(label, cues) {
    const elem = players.video && players.video.elem;
    if (!elem || !elem.textTracks) {
        return;
    }

    let track = null;
    for (const current of Array.from(elem.textTracks)) {
        if (current.label === label) {
            track = current;
            break;
        }
    }

    if (!cues) {
        if (track) {
            // Remove the cues first: a disabled track has no cue list in some browsers.
            track.mode = 'hidden';
            for (const cue of Array.from(track.cues || [])) {
                track.removeCue(cue);
            }
            track.mode = 'disabled';
        }
        return;
    }

    if (!track) {
        track = elem.addTextTrack('subtitles', label, 'und');
    } else {
        track.mode = 'hidden';
        for (const cue of Array.from(track.cues || [])) {
            track.removeCue(cue);
        }
    }

    track.mode = 'showing';
    const TrackCue = window.VTTCue || window.TextTrackCue;
    for (const cue of cues) {
        track.addCue(new TrackCue(cue.start, cue.end, cue.text));
    }
}

export function toggleFullscreen() {
    const root = document.documentElement;
    const elem = players.video && players.video.elem;
    let result;
    if (isFullscreen()) {
        const exit = document.exitFullscreen || document.webkitExitFullscreen || document.webkitCancelFullScreen;
        result = exit && exit.call(document);
    } else if (root.requestFullscreen) {
        result = root.requestFullscreen();
    } else if (root.webkitRequestFullscreen) {
        result = root.webkitRequestFullscreen();
    } else if (elem && elem.webkitEnterFullscreen) {
        result = elem.webkitEnterFullscreen();
    }
    if (result && result.catch) {
        result.catch(err => console.warn('[player] fullscreen request was rejected', err));
    }
}

export function setPictureInPicture(enabled) {
    const elem = players.video && players.video.elem;
    if (!elem) {
        return;
    }
    let result;
    if (document.pictureInPictureEnabled) {
        result = enabled ? elem.requestPictureInPicture() : document.exitPictureInPicture();
    } else if (elem.webkitSetPresentationMode) {
        elem.webkitSetPresentationMode(enabled ? 'picture-in-picture' : 'inline');
    }
    if (result && result.catch) {
        result.catch(err => console.warn('[player] picture-in-picture request was rejected', err));
    }
}

// The legacy WebKit picker remains the public AirPlay API in Safari/iOS. A current target change is
// reported by webkitcurrentplaybacktargetiswirelesschanged above.
export function setAirPlayEnabled(kind, enabled) {
    const player = players[kind];
    const elem = player && player.elem;
    if (!elem) {
        return;
    }
    let result;
    if (document.AirPlayEnabled) {
        result = enabled && elem.requestAirPlay ? elem.requestAirPlay() : !enabled && document.exitAirPlay ? document.exitAirPlay() : null;
    } else if (elem.webkitShowPlaybackTargetPicker) {
        result = elem.webkitShowPlaybackTargetPicker();
    }
    if (result && result.catch) {
        result.catch(err => console.warn('[player] AirPlay request was rejected', err));
    }
}


export function navigatedToOsd() {
    const player = players.video;
    if (player && player.container) {
        // The page behind the OSD becomes see-through and the video moves below it.
        setTransparency(true);
        player.container.classList.remove('videoPlayerContainer-onTop');
    }
}

export function getStats(kind) {
    const player = players[kind];
    const elem = player && player.elem;
    if (!elem) {
        return null;
    }

    const stats = { hls: !!player.hls, protocol: null, viewWidth: 0, viewHeight: 0, videoWidth: 0, videoHeight: 0, droppedFrames: null, corruptedFrames: null, sinkId: elem.sinkId || null };
    if (player.options && player.options.url) {
        const link = document.createElement('a');
        link.setAttribute('href', player.options.url);
        stats.protocol = (link.protocol || '').replace(':', '') || null;
    }
    if (kind === 'video') {
        const ratio = window.devicePixelRatio || 1;
        const rect = elem.getBoundingClientRect();
        stats.viewWidth = Math.round(rect.width * ratio);
        stats.viewHeight = Math.round(rect.height * ratio);
        stats.videoWidth = elem.videoWidth || 0;
        stats.videoHeight = elem.videoHeight || 0;
        if (elem.getVideoPlaybackQuality) {
            const quality = elem.getVideoPlaybackQuality();
            stats.droppedFrames = quality.droppedVideoFrames || 0;
            stats.corruptedFrames = quality.corruptedVideoFrames || 0;
        }
    }
    return stats;
}

// --- Media Session -------------------------------------------------------------------------------

export function setMediaSession(info) {
    if (!('mediaSession' in navigator)) {
        return;
    }

    if (!mediaSessionBound) {
        mediaSessionBound = true;
        const actions = ['pause', 'play', 'previoustrack', 'nexttrack', 'stop', 'seekto', 'seekbackward', 'seekforward'];
        for (const action of actions) {
            try {
                navigator.mediaSession.setActionHandler(action, details => {
                    if (dotnet) {
                        dotnet.invokeMethodAsync('OnMediaSessionAction', details.action, details.seekTime == null ? null : details.seekTime);
                    }
                });
            } catch (err) {
                console.warn(`[player] Failed to add "${action}" action handler`, err);
            }
        }
    }

    if (!info) {
        navigator.mediaSession.metadata = null;
        return;
    }

    const current = navigator.mediaSession.metadata;
    if (!current || current.album !== (info.album || '') || current.artist !== (info.artist || '') || current.title !== (info.title || '')) {
        navigator.mediaSession.metadata = new MediaMetadata({
            title: info.title || undefined,
            artist: info.artist || undefined,
            album: info.album || undefined,
            artwork: info.artwork || []
        });
    }
}

// --- diagnostics ----------------------------------------------------------------------------------

// Read-only view of the elements for the browser tests (blazor/e2e/playback.py).
export function debugState() {
    const result = {};
    for (const kind of ['video', 'audio']) {
        const player = players[kind];
        result[kind] = player && player.elem
            ? Object.assign(snapshot(player), { src: player.src, hls: !!player.hls, videoWidth: player.elem.videoWidth || 0, readyState: player.elem.readyState })
            : null;
    }
    return result;
}
