using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.DependencyInjection;

namespace NMU.Platform.Components.Services;

public class MediaProxyHost(IServiceProvider serviceProvider)
{
    private LocalMediaProxyServer? _server;
    private readonly object _lock = new();

    public int Port => _server?.Port ?? -1;
    public bool IsRunning => _server != null;

    public int EnsureRunning()
    {
        if (_server != null) return _server.Port;
        lock (_lock)
        {
            if (_server != null) return _server.Port;
            _server = new LocalMediaProxyServer(serviceProvider);
            _server.Start();
            return _server.Port;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            _server?.Stop();
            _server = null;
        }
    }
}

public class LocalMediaProxyServer(IServiceProvider serviceProvider) : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private const long ChunkSize = MediaCacheService.ChunkSize;
    private static readonly ConcurrentDictionary<string, Task<bool>> CriticalPrefetchTasks = new();

    public int Port { get; private set; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        CacheDiagnostics.Log($"PROXY listening on 127.0.0.1:{Port}");
        _ = WarmUpArchiveOrgAsync();
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task WarmUpArchiveOrgAsync()
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var http = scope.ServiceProvider.GetRequiredService<HttpClient>();
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://archive.org/");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        }
        catch { }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        try { _listener?.Stop(); } catch { }
    }

    public void Dispose() => Stop();

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        var listener = _listener;
        if (listener == null) return;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (ObjectDisposedException) { break; }
            catch (OperationCanceledException) { break; }
            catch { }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        client.ReceiveTimeout = 15000;
        client.SendTimeout = 300000;

        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, true))
        {
            string? requestLine;
            try
            {
                requestLine = await reader.ReadLineAsync();
            }
            catch { return; }
            if (requestLine == null) return;

            var parts = requestLine.Split(' ');
            if (parts.Length < 2) return;
            var path = parts[1];

            long rangeStart = 0;
            bool hasRange = false;
            string? cacheKey = null;

            string? line;
            while ((line = await reader.ReadLineAsync()) != null && line != "")
            {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                {
                    hasRange = true;
                    var range = line["Range:".Length..].Trim();
                    var rangeParts = range.Replace("bytes=", "").Split('-');
                    if (rangeParts.Length > 0 && long.TryParse(rangeParts[0], out var rs))
                        rangeStart = rs;
                }
            }

            var qIdx = path.IndexOf('?');
            string? urlParam = null;
            if (qIdx >= 0)
            {
                var qs = path[(qIdx + 1)..];
                path = path[..qIdx];
                foreach (var p in qs.Split('&'))
                {
                    if (p.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                        urlParam = Uri.UnescapeDataString(p[4..]);
                }
            }
            if (path.StartsWith("/media/"))
                cacheKey = path[7..];

            if (cacheKey == null)
            {
                CacheDiagnostics.Log($"PROXY 400 bad path: {path}");
                await SendText(stream, 400, "Bad Request");
                return;
            }

            var meta = await ReadMetaAsync(cacheKey);
            if (meta == null || meta.TotalSize <= 0)
            {
                if (urlParam != null)
                {
                    var resolved = await ResolveMetaAsync(cacheKey, urlParam, client, stream, rangeStart, ct);
                    meta = resolved.meta;
                    if (resolved.streamed) return;
                }
                if (meta == null)
                {
                    CacheDiagnostics.Log($"PROXY 404 no meta for {cacheKey}");
                    await SendText(stream, 404, "Not Found");
                    return;
                }
            }

            var cacheDir = Path.Combine(MediaCacheService.GetMediaCacheBasePath(), cacheKey);

            _ = StartCriticalPrefetchAsync(cacheKey, meta);

            var startChunk = (int)(rangeStart / ChunkSize);
            var chunkPath = Path.Combine(cacheDir, $"chunk_{startChunk:D6}");

            if (!File.Exists(chunkPath))
            {
                CacheDiagnostics.Log($"PROXY window fetch (persistent) from {startChunk} key {cacheKey}");
                var last = Math.Min(meta.TotalChunks - 1, startChunk + 3);
                using var scope = serviceProvider.CreateScope();
                var cache = scope.ServiceProvider.GetRequiredService<MediaCacheService>();
                for (int i = startChunk; i <= last && !ct.IsCancellationRequested && !File.Exists(chunkPath); i++)
                {
                    if (IsClientGone(client)) return;
                    await cache.FetchSingleChunkAsync(cacheKey, meta.Url, i, meta.TotalSize, ct);
                }
            }

            try
            {
                using var ms = new MemoryStream();
                var pos = rangeStart;
                var chunk = startChunk;
                var servedChunks = 0;
                const int maxChunks = 2;
                while (chunk < meta.TotalChunks && servedChunks < maxChunks)
                {
                    var cp = Path.Combine(cacheDir, $"chunk_{chunk:D6}");
                    if (!File.Exists(cp)) break;
                    using var fs = new FileStream(cp, FileMode.Open, FileAccess.Read, FileShare.Read);
                    var chunkStart = chunk * ChunkSize;
                    var offset = (int)Math.Max(0, pos - chunkStart);
                    fs.Seek(offset, SeekOrigin.Begin);
                    var remaining = (int)(fs.Length - offset);
                    if (remaining <= 0) break;
                    var buf = new byte[remaining];
                    var read = await fs.ReadAsync(buf, 0, remaining, ct);
                    if (read <= 0) break;
                    ms.Write(buf, 0, read);
                    pos += read;
                    servedChunks++;
                    if (offset > 0) break;
                    chunk++;
                }

                var data = ms.ToArray();
                if (data.Length > 0)
                {
                    await SendResponse(stream, 206, "Partial Content",
                        $"bytes {rangeStart}-{rangeStart + data.Length - 1}/{meta.TotalSize}",
                        meta.MimeType, data, data.Length, hasRange);
                    CacheDiagnostics.Log($"PROXY 206 {requestLine} -> {rangeStart}+{data.Length}/{meta.TotalSize} chunks={servedChunks}");
                }
                else
                {
                    await SendText(stream, 404, "Not Found");
                    CacheDiagnostics.Log($"PROXY 404 empty window {requestLine}");
                }
            }
            catch (Exception ex)
            {
                CacheDiagnostics.Log($"PROXY ERROR {requestLine}: {ex.Message}");
            }
        }
    }

    private async Task SendText(NetworkStream stream, int code, string text)
    {
        using var w = new StreamWriter(stream, Encoding.ASCII, 256, true);
        await w.WriteLineAsync($"HTTP/1.1 {code} {text}");
        await w.WriteLineAsync("Accept-Ranges: bytes");
        await w.WriteLineAsync("Content-Length: 0");
        await w.WriteLineAsync("Connection: close");
        await w.WriteLineAsync();
        await w.FlushAsync();
    }

    private static async Task SendResponseHeaders(NetworkStream stream, int code, string text,
        string contentRange, string? contentType, long dataLen, bool hasRange = true)
    {
        using var w = new StreamWriter(stream, Encoding.ASCII, 512, true);
        await w.WriteLineAsync($"HTTP/1.1 {code} {text}");
        await w.WriteLineAsync("Accept-Ranges: bytes");
        await w.WriteLineAsync($"Content-Range: {contentRange}");
        if (contentType != null)
            await w.WriteLineAsync($"Content-Type: {contentType}");
        await w.WriteLineAsync($"Content-Length: {dataLen}");
        await w.WriteLineAsync("Connection: close");
        await w.WriteLineAsync("Access-Control-Allow-Origin: *");
        await w.WriteLineAsync();
        await w.FlushAsync();
    }

    private static async Task SendResponse(NetworkStream stream, int code, string text,
        string contentRange, string? contentType, byte[]? data = null, int dataLen = 0, bool hasRange = true)
    {
        await SendResponseHeaders(stream, code, text, contentRange, contentType, data != null ? dataLen : 0, hasRange);
        if (data != null && dataLen > 0)
            await stream.WriteAsync(data, 0, dataLen);
        await stream.FlushAsync();
    }



    private async Task<(CacheMeta? meta, bool streamed)> ResolveMetaAsync(string key, string url, TcpClient client, NetworkStream stream, long rangeStart, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(2);
        while (!ct.IsCancellationRequested && DateTime.UtcNow < deadline)
        {
            if (IsClientGone(client)) return (null, false);
            try
            {
                using var scope = serviceProvider.CreateScope();
                var http = scope.ServiceProvider.GetRequiredService<HttpClient>();
                var cache = scope.ServiceProvider.GetRequiredService<MediaCacheService>();

                long? total = null;
                string mime = "video/mp4";
                try
                {
                    using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                    using var headResp = await http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (headResp.IsSuccessStatusCode)
                        total = headResp.Content.Headers.ContentLength;
                    if (headResp.Content.Headers.ContentType?.MediaType is { } mt)
                        mime = mt;
                }
                catch { }

                if ((total ?? 0) <= 0)
                {
                    using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
                    rangeReq.Headers.Range = new RangeHeaderValue(0, 0);
                    using var rangeResp = await http.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!rangeResp.IsSuccessStatusCode)
                    {
                        try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return (null, false); }
                        continue;
                    }
                    var cr = rangeResp.Content.Headers.ContentRange;
                    total = cr?.Length ?? rangeResp.Content.Headers.ContentLength;
                    if ((total ?? 0) <= 0)
                    {
                        try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return (null, false); }
                        continue;
                    }
                    if (rangeResp.Content.Headers.ContentType?.MediaType is { } mt2)
                        mime = mt2;
                    CacheDiagnostics.Log($"PROXY meta size via Range 0..0 key={key} size={total}");
                }

                var chunks = (int)Math.Ceiling((double)total!.Value / ChunkSize);

                await cache.InitMetaAsync(key, url, total.Value, chunks, mime);
                await cache.FetchSingleChunkAsync(key, url, 0, total.Value, ct);
                if (chunks > 1)
                {
                    var tailCount = MediaCacheService.TailChunkCount(chunks);
                    var tailStart = Math.Max(0, chunks - tailCount);
                    var tail = Enumerable.Range(tailStart, chunks - tailStart).ToList();
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope2 = serviceProvider.CreateScope();
                            var cache2 = scope2.ServiceProvider.GetRequiredService<MediaCacheService>();
                            await cache2.PriorityCacheChunksAsync(key, url, total.Value, tail, ct, maxParallel: 4);
                            CacheDiagnostics.Log($"PROXY tail prefetch done key={key} tail={tail.Count} from={tailStart}");
                        }
                        catch { }
                    });
                }
                CacheDiagnostics.Log($"PROXY meta resolved key={key} size={total} chunks={chunks}");
                return (await ReadMetaAsync(key), false);
            }
            catch (OperationCanceledException) { return (null, false); }
            catch
            {
                try { await Task.Delay(2000, ct); } catch (OperationCanceledException) { return (null, false); }
            }
        }
        return (null, false);
    }

    private Task<bool>? StartCriticalPrefetchAsync(string key, CacheMeta meta)
    {
        if (meta.TotalChunks <= 0) return null;
        return CriticalPrefetchTasks.GetOrAdd(key, k => Task.Run(async () =>
        {
            var ok = await PrefetchCriticalAsync(k, meta);
            if (!ok) CriticalPrefetchTasks.TryRemove(key, out _);
            return ok;
        }));
    }

    private async Task<bool> PrefetchCriticalAsync(string key, CacheMeta meta)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var cache = scope.ServiceProvider.GetRequiredService<MediaCacheService>();
            var ok = await cache.PrefetchCriticalAsync(meta.Url);
            CacheDiagnostics.Log($"PROXY prefetch critical key={key} ok={ok}");
            return ok;
        }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"PROXY prefetch ERROR key={key}: {ex.Message}");
            return false;
        }
    }

    private static bool IsClientGone(TcpClient client)
    {
        try
        {
            return !client.Connected ||
                   (client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0);
        }
        catch { return true; }
    }

    private static async Task<CacheMeta?> ReadMetaAsync(string key)
    {
        var path = Path.Combine(MediaCacheService.GetMediaCacheBasePath(), key, ".meta");
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            var meta = System.Text.Json.JsonSerializer.Deserialize<CacheMeta>(json,
                new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
            if (meta == null || meta.Version != MediaCacheService.CacheVersion) return null;
            return meta;
        }
        catch { return null; }
    }
}
