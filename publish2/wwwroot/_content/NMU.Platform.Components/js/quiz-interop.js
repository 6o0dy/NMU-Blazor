window.quizInterop = {
    typesetMathJax: function () {
        if (window.MathJax && window.MathJax.typesetPromise) {
            return window.MathJax.typesetPromise().catch(function () { });
        }
        return Promise.resolve();
    },

    highlightAllCode: function () {
        if (window.Prism) {
            setTimeout(function () { Prism.highlightAll(); }, 50);
        }
    },

    highlightCodeBlock: function (elementId) {
        var el = document.getElementById(elementId);
        if (el && window.Prism) {
            setTimeout(function () { Prism.highlightElement(el); }, 50);
        }
    },

    renderBarChart: function (graphDataJson, targetId, width) {
        try {
            var data = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
            var target = '#' + targetId;
            var container = document.querySelector(target);
            if (!container) return;
            container.innerHTML = '';
            var w = container.clientWidth || width || 400;
            var h = 280;
            var margin = { top: 20, right: 20, bottom: 40, left: 40 };
            var chartW = w - margin.left - margin.right;
            var chartH = h - margin.top - margin.bottom;

            var svg = d3.select(target).append("svg")
                .attr("width", w).attr("height", h)
                .append("g").attr("transform", "translate(" + margin.left + "," + margin.top + ")");

            var x = d3.scale.ordinal().rangeRoundBands([0, chartW], 0.1);
            var y = d3.scale.linear().range([chartH, 0]);
            x.domain(data.map(function (d) { return d.label; }));
            y.domain([0, d3.max(data, function (d) { return d.value; })]);

            svg.selectAll(".bar").data(data).enter().append("rect")
                .attr("x", function (d) { return x(d.label); })
                .attr("width", x.rangeBand())
                .attr("y", function (d) { return y(d.value); })
                .attr("height", function (d) { return chartH - y(d.value); })
                .style("fill", "var(--accent)");

            svg.append("g").attr("transform", "translate(0," + chartH + ")").call(d3.svg.axis().scale(x).orient("bottom"));
            svg.append("g").call(d3.svg.axis().scale(y).orient("left").ticks(5));
        } catch (e) { console.error('Bar chart error:', e); }
    },

    renderHistogram: function (graphDataJson, targetId, width) {
        try {
            var data = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
            var target = '#' + targetId;
            var container = document.querySelector(target);
            if (!container) return;
            container.innerHTML = '';
            var w = container.clientWidth || width || 400;
            var h = 280;
            var margin = { top: 20, right: 20, bottom: 40, left: 40 };
            var chartW = w - margin.left - margin.right;
            var chartH = h - margin.top - margin.bottom;

            var svg = d3.select(target).append("svg")
                .attr("width", w).attr("height", h)
                .append("g").attr("transform", "translate(" + margin.left + "," + margin.top + ")");

            var x = d3.scale.ordinal().rangeRoundBands([0, chartW], 0);
            var y = d3.scale.linear().range([chartH, 0]);
            x.domain(data.map(function (d) { return d.label; }));
            y.domain([0, d3.max(data, function (d) { return d.value; })]);

            svg.selectAll(".bar").data(data).enter().append("rect")
                .attr("x", function (d) { return x(d.label); })
                .attr("width", x.rangeBand())
                .attr("y", function (d) { return y(d.value); })
                .attr("height", function (d) { return chartH - y(d.value); })
                .style("fill", "var(--primary)");

            svg.append("g").attr("transform", "translate(0," + chartH + ")").call(d3.svg.axis().scale(x).orient("bottom"));
            svg.append("g").call(d3.svg.axis().scale(y).orient("left").ticks(5));
        } catch (e) { console.error('Histogram error:', e); }
    },

    renderPieChart: function (graphDataJson, targetId, width) {
        try {
            var data = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
            var target = '#' + targetId;
            var container = document.querySelector(target);
            if (!container) return;
            container.innerHTML = '';
            var w = container.clientWidth || width || 400;
            var h = 280;
            var radius = Math.min(w, h) / 2 - 20;

            var svg = d3.select(target).append("svg")
                .attr("width", w).attr("height", h)
                .append("g").attr("transform", "translate(" + (w / 2) + "," + (h / 2) + ")");

            var color = d3.scale.category10();
            var pie = d3.layout.pie().value(function (d) { return d.value; });
            var arc = d3.svg.arc().outerRadius(radius).innerRadius(0);

            var arcs = svg.selectAll(".arc").data(pie(data)).enter().append("g");
            arcs.append("path").attr("d", arc).style("fill", function (d, i) { return color(i); });
            arcs.append("text").attr("transform", function (d) { return "translate(" + arc.centroid(d) + ")"; })
                .attr("text-anchor", "middle").text(function (d) { return d.data.label; })
                .style("fill", "#fff").style("font-size", "14px").style("font-weight", "bold");
        } catch (e) { console.error('Pie chart error:', e); }
    },

    renderLineChart: function (graphDataJson, targetId, width) {
        try {
            var data = typeof graphDataJson === 'string' ? JSON.parse(graphDataJson) : graphDataJson;
            var target = '#' + targetId;
            var container = document.querySelector(target);
            if (!container) return;
            container.innerHTML = '';
            var w = container.clientWidth || width || 400;
            var h = 280;
            var margin = { top: 20, right: 20, bottom: 40, left: 40 };
            var chartW = w - margin.left - margin.right;
            var chartH = h - margin.top - margin.bottom;

            var x = d3.scale.ordinal().domain(data.map(function (d) { return d.label; })).rangePoints([0, chartW]);
            var y = d3.scale.linear().domain([0, d3.max(data, function (d) { return d.value; })]).range([chartH, 0]);
            var line = d3.svg.line().x(function (d) { return x(d.label); }).y(function (d) { return y(d.value); });

            var svg = d3.select(target).append("svg")
                .attr("width", w).attr("height", h)
                .append("g").attr("transform", "translate(" + margin.left + "," + margin.top + ")");

            svg.append("g").attr("transform", "translate(0," + chartH + ")").call(d3.svg.axis().scale(x).orient("bottom"));
            svg.append("g").call(d3.svg.axis().scale(y).orient("left").ticks(5));
            svg.append("path").datum(data).attr("d", line)
                .style("fill", "none").style("stroke", "var(--primary)").style("stroke-width", 3);
            svg.selectAll("circle").data(data).enter().append("circle")
                .attr("cx", function (d) { return x(d.label); })
                .attr("cy", function (d) { return y(d.value); })
                .attr("r", 5).style("fill", "var(--accent)");
        } catch (e) { console.error('Line chart error:', e); }
    },

    renderFunctionPlot: function (fn, targetId, width) {
        try {
            var target = '#' + targetId;
            var container = document.querySelector(target);
            if (!container) return;
            container.innerHTML = '';
            var w = container.clientWidth || width || 400;
            functionPlot({
                target: target,
                width: w - 20, height: 280,
                grid: true,
                xAxis: { domain: [-7, 7] }, yAxis: { domain: [-7, 7] },
                data: [{ fn: fn, color: 'var(--primary)' }]
            });
        } catch (e) { console.error('Function plot error:', e); }
    },

    renderCircuitDiagram: function (latexCode, targetId) {
        var container = document.getElementById(targetId);
        if (!container) return;
        container.innerHTML = '<div style="display:flex;flex-direction:column;align-items:center;justify-content:center;width:100%;height:200px;"><div class="spinner" style="border-top-color:var(--primary);width:45px;height:45px;border-width:4px;"></div><div style="color:var(--primary);margin-top:15px;font-weight:900;">Loading circuit...</div></div>';

        var cleaned = latexCode.replace(/^\uFEFF/, '').replace(/^\u200B/, '').trim().replace(/\\([^\x00-\x7F])/g, '\\');
        cleaned = cleaned.replace(/to\s*\[\s*zener\s+diode(.*?)\]/g, 'to[D$1, zD]');
        cleaned = cleaned.replace(/zener\s+diode/g, 'zzdiode');
        cleaned = cleaned.replace(/(^|[^\\])#/g, '$1\\\\#');

        var hasDiamond = /diamond/.test(cleaned);
        var fullCode;

        if (/^\s*\\documentclass/.test(cleaned)) {
            var modified = cleaned.replace(/\\usepackage(\[.*?\])?\{geometry\}\s*/g, '').replace(/\\pgfplotsset\{compat=[\d.]+\}\s*/g, '');
            if (!/\\usepackage.*\{amsmath\}/.test(modified)) {
                modified = modified.replace(/\\begin\{document\}/, '\\usepackage{amsmath}\n\\begin{document}');
            }
            if (hasDiamond && !/shapes\.geometric/.test(modified)) {
                modified = modified.replace(/\\begin\{document\}/, '\\usetikzlibrary{shapes.geometric}\n\\begin{document}');
            }
            fullCode = modified;
        } else if (/^\s*\\begin\s*\{tikzpicture\}/.test(cleaned)) {
            fullCode = '\\documentclass[margin=5mm]{standalone}\n\\usepackage{amsmath}\n\\usepackage{circuitikz}\n\\usepackage{pgfplots}\n\\pgfplotsset{compat=1.18}\n' + (hasDiamond ? '\\usetikzlibrary{shapes.geometric}\n' : '') + '\\begin{document}\n' + cleaned + '\n\\end{document}';
        } else if (/^\s*\\begin\s*\{circuitikz\}/.test(cleaned)) {
            fullCode = '\\documentclass[margin=5mm]{standalone}\n\\usepackage{circuitikz}\n' + (hasDiamond ? '\\usetikzlibrary{shapes.geometric}\n' : '') + '\\begin{document}\n' + cleaned + '\n\\end{document}';
        } else {
            fullCode = '\\documentclass[margin=5mm]{standalone}\n\\usepackage{circuitikz}\n' + (hasDiamond ? '\\usetikzlibrary{shapes.geometric}\n' : '') + '\\begin{document}\n\\begin{circuitikz}[scale=1.3, transform shape, thick]\n' + cleaned + '\n\\end{circuitikz}\n\\end{document}';
        }

        try {
            var data = new TextEncoder().encode(fullCode);
            var compressed = pako.deflate(data);
            var binary = '';
            for (var i = 0; i < compressed.length; i++) binary += String.fromCharCode(compressed[i]);
            var base64Safe = btoa(binary).replace(/\+/g, '-').replace(/\//g, '_');
            var cacheKey = 'circuit_' + base64Safe.substring(0, 64);
            var imageUrl = 'https://kroki.io/tikz/svg/' + base64Safe;

            function displaySvg(svgText) {
                var blob = new Blob([svgText], { type: 'image/svg+xml' });
                var url = URL.createObjectURL(blob);
                container.innerHTML = '<div style="width:100%;display:flex;justify-content:center;align-items:center;background:#fffaf0;border-radius:12px;padding:15px;"><img src="' + url + '" style="max-width:100%;max-height:400px;object-fit:contain;" alt="Circuit" /></div>';
            }

            function showOfflineMsg() {
                container.innerHTML = '<div style="text-align:center;padding:30px;border:1px solid var(--star-color);border-radius:12px;background:rgba(255,184,0,0.08);"><div style="font-size:2rem;margin-bottom:10px;">📡</div><div style="color:var(--star-color);font-weight:bold;font-size:1rem;">يتطلب اتصال بالإنترنت لعرض الدائرة</div><div style="color:#94a3b8;font-size:0.85rem;margin-top:5px;">حاول مرة أخرى عند الاتصال</div></div>';
            }

            function fetchAndCache() {
                if (!navigator.onLine) { showOfflineMsg(); return; }
                fetch(imageUrl).then(function (r) { if (!r.ok) throw Error(); return r.text(); })
                    .then(function (svgText) {
                        var dbReq = indexedDB.open('nmu-pdf-cache', 1);
                        dbReq.onupgradeneeded = function (e) {
                            var db = e.target.result;
                            if (!db.objectStoreNames.contains('pdfs')) db.createObjectStore('pdfs', { keyPath: 'key' });
                        };
                        dbReq.onsuccess = function (e) {
                            var db = e.target.result;
                            var tx = db.transaction('pdfs', 'readwrite');
                            tx.objectStore('pdfs').put({ key: cacheKey, data: svgText, timestamp: Date.now() });
                        };
                        displaySvg(svgText);
                    })
                    .catch(function () { if (!navigator.onLine) showOfflineMsg(); else displayOnline(); });
            }

            function displayOnline() {
                var img = new Image();
                img.onload = function () {
                    container.innerHTML = '<div style="width:100%;display:flex;justify-content:center;align-items:center;background:#fffaf0;border-radius:12px;padding:15px;"><img src="' + imageUrl + '" style="max-width:100%;max-height:400px;object-fit:contain;" alt="Circuit" /></div>';
                };
                img.onerror = function () {
                    container.innerHTML = '<div style="text-align:center;padding:20px;border:1px solid var(--wrong);border-radius:12px;background:rgba(255,23,68,0.05);"><div style="color:var(--wrong);font-weight:bold;">Failed to load circuit diagram.</div></div>';
                };
                img.src = imageUrl;
            }

            var cacheReq = indexedDB.open('nmu-pdf-cache', 1);
            cacheReq.onupgradeneeded = function (e) {
                var db = e.target.result;
                if (!db.objectStoreNames.contains('pdfs')) db.createObjectStore('pdfs', { keyPath: 'key' });
            };
            cacheReq.onsuccess = function (e) {
                var db = e.target.result;
                var tx = db.transaction('pdfs', 'readonly');
                var get = tx.objectStore('pdfs').get(cacheKey);
                get.onsuccess = function () {
                    if (get.result && get.result.data) {
                        displaySvg(get.result.data);
                    } else {
                        fetchAndCache();
                    }
                };
                get.onerror = function () { fetchAndCache(); };
            };
            cacheReq.onerror = function () { fetchAndCache(); };
        } catch (e) {
            container.innerHTML = '<div style="color:var(--wrong);text-align:center;padding:20px;">Error rendering circuit.</div>';
        }
    },

    renderMiniGraph: function (graphType, graphFn, graphData, targetId, width) {
        var target = '#' + targetId;
        var container = document.querySelector(target);
        if (!container) return;
        container.innerHTML = '';
        var w = container.clientWidth || width || 300;
        var h = 130;

        if (graphFn && !graphType) {
            try {
                functionPlot({
                    target: target, width: w, height: h, grid: true,
                    xAxis: { domain: [-5, 5] }, yAxis: { domain: [-5, 5] },
                    data: [{ fn: graphFn, color: 'var(--primary)' }]
                });
            } catch (e) { console.error(e); }
        } else if (graphType === 'bar' || graphType === 'histogram') {
            quizInterop.renderBarChart(graphData, targetId, w);
        } else if (graphType === 'pie') {
            quizInterop.renderPieChart(graphData, targetId, w);
        } else if (graphType === 'line') {
            quizInterop.renderLineChart(graphData, targetId, w);
        } else if (graphType === 'circuit_latex') {
            quizInterop.renderCircuitDiagram(graphData, targetId);
        }
    },

    scrollToBottom: function () {
        setTimeout(function () {
            var btn = document.querySelector('.quiz-next-btn, .quiz-submit-exam-btn');
            if (btn) btn.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }, 150);
    },

    scrollQuizToTop: function () {
        var container = document.querySelector('.quiz-play-page');
        if (container) container.scrollTop = 0;
        window.scrollTo(0, 0);
    },

    scrollPaletteActive: function () {
        var activeItem = document.querySelector('.quiz-palette-strip .mini-q-num.active');
        var wrapper = document.getElementById('quiz-palette-wrapper');
        if (activeItem && wrapper) {
            var scrollLeft = activeItem.offsetLeft - (wrapper.clientWidth / 2) + (activeItem.clientWidth / 2);
            wrapper.scrollTo({ left: scrollLeft, behavior: 'smooth' });
        }
    },

    scrollFilterIntoView: function () {
        var section = document.querySelector('.quiz-review-section');
        if (section) section.scrollIntoView({ behavior: 'smooth' });
    },

    scrollElementIntoView: function (elementId) {
        var el = document.getElementById(elementId);
        if (el) setTimeout(function () { el.scrollIntoView({ behavior: 'smooth', block: 'center' }); }, 150);
    },

    setupBeforeUnload: function () {
        window.addEventListener('beforeunload', function (e) {
            e.preventDefault();
            e.returnValue = '';
        });
    },

    removeBeforeUnload: function () {
        window.removeEventListener('beforeunload', function (e) { });
    },

    escapeHTML: function (str) {
        if (!str) return '';
        return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
    },

    getTargetIcon: function (name) {
        var n = name.toLowerCase();
        var check = function (keys) { return keys.some(function (k) { return n.includes(k); }); };
        if (check(['arabic', '\u0639\u0631\u0628\u064a', '\u0644\u063a\u0629 \u0639\u0631\u0628\u064a\u0629'])) return "fa-solid fa-pen-nib";
        if (check(['english', '\u0625\u0646\u062c\u0644\u064a\u0632\u064a', 'foreign'])) return "fa-solid fa-language";
        if (check(['communication', '\u062a\u0648\u0627\u0635\u0644', 'presentation'])) return "fa-solid fa-person-chalkboard";
        if (check(['history', '\u062a\u0627\u0631\u064a\u062e', 'civilization', '\u062d\u0636\u0627\u0631\u0629'])) return "fa-solid fa-landmark";
        if (check(['rights', '\u062d\u0642\u0648\u0642', 'law', 'ethics', '\u0623\u062e\u0644\u0627\u0642'])) return "fa-solid fa-scale-balanced";
        if (check(['psychology', '\u0639\u0644\u0645 \u0646\u0641\u0633', 'sociology', '\u0627\u062c\u062a\u0645\u0627\u0639'])) return "fa-solid fa-users-rays";
        if (check(['management', '\u0625\u062f\u0627\u0631\u0629', 'marketing', '\u062a\u0633\u0648\u064a\u0642'])) return "fa-solid fa-briefcase";
        if (check(['statist', 'probab', '\u0625\u062d\u0635\u0627\u0621', '\u0627\u062d\u062a\u0645\u0627\u0644'])) return "fa-solid fa-chart-pie";
        if (check(['discrete', '\u062a\u0631\u0627\u0643\u064a\u0628', 'logic set'])) return "fa-solid fa-share-nodes";
        if (check(['diff', 'equation', 'tial', '\u062a\u0641\u0627\u0636\u0644'])) return "fa-solid fa-wave-square";
        if (check(['numerical', '\u062a\u062d\u0644\u064a\u0644 \u0639\u062f\u062f\u064a'])) return "fa-solid fa-arrow-down-1-9";
        if (check(['linear', 'algebra', 'matrix', '\u0645\u0635\u0641\u0648\u0641\u0627\u062a'])) return "fa-solid fa-table-cells";
        if (check(['math', '\u0631\u064a\u0627\u0636'])) return "fa-solid fa-calculator";
        if (check(['physics', '\u0641\u064a\u0632\u064a\u0627\u0621'])) return "fa-solid fa-atom";
        if (check(['mechanic', 'static', '\u0645\u064a\u0643\u0627\u0646\u064a\u0643\u0627'])) return "fa-solid fa-weight-hanging";
        if (check(['chem', '\u0643\u064a\u0645\u064a\u0627\u0621'])) return "fa-solid fa-flask-vial";
        if (check(['drawing', '\u0631\u0633\u0645', 'projection'])) return "fa-solid fa-compass-drafting";
        if (check(['structured', 'intro', 'programming'])) return "fa-solid fa-terminal";
        if (check(['object', 'oop', '\u0643\u0627\u0626\u0646\u064a\u0629'])) return "fa-solid fa-cubes";
        if (check(['algorithm', '\u062e\u0648\u0627\u0631\u0632\u0645'])) return "fa-solid fa-code-branch";
        if (check(['data structure', '\u0647\u064a\u0627\u0643\u0644'])) return "fa-solid fa-sitemap";
        if (check(['software eng', '\u0647\u0646\u062f\u0633\u0629 \u0628\u0631\u0645\u062c'])) return "fa-solid fa-laptop-file";
        if (check(['web', 'html', 'internet'])) return "fa-solid fa-globe";
        if (check(['machine', 'learning', '\u062a\u0639\u0644\u0645'])) return "fa-solid fa-robot";
        if (check(['neural', 'deep', '\u0639\u0635\u0628\u064a\u0629', '\u0639\u0645\u064a\u0642'])) return "fa-solid fa-circle-nodes";
        if (check(['vision', 'image', '\u0631\u0624\u064a\u0629', '\u0635\u0648\u0631'])) return "fa-solid fa-eye";
        if (check(['nlp', 'language proc', '\u0644\u063a\u0627\u062a \u0637\u0628\u064a\u0639\u064a\u0629'])) return "fa-solid fa-comments";
        if (check(['mining', 'big data', 'science', '\u062a\u0646\u0642\u064a\u0628'])) return "fa-solid fa-database";
        if (check(['ai', 'artificial', 'intelligence', '\u0630\u0643\u0627\u0621'])) return "fa-solid fa-brain";
        if (check(['circuit', 'electric', '\u062f\u0648\u0627\u0626\u0631'])) return "fa-solid fa-plug";
        if (check(['logic', '\u0645\u0646\u0637\u0642'])) return "fa-solid fa-toggle-on";
        if (check(['architecture', 'organi', '\u0639\u0645\u0627\u0631\u0629', '\u0646\u0638\u0627\u0645'])) return "fa-solid fa-server";
        if (check(['micro', 'processor', '\u0645\u0639\u0627\u0644\u062c\u0627\u062a'])) return "fa-solid fa-microchip";
        if (check(['embed', '\u0645\u062f\u0645\u062c\u0629'])) return "fa-solid fa-memory";
        if (check(['operating', 'nux', '\u0646\u0638\u0627\u0645 \u062a\u0634\u063a\u064a\u0644'])) return "fa-solid fa-desktop";
        if (check(['network', '\u0634\u0628\u0643\u0627\u062a'])) return "fa-solid fa-network-wired";
        if (check(['security', 'secure', 'cyber', '\u0623\u0645\u0646', '\u0633\u064a\u0628\u0631\u0627\u0646\u064a'])) return "fa-solid fa-shield-cat";
        if (check(['cloud', '\u0633\u062d\u0627\u0628\u064a\u0629'])) return "fa-solid fa-cloud";
        if (check(['project', 'graduation', '\u0645\u0634\u0631\u0648\u0639'])) return "fa-solid fa-user-graduate";
        if (check(['report', 'technical', 'writ', '\u062a\u0642\u0627\u0631\u064a\u0631'])) return "fa-solid fa-file-pen";
        if (check(['university', 'uni', 'req', '\u0645\u062a\u0637\u0644\u0628'])) return "fa-solid fa-building-columns";
        return "fa-solid fa-book-open";
    }
};
