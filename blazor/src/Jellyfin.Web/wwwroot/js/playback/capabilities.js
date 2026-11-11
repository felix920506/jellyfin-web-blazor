// Media capability probing for the device profile (Services/Playback/DeviceProfileBuilder.cs).
// The answers come from the browser's own canPlayType / MediaSource, as in browserDeviceProfile.js.

function canPlay(elem, type) {
    try {
        return !!elem.canPlayType && elem.canPlayType(type).replace(/no/, '') !== '';
    } catch (err) {
        return false;
    }
}

function speakerCount() {
    const AudioContext = window.AudioContext || window.webkitAudioContext;
    if (!AudioContext) {
        return -1;
    }
    try {
        const context = new AudioContext();
        const count = context.destination.maxChannelCount;
        if (context.close) {
            context.close();
        }
        return count;
    } catch (err) {
        return -1;
    }
}

export function probe(videoTypes, audioTypes) {
    const video = document.createElement('video');
    const audio = document.createElement('audio');
    return {
        videoTypes: videoTypes.filter(t => canPlay(video, t)),
        audioTypes: audioTypes.filter(t => canPlay(audio, t)),
        mediaSource: window.MediaSource != null,
        textTracks: video.textTracks != null,
        audioTracks: video.audioTracks != null,
        speakerCount: speakerCount(),
        pictureInPicture: !!(document.pictureInPictureEnabled
            || (typeof video.webkitSupportsPresentationMode === 'function'
                && video.webkitSupportsPresentationMode('picture-in-picture')
                && typeof video.webkitSetPresentationMode === 'function')),
        fullscreen: !!(document.fullscreenEnabled || document.webkitFullscreenEnabled || video.webkitEnterFullscreen),
        airPlay: !!window.WebKitPlaybackTargetAvailabilityEvent
    };
}
