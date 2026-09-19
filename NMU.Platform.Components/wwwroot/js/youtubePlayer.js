// Chromeless YouTube player for the app's YouTube section.
//
// The embedded player is created with controls=0 / disablekb / fs=0, so NONE
// of YouTube's own UI is usable: no control bar, no keyboard shortcuts, no
// fullscreen button, minimal branding. A transparent tap layer + a pause cover
// sit above the video, so taps/hover never reach YouTube's watermark/title
// overlays either. Everything the user sees and touches is OUR OWN UI
// (play/pause, seek bar, time, volume, speed, quality, fullscreen).
//
// Blazor only calls init(videoId) / destroy() / toggleFullscreen().
// All playback UI is driven here through the DOM.
window.youtubePlayer = {
    _player: null,
    _videoId: '',
    _progressKey: '',
    _pump: null,
    _queue: null,
    _loadingApi: false,
    _lastActivity: 0,
    _lastSaved: 0,
    _speeds: [1, 1.25, 1.5, 1.75, 2, 2.5, 3, 0.5],
    _speedIdx: 0,
    _drag: false,
    _badgePrimed: false,

    _el: function (id) { return document.getElementById(id); },

    init: function (videoId) {
        var self = this;
        try { this.destroy(); } catch (e) { }
        this._videoId = videoId || '';
        this._progressKey = 'nmu_yt_' + this._videoId;
        this._lastActivity = Date.now();
        this._lastSaved = 0;
        this._speedIdx = 0;
        this._drawSpeed(1);
        this._drag = false;
        this._badgePrimed = false;
        this._hideError();
        this._hideSpinner();
        this._showCover('play');
        this._setPlayIcon(false);
        this._wireStatic();
        this._ensureApi(function () { self._create(); });
    },

    // ---------- YouTube IFrame API loading (chains any previous handler) ----------
    _ensureApi: function (cb) {
        if (window.YT && window.YT.Player) { cb(); return; }
        this._queue = this._queue || [];
        this._queue.push(cb);
        if (this._loadingApi) return;
        this._loadingApi = true;
        var tag = document.createElement('script');
        tag.src = 'https://www.youtube.com/iframe_api';
        document.head.appendChild(tag);
        var prev = window.onYouTubeIframeAPIReady;
        var self = this;
        window.onYouTubeIframeAPIReady = function () {
            if (prev) { try { prev(); } catch (e) { } }
            self._loadingApi = false;
            var q = self._queue || [];
            self._queue = [];
            q.forEach(function (f) { try { f(); } catch (e) { } });
        };
    },

    _create: function () {
        var container = this._el('yt-player-container');
        if (!container) return;
        container.innerHTML = '';
        var self = this;
        try {
            this._player = new YT.Player(container, {
                height: '100%',
                width: '100%',
                videoId: this._videoId,
                playerVars: {
                    'autoplay': 1,
                    'controls': 0,          // no YouTube control bar, ever
                    'disablekb': 1,         // no YouTube keyboard shortcuts
                    'fs': 0,                // no YouTube fullscreen button
                    'rel': 0,
                    'modestbranding': 1,    // minimal YouTube branding
                    'iv_load_policy': 3,    // no video annotations
                    'playsinline': 1
                },
                events: {
                    'onReady': function (e) { self._onReady(e); },
                    'onStateChange': function (e) { self._onState(e.data); },
                    'onPlaybackQualityChange': function (e) { self._onQualityChange(e && e.data); },
                    'onPlaybackRateChange': function (e) { self._onRateChange(e && e.data); },
                    'onError': function () { self._onError(); }
                }
            });
        } catch (e) {
            this._onError();
        }
    },

    _onReady: function () {
        var p = this._player;
        if (!p) return;
        var dur = 0;
        try { dur = p.getDuration() || 0; } catch (e) { }
        var saved = 0;
        try { saved = parseFloat(localStorage.getItem(this._progressKey)) || 0; } catch (e) { }
        if (saved > 0 && dur > 0 && saved < dur - 5) {
            try { p.seekTo(saved, true); } catch (e) { }
            this._lastSaved = saved;
        }
        try { p.unMute(); } catch (e) { }
        try { p.setVolume(100); } catch (e) { }
        var vol = this._el('ytp-volume');
        if (vol) vol.value = 100;
        this._updateMuteIcon(false);
        this._primeQuality();
        this._startPump();
        this._lastActivity = Date.now();
        try { p.playVideo(); } catch (e) { }
        // If autoplay is blocked, the cover stays visible with a play button.
        this._showCover('play');
    },

    _onState: function (s) {
        // YT.PlayerState: -1 unstarted, 0 ended, 1 playing, 2 paused, 3 buffering, 5 cued
        if (s === 1) {
            this._hideSpinner();
            this._hideCover();
            this._setPlayIcon(true);
            this._lastActivity = Date.now();
            this._primeQuality();
        } else if (s === 2) {
            this._hideSpinner();
            this._showCover('play');
            this._setPlayIcon(false);
            this._saveNow();
        } else if (s === 0) {
            this._hideSpinner();
            this._showCover('replay');
            this._setPlayIcon(false);
            try { localStorage.removeItem(this._progressKey); } catch (e) { }
            this._lastSaved = 0;
        } else if (s === 3) {
            this._showSpinner();
        } else {
            this._hideSpinner();
            this._showCover('play');
            this._setPlayIcon(false);
        }
    },

    _onError: function () {
        this._hideSpinner();
        var err = this._el('ytp-error');
        if (err) err.style.display = 'flex';
    },

    _hideError: function () {
        var err = this._el('ytp-error');
        if (err) err.style.display = 'none';
    },

    retry: function () {
        this._hideError();
        this.init(this._videoId);
    },

    openExternal: function () {
        try {
            var fn = (window.nmuFunctions && window.nmuFunctions.openExternal)
                ? window.nmuFunctions.openExternal
                : function (u) { window.open(u, '_blank'); };
            fn('https://www.youtube.com/watch?v=' + this._videoId);
        } catch (e) { }
    },

    // ---------- transport ----------
    _state: function () {
        try { return this._player ? this._player.getPlayerState() : -2; } catch (e) { return -2; }
    },

    _dur: function () {
        try { return (this._player && this._player.getDuration()) || 0; } catch (e) { return 0; }
    },

    play: function () {
        if (!this._player) return;
        try { this._player.playVideo(); } catch (e) { }
    },

    pause: function () {
        if (!this._player) return;
        try { this._player.pauseVideo(); } catch (e) { }
    },

    togglePlay: function () {
        if (this._state() === 1) this.pause();
        else this.play();
    },

    seekFraction: function (f) {
        if (!this._player) return;
        var dur = this._dur();
        if (!(dur > 0) || !isFinite(f)) return;
        try { this._player.seekTo(Math.max(0, Math.min(1, f)) * dur, true); } catch (e) { }
        this._lastActivity = Date.now();
    },

    seekBy: function (sec) {
        if (!this._player || !isFinite(sec)) return;
        var dur = this._dur(), cur = 0;
        try { cur = this._player.getCurrentTime() || 0; } catch (e) { }
        var t = Math.max(0, Math.min(dur > 0 ? dur : cur + sec, cur + sec));
        try { this._player.seekTo(t, true); } catch (e) { }
        this._lastActivity = Date.now();
    },

    setVolume: function (v) {
        if (!this._player) return;
        v = Math.max(0, Math.min(100, parseInt(v, 10) || 0));
        try {
            this._player.setVolume(v);
            if (v > 0 && this._player.isMuted()) this._player.unMute();
        } catch (e) { }
        this._updateMuteIcon(v === 0);
    },

    toggleMute: function () {
        if (!this._player) return;
        try {
            if (this._player.isMuted()) {
                this._player.unMute();
                if (this._player.getVolume() === 0) this._player.setVolume(50);
                var vol = this._el('ytp-volume');
                if (vol) vol.value = this._player.getVolume();
                this._updateMuteIcon(false);
            } else {
                this._player.mute();
                this._updateMuteIcon(true);
            }
        } catch (e) { }
    },

    cycleSpeed: function () {
        if (!this._player) return;
        this._speedIdx = (this._speedIdx + 1) % this._speeds.length;
        var r = this._speeds[this._speedIdx];
        try { this._player.setPlaybackRate(r); } catch (e) { }
        this._drawSpeed(r);
    },

    _drawSpeed: function (r) {
        var btn = this._el('ytp-speed');
        if (btn) btn.textContent = (r + 'x').replace('.0x', 'x');
    },

    // YouTube caps playback at 2x and silently rounds higher requests down.
    // This event reports the REAL applied rate — the button always shows truth.
    _onRateChange: function (r) {
        if (!isFinite(r) || r <= 0) return;
        for (var i = 0; i < this._speeds.length; i++) {
            if (Math.abs(this._speeds[i] - r) < 0.01) { this._speedIdx = i; break; }
        }
        this._drawSpeed(r);
    },

    // QUALITY, HONEST VERSION (verified against Google's docs + IssueTracker
    // "Won't Fix (Intended Behavior)"): embedded players do NOT honor manual
    // quality requests anymore — setPlaybackQuality is a no-op,
    // suggestedQuality is ignored, and setPlaybackQualityRange is routinely
    // overridden by ABR. So there is NO selector that promises control.
    // Instead we show a read-only badge with the ACTUAL current rendition.
    _primeQuality: function () {
        if (this._badgePrimed || !this._player) return;
        this._badgePrimed = true;
        var cur = null;
        try { cur = this._player.getPlaybackQuality(); } catch (e) { }
        if (cur) this._setBadge(cur);
    },

    // Fires whenever the rendition REALLY changes — badge follows reality.
    _onQualityChange: function (q) {
        if (q) this._setBadge(q);
    },

    _setBadge: function (q) {
        var badge = this._el('ytp-quality-badge');
        if (badge) badge.textContent = this._qualityLabel(q);
    },

    _qualityLabel: function (q) {
        var map = {
            highres: '2160p', hd2160: '2160p', hd1440: '1440p', hd1080: '1080p',
            hd720: '720p', large: '480p', medium: '360p', small: '240p', tiny: '144p',
            auto: 'Auto', default: 'Auto'
        };
        return map[q] || q;
    },

    // ---------- progress pump / idle hide / resume ----------
    _startPump: function () {
        this._stopPump();
        var self = this;
        this._pump = setInterval(function () { self._tick(); }, 500);
    },

    _stopPump: function () {
        if (this._pump) { clearInterval(this._pump); this._pump = null; }
    },

    _tick: function () {
        var p = this._player;
        if (!p) return;
        var dur = this._dur(), cur = 0;
        try { cur = p.getCurrentTime() || 0; } catch (e) { }
        if (!this._drag && dur > 0) this._paint(cur / dur, dur);
        this._drawTime(cur, dur);
        var st = this._state();
        var root = this._el('yt-player-body');
        if (root) {
            if (st === 1 && Date.now() - this._lastActivity > 3000) root.classList.add('idle');
            else root.classList.remove('idle');
        }
        if (st === 1 && cur > 5 && dur > 0 && cur < dur - 10 && Math.abs(cur - this._lastSaved) >= 5) {
            this._lastSaved = cur;
            try { localStorage.setItem(this._progressKey, String(cur)); } catch (e) { }
        }
    },

    _saveNow: function () {
        var p = this._player;
        if (!p || !this._progressKey) return;
        try {
            var cur = p.getCurrentTime() || 0;
            var dur = p.getDuration() || 0;
            if (cur > 5 && dur > 0 && cur < dur - 10) localStorage.setItem(this._progressKey, String(cur));
        } catch (e) { }
    },

    _paint: function (fraction, dur) {
        var f = Math.max(0, Math.min(1, fraction || 0));
        var bar = this._el('ytp-progress-bar');
        var thumb = this._el('ytp-progress-thumb');
        if (bar) bar.style.width = (f * 100).toFixed(2) + '%';
        if (thumb) thumb.style.left = (f * 100).toFixed(2) + '%';
    },

    _fmt: function (t) {
        if (!isFinite(t) || t < 0) t = 0;
        t = Math.floor(t);
        var h = Math.floor(t / 3600), m = Math.floor((t % 3600) / 60), s = t % 60;
        var mm = h > 0 ? String(m).padStart(2, '0') : String(m);
        var ss = String(s).padStart(2, '0');
        return h > 0 ? h + ':' + mm + ':' + ss : mm + ':' + ss;
    },

    _drawTime: function (cur, dur) {
        var c = this._el('ytp-time-current');
        var d = this._el('ytp-time-duration');
        if (c) c.textContent = this._fmt(cur);
        if (d) d.textContent = this._fmt(dur);
    },

    // ---------- center indicator / buffering (vp design system) ----------
    _showCenter: function (mode) {
        var el = this._el('ytp-center-play');
        var icon = this._el('ytp-center-icon');
        if (el) el.style.opacity = '1';
        if (icon) icon.className = mode === 'replay' ? 'fa-solid fa-rotate-right' : 'fa-solid fa-play';
    },

    _hideCenter: function () {
        var el = this._el('ytp-center-play');
        if (el) el.style.opacity = '0';
    },

    _showCover: function (mode) { this._showCenter(mode); },
    _hideCover: function () { this._hideCenter(); },

    _showSpinner: function () {
        var sp = this._el('ytp-buffer');
        if (sp) sp.style.display = 'block';
    },

    _hideSpinner: function () {
        var sp = this._el('ytp-buffer');
        if (sp) sp.style.display = 'none';
    },

    _setPlayIcon: function (playing) {
        var icon = this._el('ytp-play-icon');
        if (icon) icon.className = playing ? 'fa-solid fa-pause' : 'fa-solid fa-play';
    },

    _updateMuteIcon: function (muted) {
        var icon = this._el('ytp-mute-icon');
        if (icon) icon.className = muted ? 'fa-solid fa-volume-xmark' : 'fa-solid fa-volume-high';
    },

    _activity: function () {
        this._lastActivity = Date.now();
        var root = this._el('yt-player-body');
        if (root) root.classList.remove('idle');
    },

    // ---------- static wiring (property assignment: safe to repeat) ----------
    _wireStatic: function () {
        var self = this;
        var tap = this._el('ytp-tap');
        if (tap) tap.onclick = function () { self._activity(); self.togglePlay(); };
        var playBtn = this._el('ytp-play');
        if (playBtn) playBtn.onclick = function () { self._activity(); self.togglePlay(); };
        var muteBtn = this._el('ytp-mute');
        if (muteBtn) muteBtn.onclick = function () { self._activity(); self.toggleMute(); };
        var vol = this._el('ytp-volume');
        if (vol) vol.oninput = function () { self._activity(); self.setVolume(vol.value); };
        var speed = this._el('ytp-speed');
        if (speed) speed.onclick = function () { self._activity(); self.cycleSpeed(); };
        var full = this._el('ytp-full');
        if (full) full.onclick = function () { self._activity(); self.toggleFullscreen(); };
        var retry = this._el('ytp-retry');
        if (retry) retry.onclick = function () { self.retry(); };
        var errOpen = this._el('ytp-error-open');
        if (errOpen) errOpen.onclick = function () { self.openExternal(); };
        var rowOpen = this._el('ytp-open');
        if (rowOpen) rowOpen.onclick = function () { self._activity(); self.openExternal(); };
        var back10 = this._el('ytp-back10');
        if (back10) back10.onclick = function () { self._activity(); self.seekBy(-10); };
        var fw10 = this._el('ytp-fw10');
        if (fw10) fw10.onclick = function () { self._activity(); self.seekBy(10); };
        var area = this._el('ytp-progress-area');
        if (area && !area._ytpWired) { area._ytpWired = true; this._wireSeek(area); }
        var root = this._el('yt-player-body');
        if (root && !root._ytpWired) {
            root._ytpWired = true;
            root.addEventListener('pointermove', function () { self._activity(); });
            root.addEventListener('pointerdown', function () { self._activity(); });
            root.addEventListener('touchstart', function () { self._activity(); }, { passive: true });
        }
    },

    _wireSeek: function (area) {
        var self = this;
        function frac(e) {
            var r = area.getBoundingClientRect();
            if (!r || r.width <= 0) return null;
            var x = (e.touches && e.touches[0]) ? e.touches[0].clientX : e.clientX;
            if (typeof x !== 'number' || isNaN(x)) return null;
            return Math.max(0, Math.min(1, (x - r.left) / r.width));
        }
        area.addEventListener('pointerdown', function (e) {
            self._drag = true;
            try { area.setPointerCapture(e.pointerId); } catch (err) { }
            self._activity();
            var f = frac(e);
            if (f !== null) self._paint(f, self._dur());
            e.preventDefault();
        });
        area.addEventListener('pointermove', function (e) {
            if (!self._drag) return;
            var f = frac(e);
            if (f !== null) self._paint(f, self._dur());
            e.preventDefault();
        });
        function end(e) {
            if (!self._drag) return;
            self._drag = false;
            var f = frac(e);
            if (f !== null) self.seekFraction(f);
        }
        area.addEventListener('pointerup', end);
        area.addEventListener('pointercancel', function () { self._drag = false; });
    },

    toggleFullscreen: function () {
        var el = document.getElementById('yt-player-body');
        if (!el) return;
        try {
            if (!document.fullscreenElement) {
                if (el.requestFullscreen) el.requestFullscreen();
            } else {
                document.exitFullscreen();
            }
        } catch (e) { }
    },

    destroy: function () {
        this._stopPump();
        try { this._saveNow(); } catch (e) { }
        if (this._player) {
            try { this._player.destroy(); } catch (e) { }
        }
        this._player = null;
        this._videoId = '';
        var container = this._el('yt-player-container');
        if (container) container.innerHTML = '';
        var root = this._el('yt-player-body');
        if (root) root.classList.remove('idle');
    }
};
