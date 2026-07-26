using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NMU.Platform.Components.Services;

public class MediaProxyHost
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
            _server = new LocalMediaProxyServer();
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

public class LocalMediaProxyServer : IDisposable
{
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private const long ChunkSize = 524288L;

    public int Port { get; private set; }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = AcceptLoopAsync(_cts.Token);
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
        client.SendTimeout = 15000;

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

            if (path.StartsWith("/media/"))
                cacheKey = path[7..];

            if (cacheKey == null)
            {
                await SendText(stream, 400, "Bad Request");
                return;
            }

            var meta = await ReadMetaAsync(cacheKey);
            if (meta == null || meta.TotalSize <= 0)
            {
                await SendText(stream, 404, "Not Found");
                return;
            }

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NMU", "MediaCache", cacheKey);

            var startChunk = (int)(rangeStart / ChunkSize);
            var chunkPath = Path.Combine(cacheDir, $"chunk_{startChunk:D6}");

            if (!File.Exists(chunkPath))
            {
                await SendResponse(stream, 416, "Range Not Satisfiable",
                    $"bytes */{meta.TotalSize}", null, null, 0, hasRange);
                return;
            }

            using var fs = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var chunkStart = startChunk * ChunkSize;
            var offset = (int)(rangeStart - chunkStart);
            fs.Seek(offset, SeekOrigin.Begin);

            var remaining = (int)(fs.Length - offset);
            var buf = new byte[remaining];
            var read = await fs.ReadAsync(buf, 0, remaining, ct);

            await SendResponse(stream, 206, "Partial Content",
                $"bytes {rangeStart}-{rangeStart + read - 1}/{meta.TotalSize}",
                meta.MimeType, buf, read, hasRange);
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

    private static async Task SendResponse(NetworkStream stream, int code, string text,
        string contentRange, string? contentType, byte[]? data = null, int dataLen = 0, bool hasRange = true)
    {
        using var w = new StreamWriter(stream, Encoding.ASCII, 512, true);
        await w.WriteLineAsync($"HTTP/1.1 {code} {text}");
        await w.WriteLineAsync("Accept-Ranges: bytes");
        if (hasRange)
            await w.WriteLineAsync($"Content-Range: {contentRange}");
        if (contentType != null)
            await w.WriteLineAsync($"Content-Type: {contentType}");
        await w.WriteLineAsync($"Content-Length: {(data != null ? dataLen : 0)}");
        await w.WriteLineAsync("Connection: close");
        await w.WriteLineAsync("Access-Control-Allow-Origin: *");
        await w.WriteLineAsync();
        await w.FlushAsync();
        if (data != null && dataLen > 0)
            await stream.WriteAsync(data, 0, dataLen);
        await stream.FlushAsync();
    }



    private static async Task<CacheMeta?> ReadMetaAsync(string key)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NMU", "MediaCache", key, ".meta");
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return System.Text.Json.JsonSerializer.Deserialize<CacheMeta>(json,
                new System.Text.Json.JsonSerializerOptions
                { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
        }
        catch { return null; }
    }
}
