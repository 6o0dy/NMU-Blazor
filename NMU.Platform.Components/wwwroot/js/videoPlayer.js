window.videoPlayer = {
    _progressKey: '',
    _buffer: [],

    init: function (progressKey) {
        this._progressKey = progressKey;
        this._buffer = [];
    },

    setSource: function (src) {
        this._buffer = [];
        var video = document.getElementById('vp-video');
        if (video) video.src = src;
    },

    appendChunk: function (bytes) {
        this._buffer.push(new Uint8Array(bytes));
    },

    finalizeBlob: function (mimeType) {
        var blob = new Blob(this._buffer, { type: mimeType || 'video/mp4' });
        var url = URL.createObjectURL(blob);
        this._buffer = [];
        var video = document.getElementById('vp-video');
        if (video) video.src = url;
    },

    play: function () { var v = document.getElementById('vp-video'); if (v && v.paused) v.play(); },
    pause: function () { var v = document.getElementById('vp-video'); if (v && !v.paused) v.pause(); },
    seekTo: function (t) { var v = document.getElementById('vp-video'); if (v && isFinite(t)) v.currentTime = t; },
    setVolume: function (val) { var v = document.getElementById('vp-video'); if (v) v.volume = parseFloat(val); },
    setSpeed: function (rate) { var v = document.getElementById('vp-video'); if (v) v.playbackRate = parseFloat(rate); },

    getState: function () {
        var v = document.getElementById('vp-video');
        if (!v) return '0|0';
        return String(v.currentTime) + '|' + String(v.duration || 0);
    },

    toggleFullscreen: function () {
        var el = document.getElementById('vp-body');
        if (!el) return;
        if (!document.fullscreenElement) el.requestFullscreen(); else document.exitFullscreen();
    },

    togglePiP: function () {
        var v = document.getElementById('vp-video');
        if (!v) return;
        if (document.pictureInPictureElement) document.exitPictureInPicture();
        else if (document.pictureInPictureEnabled) v.requestPictureInPicture();
    },

    loadSavedTime: function () {
        return this._progressKey ? (localStorage.getItem(this._progressKey) || '0') : '0';
    },

    saveTime: function (time) {
        if (this._progressKey && parseFloat(time) > 0) {
            try { localStorage.setItem(this._progressKey, String(time)); } catch (e) { }
        }
    },

    destroy: function () {
        var v = document.getElementById('vp-video');
        if (v) { v.pause(); v.src = ''; v.load(); }
        if (document.pictureInPictureElement) document.exitPictureInPicture();
    }
};
