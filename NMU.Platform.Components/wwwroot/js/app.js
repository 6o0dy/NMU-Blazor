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

    // YouTube thumbnail fallback chain: maxresdefault.jpg does NOT exist for
    // every video (404). Step down until one loads: maxres -> sd -> hq -> mq.
    // Usage: <img ... onerror="nmuFunctions.ytImgFallback(this)" />
    ytImgFallback: function (img) {
        if (!img || !img.src) return;
        var order = ['maxresdefault', 'sddefault', 'hqdefault', 'mqdefault', 'default'];
        for (var i = 0; i < order.length - 1; i++) {
            if (img.src.indexOf('/' + order[i] + '.') !== -1) {
                img.src = img.src.replace('/' + order[i] + '.', '/' + order[i + 1] + '.');
                return;
            }
        }
        try { img.onerror = null; } catch (e) { }
    },

    // Hard-reload a URL without adding a browser history entry
    // (location.replace rewrites the current entry instead of pushing a new one).
    reloadWithoutHistory: function (url) {
        window.location.replace(url || window.location.href);
    },

    // ---------- Theme (light / dark), persisted in localStorage ----------
    nmuThemeGet: function () {
        try {
            return localStorage.getItem('nmu_theme') === 'dark' ? 'dark' : 'light';
        } catch (e) { return 'light'; }
    },

    nmuThemeApply: function (theme) {
        var t = (theme === 'dark') ? 'dark' : 'light';
        try {
            if (t === 'dark') document.documentElement.setAttribute('data-theme', 'dark');
            else document.documentElement.removeAttribute('data-theme');
        } catch (e) {}
        try {
            var meta = document.querySelector('meta[name="theme-color"]');
            if (meta) meta.setAttribute('content', t === 'dark' ? '#0a1120' : '#f3f7fb');
        } catch (e) {}
        return t;
    },

    nmuThemeSet: function (theme) {
        var t = this.nmuThemeApply(theme);
        try { localStorage.setItem('nmu_theme', t); } catch (e) {}
        return t;
    },

    nmuThemeToggle: function () {
        return this.nmuThemeSet(this.nmuThemeGet() === 'dark' ? 'light' : 'dark');
    },

    // Auto marquee for file/video titles that overflow their slot (.file-name,
    // .recorded-title). Only overflowing labels animate; short ones stay static.
    // Safe with Blazor: the text node Blazor owns is moved (not replaced), so
    // later re-renders keep updating it inside the wrapper.
    nmuEnableMarquee: function () {
        if (window.__nmuMarqueeWired) return;
        window.__nmuMarqueeWired = true;
        var SELECTOR = '.file-name, .recorded-title';
        var scheduled = false;

        function scan() {
            scheduled = false;
            var els;
            try { els = document.querySelectorAll(SELECTOR); } catch (e) { return; }
            for (var i = 0; i < els.length; i++) {
                (function (el) {
                    try {
                        var overflow = el.scrollWidth - el.clientWidth;
                        var inner = el.querySelector(':scope > .marquee-inner');
                        if (overflow > 8) {
                            if (!inner) {
                                inner = document.createElement('span');
                                inner.className = 'marquee-inner';
                                while (el.firstChild) inner.appendChild(el.firstChild);
                                el.appendChild(inner);
                            }
                            var dist = -(overflow + 16);
                            el.style.setProperty('--marquee-dist', dist + 'px');
                            var dur = Math.min(14, Math.max(5, Math.abs(dist) / 28));
                            el.style.setProperty('--marquee-dur', dur + 's');
                            el.classList.add('marquee-on');
                        } else if (inner) {
                            el.classList.remove('marquee-on');
                        }
                    } catch (e) {}
                })(els[i]);
            }
        }

        function schedule() {
            if (scheduled) return;
            scheduled = true;
            if (window.requestAnimationFrame) requestAnimationFrame(scan);
            else setTimeout(scan, 50);
        }

        try {
            var obs = new MutationObserver(schedule);
            obs.observe(document.body, { childList: true, subtree: true });
        } catch (e) {}
        window.addEventListener('resize', schedule);
        schedule();
    },

    // Desktop/mobile (Blazor Hybrid) only: the app origin is a virtual host
    // (https://0.0.0.1/) served from the app bundle, and deep-link document
    // reloads are NOT served there (unreachable error page). So reload keys
    // NEVER reload the document here; instead Blazor soft-refreshes the
    // current page data in place via HandleReloadKey. Never call this on
    // the web version (hard refresh is valid there).
    enableHybridReloadGuard: function (dotNetRef) {
        if (dotNetRef) window.__nmuReloadRef = dotNetRef;
        if (window.__nmuReloadGuardWired) return;
        window.__nmuReloadGuardWired = true;
        document.addEventListener('keydown', function (e) {
            // e.code is the PHYSICAL key, layout-independent: with an Arabic
            // keyboard active, Ctrl+R reports key='ق' but code='KeyR'.
            var isReloadKey = (e.code === 'F5' || e.key === 'F5') ||
                ((e.ctrlKey || e.metaKey) && (e.code === 'KeyR' || e.key === 'r' || e.key === 'R'));
            if (!isReloadKey) return;
            e.preventDefault();
            e.stopImmediatePropagation();
            try {
                if (window.__nmuReloadRef) window.__nmuReloadRef.invokeMethodAsync('HandleReloadKey');
            } catch (err) {}
        }, true);
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
        // no-store: metadata/order files must never come from the HTTP cache
        // (freshness is decided by the app-level caches + force refresh).
        return fetch(url, { cache: 'no-store' }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (data) {
            return JSON.stringify(data);
        });
    },

    // Central per-semester archive mapping: Level_1/Semester_1 -> NMU.CE_1.1 ... Level_5/Semester_2 -> NMU.CE_5.2
    nmuGetArchiveId: function (level, semester) {
        function normLevel(l) {
            if (!l) return null;
            var m = String(l).trim().replace(/\s+/g, '_').match(/^Level_([1-5])$/i);
            return m ? ('Level_' + m[1]) : null;
        }
        function normSem(s) {
            if (!s) return null;
            var t = String(s).trim().replace(/\s+/g, '_').toLowerCase();
            if (t === 'semester_1' || t === 'first_term' || t === 'term_1' || t === 'semester1' || t === 'term1') return 'Semester_1';
            if (t === 'semester_2' || t === 'second_term' || t === 'term_2' || t === 'semester2' || t === 'term2') return 'Semester_2';
            var m = t.match(/^semester_([12])$/);
            return m ? ('Semester_' + m[1]) : null;
        }
        var lvl = normLevel(level);
        var sem = normSem(semester);
        if (!lvl || !sem) return null;
        return 'NMU.CE_' + lvl.split('_')[1] + '.' + sem.split('_')[1];
    },

    nmuParseSubjectFolder: function (folder) {
        var code = '', clean = String(folder || '').trim(), branch = 'ALL';
        if (!folder) return { code: code, clean: clean, branch: branch };
        var bm = /\.\(([^)]+)\)\s*$/.exec(clean);
        if (bm) {
            branch = (bm[1] || 'ALL').trim().toUpperCase() || 'ALL';
            clean = clean.substring(0, bm.index).trim();
        }
        var sep = clean.indexOf(' - ');
        if (sep > 0) {
            var left = clean.substring(0, sep).trim();
            var right = clean.substring(sep + 3).trim();
            if (left && right) { code = left; clean = right; }
        }
        return { code: code, clean: clean || String(folder), branch: branch };
    },

    fetchQuizContent: function (filePath, archiveId, level, semester) {
        var cacheKey = "nmu_q_content_" + filePath;
        var arch = archiveId || (level && semester ? this.nmuGetArchiveId(level, semester) : null);
        if (!arch) return Promise.reject(new Error('unknown archive'));
        var enc = String(filePath).split('/').map(encodeURIComponent).join('/');
        var url = "https://archive.org/download/" + arch + "/" + enc + "?t=" + Date.now();
        return fetch(url, { cache: "no-store" }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.json();
        }).then(function (data) {
            try { localStorage.setItem(cacheKey, JSON.stringify(data)); } catch (e) { /* ignore */ }
            return JSON.stringify(data);
        });
    },

    refreshQuizContent: function (filePath, archiveId, level, semester) {
        var cacheKey = "nmu_q_content_" + filePath;
        var arch = archiveId || (level && semester ? this.nmuGetArchiveId(level, semester) : null);
        if (!arch) return;
        var enc = String(filePath).split('/').map(encodeURIComponent).join('/');
        var url = "https://archive.org/download/" + arch + "/" + enc + "?t=" + Date.now();
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
        // no-store: archive metadata must never come from the HTTP cache,
        // otherwise forced refreshes would still read stale data.
        return fetch(url, { cache: 'no-store' }).then(function (r) {
            if (!r.ok) throw new Error('HTTP ' + r.status);
            return r.text();
        });
    },

    // Each semester lives in its own archive (NMU.CE_1.1 ... NMU.CE_5.2).
    // Metadata is cached PER ARCHIVE in IndexedDB so features share one copy.
    getRawMetadata: function (archiveId, level, semester) {
        var arch = archiveId || (level && semester ? this.nmuGetArchiveId(level, semester) : null);
        if (!arch) {
            // Back-compat: old callers with no args -> miss (forces new per-archive path).
            return Promise.resolve('');
        }
        return this.getCacheItem('raw_meta_' + arch);
    },

    setRawMetadata: function (archiveId, json, level, semester) {
        // Supports both setRawMetadata(archiveId, json) and legacy setRawMetadata(json).
        if (json === undefined && typeof archiveId === 'string' && (archiveId.charAt(0) === '{' || archiveId.charAt(0) === '[')) {
            return this.setCacheItem('raw_meta_legacy', archiveId);
        }
        var arch = archiveId;
        if ((level && semester) && (!arch || arch.indexOf('NMU.CE_') !== 0)) {
            arch = this.nmuGetArchiveId(level, semester) || arch;
        }
        if (!arch) return Promise.resolve();
        // Legacy single-arg call setRawMetadata(json) -> ignore (no archive context).
        if (json === undefined) return Promise.resolve();
        return this.setCacheItem('raw_meta_' + arch, json);
    },

    // Returns the raw metadata for one semester archive, fetching + caching when missing.
    // Concurrent callers for the SAME archive share the in-flight fetch.
    // force:true always re-fetches from network and overwrites the cache
    // (used when an archive change was detected).
    ensureRawMetadata: function (archiveId, level, semester, force) {
        var self = this;
        var arch = archiveId;
        if ((!arch || arch.indexOf('NMU.CE_') !== 0) && level && semester) {
            arch = self.nmuGetArchiveId(level, semester);
        }
        // Legacy no-arg call -> cannot resolve archive.
        if (!arch || arch.indexOf('NMU.CE_') !== 0) return Promise.resolve('');
        self._rawMetaPromises = self._rawMetaPromises || {};
        if (!force && self._rawMetaPromises[arch]) return self._rawMetaPromises[arch];
        var job = (force ? Promise.resolve('') : self.getRawMetadata(arch)).then(function (cached) {
            if (cached) return cached;
            return self.fetchText('https://archive.org/metadata/' + arch).then(function (json) {
                if (json) self.setRawMetadata(arch, json);
                return json;
            });
        });
        self._rawMetaPromises[arch] = job;
        job.catch(function () {
            if (self._rawMetaPromises[arch] === job) self._rawMetaPromises[arch] = null;
        });
        return job;
    },

    _isDerivativeName: function (nm) {
        if (!nm) return true;
        var l = nm.toLowerCase();
        if (l.endsWith('_djvu.txt') || l.endsWith('_djvu.xml')) return true;
        if (l.indexOf('_chocr.html') !== -1 || l.indexOf('_hocr.html') !== -1) return true;
        if (l.indexOf('_hocr_pageindex') !== -1 || l.indexOf('_hocr_searchtext') !== -1) return true;
        if (l.endsWith('_jp2.zip') || l.endsWith('_scandata.xml') || l.endsWith('_page_numbers.json')) return true;
        if (l.endsWith('.ia.mp4') || l.endsWith('_meta.xml') || l.endsWith('_files.xml')) return true;
        if (l.endsWith('_meta.sqlite') || l.endsWith('__ia_thumb.jpg')) return true;
        return false;
    },

    _isRecordedMediaName: function (nm) {
        if (!nm) return false;
        var l = nm.toLowerCase();
        if (l.endsWith('.ia.mp4')) return false;
        if (l.endsWith('.png') || l.endsWith('.jpg') || l.endsWith('.jpeg')) return false;
        if (l.endsWith('.afpk') || l.endsWith('_spectrogram.png')) return false;
        if (l.endsWith('order_config.json')) return false;
        return l.endsWith('.mp4') || l.endsWith('.mkv') || l.endsWith('.webm') ||
               l.endsWith('.mp3') || l.endsWith('.wav') || l.endsWith('.m4a');
    },

    // Parse the semester archive metadata ENTIRELY in JS and return only
    // the small {Name,Size} list for Data/ files. Result cached per semester.
    // force:true re-fetches the raw metadata and overwrites the cache.
    getSemesterFiles: function (level, semester, force) {
        var self = this;
        var arch = self.nmuGetArchiveId(level, semester);
        if (!arch) return Promise.resolve('');
        var semCacheKey = 'sem_files_v3_' + level + '_' + semester;
        function parse(json) {
            if (!json) return '';
            var data;
            try { data = JSON.parse(json); } catch (e) { return ''; }
            var out = [];
            var files = data.files || [];
            for (var i = 0; i < files.length; i++) {
                var f = files[i];
                var nm = f.name || '';
                if (nm.indexOf('Data/') !== 0) continue;
                if (nm.indexOf(arch + '.thumbs/') === 0) continue;
                if (self._isDerivativeName(nm)) continue;
                var sz = f.size;
                var n = (sz === undefined || sz === null || sz === '') ? null : Number(sz);
                // PascalCase keys match the ArchiveFile model exactly.
                out.push({ Name: nm, Size: (n === null || isNaN(n)) ? null : n });
            }
            var result = JSON.stringify(out);
            self.setCacheItem(semCacheKey, result);
            return result;
        }
        if (force) {
            return self.ensureRawMetadata(arch, null, null, true).then(parse).catch(function () {
                return '';
            });
        }
        return this.getCacheItem(semCacheKey).then(function (cached) {
            if (cached) return cached;
            return self.ensureRawMetadata(arch).then(parse);
        }).catch(function () {
            return '';
        });
    },

    // Same idea for the Quizzes folder list used by the quiz pages.
    // New layout: Data/{Subject}/Quizzes/{Lecturer}/*.json
    getQuizFiles: function (level, semester, force) {
        return this.getSemesterFiles(level, semester, force).then(function (json) {
            if (!json) return '';
            var data;
            try { data = JSON.parse(json); } catch (e) { return ''; }
            var out = [];
            for (var i = 0; i < data.length; i++) {
                var nm = data[i].Name || '';
                if (nm.toLowerCase().indexOf('/quizzes/') !== -1 &&
                    nm.toLowerCase().slice(-5) === '.json' &&
                    nm.toLowerCase().slice(-17) !== 'order_config.json') out.push(nm);
            }
            return JSON.stringify(out);
        });
    },

    // Build the catalog of every subject that exists across ALL semester archives
    // (PDF folders). Used by the custom-subjects picker. Each archive is fetched
    // once and cached; empty archives resolve instantly.
    getSubjectCatalog: function (force) {
        var self = this;
        var catCacheKey = 'subject_catalog_v2';
        function build() {
            var archives = [
                { id: 'NMU.CE_1.1', level: 'Level_1', semester: 'Semester_1' },
                { id: 'NMU.CE_1.2', level: 'Level_1', semester: 'Semester_2' },
                { id: 'NMU.CE_2.1', level: 'Level_2', semester: 'Semester_1' },
                { id: 'NMU.CE_2.2', level: 'Level_2', semester: 'Semester_2' },
                { id: 'NMU.CE_3.1', level: 'Level_3', semester: 'Semester_1' },
                { id: 'NMU.CE_3.2', level: 'Level_3', semester: 'Semester_2' },
                { id: 'NMU.CE_4.1', level: 'Level_4', semester: 'Semester_1' },
                { id: 'NMU.CE_4.2', level: 'Level_4', semester: 'Semester_2' },
                { id: 'NMU.CE_5.1', level: 'Level_5', semester: 'Semester_1' },
                { id: 'NMU.CE_5.2', level: 'Level_5', semester: 'Semester_2' }
            ];
            var jobs = archives.map(function (a) {
                return self.ensureRawMetadata(a.id, null, null, force).then(function (json) {
                    return { arch: a, json: json };
                }).catch(function () { return { arch: a, json: '' }; });
            });
            return Promise.all(jobs).then(function (results) {
                var out = [];
                var seen = {};
                for (var r = 0; r < results.length; r++) {
                    var arch = results[r].arch;
                    var json = results[r].json;
                    if (!json) continue;
                    var data;
                    try { data = JSON.parse(json); } catch (e) { continue; }
                    var files = data.files || [];
                    for (var i = 0; i < files.length; i++) {
                        var nm = files[i].name || '';
                        if (nm.indexOf('Data/') !== 0) continue;
                        if (nm.toLowerCase().indexOf('/pdfs/') === -1) continue;
                        if (nm.slice(-4).toLowerCase() !== '.pdf') continue;
                        if (nm.toLowerCase().slice(-9) === '_text.pdf') continue;
                        var segs = nm.split('/');
                        if (segs.length < 2) continue;
                        var subject = segs[1];
                        if (!subject || subject.toLowerCase() === 'order_config.json') continue;
                        var key = arch.level + '|' + arch.semester + '|' + subject;
                        if (seen[key]) continue;
                        seen[key] = true;
                        var parsed = self.nmuParseSubjectFolder(subject);
                        out.push({ Level: arch.level, Semester: arch.semester, Subject: subject, Code: parsed.code, DisplayName: parsed.clean, Branch: parsed.branch });
                    }
                }
                var result = JSON.stringify(out);
                self.setCacheItem(catCacheKey, result);
                return result;
            });
        }
        if (force) {
            return build().catch(function () {
                return '';
            });
        }
        return this.getCacheItem(catCacheKey).then(function (cached) {
            if (cached) return cached;
            return build();
        }).catch(function () {
            return '';
        });
    },

    // Same idea for the Records folders used by the recorded lectures pages.
    // New layout: Data/{Subject}/Records/{Lecturer}/*.{mp4|mp3|...}
    // Thumbs: {ArchiveId}.thumbs/Data/... (*.jpg)
    getRecordedFiles: function (level, semester, force) {
        var self = this;
        var arch = self.nmuGetArchiveId(level, semester);
        if (!arch) return Promise.resolve('');
        var recCacheKey = 'rec_files_v3_' + level + '_' + semester;
        return this.getCacheItem(recCacheKey).then(function (cached) {
            if (cached && !force) return cached;
            return self.ensureRawMetadata(arch, null, null, force).then(function (json) {
                if (!json) return '';
                var data;
                try { data = JSON.parse(json); } catch (e) { return ''; }
                var files = data.files || [];
                var thumbPrefix = arch + '.thumbs/Data/';

                // Collect thumb names (video frame previews) for thumbnail matching.
                var thumbs = [];
                for (var i = 0; i < files.length; i++) {
                    var nm = files[i].name || '';
                    if (nm.indexOf(thumbPrefix) !== 0) continue;
                    if (nm.slice(-4).toLowerCase() !== '.jpg') continue;
                    thumbs.push(nm);
                }

                function parseRec(nm) {
                    var segs = nm.split('/');
                    if (segs.length < 4 || segs[0] !== 'Data') return { subject: '', lecturer: '' };
                    var subject = segs[1] || '';
                    var recIdx = -1;
                    for (var k = 0; k < segs.length; k++) {
                        if (segs[k].toLowerCase() === 'records') { recIdx = k; break; }
                    }
                    var lecturer = (recIdx >= 0 && recIdx + 1 < segs.length) ? segs[recIdx + 1] : '';
                    return { subject: subject, lecturer: lecturer };
                }

                var out = [];
                for (var i = 0; i < files.length; i++) {
                    var f = files[i];
                    var nm = f.name || '';
                    if (nm.indexOf('Data/') !== 0) continue;
                    if (nm.toLowerCase().indexOf('/records/') === -1) continue;
                    if (!self._isRecordedMediaName(nm)) continue;
                    var lower = nm.toLowerCase();
                    var fileNoExt = nm.slice(nm.lastIndexOf('/') + 1);
                    var dot = fileNoExt.lastIndexOf('.');
                    if (dot > 0) fileNoExt = fileNoExt.slice(0, dot);
                    var thumb = '';
                    for (var j = 0; j < thumbs.length; j++) {
                        if (thumbs[j].indexOf(fileNoExt) !== -1) { thumb = thumbs[j]; break; }
                    }
                    var pr = parseRec(nm);
                    var sz = f.size;
                    var n = (sz === undefined || sz === null || sz === '') ? null : Number(sz);
                    out.push({
                        Name: nm,
                        Size: (n === null || isNaN(n)) ? null : n,
                        ThumbName: thumb || null,
                        IsAudio: lower.endsWith('.mp3') || lower.endsWith('.wav') || lower.endsWith('.m4a'),
                        Lecturer: pr.lecturer || '',
                        SubjectFullName: pr.subject || '',
                        ArchiveId: arch,
                        DisplayName: fileNoExt.split('_').join(' '),
                        SubFolder: pr.lecturer || 'General'
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
              // Pattern: nmu_quiz_list_Level_1_Semester_1_v5_newarch or nmu_q_content_Data/...
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
          // New layout content keys: nmu_q_content_Data/... ; legacy: nmu_q_content_NMU/...
          var contentPrefixNew = 'Data/';
          var contentPrefixOld = 'NMU/' + oldLevelClean + '/' + oldSemClean + '/QUIZE/';

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
                          // Also match content keys with the old + new paths
                          if (key.startsWith('nmu_q_content_') && (key.indexOf(contentPrefixOld) >= 0 || key.indexOf(contentPrefixNew) >= 0)) {
                              if (toRemove.indexOf(key) < 0) toRemove.push(key);
                          }
                          if (key.startsWith('nmu_q_meta_') && (key.indexOf(contentPrefixOld) >= 0 || key.indexOf(contentPrefixNew) >= 0)) {
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
