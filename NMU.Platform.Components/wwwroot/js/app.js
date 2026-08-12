// --- Back System ---
// The app owns navigation. Every internal navigation uses replaceState, so the
// browser history NEVER accumulates entries for app pages. As long as the app
// is the first page opened in the tab (history.length === 1), the browser's
// native back button stays disabled (grayed out) on its own — a web page
// cannot disable the browser chrome button directly.
//
// Browser back/forward is additionally neutralized here: any popstate that
// still fires (e.g. a leftover entry from before the app loaded) is swallowed
// in the capture phase before Blazor's own router sees it. Desktop restores in
// place with history.go(1) — moving back to the same entry WITHOUT adding a new
// one (unlike history.pushState, which duplicates the URL on every press).
// Mobile web instead re-pushes a live entry on top of the guard and drives the
// app's own hierarchical back (GetBackTarget), the single source of truth.
//
// Real back navigation is driven only by the header button and the Android
// hardware back key (__goBack → nmu-goback → HandleGoBack).

window.addEventListener('popstate', function (e) {
    e.stopImmediatePropagation();
    if (window.__nmuRestoring) { window.__nmuRestoring = false; return; }
    if (window.__nmuWebBackRef) {
        // Mobile web: cancel the browser's back the same reliable way as
        // desktop — go one step FORWARD with history.go(1). The browser moves
        // back onto the live entry, so the guard below is never touched and a
        // later replaceState can never overwrite it (pushState inside this
        // handler was unreliable: some mobile browsers drop it mid-gesture, the
        // guard got replaced, and the next back press exited the site). Drive
        // the app's own hierarchical back (HandleGoBack) only AFTER the forward
        // jump has settled, so Blazor's NavigateTo(replace) replaces the live
        // entry — not the guard — and the URL stays correct. Navigation.Uri was
        // never changed (popstate is swallowed), so GetBackTarget still knows
        // the page we were on.
        window.__nmuRestoring = true;
        history.go(1);
        setTimeout(function () { window.__nmuRestoring = false; }, 500);
        setTimeout(function () {
            window.__nmuWebBackRef.invokeMethodAsync('HandleGoBack');
        }, 200);
    } else {
        // Desktop web: swallow back and restore position (button stays dead).
        window.__nmuRestoring = true;
        history.go(1);
        setTimeout(function () { window.__nmuRestoring = false; }, 500);
    }
}, true);

// Guarantee a guard entry always sits below the app's current page, so the
// browser back can never step off the site. Call after every internal
// navigation (replace:true would otherwise overwrite the guard).
window.nmuEnsureWebBackTrap = function () {
    if (!window.__nmuWebBackRef) return;
    if (history.length < 2) {
        history.pushState({ nmuWebBack: true }, '', location.href);
    }
};

// Detect mobile web so the back trap is only enabled there (desktop keeps the
// grayed-out / dead browser back button).
window.nmuIsMobile = function () {
    var ua = navigator.userAgent || '';
    if (/(iPad|iPhone|iPod)/i.test(ua)) return true;
    if (/Android/i.test(ua) && /Mobile/i.test(ua)) return true;
    return /Mobile|Opera Mini|IEMobile|webOS|BlackBerry/i.test(ua);
};

// Mobile web: build a permanent guard below the app's live page.
//   replaceState: turn the CURRENT entry into the guard (same URL).
//   pushState:    put a live page on top of it.
// From then on, every internal navigation uses replace:true, so it replaces the
// TOP entry only — the guard survives below forever. A browser-back press pops
// to the guard (same-origin, never the external page the user arrived from),
// the popstate handler cancels it (history.go(1)) and drives HandleGoBack —
// the same hierarchical back as the Android app.
window.nmuEnableWebBack = function (dotNetRef) {
    window.__nmuWebBackRef = dotNetRef;
    if (window.__nmuWebBackWired) return;
    window.__nmuWebBackWired = true;
    history.replaceState({ nmuWebBack: true }, '', location.href);
    history.pushState(null, '', location.href);
};

window.__goBack = function() {
    window.dispatchEvent(new CustomEvent('nmu-goback'));
};

window.nmuAddGoBackListener = function(dotNetRef) {
    window.__nmuGoBackRef = dotNetRef;
    if (window.__nmuGoBackWired) return;
    window.__nmuGoBackWired = true;

    // Android hardware back (MainPage fires __goBack → nmu-goback)
    window.addEventListener('nmu-goback', function () {
        if (window.__nmuGoBackRef) {
            window.__nmuGoBackRef.invokeMethodAsync('HandleGoBack');
        }
    });
};

// Intercept internal <a> clicks so Blazor navigates with replaceState instead
// of pushState. This keeps the browser history at a single entry, so the
// browser's native back button stays disabled (grayed out).
window.nmuWireInternalLinks = function (dotNetRef) {
    window.__nmuLinkRef = dotNetRef;
    if (window.__nmuLinkWired) return;
    window.__nmuLinkWired = true;

    document.addEventListener('click', function (e) {
        if (e.defaultPrevented) return;
        // Let modified clicks (new tab / new window) keep native browser behavior.
        if (e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var a = e.target && e.target.closest ? e.target.closest('a') : null;
        if (!a) return;
        var href = a.getAttribute('href');
        if (!href) return;

        // Leave external links, new-tab links, downloads and special schemes alone.
        if (a.target === '_blank' || a.hasAttribute('download')) return;
        if (href.startsWith('#') || href.startsWith('mailto:') || href.startsWith('tel:') || href.startsWith('javascript:')) return;

        var url;
        try { url = new URL(href, location.href); } catch (err) { return; }
        if (url.origin !== location.origin) return;

        e.preventDefault();
        e.stopImmediatePropagation();
        if (window.__nmuLinkRef) {
            window.__nmuLinkRef.invokeMethodAsync('HandleInternalLink', url.pathname + url.search + url.hash);
        }
    }, true); // capture phase on document: runs before Blazor's window-level navigation handler
};

window.nmuFunctions = {
    // Open an external URL in a new tab. Keeping the app in the current tab
    // means the browser history never gains an entry for the app itself.
    openExternal: function (url) {
        window.open(url, '_blank', 'noopener');
    },

    // Hard-reload a URL without adding a browser history entry
    // (location.replace rewrites the current entry instead of pushing a new one).
    reloadWithoutHistory: function (url) {
        window.location.replace(url || window.location.href);
    },

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

    // Store PDF bytes from Uint8Array (byte[] in C#), with optional HTTP metadata
    setCachedPdfBytes: function (pdfKey, bytes, contentLength, etag, lastModified) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve, reject) {
                var tx = db.transaction('pdfs', 'readwrite');
                var store = tx.objectStore('pdfs');
                store.put({
                    key: pdfKey,
                    data: bytes.buffer,
                    contentLength: contentLength || bytes.buffer.byteLength,
                    etag: etag || '',
                    lastModified: lastModified || '',
                    timestamp: Date.now()
                });
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

    // Get cached PDF metadata (contentLength, etag, lastModified, timestamp)
    getCachedPdfMeta: function (pdfKey) {
        return this._openPdfDb().then(function (db) {
            return new Promise(function (resolve) {
                var tx = db.transaction('pdfs', 'readonly');
                var store = tx.objectStore('pdfs');
                var get = store.get(pdfKey);
                get.onsuccess = function () {
                    if (get.result) {
                        resolve({
                            contentLength: get.result.contentLength || 0,
                            etag: get.result.etag || '',
                            lastModified: get.result.lastModified || '',
                            timestamp: get.result.timestamp || 0
                        });
                    } else {
                        resolve(null);
                    }
                };
                get.onerror = function () { resolve(null); };
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

    // Write to BOTH localStorage (fast reads) and IndexedDB (survives localStorage
    // being cleared / full). Reads via safeGetItem hit localStorage first, then
    // fall back to IndexedDB, so the data survives either way.
    safeSetItemBoth: function (key, value) {
        if (this._lsAvail) {
            try { localStorage.setItem(key, value); } catch (e) {}
        }
        return this.setCacheItem(key, value);
    },

    // Raw text fetch (avoids JSON.parse + JSON.stringify round trip for big payloads)
    fetchText: function (url) {
        return fetch(url).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.text();
        });
    },

    // The full archive.org metadata is ~2.3 MB. It is downloaded ONCE and kept in
    // IndexedDB (high quota) so every feature page (materials / quizzes / recorded)
    // shares the same copy instead of re-downloading it.
    getRawMetadata: function () {
        return this.getCacheItem('raw_meta_nmu.ce');
    },

    setRawMetadata: function (json) {
        return this.setCacheItem('raw_meta_nmu.ce', json);
    },

    // Returns the raw metadata, fetching it (and caching it) only when missing.
    // Concurrent callers share the same in-flight fetch.
    ensureRawMetadata: function () {
        var self = this;
        if (this._rawMetaPromise) return this._rawMetaPromise;
        this._rawMetaPromise = this.getRawMetadata().then(function (cached) {
            if (cached) return cached;
            return self.fetchText('https://archive.org/metadata/nmu.ce').then(function (json) {
                if (json) self.setRawMetadata(json);
                return json;
            });
        });
        // If the fetch fails, reset the promise so a later call retries instead of
        // reusing a rejected (poisoned) promise for the rest of the session.
        this._rawMetaPromise.catch(function () {
            self._rawMetaPromise = null;
        });
        return this._rawMetaPromise;
    },

    // Parse the 2.3 MB metadata ENTIRELY in JS (native JSON.parse) and return only
    // the small {name,size} list for a semester (~150 KB). The big payload never
    // crosses the JS/.NET boundary, so the page doesn't freeze. Result is cached in
    // IndexedDB, so the parse happens only once.
    getSemesterFiles: function (level, semester) {
        var self = this;
        var semCacheKey = 'sem_files_v2_' + level + '_' + semester;
        return this.getCacheItem(semCacheKey).then(function (cached) {
            if (cached) return cached;
            return self.ensureRawMetadata().then(function (json) {
                if (!json) return '';
                var data;
                try { data = JSON.parse(json); } catch (e) { return ''; }
                var prefix = 'NMU/' + level + '/' + semester + '/';
                var out = [];
                var files = data.files || [];
                for (var i = 0; i < files.length; i++) {
                    var f = files[i];
                    if (f.name && f.name.indexOf(prefix) === 0) {
                        var sz = f.size;
                        var n = (sz === undefined || sz === null || sz === '') ? null : Number(sz);
                        // PascalCase keys match the ArchiveFile model exactly (STJ is
                        // case-sensitive by default), so no silent empty objects.
                        out.push({ Name: f.name, Size: (n === null || isNaN(n)) ? null : n });
                    }
                }
                var result = JSON.stringify(out);
                self.setCacheItem(semCacheKey, result);
                return result;
            });
        }).catch(function () {
            return '';
        });
    },

    // Same idea for the QUIZE folder list used by the quiz pages.
    getQuizFiles: function (level, semester) {
        return this.getSemesterFiles(level, semester).then(function (json) {
            if (!json) return '';
            var data;
            try { data = JSON.parse(json); } catch (e) { return ''; }
            var out = [];
            for (var i = 0; i < data.length; i++) {
                if (data[i].Name.indexOf('/QUIZE/') !== -1) out.push(data[i].Name);
            }
            return JSON.stringify(out);
        });
    },

    // Build the catalog of every subject that exists in the archive (PDF folders)
    // across ALL levels and semesters. Used by the custom-subjects picker so a
    // credit-hours student can pin subjects from any level/semester. Parsed in JS
    // from the shared cached metadata; result cached in IndexedDB.
    getSubjectCatalog: function () {
        var self = this;
        var catCacheKey = 'subject_catalog_v1';
        return this.getCacheItem(catCacheKey).then(function (cached) {
            if (cached) return cached;
            return self.ensureRawMetadata().then(function (json) {
                if (!json) return '';
                var data;
                try { data = JSON.parse(json); } catch (e) { return ''; }
                var files = data.files || [];
                var out = [];
                var seen = {};
                for (var i = 0; i < files.length; i++) {
                    var nm = files[i].name || '';
                    var m = /^NMU\/([^/]+)\/([^/]+)\/PDF\/([^/]+)\//.exec(nm);
                    if (!m) continue;
                    var key = m[1] + '|' + m[2] + '|' + m[3];
                    if (seen[key]) continue;
                    seen[key] = true;
                    out.push({ Level: m[1], Semester: m[2], Subject: m[3] });
                }
                var result = JSON.stringify(out);
                self.setCacheItem(catCacheKey, result);
                return result;
            });
        }).catch(function () {
            return '';
        });
    },

    // Same idea for the RECORDED_LECTURER folders used by the recorded lectures
    // pages. Parses the big metadata entirely in JS, resolves the thumbnail name
    // for each video (same logic as the old .NET path), and returns a compact
    // PascalCase list matching the RecordedFile model exactly.
    getRecordedFiles: function (level, semester) {
        var self = this;
        var recCacheKey = 'rec_files_v2_' + level + '_' + semester;
        return this.getCacheItem(recCacheKey).then(function (cached) {
            if (cached) return cached;
            return self.ensureRawMetadata().then(function (json) {
                if (!json) return '';
                var data;
                try { data = JSON.parse(json); } catch (e) { return ''; }
                var files = data.files || [];
                var prefix1 = 'NMU/' + level + '/' + semester + '/RECORDED_LECTURER/';
                var prefix2 = 'NMU/' + level + '/' + semester + '/RECORDED LECTURER/';
                var thumbPrefix1 = 'nmu.ce.thumbs/' + prefix1;
                var thumbPrefix2 = 'nmu.ce.thumbs/' + prefix2;

                // Collect thumb names (video frame previews) for thumbnail matching.
                var thumbs = [];
                for (var i = 0; i < files.length; i++) {
                    var nm = files[i].name || '';
                    if (nm.indexOf(thumbPrefix1) !== 0 && nm.indexOf(thumbPrefix2) !== 0) continue;
                    if (nm.slice(-4).toLowerCase() !== '.jpg') continue;
                    thumbs.push(nm);
                }

                var out = [];
                for (var i = 0; i < files.length; i++) {
                    var f = files[i];
                    var nm = f.name || '';
                    if (nm.indexOf(prefix1) !== 0 && nm.indexOf(prefix2) !== 0) continue;
                    var lower = nm.toLowerCase();
                    var fileNoExt = nm.slice(nm.lastIndexOf('/') + 1);
                    var dot = fileNoExt.lastIndexOf('.');
                    if (dot > 0) fileNoExt = fileNoExt.slice(0, dot);
                    var thumb = '';
                    for (var j = 0; j < thumbs.length; j++) {
                        if (thumbs[j].indexOf(fileNoExt) !== -1) { thumb = thumbs[j]; break; }
                    }
                    var sz = f.size;
                    var n = (sz === undefined || sz === null || sz === '') ? null : Number(sz);
                    out.push({
                        Name: nm,
                        Size: (n === null || isNaN(n)) ? null : n,
                        ThumbName: thumb || null,
                        IsAudio: lower.endsWith('.mp3') || lower.endsWith('.wav') || lower.endsWith('.m4a')
                    });
                }
                var result = JSON.stringify(out);
                self.setCacheItem(recCacheKey, result);
                return result;
            });
        }).catch(function () {
            return '';
        });
    },

    // Pre-fetch + pre-parse the metadata for a semester during app startup, so the
    // first open of Materials/Quizzes is instant. No-op (fast) once cached.
    prefetchSemesterFiles: function (level, semester) {
        return this.getSemesterFiles(level, semester).then(function () {
            return true;
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
     },

     // ---- Cache Clearing Functions ----

     // Clear PDF cache only (keys starting with "pdf_" in IndexedDB)
     clearPdfCache: function () {
         return this._openPdfDb().then(function (db) {
             return new Promise(function (resolve) {
                 var tx = db.transaction('pdfs', 'readwrite');
                 var store = tx.objectStore('pdfs');
                 var req = store.getAllKeys();
                 req.onsuccess = function () {
                     var keys = req.result || [];
                     var deleted = 0;
                     keys.forEach(function (k) {
                         if (k.startsWith('pdf_')) {
                             store.delete(k);
                             deleted++;
                         }
                     });
                     tx.oncomplete = function () { resolve(deleted); };
                     tx.onerror = function () { resolve(0); };
                 };
                 req.onerror = function () { resolve(0); };
             });
         });
     },

      // Get all localStorage keys matching a prefix (for cleanup detection)
      getKeysByPrefix: function (prefix) {
          var keys = [];
          if (this._lsAvail) {
              try {
                  for (var i = 0; i < localStorage.length; i++) {
                      var key = localStorage.key(i);
                      if (key && key.startsWith(prefix)) keys.push(key);
                  }
              } catch (e) {}
          }
          return keys;
      },

      // Remove multiple localStorage keys at once
      removeKeys: function (keys) {
          if (this._lsAvail) {
              try {
                  for (var i = 0; i < keys.length; i++) {
                      localStorage.removeItem(keys[i]);
                  }
              } catch (e) {}
          }
      },

      // Get all unique level/semester identifiers from cached quiz data (for cleanup detection)
      getCachedLevelSemesters: function () {
          var self = this;
          var result = [];
          var keys = [];
          if (self._lsAvail) {
              try {
                  for (var i = 0; i < localStorage.length; i++) {
                      var key = localStorage.key(i);
                      if (key && key.startsWith('nmu_quiz_list_') || key.startsWith('nmu_q_content_') || key.startsWith('nmu_quiz_sync_done_')) {
                          keys.push(key);
                      }
                  }
              } catch (e) {}
          }
          // Extract level_semester from keys
          var seen = {};
          keys.forEach(function (k) {
              // Pattern: nmu_quiz_list_Level_1_Semester_1_v4 or nmu_q_content_NMU/Level_1/Semester_1/...
              var match = k.match(/(Level_\d+)_(Semester_\d+)/i);
              if (match) {
                  var key = match[1] + '_' + match[2];
                  if (!seen[key]) { seen[key] = true; result.push(key); }
              }
          });
          return result;
      },

      // Clear Quiz cache (localStorage keys related to quizzes)
      clearQuizCache: function () {
         var self = this;
         var quizPrefixes = ['nmu_quiz_list_', 'nmu_q_content_', 'nmu_q_meta_', 'nmu_quiz_sync_done_', 'nmu_quiz_path_map'];
         var count = 0;
         quizPrefixes.forEach(function (prefix) {
             // Try localStorage
             if (self._lsAvail) {
                 try {
                     var toRemove = [];
                     for (var i = 0; i < localStorage.length; i++) {
                         var key = localStorage.key(i);
                         if (key && key.startsWith(prefix)) {
                             toRemove.push(key);
                         }
                     }
                     toRemove.forEach(function (k) { localStorage.removeItem(k); count++; });
                 } catch (e) {}
             }
             // Also try IndexedDB fallback (keys stored as '_meta_' + key)
             self._openPdfDb().then(function (db) {
                 return new Promise(function (resolve) {
                     var tx = db.transaction('pdfs', 'readwrite');
                     var store = tx.objectStore('pdfs');
                     var req = store.getAllKeys();
                     req.onsuccess = function () {
                         var keys = req.result || [];
                         keys.forEach(function (k) {
                             if (k.startsWith('_meta_' + prefix) || k.startsWith('_meta_nmu_q_')) {
                                 store.delete(k);
                                 count++;
                             }
                         });
                         resolve();
                     };
                     req.onerror = function () { resolve(); };
                 });
             });
         });
         return Promise.resolve(count);
     },

      // Smart cleanup: remove cached quiz data for old level/semester when student changes
      cleanupOldQuizCache: function (oldLevel, oldSemester, newLevel, newSemester) {
          var self = this;
          var oldLevelClean = oldLevel.replace(/ /g, '_');
          var oldSemClean = oldSemester.replace(/ /g, '_');
          var prefixes = [
              'nmu_quiz_list_' + oldLevelClean + '_' + oldSemClean,
              'nmu_quiz_sync_done_' + oldLevelClean + '_' + oldSemClean
          ];
          var contentPrefix = 'NMU/' + oldLevelClean + '/' + oldSemClean + '/QUIZE/';

          var removed = 0;
          // Remove localStorage keys
          if (self._lsAvail) {
              try {
                  var toRemove = [];
                  for (var i = 0; i < localStorage.length; i++) {
                      var key = localStorage.key(i);
                      if (key) {
                          for (var p = 0; p < prefixes.length; p++) {
                              if (key.startsWith(prefixes[p]) || key === 'nmu_q_content_' + contentPrefix) {
                                  toRemove.push(key);
                                  break;
                              }
                          }
                          // Also match content keys with the old path
                          if (key.startsWith('nmu_q_content_') && key.indexOf(contentPrefix) >= 0) {
                              if (toRemove.indexOf(key) < 0) toRemove.push(key);
                          }
                          if (key.startsWith('nmu_q_meta_') && key.indexOf(contentPrefix) >= 0) {
                              if (toRemove.indexOf(key) < 0) toRemove.push(key);
                          }
                      }
                  }
                  toRemove.forEach(function (k) { localStorage.removeItem(k); removed++; });
              } catch (e) {}
          }
          return removed;
      },

      // Clear Video/Audio cache only (keys starting with "mch_", "mm_", "mc_" in IndexedDB)
      clearMediaCache: function () {
          return this._openPdfDb().then(function (db) {
              return new Promise(function (resolve) {
                  var tx = db.transaction('pdfs', 'readwrite');
                  var store = tx.objectStore('pdfs');
                  var req = store.getAllKeys();
                  req.onsuccess = function () {
                      var keys = req.result || [];
                      var deleted = 0;
                      keys.forEach(function (k) {
                          if (typeof k === 'string' && (k.startsWith('mch_') || k.startsWith('mm_') || k.startsWith('mc_'))) {
                              store.delete(k);
                              deleted++;
                          }
                      });
                      tx.oncomplete = function () { resolve(deleted); };
                      tx.onerror = function () { resolve(0); };
                  };
                  req.onerror = function () { resolve(0); };
              });
          });
      },

      // Clear ALL cache: localStorage + IndexedDB (all stores)
     clearAllCache: function () {
         var self = this;
         // Clear localStorage entirely
         if (self._lsAvail) {
             try { localStorage.clear(); } catch (e) {}
         }
         // Delete entire IndexedDB database
         return new Promise(function (resolve) {
             var deleteReq = indexedDB.deleteDatabase('nmu-pdf-cache');
             deleteReq.onsuccess = function () {
                 self._pdfDbPromise = null;
                 resolve(true);
             };
             deleteReq.onerror = function () { resolve(false); };
             deleteReq.onblocked = function () { resolve(false); };
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
