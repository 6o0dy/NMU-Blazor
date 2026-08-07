using System.Globalization;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NMU.Platform.Components.Services;

namespace NMU.Platform.Components.Pages;

public partial class VideoPlayer : IDisposable
{
    [Parameter] public string Group { get; set; } = "";

    private string _fileUrl = "";
    private string _proxyUrl = "";
    private int _directRetries = 0;
    private string _fileName = "";
    private string _group = "";
    private bool _isAudio;
    private double _currentTime;
    private double _duration;
    private bool _isPaused = true;
    private bool _isBuffering;
    private double _volume = 1;
    private double _playbackRate = 1;
    private bool _showCenterPlay;
    private bool _idle;
    private DateTime _lastActivity = DateTime.UtcNow;
    private Timer? _idleTimer;
    private bool _disposed;
    private CacheProgress? _cacheProgress;
    private Timer? _cachePollTimer;
    private string _cacheKey = "";
    private CancellationTokenSource? _cacheCts;
    private bool _cacheStarted;
    private int _cacheFillRunning;
    private VideoSegmentCollection _cachedSegments = [];
    private Mp4TimeMap? _timeMap;
    private DateTime _lastTimeMapAttempt = DateTime.MinValue;
    private string _videoError = "0";
    private string _videoReady = "0";
    private string _videoNet = "0";
    private bool _sourceIsProxy;
    private bool _showDebug;
    private string _debugText = "";
    private long _chunksCachedLogged = -1;

    protected override void OnInitialized()
    {
        _group = Uri.UnescapeDataString(Group);
        var uri = new Uri(Navigation.Uri);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        var urlFromQuery = query["url"];
        var nameFromQuery = query["name"];
        _fileUrl = urlFromQuery ?? NavState.CurrentFileUrl ?? "";
        _fileName = nameFromQuery ?? NavState.CurrentFileName ?? "Untitled";
        if (!string.IsNullOrEmpty(_fileUrl))
        {
            NavState.CurrentFileUrl = _fileUrl;
            NavState.CurrentFileName = _fileName;
        }
        if (string.IsNullOrEmpty(_fileUrl))
        {
            Navigation.NavigateTo($"recorded/{Uri.EscapeDataString(_group)}", replace: true);
            return;
        }
        _isAudio = _fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                   _fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                   _fileName.EndsWith(".m4a", StringComparison.OrdinalIgnoreCase);
        _cacheKey = MediaCache.GetCacheKey(_fileUrl);
        NavState.IsFullScreen = true;
        NavState.PageTitle = _fileName;
        VideoPlayerSeekHub.Seek += HandleJsSeek;
        VideoPlayerSeekHub.Tick += HandleTick;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            var key = "nmu_prog_" + _fileUrl;
            await JS.InvokeVoidAsync("videoPlayer.init", key);
            await JS.InvokeVoidAsync("videoPlayer.initSeek");
            await JS.InvokeVoidAsync("videoPlayer.startPump");
            await InitVideoSourceAsync();
            await RefreshCachedSegmentsAsync();
            StartIdleTimer();
            StartCachePollTimer();
            CacheDiagnostics.Log($"PAGE init ok, url={_fileUrl} cacheKey={_cacheKey} web={Platform.IsWeb} cacheBase={CacheDiagnostics.BasePath}");
        }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"PAGE OnAfterRenderAsync ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task InitVideoSourceAsync()
    {
        if (Platform.IsWeb)
        {
            await JS.InvokeVoidAsync("videoPlayer.setSource", _fileUrl);
            CacheDiagnostics.Log($"INIT web setSource url={_fileUrl} cacheKey={_cacheKey}");
            return;
        }
        try
        {
            var proxy = Services.GetService<MediaProxyHost>();
            if (proxy is null)
                throw new InvalidOperationException("MediaProxyHost not registered");
            var port = proxy.EnsureRunning();
            var proxyUrl = $"http://127.0.0.1:{port}/media/{_cacheKey}?url={Uri.EscapeDataString(_fileUrl)}";
            _proxyUrl = proxyUrl;
            _sourceIsProxy = true;
            await JS.InvokeVoidAsync("videoPlayer.setSource", proxyUrl);
            CacheDiagnostics.Log($"INIT proxy (always) url={proxyUrl}");
        }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"INIT ERROR: {ex.GetType().Name}: {ex.Message}");
            await JS.InvokeVoidAsync("videoPlayer.setSource", _fileUrl);
        }
    }

    private async Task OnLoadedData()
    {
        try
        {
            var saved = await JS.InvokeAsync<string>("videoPlayer.loadSavedTime");
            CacheDiagnostics.Log($"EVENT loadeddata, savedTime={saved}");
            if (double.TryParse(saved, NumberStyles.Any, CultureInfo.InvariantCulture, out var t) && t > 0)
            {
                _currentTime = t;
                await JS.InvokeVoidAsync("videoPlayer.restore", t.ToString(CultureInfo.InvariantCulture));
                StateHasChanged();
            }
        }
        catch { }
    }

    private void HandleTick(double current, double duration, string error, string ready, string net)
    {
        _videoError = error;
        _videoReady = ready;
        _videoNet = net;
        if (duration > 0)
        {
            if (Math.Abs(_duration - duration) > 0.5)
            {
                _duration = duration;
                if (!Platform.IsWeb)
                    _ = Task.Run(() => MediaCache.SetDurationAsync(_cacheKey, duration));
            }
            else
            {
                _duration = duration;
            }
        }
        if (current > 0)
            _currentTime = current;
        else if (_isPaused && ready != "0" && error == "0" && !_showCenterPlay)
        {
            _showCenterPlay = true;
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnSeeked()
    {
        try
        {
            var state = await JS.InvokeAsync<string>("videoPlayer.getState");
            var parts = state.Split('|');
            if (parts.Length >= 2 && double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var ct) && ct > 0)
                CacheDiagnostics.Log($"EVENT seeked ct={ct:0.##}");
        }
        catch { }
    }

    private void OnPlay()
    {
        _isPaused = false;
        _showCenterPlay = false;
        StateHasChanged();
        ResetIdle();
    }

    private void OnPause()
    {
        _isPaused = true;
        _showCenterPlay = true;
        StateHasChanged();
        _ = HideCenterPlayDelayed();
    }

    private async Task HideCenterPlayDelayed()
    {
        await Task.Delay(600);
        _showCenterPlay = false;
        await InvokeAsync(StateHasChanged);
    }

    private void OnWaiting() { _isBuffering = true; StateHasChanged(); }
    private void OnPlaying()
    {
        _isBuffering = false;
        StateHasChanged();
        _ = JS.InvokeVoidAsync("videoPlayer.forcePaint");
        if (!_cacheStarted)
        {
            _cacheStarted = true;
            CacheDiagnostics.Log("EVENT playing -> start background cache");
            StartBackgroundCacheIfIdle();
        }
    }
    private void OnCanPlay()
    {
        _isBuffering = false;
        StateHasChanged();
        if (!_cacheStarted)
        {
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(10000); } catch { return; }
                if (_disposed || _cacheStarted) return;
                _cacheStarted = true;
                CacheDiagnostics.Log("EVENT canplay fallback (10s) -> start background cache");
                StartBackgroundCacheIfIdle();
            });
        }
    }
    private void OnLoadStart() { _isBuffering = true; StateHasChanged(); }

    private async Task TogglePlay()
    {
        try
        {
            await JS.InvokeVoidAsync("videoPlayer.togglePlay");
        }
        catch { }
    }

    private DateTime _lastSeekMoveLog = DateTime.MinValue;

    private void HandleJsSeek(double time, bool committed)
    {
        if (committed)
        {
            _currentTime = time;
            CacheDiagnostics.Log($"SEEK committed t={time:0.##}");
            _ = InvokeAsync(async () =>
            {
                await JS.InvokeVoidAsync("videoPlayer.seekTo", time.ToString(CultureInfo.InvariantCulture));
                if (_cacheStarted)
                    StartBackgroundCacheIfIdle();
                StateHasChanged();
            });
        }
        else
        {
            _currentTime = time;
            if ((DateTime.UtcNow - _lastSeekMoveLog).TotalSeconds > 1)
            {
                _lastSeekMoveLog = DateTime.UtcNow;
                CacheDiagnostics.Log($"SEEK move t={time:0.##}");
            }
            _ = InvokeAsync(StateHasChanged);
        }
    }

    private async Task OnVolumeChange(ChangeEventArgs e)
    {
        if (double.TryParse(e.Value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var v))
        {
            _volume = v;
            await JS.InvokeVoidAsync("videoPlayer.setVolume", v.ToString(CultureInfo.InvariantCulture));
        }
    }

    private async Task Skip(double sec)
    {
        try
        {
            var newTime = Math.Max(0, _currentTime + sec);
            _currentTime = newTime;
            await JS.InvokeVoidAsync("videoPlayer.seekTo", newTime.ToString(CultureInfo.InvariantCulture));
            StateHasChanged();
        }
        catch { }
        ResetIdle();
    }

    private async Task ToggleSpeed()
    {
        _playbackRate = _playbackRate switch { 1 => 1.5, 1.5 => 2, 2 => 3, _ => 1 };
        await JS.InvokeVoidAsync("videoPlayer.setSpeed", _playbackRate.ToString(CultureInfo.InvariantCulture));
        StateHasChanged();
    }

    private async Task ToggleFullscreen()
    {
        await JS.InvokeVoidAsync("videoPlayer.toggleFullscreen");
    }

    private async Task TogglePiP()
    {
        try { await JS.InvokeVoidAsync("videoPlayer.togglePiP"); } catch { }
    }

    private void ToggleDebug()
    {
        _showDebug = !_showDebug;
        if (_showDebug)
            _debugText = CacheDiagnostics.GetRecentLines(90);
        StateHasChanged();
    }

    private async Task DownloadCurrentFile()
    {
        if (string.IsNullOrEmpty(_fileUrl)) return;
        if (Platform.IsWeb)
            Navigation.NavigateTo(_fileUrl);
        else
            await Platform.DownloadFileAsync(_fileUrl, _fileName);
    }

    private void OnActivity() { _lastActivity = DateTime.UtcNow; if (_idle) { _idle = false; StateHasChanged(); } }

    private void ResetIdle()
    {
        _lastActivity = DateTime.UtcNow;
        if (_idle) { _idle = false; InvokeAsync(StateHasChanged); }
    }

    private void StartBackgroundCacheIfIdle()
    {
        if (_disposed || Platform.IsWeb) return;
        if (Interlocked.CompareExchange(ref _cacheFillRunning, 1, 0) != 0) return;
        CacheDiagnostics.Log("CACHE fill start");
        _ = Task.Run(async () =>
        {
            try
            {
                await BackgroundCacheFromCSharpAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _cacheFillRunning, 0);
            }
        });
    }

    private async Task BackgroundCacheFromCSharpAsync()
    {
        if (Platform.IsWeb) return;
        _cacheCts?.Cancel();
        _cacheCts?.Dispose();
        _cacheCts = new CancellationTokenSource();
        var ct = _cacheCts.Token;

        try
        {
            if (await MediaCache.IsCompleteAsync(_cacheKey)) return;

            long totalSize = 0;
            string mimeType = "video/mp4";
            int totalChunks = 0;

            var existingMeta = await MediaCache.GetMetaAsync(_cacheKey);
            if (existingMeta != null && existingMeta.TotalSize > 0)
            {
                totalSize = existingMeta.TotalSize;
                totalChunks = existingMeta.TotalChunks;
                mimeType = existingMeta.MimeType;
                CacheDiagnostics.Log($"CACHE existing meta size={totalSize} chunks={totalChunks}");
            }
            else
            {
                var headDeadline = DateTime.UtcNow.AddSeconds(20);
                while (!ct.IsCancellationRequested && DateTime.UtcNow < headDeadline)
                {
                    var gotSize = false;
                    try
                    {
                        using var headReq = new HttpRequestMessage(HttpMethod.Head, _fileUrl);
                        using var headResp = await Http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                        var len = headResp.Content.Headers.ContentLength;
                        CacheDiagnostics.Log($"CACHE HEAD status={(int)headResp.StatusCode} len={len ?? -1}");
                        if (headResp.IsSuccessStatusCode && (len ?? 0) > 0)
                        {
                            totalSize = len!.Value;
                            mimeType = headResp.Content.Headers.ContentType?.MediaType ?? "video/mp4";
                            gotSize = true;
                        }
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex)
                    {
                        CacheDiagnostics.Log($"CACHE HEAD ERROR {ex.GetType().Name}: {ex.Message}");
                    }
                    if (gotSize) break;
                    try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { break; }
                }

                if (totalSize <= 0 && !ct.IsCancellationRequested)
                {
                    try
                    {
                        using var req = new HttpRequestMessage(HttpMethod.Get, _fileUrl);
                        req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 0);
                        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                        if (resp.IsSuccessStatusCode)
                        {
                            var cr = resp.Content.Headers.ContentRange;
                            if (cr?.HasRange == true)
                                totalSize = cr.Length ?? 0;
                            else if (resp.Content.Headers.ContentLength is { } len && len > 0)
                                totalSize = len;
                            mimeType = resp.Content.Headers.ContentType?.MediaType ?? "video/mp4";
                            CacheDiagnostics.Log($"CACHE HEAD failed -> Range fallback ok size={totalSize}");
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch { }
                }

                if (totalSize <= 0)
                {
                    CacheDiagnostics.Log("CACHE size discovery FAILED, aborting");
                    return;
                }
                totalChunks = (int)Math.Ceiling((double)totalSize / MediaCacheService.ChunkSize);

                await MediaCache.InitMetaAsync(_cacheKey, _fileUrl, totalSize, totalChunks, mimeType);
                CacheDiagnostics.Log($"CACHE meta written chunks={totalChunks} size={totalSize}");
            }

            var posChunk = _duration > 0
                ? (int)Math.Clamp(_currentTime / _duration * totalChunks, 0, totalChunks - 1)
                : 0;

            var fillTask = MediaCache.CacheFromNetworkAsync(
                _cacheKey, _fileUrl, posChunk, totalChunks, totalSize, mimeType,
                onProgress: async cp =>
                {
                    _cacheProgress = cp;
                    await InvokeAsync(StateHasChanged);
                },
                ct: ct, markComplete: false, maxParallel: 2);

            var probeTask = EnsureMoovCachedAsync(ct);

            await Task.WhenAll(fillTask, probeTask);

            if (ct.IsCancellationRequested) return;

            await MediaCache.CacheFromNetworkAsync(
                _cacheKey, _fileUrl, 0, posChunk, totalSize, mimeType,
                onProgress: async cp =>
                {
                    _cacheProgress = cp;
                    await InvokeAsync(StateHasChanged);
                },
                ct: ct, markComplete: true, maxParallel: 2);

            if (ct.IsCancellationRequested) return;

            var finalProgress = await MediaCache.GetProgressAsync(_cacheKey);
            if (finalProgress.Total > 0 && finalProgress.Cached >= finalProgress.Total)
            {
                await MediaCache.MarkCompleteAsync(_cacheKey, mimeType);
                CacheDiagnostics.Log($"CACHE marked complete ({finalProgress.Cached}/{finalProgress.Total} chunks)");
            }
        }
        catch (OperationCanceledException) { }
        catch { }
    }

    private async Task EnsureMoovCachedAsync(CancellationToken ct)
    {
        var meta = await MediaCache.GetMetaAsync(_cacheKey);
        if (meta == null || meta.TotalChunks <= 0) return;
        if (await MediaCache.GetTimeMapAsync(_cacheKey) != null) return;

        const int maxProbes = 8;
        var probes = new List<int> { 0 };
        for (int i = meta.TotalChunks - 1; i >= Math.Max(0, meta.TotalChunks - maxProbes); i--)
            if (i != 0) probes.Add(i);

        foreach (var idx in probes)
        {
            if (ct.IsCancellationRequested) return;
            if (idx < 0 || idx >= meta.TotalChunks) continue;
            var ok = await MediaCache.FetchSingleChunkAsync(_cacheKey, _fileUrl, idx, meta.TotalSize, ct);
            if (await MediaCache.GetTimeMapAsync(_cacheKey) != null)
            {
                CacheDiagnostics.Log($"MOOV found after caching chunk {idx}");
                return;
            }
            CacheDiagnostics.Log($"MOOV probe chunk={idx} ok={ok}");
        }
        CacheDiagnostics.Log("MOOV not found after probes");
    }

    private void StartCachePollTimer()
    {
        _cachePollTimer?.Dispose();
        var ticks = 0;
        _cachePollTimer = new Timer(async _ =>
        {
            if (_disposed) return;
            try
            {
                var result = await MediaCache.GetProgressAsync(_cacheKey);
                await RefreshCachedSegmentsAsync();

                try
                {
                    var state = await JS.InvokeAsync<string>("videoPlayer.getState");
                    var parts = state.Split('|');
                    if (parts.Length >= 5)
                    {
                        _videoError = parts[2];
                        _videoReady = parts[3];
                        _videoNet = parts[4];
                    }
                }
                catch { }

                if (!Platform.IsWeb && _sourceIsProxy && _videoError != "0" && _videoError != "1")
                {
                    _sourceIsProxy = false;
                    _directRetries = 0;
                    CacheDiagnostics.Log($"PROXY fallback to direct (video error code {_videoError})");
                    await JS.InvokeVoidAsync("videoPlayer.switchSource", _fileUrl);
                }
                else if (!Platform.IsWeb && !_sourceIsProxy && _videoError != "0" && _videoError != "1")
                {
                    _directRetries++;
                    if (result.Complete || _directRetries >= 3)
                    {
                        _sourceIsProxy = true;
                        CacheDiagnostics.Log($"DIRECT still failing retries={_directRetries} complete={result.Complete} -> back to PROXY");
                        _directRetries = 0;
                        await JS.InvokeVoidAsync("videoPlayer.switchSource", _proxyUrl);
                    }
                    else
                    {
                        CacheDiagnostics.Log($"DIRECT retry load (err {_videoError}, ready={_videoReady}, net={_videoNet})");
                        try { await JS.InvokeVoidAsync("videoPlayer.retryLoad"); } catch { }
                    }
                }

                if (result.Cached != _chunksCachedLogged)
                {
                    _chunksCachedLogged = result.Cached;
                    CacheDiagnostics.Log($"POLL cached={result.Cached}/{result.Total} complete={result.Complete} err={_videoError} ready={_videoReady} net={_videoNet} dur={_duration:0.##} segs={_cachedSegments.Count}");
                }

                _cacheProgress = result;
                if (result.Complete)
                {
                    _cachePollTimer?.Dispose();
                    _cachePollTimer = null;
                }

                if (_showDebug)
                    _debugText = CacheDiagnostics.GetRecentLines(90);

                if (++ticks % 5 == 0)
                    CacheDiagnostics.Log($"POLL tick cached={result.Cached} complete={result.Complete} err={_videoError} dur={_duration:0.##}");
                await InvokeAsync(StateHasChanged);
            }
            catch { }
        }, null, 2000, 3000);
    }

    private async Task RefreshCachedSegmentsAsync()
    {
        if (Platform.IsWeb)
        {
            _cachedSegments = [];
            return;
        }
        try
        {
            var ranges = await MediaCache.GetCachedChunkRangesAsync(_cacheKey);
            if (ranges.Count == 0)
            {
                _cachedSegments = [];
                return;
            }
            if (_timeMap == null && (DateTime.UtcNow - _lastTimeMapAttempt).TotalSeconds > 10)
            {
                _lastTimeMapAttempt = DateTime.UtcNow;
                _timeMap = await MediaCache.GetTimeMapAsync(_cacheKey);
                CacheDiagnostics.Log($"REFRESH timemap={( _timeMap != null ? $"count={_timeMap.Count}" : "NULL" )} ranges={ranges.Count}");
            }
            var meta = await MediaCache.GetMetaAsync(_cacheKey);
            var total = meta?.TotalSize ?? 0;
            var dur = _duration > 0 ? _duration : meta?.Duration ?? 0;
            var segs = new VideoSegmentCollection();
            foreach (var (s, e) in ranges)
            {
                var byteStart = s * MediaCacheService.ChunkSize;
                var byteEnd = Math.Min((e + 1) * MediaCacheService.ChunkSize, total);
                if (byteEnd <= byteStart) continue;
                double startT = 0, endT = 0;
                if (_timeMap != null)
                {
                    startT = _timeMap.GetSyncTimeAtOrAfter(byteStart);
                    endT = _timeMap.MapTimeAt(byteEnd);
                }
                else if (total > 0 && dur > 0)
                {
                    startT = byteStart / (double)total * dur;
                    endT = byteEnd / (double)total * dur;
                }
                segs.Add(new VideoSegmentModel { Start = startT, End = endT });
            }
            _cachedSegments = segs;
        }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"REFRESH ERROR: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void StartIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = new Timer(_ =>
        {
            if (_disposed) return;
            if (!_isPaused && (DateTime.UtcNow - _lastActivity).TotalSeconds >= 3 && !_idle)
            {
                _idle = true;
                InvokeAsync(StateHasChanged);
            }
        }, null, 1000, 1000);
    }

    private async Task Close()
    {
        _cacheCts?.Cancel();
        _cachePollTimer?.Dispose();
        _cachePollTimer = null;
        _idleTimer?.Dispose();
        try { await JS.InvokeVoidAsync("videoPlayer.stopPump"); } catch { }
        try { await JS.InvokeVoidAsync("videoPlayer.destroy"); } catch { }
        NavState.IsFullScreen = false;
        NavState.CurrentFileUrl = null;
        NavState.CurrentFileName = null;
        NavState.PageTitle = "NMU-CE & AIE";
        Navigation.NavigateTo($"recorded/{Uri.EscapeDataString(_group)}", replace: true);
    }

    public void Dispose()
    {
        _disposed = true;
        VideoPlayerSeekHub.Seek -= HandleJsSeek;
        VideoPlayerSeekHub.Tick -= HandleTick;
        _cacheCts?.Cancel();
        _cacheCts?.Dispose();
        _cachePollTimer?.Dispose();
        _idleTimer?.Dispose();
    }
}
