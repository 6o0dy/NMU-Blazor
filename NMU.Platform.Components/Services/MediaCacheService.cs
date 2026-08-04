using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NMU.Platform.Components.Services;

public class MediaCacheService
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const long ChunkSize = 524288L;
    private static readonly ConcurrentDictionary<string, Mp4TimeMap> TimeMaps = new();

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
            cached = Directory.GetFiles(dir, "chunk_*").Length;

        return new CacheProgress(cached, meta.TotalChunks, complete, meta.TotalSize);
    }

    public async Task<Mp4TimeMap?> GetTimeMapAsync(string key)
    {
        if (TimeMaps.TryGetValue(key, out var cached)) return cached;
        var meta = await GetMetaAsync(key);
        if (meta == null || meta.TotalSize <= 0) return null;
        try
        {
            var map = await Mp4SampleTable.TryParseFromCacheAsync(GetCacheDir(key), meta.TotalSize);
            if (map != null && map.Count > 0)
            {
                TimeMaps.TryAdd(key, map);
                CacheDiagnostics.Log($"TIMEMAP built count={map.Count}");
            }
            else
            {
                CacheDiagnostics.Log("TIMEMAP null (moov not cached yet)");
            }
            return map;
        }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"TIMEMAP ERROR {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public async Task<List<(int Start, int End)>> GetCachedChunkRangesAsync(string key)
    {
        var meta = await GetMetaAsync(key);
        if (meta == null) return [];
        var dir = GetCacheDir(key);
        if (!Directory.Exists(dir)) return [];
        var result = new List<(int, int)>();
        int i = 0;
        while (i < meta.TotalChunks)
        {
            if (!File.Exists(Path.Combine(dir, $"chunk_{i:D6}"))) { i++; continue; }
            var start = i;
            while (i < meta.TotalChunks && File.Exists(Path.Combine(dir, $"chunk_{i:D6}"))) i++;
            result.Add((start, i - 1));
        }
        return result;
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

    public async Task SetDurationAsync(string key, double duration)
    {
        if (duration <= 0) return;
        var meta = await GetMetaAsync(key);
        if (meta == null) return;
        if (Math.Abs(meta.Duration - duration) < 0.5) return;
        meta = meta with { Duration = duration, Timestamp = DateTime.UtcNow };
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

    public async Task CacheFromNetworkAsync(string key, string url, int startChunk, int totalChunks, long totalSize, string mimeType, Func<CacheProgress, Task>? onProgress = null, CancellationToken ct = default, bool markComplete = true, int maxParallel = 2)
    {
        var cachedCount = 0;

        await Parallel.ForAsync(startChunk, totalChunks, new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct
        }, async (i, innerCt) =>
        {
            if (innerCt.IsCancellationRequested) return;

            if (await ChunkExistsAsync(key, i))
            {
                Interlocked.Increment(ref cachedCount);
                return;
            }

            var chunkStart = i * ChunkSize;
            var chunkEnd = Math.Min(chunkStart + ChunkSize - 1, totalSize - 1);

            byte[]? bytes = null;
            for (int retry = 0; retry < 3; retry++)
            {
                if (innerCt.IsCancellationRequested) return;
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(chunkStart, chunkEnd);
                    using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, innerCt);
                    if (!resp.IsSuccessStatusCode)
                    {
                        try { await Task.Delay(1000 * (1 << retry), innerCt); } catch (OperationCanceledException) { return; }
                        continue;
                    }
                    bytes = await resp.Content.ReadAsByteArrayAsync(innerCt);
                    break;
                }
                catch (OperationCanceledException) { return; }
                catch
                {
                    if (retry < 2)
                    {
                        try { await Task.Delay(1000 * (1 << retry), innerCt); } catch (OperationCanceledException) { return; }
                    }
                }
            }

            if (bytes == null) return;

            await StoreChunkAsync(key, i, bytes);
            Interlocked.Increment(ref cachedCount);
        });

        if (!ct.IsCancellationRequested && markComplete && cachedCount > 0)
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
        TimeMaps.TryRemove(key, out _);
        Mp4SampleTable.ResetScanCache();
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        var dir = GetBasePath();
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        TimeMaps.Clear();
        Mp4SampleTable.ResetScanCache();
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
                    try { await Task.Delay(1000 * (1 << retry), ct); } catch (OperationCanceledException) { return false; }
                    continue;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                await StoreChunkAsync(key, lastIdx, bytes);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch
            {
                if (retry < 2)
                {
                    try { await Task.Delay(1000 * (1 << retry), ct); } catch (OperationCanceledException) { return false; }
                }
            }
        }
        return false;
    }

    private static readonly ConcurrentDictionary<string, Task<bool>> InFlightChunks = new();

    public async Task<bool> FetchSingleChunkAsync(string key, string url, int index, long totalSize, CancellationToken ct = default)
    {
        if (await ChunkExistsAsync(key, index)) return true;

        var id = $"{key}:{index}";
        var task = InFlightChunks.GetOrAdd(id, _ => FetchChunkCoreAsync(key, url, index, totalSize, ct));
        try
        {
            return await task;
        }
        finally
        {
            InFlightChunks.TryRemove(id, out _);
        }
    }

    private async Task<bool> FetchChunkCoreAsync(string key, string url, int index, long totalSize, CancellationToken ct)
    {
        var chunkStart = index * ChunkSize;
        var chunkEnd = Math.Min(chunkStart + ChunkSize - 1, totalSize - 1);

        for (int retry = 0; retry < 3; retry++)
        {
            if (ct.IsCancellationRequested) return false;
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(chunkStart, chunkEnd);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    try { await Task.Delay(1000 * (1 << retry), ct); } catch (OperationCanceledException) { return false; }
                    continue;
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
                await StoreChunkAsync(key, index, bytes);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch
            {
                if (retry < 2)
                {
                    try { await Task.Delay(1000 * (1 << retry), ct); } catch (OperationCanceledException) { return false; }
                }
            }
        }
        return false;
    }

    public async Task WarmUpAsync()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://archive.org/");
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        }
        catch { }
    }

    public async Task<bool> PriorityCacheFirstChunksAsync(string key, string url, long totalSize, int count, CancellationToken ct = default)
    {
        var tasks = new List<Task<bool>>();
        for (int i = 0; i < count; i++)
        {
            tasks.Add(FetchSingleChunkAsync(key, url, i, totalSize, ct));
        }
        var results = await Task.WhenAll(tasks);
        return results.All(r => r);
    }

    public async Task<bool> PriorityCacheChunksAsync(string key, string url, long totalSize,
        IEnumerable<int> indices, CancellationToken ct = default, int maxParallel = 3)
    {
        var distinct = indices.Distinct().ToList();
        if (distinct.Count == 0) return true;
        var ok = 0;
        var total = 0;
        await Parallel.ForEachAsync(distinct, new ParallelOptions
        {
            MaxDegreeOfParallelism = maxParallel,
            CancellationToken = ct
        }, async (idx, innerCt) =>
        {
            Interlocked.Increment(ref total);
            if (await FetchSingleChunkAsync(key, url, idx, totalSize, innerCt))
                Interlocked.Increment(ref ok);
        });
        return ok == total;
    }

    public async Task<bool> PriorityCacheFirstChunkAsync(string key, string url, long totalSize, CancellationToken ct = default)
    {
        return await PriorityCacheFirstChunksAsync(key, url, totalSize, 1, ct);
    }

    private static readonly ConcurrentDictionary<string, Task<bool>> CriticalPrefetchTasks = new();

    public Task<bool> PrefetchCriticalAsync(string url, CancellationToken ct = default)
    {
        var key = GetCacheKey(url);
        return CriticalPrefetchTasks.GetOrAdd(key, k => CorePrefetchCriticalAsync(k, url, ct));
    }

    private async Task<bool> CorePrefetchCriticalAsync(string key, string url, CancellationToken ct)
    {
        try
        {
            var meta = await GetMetaAsync(key);
            if (meta == null || meta.TotalChunks <= 0)
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResp = await _http.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead, ct);
                var len = headResp.Content.Headers.ContentLength;
                if (!headResp.IsSuccessStatusCode || (len ?? 0) <= 0) return false;
                var mime = headResp.Content.Headers.ContentType?.MediaType ?? "video/mp4";
                var chunks = (int)Math.Ceiling((double)len!.Value / ChunkSize);
                await InitMetaAsync(key, url, len.Value, chunks, mime);
                meta = await GetMetaAsync(key);
            }
            if (meta == null || meta.TotalChunks <= 0) return false;

            var targets = new List<int> { 0 };
            for (int i = meta.TotalChunks - 1; i >= Math.Max(0, meta.TotalChunks - 5); i--)
                if (i != 0) targets.Add(i);
            for (int i = 1; i < 8 && i < meta.TotalChunks; i++)
                targets.Add(i);

            var ok = await PriorityCacheChunksAsync(key, meta.Url, meta.TotalSize, targets, ct, maxParallel: 12);
            CacheDiagnostics.Log($"PREFETCH critical key={key} targets={targets.Count} ok={ok}");
            return ok;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception ex)
        {
            CacheDiagnostics.Log($"PREFETCH ERROR {key}: {ex.Message}");
            return false;
        }
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
    public double Duration { get; init; }
    public DateTime Timestamp { get; init; }
}

public record CacheProgress(int Cached, int Total, bool Complete, long Bytes);
