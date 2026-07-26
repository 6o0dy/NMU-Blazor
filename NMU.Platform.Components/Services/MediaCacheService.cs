using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NMU.Platform.Components.Services;

public class MediaCacheService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const long ChunkSize = 524288L;

    public MediaCacheService(HttpClient http) => _http = http;

    public string GetCacheKey(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexStringLower(hash)[..32];
    }

    private static string GetBasePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NMU", "MediaCache");

    private string GetCacheDir(string key) =>
        Path.Combine(GetBasePath(), key);

    public async Task<CacheMeta?> GetMetaAsync(string key)
    {
        var path = Path.Combine(GetCacheDir(key), ".meta");
        if (!File.Exists(path)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<CacheMeta>(json, JsonOpts);
        }
        catch { return null; }
    }

    public Task<bool> IsCompleteAsync(string key)
    {
        var dir = GetCacheDir(key);
        return Task.FromResult(Directory.Exists(dir) && File.Exists(Path.Combine(dir, ".complete")));
    }

    public Task<bool> HasAnyCacheAsync(string key)
    {
        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return Task.FromResult(false);
        return Task.FromResult(
            File.Exists(Path.Combine(dir, ".complete")) ||
            (File.Exists(Path.Combine(dir, ".meta")) && Directory.GetFiles(dir, "chunk_*").Length > 0));
    }

    public async Task<CacheProgress> GetProgressAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return new CacheProgress(0, 0, false, 0);

        var complete = await IsCompleteAsync(key);
        var dir = GetCacheDir(key);
        var cached = 0;
        if (Directory.Exists(dir))
        {
            for (int i = 0; i < meta.TotalChunks; i++)
            {
                if (File.Exists(Path.Combine(dir, $"chunk_{i:D6}")))
                    cached++;
                else
                    break;
            }
        }

        return new CacheProgress(cached, meta.TotalChunks, complete, meta.TotalSize);
    }

    public async Task InitMetaAsync(string key, string url, long totalSize, int totalChunks, string mimeType)
    {
        var dir = GetCacheDir(key);
        Directory.CreateDirectory(dir);
        var meta = new CacheMeta
        {
            Url = url,
            TotalSize = totalSize,
            TotalChunks = totalChunks,
            MimeType = mimeType,
            NextChunk = 0,
            Timestamp = DateTime.UtcNow
        };
        var json = JsonSerializer.Serialize(meta, JsonOpts);
        await File.WriteAllTextAsync(Path.Combine(dir, ".meta"), json);
    }

    public async Task UpdateProgressAsync(string key, int nextChunk)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return;
        meta = meta with { NextChunk = nextChunk, Timestamp = DateTime.UtcNow };
        var json = JsonSerializer.Serialize(meta, JsonOpts);
        await File.WriteAllTextAsync(Path.Combine(GetCacheDir(key), ".meta"), json);
    }

    public async Task StoreChunkAsync(string key, int index, byte[] data)
    {
        var dir = GetCacheDir(key);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"chunk_{index:D6}");
        await File.WriteAllBytesAsync(path, data);
    }

    public Task<bool> ChunkExistsAsync(string key, int index)
    {
        var path = Path.Combine(GetCacheDir(key), $"chunk_{index:D6}");
        return Task.FromResult(File.Exists(path));
    }

    public async Task MarkCompleteAsync(string key, string mimeType)
    {
        var dir = GetCacheDir(key);
        await File.WriteAllTextAsync(Path.Combine(dir, ".complete"), mimeType);
        await UpdateProgressAsync(key, int.MaxValue);
    }

    public async Task<List<byte[]>?> ReadAllChunksAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return null;

        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return null;

        var chunks = new List<byte[]>();
        for (int i = 0; i < meta.TotalChunks; i++)
        {
            var path = Path.Combine(dir, $"chunk_{i:D6}");
            if (!File.Exists(path)) return null;
            chunks.Add(await File.ReadAllBytesAsync(path));
        }
        return chunks;
    }

    public async Task<List<byte[]>?> ReadAvailableChunksAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return null;

        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return null;

        var chunks = new List<byte[]>();
        for (int i = 0; i < meta.TotalChunks; i++)
        {
            var path = Path.Combine(dir, $"chunk_{i:D6}");
            if (!File.Exists(path)) break;
            chunks.Add(await File.ReadAllBytesAsync(path));
        }
        return chunks.Count > 0 ? chunks : null;
    }

    public async Task<Dictionary<int, byte[]>> ReadAllCachedChunksAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return [];

        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return [];

        var result = new Dictionary<int, byte[]>();
        for (int i = 0; i < meta.TotalChunks; i++)
        {
            var path = Path.Combine(dir, $"chunk_{i:D6}");
            if (File.Exists(path))
                result[i] = await File.ReadAllBytesAsync(path);
        }
        return result;
    }

    public async Task CacheFromNetworkAsync(string key, string url, int startChunk, int totalChunks, long totalSize, string mimeType, Func<CacheProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        var anyCached = false;
        for (int i = startChunk; i < totalChunks; i++)
        {
            if (ct.IsCancellationRequested) return;

            if (await ChunkExistsAsync(key, i)) continue;

            var chunkStart = i * ChunkSize;
            var chunkEnd = Math.Min(chunkStart + ChunkSize - 1, totalSize - 1);

            byte[]? bytes = null;
            for (int retry = 0; retry < 3; retry++)
            {
                if (ct.IsCancellationRequested) return;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(chunkStart, chunkEnd);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                    if (!resp.IsSuccessStatusCode)
                    {
                        await Task.Delay(1000 * (1 << retry), ct);
                        continue;
                    }
                    bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                    break;
                }
                catch when (!ct.IsCancellationRequested)
                {
                    if (retry < 2) await Task.Delay(1000 * (1 << retry), ct);
                }
            }

            if (bytes == null) continue;

            anyCached = true;
            await StoreChunkAsync(key, i, bytes);
            await UpdateProgressAsync(key, i + 1);

            if (onProgress != null)
                await onProgress(new CacheProgress(i + 1, totalChunks, false, totalSize));
        }

        if (!ct.IsCancellationRequested && anyCached)
        {
            await MarkCompleteAsync(key, mimeType);
            if (onProgress != null)
                await onProgress(new CacheProgress(totalChunks, totalChunks, true, totalSize));
        }
    }

    public Task ClearAsync(string key)
    {
        var dir = GetCacheDir(key);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        var dir = GetBasePath();
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        return Task.CompletedTask;
    }

    public async Task<bool> IsCachePlayableAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return false;
        if (await IsCompleteAsync(key)) return true;

        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return false;

        var hasBeginning = false;
        var tail = "";

        for (int i = 0; i < meta.TotalChunks; i++)
        {
            var path = Path.Combine(dir, $"chunk_{i:D6}");
            if (!File.Exists(path))
            {
                tail = "";
                continue;
            }
            if (i == 0) hasBeginning = true;
            var chunk = await File.ReadAllBytesAsync(path);
            var text = System.Text.Encoding.ASCII.GetString(chunk);
            if ((tail + text).Contains("moov") && hasBeginning)
                return true;
            tail = text.Length >= 3 ? text[^3..] : text;
        }
        return false;
    }

    public async Task<bool> PriorityCacheLastChunkAsync(string key, string url, long totalSize, int totalChunks, CancellationToken ct = default)
    {
        var lastIdx = totalChunks - 1;
        if (await ChunkExistsAsync(key, lastIdx)) return true;

        var lastStart = lastIdx * ChunkSize;
        var lastEnd = Math.Min(lastStart + ChunkSize - 1, totalSize - 1);

        for (int retry = 0; retry < 3; retry++)
        {
            if (ct.IsCancellationRequested) return false;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(lastStart, lastEnd);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    await Task.Delay(1000 * (1 << retry), ct);
                    continue;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                await StoreChunkAsync(key, lastIdx, bytes);
                return true;
            }
            catch when (!ct.IsCancellationRequested)
            {
                if (retry < 2) await Task.Delay(1000 * (1 << retry), ct);
            }
        }
        return false;
    }

    public async Task<List<(string Name, string Status)>> GetStatusForFilesAsync(IEnumerable<string> fileUrls)
    {
        var results = new List<(string Name, string Status)>();
        foreach (var url in fileUrls)
        {
            var key = GetCacheKey(url);
            var complete = await IsCompleteAsync(key);
            var partial = !complete && await HasAnyCacheAsync(key);
            var status = complete ? "✓" : partial ? "⏳" : "";
            results.Add((url, status));
        }
        return results;
    }
}

public record CacheMeta
{
    public string Url { get; init; } = "";
    public long TotalSize { get; init; }
    public int TotalChunks { get; init; }
    public string MimeType { get; init; } = "";
    public int NextChunk { get; init; }
    public DateTime Timestamp { get; init; }
}

public record CacheProgress(int Cached, int Total, bool Complete, long Bytes);
