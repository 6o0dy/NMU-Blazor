window.youtubePlayer = {
    _player: null,
    _dotNetRef: null,
    _videoId: '',
    _progressKey: '',

    init: function (videoId, dotNetRef) {
        this._videoId = videoId;
        this._dotNetRef = dotNetRef;
        this._progressKey = 'nmu_yt_' + videoId;

        var container = document.getElementById('yt-player-container');
        if (!container) return;
        container.innerHTML = '';

        var self = this;

        if (window.YT && window.YT.Player) {
            self._createPlayer(container);
        } else {
            var tag = document.createElement('script');
            tag.src = 'https://www.youtube.com/iframe_api';
            var firstScript = document.getElementsByTagName('script')[0];
            firstScript.parentNode.insertBefore(tag, firstScript);

            window.onYouTubeIframeAPIReady = function () {
                self._createPlayer(container);
            };
        }
    },

    _createPlayer: function (container) {
        var self = this;
        var savedTime = localStorage.getItem(this._progressKey);
        var startSeconds = (savedTime && parseFloat(savedTime) > 0) ? parseFloat(savedTime) : 0;

        this._player = new YT.Player(container, {
            height: '100%',
            width: '100%',
            videoId: this._videoId,
            playerVars: {
                'playsinline': 1,
                'controls': 0,
                'disablekb': 1,
                'fs': 0,
                'rel': 0,
                'modestbranding': 1,
                'iv_load_policy': 3,
                'start': Math.floor(startSeconds)
            },
            events: {
                'onReady': function (event) {
                    event.target.playVideo();
                    if (self._dotNetRef) {
                        self._dotNetRef.invokeMethodAsync('OnPlayerReady');
                    }
                },
                'onStateChange': function (event) {
                    if (self._dotNetRef) {
                        self._dotNetRef.invokeMethodAsync('OnPlayerStateChange', event.data);
                    }
                }
            }
        });
    },

    play: function () {
        if (this._player && this._player.playVideo) this._player.playVideo();
    },

    pause: function () {
        if (this._player && this._player.pauseVideo) this._player.pauseVideo();
    },

    seekTo: function (seconds) {
        if (this._player && this._player.seekTo) this._player.seekTo(seconds, true);
    },

    setVolume: function (val) {
        if (this._player && this._player.setVolume) this._player.setVolume(val);
    },

    setSpeed: function (rate) {
        if (this._player && this._player.setPlaybackRate) this._player.setPlaybackRate(rate);
    },

    getCurrentTime: function () {
        if (this._player && this._player.getCurrentTime) return this._player.getCurrentTime();
        return 0;
    },

    getDuration: function () {
        if (this._player && this._player.getDuration) return this._player.getDuration();
        return 0;
    },

    saveTime: function (videoId, time) {
        try { localStorage.setItem('nmu_yt_' + videoId, time); } catch (e) { }
    },

    toggleFullscreen: function () {
        var el = document.getElementById('yt-player-body');
        if (!el) return;
        if (!document.fullscreenElement) {
            el.requestFullscreen();
        } else {
            document.exitFullscreen();
        }
    },

    destroy: function () {
        if (this._player && this._player.destroy) {
            try { this._player.destroy(); } catch (e) { }
        }
        this._player = null;
        this._dotNetRef = null;
        var container = document.getElementById('yt-player-container');
        if (container) container.innerHTML = '';
    }
};
