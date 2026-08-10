using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class RecordedService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly ILogger<RecordedService> _logger;
    private const string ArchiveId = "nmu.ce";
    private const string BaseFolder = "NMU";
    private const string CacheVersion = "v1_recorded_";
    private const string GroupsCacheVersion = "v1_rec_groups_";

    public RecordedService(IJSRuntime js, HttpClient http, ILogger<RecordedService> logger)
    {
        _js = js;
        _http = http;
        _logger = logger;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<RecordedFile>> _filesMemCache = new();

    public async Task<List<RecordedFile>> GetFilesAsync(string level, string semester)
    {
        var memKey = $"{level}_{semester}";
        if (_filesMemCache.TryGetValue(memKey, out var mem) && mem != null)
            return mem;

        var cacheKey = $"{CacheVersion}{level}_{semester}";
        var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<RecordedFile>>(cached, CaseInsensitive);
                if (parsed != null && parsed.Count > 0)
                {
                    _filesMemCache[memKey] = parsed;
                    return parsed;
                }
            }
            catch { }
        }

        try
        {
            // Parse the big metadata in JS and receive only this semester's recorded
            // list (with resolved thumbnails) as a compact PascalCase JSON string.
            var json = await _js.InvokeAsync<string>("nmuFunctions.getRecordedFiles", level, semester);
            _logger.LogDebug("GetFilesAsync: getRecordedFiles returned {Len} chars", json?.Length ?? 0);
            if (!string.IsNullOrEmpty(json))
            {
                var files = JsonSerializer.Deserialize<List<RecordedFile>>(json, CaseInsensitive) ?? new List<RecordedFile>();
                _logger.LogDebug("GetFilesAsync: deserialized {Count} files (path A)", files.Count);
                if (files.Count > 0)
                {
                    _filesMemCache[memKey] = files;
                    await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", cacheKey, json);
                    return files;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFilesAsync: path A failed: {Message}", ex.Message);
        }

        // Fallback: fetch the full metadata and filter in .NET (older cached app.js).
        try
        {
            var fullJson = await GetRawMetadataAsync();
            if (string.IsNullOrEmpty(fullJson))
                return new List<RecordedFile>();
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(fullJson);
            var targetPrefix1 = $"{BaseFolder}/{level}/{semester}/RECORDED_LECTURER/";
            var targetPrefix2 = $"{BaseFolder}/{level}/{semester}/RECORDED LECTURER/";
            var thumbsPrefix1 = $"nmu.ce.thumbs/{targetPrefix1}";
            var thumbsPrefix2 = $"nmu.ce.thumbs/{targetPrefix2}";

            var thumbNames = data?.Files?
                .Where(f => (f.Name.StartsWith(thumbsPrefix1) || f.Name.StartsWith(thumbsPrefix2)) && f.Name.EndsWith(".jpg"))
                .Select(f => f.Name)
                .ToHashSet() ?? new HashSet<string>();

            var files = data?.Files?
                .Where(f => f.Name.StartsWith(targetPrefix1) || f.Name.StartsWith(targetPrefix2))
                .Select(f =>
                {
                    var lower = f.Name.ToLower();
                    var fileNoExt = System.IO.Path.GetFileNameWithoutExtension(f.Name);
                    return new RecordedFile
                    {
                        Name = f.Name,
                        Size = long.TryParse(f.Size, out var s) ? s : null,
                        ThumbName = thumbNames.FirstOrDefault(t => t.Contains(fileNoExt)),
                        IsAudio = lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".m4a")
                    };
                })
                .ToList() ?? new List<RecordedFile>();

            _logger.LogInformation("GetFilesAsync: fallback found {Count} files", files.Count);
            if (files.Count > 0)
            {
                _filesMemCache[memKey] = files;
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", cacheKey, JsonSerializer.Serialize(files));
            }

            return files;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFilesAsync: fallback failed: {Message}", ex.Message);
            return new List<RecordedFile>();
        }
    }

    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the small cached group list (name + count) instantly without touching
    /// the full file list, mirroring the materials page's fast path.
    /// </summary>
    public async Task<List<RecordedGroupInfo>> GetCachedGroupsInfoAsync(string level, string semester)
    {
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", GroupsCacheKey(level, semester));
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<RecordedGroupInfo>>(cached, CaseInsensitive);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }
        return new List<RecordedGroupInfo>();
    }

    public async Task<List<RecordedGroupInfo>> GetGroupsInfoAsync(string level, string semester)
    {
        var cached = await GetCachedGroupsInfoAsync(level, semester);
        if (cached.Count > 0)
            return cached;

        var files = await GetFilesAsync(level, semester);
        var groups = GetGroups(files, level, semester);
        var info = groups.Select(g => new RecordedGroupInfo
        {
            Name = g,
            Count = GetFilesForGroup(files, level, semester, g).Count
        }).ToList();

        if (info.Count > 0)
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", GroupsCacheKey(level, semester), JsonSerializer.Serialize(info, CaseInsensitive));
        return info;
    }

    private static string GroupsCacheKey(string level, string semester)
        => $"{GroupsCacheVersion}{level}_{semester}";

    /// <summary>
    /// Background revalidation for the recorded lectures groups: sends a HEAD request
    /// to the archive metadata; only if it changed does it re-fetch the recorded list,
    /// rebuild the group counts, refresh the caches, and notify the page.
    /// </summary>
    public async Task CheckAndUpdateRecordedAsync(string level, string semester, Action<List<RecordedGroupInfo>>? onGroupsUpdated = null)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester)) return;
        try
        {
            var metaKey = $"{GroupsCacheVersion}meta_{level}_{semester}";

            var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
            if (!online) return;

            // archive.org rejects HEAD on /metadata (405, no CORS). GET the item size
            // from the search API instead; it changes whenever any file is added.
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://archive.org/advancedsearch.php?q=identifier:{ArchiveId}&fl[]=identifier&fl[]=item_size&rows=1&output=json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return;

            var serverLength = 0L;
            try
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<SearchResponse>(json, CaseInsensitive);
                serverLength = data?.Response?.Docs?.FirstOrDefault()?.ItemSize ?? 0;
            }
            catch { serverLength = 0; }
            if (serverLength <= 0) return;

            QuizMeta? cachedMeta = null;
            try
            {
                var cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey);
                if (!string.IsNullOrEmpty(cachedMetaJson))
                    cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson, CaseInsensitive);
            }
            catch { }

            if (cachedMeta != null && cachedMeta.ContentLength > 0 && serverLength == cachedMeta.ContentLength)
                return;

            _filesMemCache.TryRemove($"{level}_{semester}", out _);
            var files = await GetFilesAsync(level, semester);
            var groups = GetGroups(files, level, semester);
            var info = groups.Select(g => new RecordedGroupInfo
            {
                Name = g,
                Count = GetFilesForGroup(files, level, semester, g).Count
            }).ToList();

            if (info.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", GroupsCacheKey(level, semester), JsonSerializer.Serialize(info, CaseInsensitive));
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(new QuizMeta
                {
                    ContentLength = serverLength > 0 ? serverLength : files.Count,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }));
                onGroupsUpdated?.Invoke(info);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckAndUpdateRecordedAsync error: {Message}", ex.Message);
        }
    }

    public static List<string> GetGroups(List<RecordedFile> files, string level, string semester)
    {
        var prefix1 = $"{BaseFolder}/{level}/{semester}/RECORDED_LECTURER/";
        var prefix2 = $"{BaseFolder}/{level}/{semester}/RECORDED LECTURER/";
        var groups = new HashSet<string>();
        foreach (var f in files)
        {
            string rel;
            if (f.Name.StartsWith(prefix1))
                rel = f.Name[prefix1.Length..];
            else if (f.Name.StartsWith(prefix2))
                rel = f.Name[prefix2.Length..];
            else
                continue;
            var parts = rel.Split('/');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                groups.Add(parts[0]);
        }
        return groups.OrderBy(g => g).ToList();
    }

    public static List<RecordedFile> GetFilesForGroup(List<RecordedFile> allFiles, string level, string semester, string group)
    {
        var prefix1 = $"{BaseFolder}/{level}/{semester}/RECORDED_LECTURER/{group}/";
        var prefix2 = $"{BaseFolder}/{level}/{semester}/RECORDED LECTURER/{group}/";
        var result = new List<RecordedFile>();

        foreach (var f in allFiles)
        {
            if (!f.Name.StartsWith(prefix1) && !f.Name.StartsWith(prefix2))
                continue;

            var lower = f.Name.ToLower();
            if (lower.EndsWith(".ia.mp4")) continue;
            if (!lower.EndsWith(".mp4") && !lower.EndsWith(".mkv") && !lower.EndsWith(".webm") &&
                !lower.EndsWith(".mp3") && !lower.EndsWith(".wav") && !lower.EndsWith(".m4a"))
                continue;

            string rel;
            if (f.Name.StartsWith(prefix1))
                rel = f.Name[prefix1.Length..];
            else
                rel = f.Name[prefix2.Length..];

            var parts = rel.Split('/');
            var displayName = System.IO.Path.GetFileNameWithoutExtension(parts[^1]).Replace("_", " ");
            var subFolder = parts.Length > 1 ? parts[^2].Replace("_", " ") : "General";

            result.Add(new RecordedFile
            {
                Name = f.Name,
                Size = f.Size,
                DisplayName = displayName,
                SubFolder = subFolder,
                IsAudio = f.IsAudio,
                ThumbName = f.ThumbName
            });
        }

        return result.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string GetDownloadUrl(string filePath)
    {
        return $"https://archive.org/download/{ArchiveId}/{filePath}";
    }

    /// <summary>
    /// Returns the full archive metadata JSON. Downloaded ONCE (2.3 MB) and cached in
    /// IndexedDB, shared across materials / quizzes / recorded pages so it is never
    /// re-downloaded by each feature.
    /// </summary>
    public async Task<string?> GetRawMetadataAsync()
    {
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.getRawMetadata");
            if (!string.IsNullOrEmpty(cached))
                return cached;
        }
        catch { }

        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchText", $"https://archive.org/metadata/{ArchiveId}");
            if (!string.IsNullOrEmpty(json))
            {
                try { await _js.InvokeVoidAsync("nmuFunctions.setRawMetadata", json); } catch { }
                return json;
            }
        }
        catch { }
        return null;
    }

    public static string GetIconClass(string name)
    {
        var n = name.ToLower();
        if (n.Contains("arabic")) return "fa-solid fa-book-quran";
        if (n.Contains("english") || n.Contains("communication")) return "fa-solid fa-language";
        if (n.Contains("history") || n.Contains("psychology") || n.Contains("social") || n.Contains("humanities")) return "fa-solid fa-landmark";
        if (n.Contains("university") || n.Contains("management") || n.Contains("marketing")) return "fa-solid fa-briefcase";
        if (n.Contains("math") || n.Contains("calc") || n.Contains("algebra") || n.Contains("diff") || n.Contains("stat") || n.Contains("numerical") || n.Contains("discrete") || n.Contains("optimization") || n.Contains("analysis") || n.Contains("probabilit")) return "fa-solid fa-square-root-variable";
        if (n.Contains("chem")) return "fa-solid fa-flask";
        if (n.Contains("phy") || n.Contains("magnetic") || n.Contains("optic") || n.Contains("field")) return "fa-solid fa-atom";
        if (n.Contains("robot") || n.Contains("kinematic") || n.Contains("autonomous")) return "fa-solid fa-robot";
        if (n.Contains("mec") || n.Contains("mech") || n.Contains("static") || n.Contains("dynamic") || n.Contains("control") || n.Contains("material")) return "fa-solid fa-gears";
        if (n.Contains("draw") || n.Contains("graphic") || n.Contains("vision")) return "fa-solid fa-compass-drafting";
        if (n.Contains("network")) return "fa-solid fa-network-wired";
        if (n.Contains("iot") || n.Contains("internet of things") || n.Contains("sensor")) return "fa-solid fa-wifi";
        if (n.Contains("ele") || n.Contains("electric") || n.Contains("electronic") || n.Contains("circuit") || n.Contains("signal") || n.Contains("measure") || n.Contains("hardware") || n.Contains("architect") || n.Contains("logic")) return "fa-solid fa-microchip";
        if (n.Contains("database") || n.Contains("sql") || n.Contains("mining")) return "fa-solid fa-database";
        if (n.Contains("ai") || n.Contains("intelligen") || n.Contains("learning") || n.Contains("neural") || n.Contains("knowledg") || n.Contains("nlp") || n.Contains("cognitive") || n.Contains("pattern") || n.Contains("evolution") || n.Contains("reasoning") || n.Contains("fuzzy") || n.Contains("decision")) return "fa-solid fa-brain";
        if (n.Contains("bio-inspired")) return "fa-solid fa-dna";
        if (n.Contains("cloud")) return "fa-solid fa-cloud";
        if (n.Contains("security") || n.Contains("secure") || n.Contains("crypto") || n.Contains("forensic") || n.Contains("cyber")) return "fa-solid fa-user-shield";
        if (n.Contains("web") || n.Contains("html") || n.Contains("css")) return "fa-solid fa-globe";
        if (n.Contains("cse") || n.Contains("prog") || n.Contains("code") || n.Contains("struct") || n.Contains("object") || n.Contains("oop") || n.Contains("soft") || n.Contains("parallel") || n.Contains("distribut") || n.Contains("compiler") || n.Contains("comput") || n.Contains("algorithm") || n.Contains("os") || n.Contains("operating") || n.Contains("system")) return "fa-solid fa-laptop-code";
        if (n.Contains("project") || n.Contains("training") || n.Contains("grad")) return "fa-solid fa-user-graduate";
        if (n.Contains("report") || n.Contains("tech") || n.Contains("writ") || n.Contains("search")) return "fa-solid fa-file-pen";
        return "fa-solid fa-folder-open";
    }
}
