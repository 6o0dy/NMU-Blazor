// Browser & Android hardware back → يرجع للصفحة السابقة
window.nmuBrowserBack = function() {
    history.back();
};

window.__goBack = function() {
    window.dispatchEvent(new CustomEvent('nmu-goback'));
};

window.nmuAddGoBackListener = function(dotNetRef) {
    window.addEventListener('nmu-goback', function() {
        dotNetRef.invokeMethodAsync('HandleGoBack');
    });
};

window.nmuFunctions = {
    toggleFullScreen: function () {
        const btn = document.querySelector('#fullscreen-btn i');
        if (!document.fullscreenElement) {
            document.documentElement.requestFullscreen?.();
            if (btn) btn.className = 'fas fa-compress';
        } else {
            document.exitFullscreen?.();
            if (btn) btn.className = 'fas fa-expand';
        }
    },

    fetchJson: function (url) {
        return fetch(url).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (data) {
            return JSON.stringify(data);
        });
    },

    resolveDirectUrl: function (downloadUrl) {
        var parts = downloadUrl.split('/download/');
        if (parts.length < 2) return Promise.resolve(downloadUrl);
        var rest = parts[1].split('/');
        var itemName = rest[0];
        var filePath = rest.slice(1).join('/');

        return fetch('https://archive.org/metadata/' + itemName)
            .then(function (r) { return r.json(); })
            .then(function (data) {
                var server = data.d1 || (data.workable_servers && data.workable_servers[0]) || 'ia800100.us.archive.org';
                var dir = data.dir || '';
                var cleanDir = dir.startsWith('/') ? dir : '/' + dir;
                return 'https://' + server + cleanDir + '/' + filePath;
            })
            .catch(function () {
                return downloadUrl;
            });
    },

    enablePinchZoom: function () {
        var meta = document.querySelector('meta[name="viewport"]');
        if (meta) {
            var original = meta.getAttribute('content');
            if (!window.__originalViewport) window.__originalViewport = original;
            meta.setAttribute('content', 'width=device-width, initial-scale=1.0, maximum-scale=5.0, user-scalable=yes');
        }
    },

    disablePinchZoom: function () {
        var meta = document.querySelector('meta[name="viewport"]');
        if (meta && window.__originalViewport) {
            meta.setAttribute('content', window.__originalViewport);
        }
    },

    getUserAgent: function () {
        return navigator.userAgent;
    },

    createBlobUrl: function (base64, mimeType) {
        var byteChars = atob(base64);
        var byteNums = new Array(byteChars.length);
        for (var i = 0; i < byteChars.length; i++) {
            byteNums[i] = byteChars.charCodeAt(i);
        }
        var byteArray = new Uint8Array(byteNums);
        var blob = new Blob([byteArray], { type: mimeType });
        return URL.createObjectURL(blob);
    },

    _pdfJsReady: null,

    _ensurePdfJs: function () {
        if (window.pdfjsLib) return Promise.resolve();
        if (this._pdfJsReady) return this._pdfJsReady;
        this._pdfJsReady = new Promise(function (resolve, reject) {
            var s = document.createElement('script');
            s.src = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.min.js';
            s.onload = function () {
                pdfjsLib.GlobalWorkerOptions.workerSrc = 'https://cdnjs.cloudflare.com/ajax/libs/pdf.js/3.11.174/pdf.worker.min.js';
                resolve();
            };
            s.onerror = reject;
            document.head.appendChild(s);
        });
        return this._pdfJsReady;
    },

    renderPdfWithPdfJs: function (base64) {
        var self = this;
        self._ensurePdfJs().then(function () {
            var container = document.getElementById('pdf-pages');
            var overlay = document.getElementById('pdf-loading-overlay');
            if (!container) return;

            container.innerHTML = '';
            var binary = atob(base64);
            var len = binary.length;
            var bytes = new Uint8Array(len);
            for (var i = 0; i < len; i++) bytes[i] = binary.charCodeAt(i);

            var loadingTask = pdfjsLib.getDocument({ data: bytes });
            loadingTask.promise.then(function (pdf) {
                if (overlay) { overlay.style.display = 'none'; overlay.style.opacity = '0'; }

                for (var pageNum = 1; pageNum <= pdf.numPages; pageNum++) {
                    (function (num) {
                        pdf.getPage(num).then(function (page) {
                            var viewport = page.getViewport({ scale: 1.5 });
                            var canvas = document.createElement('canvas');
                            canvas.className = 'pdf-canvas-page';
                            canvas.height = viewport.height;
                            canvas.width = viewport.width;
                            container.appendChild(canvas);

                            page.render({
                                canvasContext: canvas.getContext('2d'),
                                viewport: viewport
                            });
                        });
                    })(pageNum);
                }
            }).catch(function () {
                if (overlay) overlay.innerHTML = '<div style="color:#ef4444;text-align:center;padding:40px;">Failed to load PDF.</div>';
            });
        }).catch(function () {
            var overlay = document.getElementById('pdf-loading-overlay');
            if (overlay) overlay.innerHTML = '<div style="color:#ef4444;text-align:center;padding:40px;">Failed to load PDF.js library.</div>';
        });
    },

    getViewerUrl: function (downloadUrl) {
        var isAndroid = /Android/i.test(navigator.userAgent);
        var isChrome = /Chrome|Chromium/i.test(navigator.userAgent) || /Google Inc/i.test(navigator.vendor);
        if (!(isAndroid && isChrome)) return Promise.resolve('');
        return this.resolveDirectUrl(downloadUrl).then(function (directUrl) {
            return 'https://docs.google.com/viewer?url=' + encodeURIComponent(directUrl) + '&embedded=true';
        });
    },

    fetchPdfAsBlob: function (url) {
        var proxies = [
            'https://corsproxy.io/?url=' + encodeURIComponent(url),
            'https://api.allorigins.win/raw?url=' + encodeURIComponent(url)
        ];

        return (function attempt(idx) {
            if (idx >= proxies.length) return Promise.resolve('');
            return fetch(proxies[idx])
                .then(function (r) {
                    if (!r.ok) throw new Error();
                    return r.blob();
                })
                .then(function (blob) {
                    if (blob && blob.size > 0) return URL.createObjectURL(blob);
                    throw new Error();
                })
                .catch(function () {
                    return attempt(idx + 1);
                });
        })(0);
    }
};
