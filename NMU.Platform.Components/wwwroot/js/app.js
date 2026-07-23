// منع أزرار باك وفورورد في المتصفح - التحكم يكون بس من الهيدر
window.addEventListener('popstate', function () {
    var url = location.href;
    history.pushState(null, '', url);
});

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

    fetchQuizContent: function (filePath) {
        var cacheKey = "nmu_q_content_" + filePath;
        var url = "https://archive.org/download/nmu.ce/" + filePath + "?t=" + Date.now();
        return fetch(url, { cache: "no-store" }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (data) {
            try { localStorage.setItem(cacheKey, JSON.stringify(data)); } catch (e) { /* ignore */ }
            return JSON.stringify(data);
        });
    },

    refreshQuizContent: function (filePath) {
        var cacheKey = "nmu_q_content_" + filePath;
        var url = "https://archive.org/download/nmu.ce/" + filePath + "?t=" + Date.now();
        fetch(url, { cache: "no-store" }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (data) {
            try { localStorage.setItem(cacheKey, JSON.stringify(data)); } catch (e) { /* ignore */ }
        }).catch(function () {});
    },

    _iaServerCache: {},

    resolveDirectUrl: function (downloadUrl) {
        var parts = downloadUrl.split('/download/');
        if (parts.length < 2) return Promise.resolve(downloadUrl);
        var rest = parts[1].split('/');
        var itemName = rest[0];
        var filePath = rest.slice(1).join('/');

        // Instant cache check (0ms)
        if (this._iaServerCache[itemName]) {
            return Promise.resolve('https://' + this._iaServerCache[itemName] + '/' + filePath);
        }
        try {
            var cached = sessionStorage.getItem('_ia_server_' + itemName);
            if (cached) {
                this._iaServerCache[itemName] = cached;
                return Promise.resolve('https://' + cached + '/' + filePath);
            }
        } catch (e) { }

        var self = this;
        var controller = new AbortController();
        var timer = setTimeout(function () { controller.abort(); }, 3000);

        return fetch('https://archive.org/metadata/' + itemName, { signal: controller.signal })
            .then(function (r) {
                clearTimeout(timer);
                return r.json();
            })
            .then(function (data) {
                var server = data.d1 || (data.workable_servers && data.workable_servers[0]) || 'ia800100.us.archive.org';
                var dir = data.dir || '';
                var cleanDir = dir.startsWith('/') ? dir : '/' + dir;
                var serverPath = server + cleanDir;
                self._iaServerCache[itemName] = serverPath;
                try { sessionStorage.setItem('_ia_server_' + itemName, serverPath); } catch (e) { }
                return 'https://' + serverPath + '/' + filePath;
            })
            .catch(function () {
                clearTimeout(timer);
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
        return Promise.reject();
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

    getFingerprint: async function () {
        try {
            const fp = await FingerprintJS.load();
            const result = await fp.get();
            return result.visitorId;
        } catch (e) {
            return 'unknown-' + Date.now();
        }
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
    },

    _pdfDbPromise: null,

    _openPdfDb: function () {
        if (!this._pdfDbPromise) {
            this._pdfDbPromise = new Promise(function (resolve, reject) {
                var request = indexedDB.open('nmu-pdf-cache', 1);
                request.onupgradeneeded = function (e) {
                    var db = e.target.result;
                    if (!db.objectStoreNames.contains('pdfs')) {
                        db.createObjectStore('pdfs', { keyPath: 'key' });
                    }
                };
                request.onsuccess = function (e) { resolve(e.target.result); };
                request.onerror = function (e) { reject(e.target.error); };
            });
        }
        return this._pdfDbPromise;
    },

    // Get cached PDF bytes as Uint8Array (byte[] in C#)
    getCachedPdfBytes: function (pdfKey) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    if (get.result) {
                        resolve(new Uint8Array(get.result.data));
                    } else {
                        resolve(null);
                    }
                };
                get.onerror = function () { resolve(null); };
            });
        });
    },

    // Get cached PDF Blob URL directly (for Web path fast load)
    getCachedPdfBlobUrl: function (pdfKey) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    if (get.result && get.result.data) {
                        var blob = new Blob([get.result.data], { type: 'application/pdf' });
                        resolve(URL.createObjectURL(blob));
                    } else {
                        resolve('');
                    }
                };
                get.onerror = function () { resolve(''); };
            });
        });
    },

    // Store PDF bytes from Uint8Array (byte[] in C#)
    setCachedPdfBytes: function (pdfKey, bytes) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readwrite');
                var store = tx.objectStore('pdfs');
                store.put({ key: pdfKey, data: bytes.buffer, timestamp: Date.now() });
                tx.oncomplete = resolve;
                tx.onerror = function () { resolve(); };
            });
        });
    },

    // Get all cached PDF keys for status indicators (keys only, no cursor iteration)
    getCachedPdfKeys: function () {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var req = store.getAllKeys();
                req.onsuccess = function () {
                    var keys = [];
                    for (var i = 0; i < req.result.length; i++) {
                        if (req.result[i].startsWith('pdf_')) keys.push(req.result[i]);
                    }
                    resolve(keys);
                };
                req.onerror = function () { resolve([]); };
            });
        });
    },

    // Check online status
    isOnline: function () {
        return navigator.onLine;
    },

    // Render PDF from Uint8Array (byte[] from C#) using PDF.js
    renderPdfWithPdfJsFromBytes: function (bytes) {
        var self = this;
        self._ensurePdfJs().then(function () {
            var container = document.getElementById('pdf-pages');
            var overlay = document.getElementById('pdf-loading-overlay');
            if (!container) return;
            container.innerHTML = '';
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

    // Check if PDF exists in cache (for Canvas path)
    checkPdfCache: function (pdfKey) {
        var self = this;
        return self._openPdfDb().then(function (db) {
            return new Promise(function (resolve) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    resolve(!!(get.result && get.result.data));
                };
                get.onerror = function () { resolve(false); };
            });
        });
    },

    // Canvas path: render PDF from cache (container must already exist)
    renderPdfFromCache: function (pdfKey) {
        var self = this;
        return self._openPdfDb().then(function (db) {
            return new Promise(function (resolve) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    if (get.result && get.result.data) {
                        self.renderPdfWithPdfJsFromBytes(new Uint8Array(get.result.data));
                    }
                    resolve();
                };
                get.onerror = function () { resolve(); };
            });
        });
    },

    // Create blob URL from Uint8Array (byte[] from C#)
    createBlobUrlFromBytes: function (bytes, mimeType) {
        var blob = new Blob([bytes], { type: mimeType });
        return URL.createObjectURL(blob);
    },

    // Web path: check cache (instant), or fetch + cache + return blob URL
    fetchPdfAsBlobWithCache: function (pdfKey, url) {
        var self = this;
        return self._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    if (get.result) {
                        var blob = new Blob([get.result.data], { type: 'application/pdf' });
                        resolve(URL.createObjectURL(blob));
                    } else {
                        resolve('');
                    }
                };
                get.onerror = function () { resolve(''); };
            });
        }).then(function (blobUrl) {
            if (blobUrl) return blobUrl;
            return self._fetchPdfBytes(url).then(function (buf) {
                if (!buf) return '';
                // Await cache write before returning
                return self._openPdfDb().then(function (db) {
                    return new Promise(function (resolve, reject) {
                        var tx = db.transaction('pdfs', 'readwrite');
                        var store = tx.objectStore('pdfs');
                        store.put({ key: pdfKey, data: buf, timestamp: Date.now() });
                        tx.oncomplete = function () {
                            var blob = new Blob([buf], { type: 'application/pdf' });
                            resolve(URL.createObjectURL(blob));
                        };
                        tx.onerror = function () {
                            // Cache write failed, still return blob URL
                            var blob = new Blob([buf], { type: 'application/pdf' });
                            resolve(URL.createObjectURL(blob));
                        };
                    });
                });
            });
        });
    },

    // Web path: cache PDF in background without blocking (for direct-URL display)
    cachePdfInBackground: function (pdfKey, url) {
        var self = this;
        // Skip if already cached
        self._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () { resolve(!!get.result); };
                get.onerror = function () { resolve(false); };
            });
        }).then(function (cached) {
            if (cached) return;
            return self._fetchPdfBytes(url).then(function (buf) {
                if (!buf) return;
                return self._openPdfDb().then(function (db) {
                    return new Promise(function (resolve, reject) {
                        var tx = db.transaction('pdfs', 'readwrite');
                        var store = tx.objectStore('pdfs');
                        store.put({ key: pdfKey, data: buf, timestamp: Date.now() });
                        tx.oncomplete = resolve;
                        tx.onerror = resolve;
                    });
                });
            });
        });
    },

    // Safe localStorage wrapper - falls back to IndexedDB when blocked by tracking prevention
    _lsAvail: (function () {
        try { localStorage.setItem('_t_', '1'); localStorage.removeItem('_t_'); return true; } catch (e) { return false; }
    })(),

    safeGetItem: function (key) {
        if (this._lsAvail) {
            try {
                var val = localStorage.getItem(key);
                if (val !== null) return val;
            } catch (e) {}
        }
        return this.getCacheItem(key);
    },

    safeSetItem: function (key, value) {
        if (this._lsAvail) {
            try { localStorage.setItem(key, value); return; } catch (e) {}
        }
        this.setCacheItem(key, value);
    },

    safeRemoveItem: function (key) {
        if (this._lsAvail) {
            try { localStorage.removeItem(key); return; } catch (e) {}
        }
        this.removeCacheItem(key);
    },

    // Generic IndexedDB cache (for metadata, not PDFs)
    setCacheItem: function (key, value) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readwrite');
                var store = tx.objectStore('pdfs');
                store.put({ key: '_meta_' + key, data: value, timestamp: Date.now() });
                tx.oncomplete = resolve;
                tx.onerror = function () { resolve(); };
            });
        });
    },

    getCacheItem: function (key) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get('_meta_' + key);
                get.onsuccess = function () {
                    if (get.result) {
                        resolve(get.result.data);
                    } else {
                        resolve('');
                    }
                };
                get.onerror = function () { resolve(''); };
            });
        });
    },

    removeCacheItem: function (key) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readwrite');
                var store = tx.objectStore('pdfs');
                store.delete('_meta_' + key);
                tx.oncomplete = resolve;
                tx.onerror = function () { resolve(); };
            });
        });
    },

    // Try local /api/proxy first, then direct fetch / external proxies
    _fetchPdfBytes: function (url) {
        var self = this;
        var localProxy = '/api/proxy?url=' + encodeURIComponent(url);
        var proxies = [
            'https://api.codetabs.com/v1/proxy?quest=' + encodeURIComponent(url),
            'https://corsproxy.io/?url=' + encodeURIComponent(url)
        ];

        function fetchWithTimeout(fetchUrl, ms) {
            var controller = new AbortController();
            var timer = setTimeout(function () { controller.abort(); }, ms || 5000);
            return fetch(fetchUrl, { signal: controller.signal })
                .then(function (r) {
                    clearTimeout(timer);
                    if (!r.ok) throw new Error('HTTP ' + r.status);
                    return r.arrayBuffer();
                })
                .catch(function (err) {
                    clearTimeout(timer);
                    throw err;
                });
        }

        function attempt(idx) {
            if (idx >= proxies.length) return Promise.resolve(null);
            return fetchWithTimeout(proxies[idx], 4000)
                .then(function (buf) {
                    if (!buf || buf.byteLength === 0) throw new Error('empty');
                    return buf;
                })
                .catch(function () {
                    return attempt(idx + 1);
                });
        }

        // Try local backend proxy first (blazing fast, no CORS issues)
        return fetchWithTimeout(localProxy, 5000)
            .then(function (buf) {
                if (!buf || buf.byteLength === 0) throw new Error('empty');
                return buf;
            })
            .catch(function () {
                // Fallback to direct fetch
                return fetchWithTimeout(url, 3000)
                    .then(function (buf) {
                        if (!buf || buf.byteLength === 0) throw new Error('empty');
                        return buf;
                    })
                    .catch(function () {
                        return attempt(0);
                    });
            });
    }
};

// Quiz iframe message listener
window.__quizMessageHandler = null;
window.nmuRegisterQuizMessageListener = function (dotNetRef) {
    if (window.__quizMessageHandler) {
        window.removeEventListener('message', window.__quizMessageHandler);
    }
    window.__quizMessageHandler = function (e) {
        if (e.data && e.data.type === 'quiz') {
            dotNetRef.invokeMethodAsync('OnQuizMessage', e.data.action);
        }
    };
    window.addEventListener('message', window.__quizMessageHandler);
};
window.nmuRemoveQuizMessageListener = function () {
    if (window.__quizMessageHandler) {
        window.removeEventListener('message', window.__quizMessageHandler);
        window.__quizMessageHandler = null;
    }
};
