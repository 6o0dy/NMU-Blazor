window.videoPlayer = {
    _progressKey: '',
    _buffer: [],
    _drag: false,
    _pumpTimer: null,
    _lastDur: 0,
    _lastDrawT: 0,
    _synth: null,
    _stuckFrames: 0,
    _notifyCounter: 0,
    _lastNotifiedSig: '',
    _lastSaved: 0,
    _pendingRestore: 0,
    _pendingRestoreTries: 0,
    _boundMove: null,
    _boundUp: null,

    init: function (progressKey) {
        this._progressKey = progressKey;
        this._buffer = [];
    },

    setSource: function (src) {
        this._buffer = [];
        this._surfaceFixed = false;
        if (!src) return;
        var video = document.getElementById('vp-video');
        if (video) video.src = src;
        this._autoPlay();
    },

    appendChunk: function (bytes) {
        this._buffer.push(new Uint8Array(bytes));
    },

    finalizeBlob: function (mimeType) {
        var blob = new Blob(this._buffer, { type: mimeType || 'video/mp4' });
        var url = URL.createObjectURL(blob);
        this._buffer = [];
        this._surfaceFixed = false;
        var video = document.getElementById('vp-video');
        if (video) video.src = url;
        this._autoPlay();
    },

    _autoPlay: function () {
        var v = document.getElementById('vp-video');
        if (!v) return;
        try {
            var p = v.play();
            if (p && p.catch) p.catch(function () { });
        } catch (e) { }
    },

    play: function () { var v = document.getElementById('vp-video'); if (v && v.paused) v.play(); },
    pause: function () { var v = document.getElementById('vp-video'); if (v && !v.paused) v.pause(); },

    forcePaint: function () {
        var self = this;
        var el = document.getElementById('vp-body');
        var v = document.getElementById('vp-video');
        try {
            if (el) el.classList.add('vp-force-paint');
            if (v && v.videoWidth > 0 && !this._surfaceFixed) {
                this._surfaceFixed = true;
                var vis = v.style.visibility;
                v.style.visibility = 'hidden';
                void v.offsetHeight;
                v.style.visibility = vis || '';
            }
        } catch (e) { }
        setTimeout(function () {
            if (el) el.classList.remove('vp-force-paint');
        }, 600);
    },
    seekTo: function (t) { var v = document.getElementById('vp-video'); if (v && isFinite(t)) v.currentTime = t; },
    setVolume: function (val) { var v = document.getElementById('vp-video'); if (v) v.volume = parseFloat(val); },
    setSpeed: function (rate) { var v = document.getElementById('vp-video'); if (v) v.playbackRate = parseFloat(rate); },

    retryLoad: function () {
        var v = document.getElementById('vp-video');
        if (!v || !v.src) return false;
        var t = v.currentTime || 0;
        v.load();
        if (t > 0) {
            var self = this;
            setTimeout(function () {
                try { v.currentTime = t; } catch (e) { }
                self._autoPlay();
            }, 100);
        } else {
            this._autoPlay();
        }
        return true;
    },

    switchSource: function (src) {
        var v = document.getElementById('vp-video');
        if (!v) return;
        var t = v.currentTime || 0;
        this._surfaceFixed = false;
        v.src = src;
        if (t > 0) {
            var self = this;
            setTimeout(function () {
                try { v.currentTime = t; } catch (e) { }
                self._autoPlay();
            }, 150);
        } else {
            this._autoPlay();
        }
    },

    warmList: function (urls) {
        if (!urls || !urls.length) return;
        if (typeof urls === 'string') urls = [urls];
        var self = this;
        for (var i = 0; i < urls.length; i++) {
            var u = urls[i];
            if (u && !this._warmSeen[u]) {
                this._warmSeen[u] = 1;
                this._warmQueue.push(u);
            }
        }
        this._warmPump();
    },

    _warmQueue: [],
    _warmSeen: {},
    _warmActive: false,

    _warmPump: function () {
        var self = this;
        if (this._warmActive) return;
        var url = this._warmQueue.shift();
        if (!url) return;
        this._warmActive = true;
        var v = null;
        var done = function () {
            try {
                if (v) { v.removeAttribute('src'); v.load(); }
                if (v && v.parentNode) v.parentNode.removeChild(v);
            } catch (e) { }
            v = null;
            self._warmActive = false;
            setTimeout(function () { self._warmPump(); }, 200);
        };
        try {
            v = document.createElement('video');
            v.preload = 'metadata';
            v.muted = true;
            v.playsInline = true;
            v.style.cssText = 'position:fixed;width:1px;height:1px;opacity:0;pointer-events:none;';
            v.addEventListener('loadedmetadata', done);
            v.addEventListener('error', done);
            v.src = url;
            document.body.appendChild(v);
        } catch (e) { done(); }
    },

    _ui: function () {
        return {
            bar: document.getElementById('vp-progress-bar') || document.querySelector('.vp-progress-bar'),
            thumb: document.getElementById('vp-progress-thumb') || document.querySelector('.vp-progress-thumb'),
            cur: document.getElementById('vp-time-current'),
            dur: document.getElementById('vp-time-duration')
        };
    },

    _fmt: function (t) {
        if (!isFinite(t) || t <= 0) return '0:00';
        if (t > 359999) t = 359999;
        t = Math.floor(t);
        var m = Math.floor(t / 60), s = t % 60;
        return m + ':' + (s < 10 ? '0' + s : s);
    },

    _draw: function (cur, dur) {
        var ui = this._ui();
        var pct = (dur > 0 && isFinite(cur)) ? Math.max(0, Math.min(1, (cur / dur))) * 100 : 0;
        if (ui.bar) ui.bar.style.width = pct.toFixed(2) + '%';
        if (ui.thumb) ui.thumb.style.left = pct.toFixed(2) + '%';
        if (ui.cur) ui.cur.textContent = this._fmt(cur);
        if (ui.dur) ui.dur.textContent = this._fmt(dur);
        this._lastDrawT = cur;
        this._lastDur = dur;
    },

    startPump: function () {
        if (this._pumpTimer) return;
        var self = this;
        this._pumpTimer = setInterval(function () { self._pumpTick(); }, 250);
    },

    stopPump: function () {
        if (this._pumpTimer) { clearInterval(this._pumpTimer); this._pumpTimer = null; }
    },

    _pumpTick: function () {
        var v = document.getElementById('vp-video');
        if (!v || !v.duration) return;
        if (this._drag) { this._synth = null; return; }

        var dur = (isFinite(v.duration) && v.duration > 0) ? v.duration : this._lastDur;
        var raw = (isFinite(v.currentTime) && v.currentTime > 0) ? v.currentTime : 0;
        var err = (v.error && v.error.code) ? String(v.error.code) : '0';
        var ready = String(v.readyState);
        var net = String(v.networkState);
        var forceNotify = false;

        if (this._pendingRestore > 0 && dur > 0) {
            if (this._pendingRestore <= dur + 2) {
                try { v.currentTime = this._pendingRestore; this._pendingRestore = 0; this._pendingRestoreTries = 0; }
                catch (e) { this._pendingRestoreTries++; }
            } else {
                this._pendingRestoreTries++;
            }
            if (this._pendingRestoreTries > 60) { this._pendingRestore = 0; this._pendingRestoreTries = 0; }
            raw = (isFinite(v.currentTime) && v.currentTime > 0) ? v.currentTime : raw;
        }

        var savedCur = 0;
        if (raw > 0) {
            this._draw(raw, dur);
            this._stuckFrames = 0;
            this._synth = null;
            savedCur = raw;
        } else if (!v.paused && dur > 0) {
            if (this._synth == null) {
                this._synth = { t: this._lastDrawT };
                this._stuckFrames = 0;
            }
            this._synth.t += 0.25 * (v.playbackRate || 1);
            this._draw(this._synth.t, dur);
            this._stuckFrames++;
            if (this._stuckFrames === 3) forceNotify = true;
            savedCur = this._synth.t;
        } else if (this._synth) {
            this._synth = null;
        }

        if (savedCur > 0) this._saveTick(savedCur);
        if (v.ended && this._progressKey) {
            try { localStorage.removeItem(this._progressKey); } catch (e) { }
        }

        this._notifyCounter++;
        if ((this._notifyCounter % 4 === 0) || forceNotify) {
            var cur = raw > 0 ? raw : (this._synth ? this._synth.t : 0);
            try { DotNet.invokeMethodAsync('NMU.Platform.Components', 'OnTimestateFromJs', cur, dur, err, ready, net); } catch (e) { }
        }
    },

    initSeek: function () {
        var self = this;
        var area = document.querySelector('.vp-progress-area');
        if (!area) return;

        if (self._boundMove) window.removeEventListener('mousemove', self._boundMove);
        if (self._boundUp) window.removeEventListener('mouseup', self._boundUp);

        var lastSent = { t: -999, at: 0 };
        var lastScrub = 0;
        var lastT = 0;

        function getDur() {
            var v = document.getElementById('vp-video');
            return (v && isFinite(v.duration) && v.duration > 0) ? v.duration : 0;
        }

        function getPos(e) {
            var curArea = document.querySelector('.vp-progress-area');
            if (!curArea) return lastT;
            var r = curArea.getBoundingClientRect();
            var tch = (e.touches && e.touches.length) ? e.touches[0]
                : (e.changedTouches && e.changedTouches.length) ? e.changedTouches[0]
                : null;
            var x = tch ? tch.clientX : e.clientX;
            if (typeof x !== 'number' || isNaN(x)) return lastT;
            if (x === 0 && (e.touches || e.changedTouches)) return lastT;
            var d = getDur();
            var frac = (r.width > 0) ? Math.max(0, Math.min(1, (x - r.left) / r.width)) : 0;
            return d ? frac * d : 0;
        }

        function scrub(t, now, force) {
            self._draw(t, getDur());
            var v = document.getElementById('vp-video');
            if (!v || !isFinite(t) || t < 0) return;
            if (!force) {
                if (now - lastScrub < 200) return;
                if (!v.paused) return;
                lastScrub = now;
            }
            try { if (Math.abs(v.currentTime - t) > 0.12) v.currentTime = t; } catch (e) { }
        }

        function send(t, committed) {
            var now = Date.now();
            if (!committed && (t === lastSent.t || (now - lastSent.at) < 400)) return;
            lastSent.t = t;
            lastSent.at = now;
            try { DotNet.invokeMethodAsync('NMU.Platform.Components', 'OnSeekFromJs', t, committed); } catch (e) { }
        }

        function down(e) {
            if (e.cancelable) e.preventDefault();
            self._drag = true;
            self._synth = null;
            var t = getPos(e);
            lastT = t;
            scrub(t, Date.now(), false);
            send(t, false);
        }

        function move(e) {
            if (!self._drag) return;
            if (e.cancelable) e.preventDefault();
            var t = getPos(e);
            lastT = t;
            scrub(t, Date.now(), false);
            send(t, false);
        }

        function end(e) {
            if (!self._drag) return;
            self._drag = false;
            var t = getPos(e);
            scrub(t, Number.MAX_SAFE_INTEGER, true);
            send(t, true);
        }

        area.addEventListener('touchstart', down, { passive: false });
        area.addEventListener('touchmove', move, { passive: false });
        area.addEventListener('touchend', end);
        area.addEventListener('touchcancel', end);
        area.addEventListener('mousedown', down);

        self._boundMove = move;
        self._boundUp = end;
        window.addEventListener('mousemove', move);
        window.addEventListener('mouseup', end);
    },

    getState: function () {
        var v = document.getElementById('vp-video');
        if (!v) return '0|0|0|0|0|';
        var err = (v.error && v.error.code) ? String(v.error.code) : '0';
        return String(v.currentTime) + '|' + String(v.duration || 0) + '|' + err + '|'
            + String(v.readyState) + '|' + String(v.networkState) + '|' + (v.src || '');
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

    restore: function (t) {
        if (!isFinite(parseFloat(t)) || parseFloat(t) <= 0) return;
        this._pendingRestore = parseFloat(t);
        this._pendingRestoreTries = 0;
    },

    _saveTick: function (cur) {
        if (!this._progressKey || !isFinite(cur) || cur <= 0) return;
        if (Math.abs(cur - this._lastSaved) >= 5 || cur < this._lastSaved) {
            this._lastSaved = cur;
            try { localStorage.setItem(this._progressKey, String(cur)); } catch (e) { }
        }
    },

    destroy: function () {
        if (this._boundMove) { window.removeEventListener('mousemove', this._boundMove); this._boundMove = null; }
        if (this._boundUp) { window.removeEventListener('mouseup', this._boundUp); this._boundUp = null; }
        var v = document.getElementById('vp-video');
        if (v) {
            if (v.currentTime > 0 && !v.ended && this._progressKey) {
                try { localStorage.setItem(this._progressKey, String(v.currentTime)); } catch (e) { }
            }
            v.pause(); v.src = ''; v.load();
        }
        if (document.pictureInPictureElement) document.exitPictureInPicture();
    }
};