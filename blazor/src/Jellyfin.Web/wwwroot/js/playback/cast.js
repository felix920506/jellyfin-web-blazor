// The Google Cast sender of the Chromecast player. Port of the browser parts of
// plugins/chromecastPlayer (castSenderApi.ts and the chrome.cast calls of CastPlayer); every decision
// is made by CastPlayer.cs, this module only talks to the SDK.

const SDK_URL = 'https://www.gstatic.com/cv/js/sender/v1/cast_sender.js';
const NAMESPACE = 'urn:x-cast:com.connectsdk';

let dotnet = null;
let sdk = null;
let session = null;

function notify(name, ...args) {
    dotnet?.invokeMethodAsync(name, ...args).catch(err => {
        console.warn(`[chromecastPlayer] ${name} could not be delivered`, err);
    });
}

function describe(s) {
    const volume = s.receiver?.volume;
    return {
        sessionId: s.sessionId ?? null,
        receiverName: s.receiver?.friendlyName ?? null,
        volumeLevel: volume?.level ?? null,
        muted: volume?.muted ?? null
    };
}

function onMessage(namespace, message) {
    notify('OnMessage', typeof message === 'string' ? message : JSON.stringify(message));
}

function onUpdate(isAlive) {
    if (isAlive) {
        if (session) {
            notify('OnSessionUpdated', describe(session));
        }
        return;
    }

    session = null;
    notify('OnSessionEnded');
}

// onSessionConnected(): the listeners of a new or joined session.
function attach(s) {
    session = s;
    s.addMessageListener(NAMESPACE, onMessage);
    s.addUpdateListener(onUpdate);
}

function waitForApi(resolve) {
    if (window.chrome?.cast?.isAvailable) {
        resolve(true);
    } else {
        setTimeout(() => waitForApi(resolve), 1000);
    }
}

function loadSdk() {
    // The original loads the plugin in Chromium browsers only and does not initialise it without a
    // window.chrome object (CastPlayer.initializeCastPlayer). Checking first keeps other browsers,
    // and Chromium builds without the Cast component, from fetching a script they cannot use.
    if (!window.chrome) {
        return Promise.resolve(false);
    }

    return new Promise(resolve => {
        window.__onGCastApiAvailable = available => {
            if (!available) {
                resolve(false);
            }
        };

        const script = document.createElement('script');
        script.setAttribute('type', 'text/javascript');
        script.onload = () => waitForApi(resolve);
        script.onerror = () => {
            script.remove();
            resolve(false);
        };
        script.setAttribute('src', SDK_URL);
        document.head.appendChild(script);
    });
}

/** Loads the sender SDK once; resolves with whether the Cast API can be used. */
export function load(dotnetRef) {
    dotnet = dotnetRef;
    sdk ??= loadSdk();
    return sdk;
}

/** chrome.cast.initialize with the receiver application; resolves with success. */
export function initialize(applicationId) {
    const cast = window.chrome?.cast;
    if (!cast?.isAvailable) {
        return Promise.resolve(false);
    }

    return new Promise(resolve => {
        const sessionRequest = new cast.SessionRequest(applicationId);
        const apiConfig = new cast.ApiConfig(
            sessionRequest,
            s => {
                if (s) {
                    attach(s);
                    notify('OnSessionJoined', describe(s));
                }
            },
            availability => notify('OnReceiverAvailability', availability === 'available'));
        cast.initialize(apiConfig, () => resolve(true), () => resolve(false));
    });
}

/** chrome.cast.requestSession: the browser's device picker. Resolves with the session, or null. */
export function requestSession() {
    const cast = window.chrome?.cast;
    if (!cast?.isAvailable) {
        return Promise.resolve(null);
    }

    return new Promise(resolve => {
        cast.requestSession(
            s => {
                attach(s);
                resolve(describe(s));
            },
            () => resolve(null));
    });
}

export function sendMessage(message) {
    return new Promise(resolve => {
        if (!session) {
            resolve();
            return;
        }

        session.sendMessage(NAMESPACE, message, () => resolve(), err => {
            console.warn('[chromecastPlayer] message not sent', err);
            resolve();
        });
    });
}

export function setReceiverVolumeLevel(level) {
    return new Promise(resolve => {
        if (!session) {
            resolve();
            return;
        }

        session.setReceiverVolumeLevel(level, () => resolve(), () => resolve());
    });
}

export function stopSession() {
    return new Promise(resolve => {
        if (!session) {
            resolve();
            return;
        }

        session.stop(() => resolve(), () => resolve());
    });
}
