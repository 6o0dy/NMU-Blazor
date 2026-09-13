using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;
using Microsoft.Extensions.Logging;

namespace NMU.Platform.Components.Services;

public class QuizMeta
{
    public long ContentLength { get; set; }
    public string Etag { get; set; } = "";
    public string LastModified { get; set; } = "";
    public long Timestamp { get; set; }
}

public class SearchDoc
{
    [System.Text.Json.Serialization.JsonPropertyName("identifier")]
    public string Identifier { get; set; } = "";
    [System.Text.Json.Serialization.JsonPropertyName("item_size")]
    public long ItemSize { get; set; }
}

public class SearchResponse
{
    public SearchResponseBody? Response { get; set; }
}

public class SearchResponseBody
{
    public List<SearchDoc>? Docs { get; set; }
}

public class SyncProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public string SubjectName { get; set; } = "";
    public bool IsComplete { get; set; }
    public bool IsDownloading { get; set; }
}

public class QuizService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly ILogger<QuizService> _logger;
    private readonly ToastService _toast;

    public event Action<SyncProgress>? SyncProgressChanged;

    public QuizService(IJSRuntime js, HttpClient http, ILogger<QuizService> logger, ToastService toast)
    {
        _js = js;
        _http = http;
        _logger = logger;
        _toast = toast;
    }

    private static string MapSemester(string sem)
    {
        return ArchiveCatalog.NormalizeSemester(sem) ?? sem.Replace(" ", "_");
    }

    private static string MapLevel(string level)
    {
        return ArchiveCatalog.NormalizeLevel(level) ?? level.Replace(" ", "_");
    }

    private static string QuizListCacheKey(string level, string semester)
        => $"nmu_quiz_list_{level}_{semester}_v5_newarch";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<QuizSubject>> _listMemCache = new();

    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Returns the cached quiz subject list instantly (in-memory or localStorage),
    /// without fetching order_config.json or touching the network. Callers should
    /// still trigger CheckAndUpdateQuizListAsync for background revalidation.
    /// </summary>
    public async Task<List<QuizSubject>> GetCachedQuizListAsync(string level, string semester)
    {
        level = MapLevel(level);
        semester = MapSemester(semester);
        var memKey = $"{level}_{semester}";
        if (_listMemCache.TryGetValue(memKey, out var mem) && mem != null)
            return mem;

        try
        {
            var cacheKey = QuizListCacheKey(level, semester);
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizSubject>>(cached, CaseInsensitive);
                if (parsed != null && parsed.Count > 0)
                {
                    _listMemCache[memKey] = parsed;
                    return parsed;
                }
            }
        }
        catch { }
        return new List<QuizSubject>();
    }

    public async Task<List<QuizSubject>> GetQuizListAsync(string level, string semester)
    {
        level = MapLevel(level);
        semester = MapSemester(semester);
        var memKey = $"{level}_{semester}";
        if (_listMemCache.TryGetValue(memKey, out var mem) && mem != null)
            return mem;

        var archiveId = ArchiveCatalog.GetArchiveId(level, semester);
        if (archiveId == null) return new List<QuizSubject>();

        var cacheKey = QuizListCacheKey(level, semester);
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizSubject>>(cached, CaseInsensitive);
                if (parsed != null && parsed.Count > 0)
                {
                    _listMemCache[memKey] = parsed;
                    return parsed;
                }
            }
        }
        catch { }
        try
        {
            var names = await GetQuizFileNamesAsync(level, semester);

            var matchedFiles = names
                .Where(n => n.EndsWith(".json") && !n.EndsWith("order_config.json"))
                .ToList();

            var files = BuildQuizSubjects(matchedFiles, level, semester, archiveId);

            if (files.Count > 0)
            {
                _listMemCache[memKey] = files;
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(files));
            }

            return files;
        }
        catch
        {
            return new List<QuizSubject>();
        }
    }

    /// <summary>
    /// Background revalidation for Quiz Subject List: Sends HEAD request to check if metadata changed.
    /// Updates list cache and notifies UI if changed.
    /// </summary>
    public async Task CheckAndUpdateQuizListAsync(string level, string semester, Action<List<QuizSubject>>? onListUpdated = null)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester)) return;
        level = MapLevel(level);
        var mappedSemester = MapSemester(semester);
        var cacheKey = QuizListCacheKey(level, mappedSemester);
        var metaKey = $"nmu_quiz_list_meta_{level}_{mappedSemester}_v5";

        try
        {
            var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
            if (!online) return;

            string? cachedMetaJson = null;
            try { cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey); } catch { }

            QuizMeta? cachedMeta = null;
            if (!string.IsNullOrEmpty(cachedMetaJson))
            {
                try { cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson); } catch { }
            }

            var archiveId = ArchiveCatalog.GetArchiveId(level, mappedSemester);
            if (archiveId == null) return;
            var metaUrl = ArchiveCatalog.GetAdvancedSearchUrl(archiveId);

            using var request = new HttpRequestMessage(HttpMethod.Get, metaUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return;

            var serverLength = 0L;
            try
            {
                var json = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<SearchResponse>(json);
                serverLength = data?.Response?.Docs?.FirstOrDefault()?.ItemSize ?? 0;
            }
            catch { serverLength = 0; }
            if (serverLength <= 0) return;

            if (cachedMeta != null && cachedMeta.ContentLength > 0 && serverLength == cachedMeta.ContentLength)
                return;

            var names = await GetQuizFileNamesAsync(level, mappedSemester);
            if (names.Count == 0) return;

            var matchedFiles = names
                .Where(n => n.EndsWith(".json") && !n.EndsWith("order_config.json"))
                .ToList();

            if (matchedFiles.Count == 0) return;

            var mappedArchiveId = ArchiveCatalog.GetArchiveId(level, mappedSemester);
            if (mappedArchiveId == null) return;
            var files = BuildQuizSubjects(matchedFiles, level, mappedSemester, mappedArchiveId);

            if (files.Count > 0)
            {
                _listMemCache[$"{level}_{mappedSemester}"] = files;
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(files));

                var newMeta = new QuizMeta
                {
                    ContentLength = serverLength > 0 ? serverLength : matchedFiles.Count,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(newMeta));

                onListUpdated?.Invoke(files);
                _toast.ShowToast("تم تحديث قائمة الكويزات", ToastType.Success);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckAndUpdateQuizListAsync error: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Builds QuizSubject entries from new-layout quiz paths:
    /// Data/{SubjectFolder}/Quizzes/{Lecturer}/*.json (plus legacy duplicate paths).
    /// Deduplicates identical quiz files that exist under both the legacy duplicate
    /// folder and the canonical folder. Display name = subject clean name.
    /// </summary>
    public static List<QuizSubject> BuildQuizSubjects(List<string> paths, string level, string semester, string archiveId)
    {
        var seenFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<QuizSubject>();

        foreach (var f in paths)
        {
            if (string.IsNullOrWhiteSpace(f)) continue;
            if (!f.StartsWith("Data/", StringComparison.Ordinal)) continue;
            if (!f.Contains("/Quizzes/", StringComparison.OrdinalIgnoreCase)) continue;
            var segs = f.Split('/');
            if (segs.Length < 4) continue;
            var subjectFull = segs[1];
            if (string.IsNullOrWhiteSpace(subjectFull)) continue;
            // Skip the legacy nested duplicate: Data/{S}/{S}/Quizzes/... (keep canonical one).
            if (segs.Length >= 3 && string.Equals(segs[2], subjectFull, StringComparison.Ordinal))
                continue;

            var fileName = segs[^1];
            var dedupeKey = $"{subjectFull}|{fileName}".ToLowerInvariant();
            if (!seenFileNames.Add(dedupeKey)) continue;

            var qIdx = Array.FindIndex(segs, s => s.Equals("Quizzes", StringComparison.OrdinalIgnoreCase));
            var lecturer = (qIdx >= 0 && qIdx + 1 < segs.Length - 1) ? segs[qIdx + 1] : "";

            ArchiveCatalog.ParseSubjectFolder(subjectFull, out var code, out var clean, out var branch);
            var display = string.IsNullOrEmpty(clean) ? fileName.Replace(".json", "").Replace("_", " ") : clean;

            result.Add(new QuizSubject
            {
                Name = display,
                Path = f,
                Rel = f,
                SubjectFullName = subjectFull,
                Code = code,
                Branch = branch,
                Lecturer = lecturer,
                Level = level,
                Semester = semester,
                ArchiveId = archiveId
            });
        }

        return result.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string ResolveQuizArchiveId(QuizSubject subject, string? level = null, string? semester = null)
    {
        if (subject != null && !string.IsNullOrEmpty(subject.ArchiveId))
            return subject.ArchiveId;
        var lvl = subject != null && !string.IsNullOrEmpty(subject.Level) ? subject.Level : level;
        var sem = subject != null && !string.IsNullOrEmpty(subject.Semester) ? subject.Semester : semester;
        return ArchiveCatalog.GetArchiveId(lvl, sem) ?? "";
    }

    public async Task<List<QuizChapter>> GetQuizDataAsync(string filePath, string? level = null, string? semester = null, string? archiveId = null)
    {
        archiveId ??= ArchiveCatalog.GetArchiveId(level, semester) ?? "";
        // Back-compat: old cached callers pass only filePath; try to infer archive from
        // the quiz list memory cache when level/semester are not supplied.
        if (string.IsNullOrEmpty(archiveId))
        {
            foreach (var kv in _listMemCache)
            {
                var hit = kv.Value.FirstOrDefault(q => string.Equals(q.Path, filePath, StringComparison.Ordinal));
                if (hit != null && !string.IsNullOrEmpty(hit.ArchiveId))
                {
                    archiveId = hit.ArchiveId;
                    break;
                }
            }
        }
        var cacheKey = $"nmu_q_content_{filePath}";

        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizChapter>>(cached);
                if (parsed != null && parsed.Count > 0)
                {
                    // Trigger smart background revalidation using HEAD request
                    _ = CheckAndUpdateQuizContentAsync(filePath, level, semester, archiveId);
                    return parsed;
                }
            }
        }
        catch { }

        try
        {
            _logger.LogInformation("QuizService: fetching path: {FilePath}", filePath);
            if (string.IsNullOrEmpty(archiveId)) return new List<QuizChapter>();
            var url = $"{ArchiveCatalog.GetDownloadUrl(archiveId, filePath)}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _http.GetStringAsync(url);
            _logger.LogInformation("QuizService: got json length: {Length}", json?.Length ?? 0);

            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("QuizService: empty json response");
                return new List<QuizChapter>();
            }

            await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, json);

            // Store metadata for initial download
            var metaKey = $"nmu_q_meta_{filePath}";
            var initialMeta = new QuizMeta
            {
                ContentLength = json.Length,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            try { await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(initialMeta)); } catch { }

            var data = JsonSerializer.Deserialize<List<QuizChapter>>(json);
            if (data == null)
                _logger.LogWarning("QuizService: deserialized null");
            return data ?? new List<QuizChapter>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuizService error: {Message}", ex.Message);
            return new List<QuizChapter>();
        }
    }

    /// <summary>
    /// Background revalidation: Sends HTTP HEAD request to check if quiz content changed on server.
    /// Updates cache and shows Toast if changes are detected.
    /// </summary>
    public async Task CheckAndUpdateQuizContentAsync(string filePath, string? level = null, string? semester = null, string? archiveId = null)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        archiveId ??= ArchiveCatalog.GetArchiveId(level, semester);
        if (string.IsNullOrEmpty(archiveId))
        {
            foreach (var kv in _listMemCache)
            {
                var hit = kv.Value.FirstOrDefault(q => string.Equals(q.Path, filePath, StringComparison.Ordinal));
                if (hit != null && !string.IsNullOrEmpty(hit.ArchiveId))
                {
                    archiveId = hit.ArchiveId;
                    break;
                }
            }
        }
        if (string.IsNullOrEmpty(archiveId)) return;
        try
        {
            var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
            if (!online) return;

            var cacheKey = $"nmu_q_content_{filePath}";
            var metaKey = $"nmu_q_meta_{filePath}";

            string? cachedMetaJson = null;
            try { cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey); } catch { }

            QuizMeta? cachedMeta = null;
            if (!string.IsNullOrEmpty(cachedMetaJson))
            {
                try { cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson); } catch { }
            }

            var url = ArchiveCatalog.GetDownloadUrl(archiveId, filePath);
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return;

            var serverLength = response.Content.Headers.ContentLength ?? 0;
            var serverEtag = response.Headers.ETag?.Tag ?? "";
            var serverLastMod = response.Content.Headers.LastModified?.ToString("R") ?? "";

            if (cachedMeta != null)
            {
                bool changed = false;
                if (serverLength > 0 && cachedMeta.ContentLength > 0 && serverLength != cachedMeta.ContentLength)
                    changed = true;
                else if (!string.IsNullOrEmpty(serverEtag) && !string.IsNullOrEmpty(cachedMeta.Etag) && serverEtag != cachedMeta.Etag)
                    changed = true;
                else if (!string.IsNullOrEmpty(serverLastMod) && !string.IsNullOrEmpty(cachedMeta.LastModified) && serverLastMod != cachedMeta.LastModified)
                    changed = true;

                if (!changed) return;
            }

            var downloadUrl = $"{url}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var latestJson = await _http.GetStringAsync(downloadUrl);
            if (string.IsNullOrEmpty(latestJson)) return;

            await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, latestJson);

            var newMeta = new QuizMeta
            {
                ContentLength = serverLength > 0 ? serverLength : latestJson.Length,
                Etag = serverEtag,
                LastModified = serverLastMod,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(newMeta));

            _toast.ShowToast("تم تحديث أسئلة الكويز بنجاح", ToastType.Success);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckAndUpdateQuizContentAsync error: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Initial Sync: Pre-caches ALL quizzes for the student's level and semester in the background.
    /// Resilient: Skips already cached files, so it can safely resume if interrupted!
    /// </summary>
    public async Task EnsureQuizSyncedAsync(string level, string semester)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester)) return;
        var mappedLevel = MapLevel(level);
        var mappedSemester = MapSemester(semester);
        var syncFlagKey = $"nmu_quiz_sync_done_{mappedLevel}_{mappedSemester}_v5";

        try
        {
            var isDone = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", syncFlagKey);
            if (isDone == "true")
            {
                _logger.LogInformation("EnsureQuizSyncedAsync: already done for {Level}/{Semester}", mappedLevel, mappedSemester);
                return;
            }
        }
        catch { }

        try
        {
            _logger.LogInformation("EnsureQuizSyncedAsync: starting sync for {Level}/{Semester}", mappedLevel, mappedSemester);

            var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
            if (!online)
            {
                _logger.LogInformation("EnsureQuizSyncedAsync: offline, skipping");
                return;
            }

            var subjects = await GetQuizListAsync(mappedLevel, mappedSemester);
            if (subjects == null || subjects.Count == 0)
            {
                _logger.LogInformation("EnsureQuizSyncedAsync: no subjects found for {Level}/{Semester}", mappedLevel, mappedSemester);
                return;
            }

            _logger.LogInformation("EnsureQuizSyncedAsync: found {Count} subjects", subjects.Count);

            SyncProgressChanged?.Invoke(new SyncProgress { Current = 0, Total = subjects.Count, IsDownloading = false });

            bool allDownloaded = true;
            int newlyDownloadedCount = 0;
            int processedCount = 0;

            foreach (var subject in subjects)
            {
                if (string.IsNullOrEmpty(subject.Path)) { processedCount++; continue; }

                var cacheKey = $"nmu_q_content_{subject.Path}";
                string? existing = null;
                try { existing = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", cacheKey); } catch { }

                if (string.IsNullOrEmpty(existing))
                {
                    SyncProgressChanged?.Invoke(new SyncProgress { Current = processedCount, Total = subjects.Count, SubjectName = subject.Name, IsDownloading = true });
                    _logger.LogInformation("EnsureQuizSyncedAsync: downloading {Path}", subject.Path);
                    var subjectArchiveId = !string.IsNullOrEmpty(subject.ArchiveId)
                        ? subject.ArchiveId
                        : ArchiveCatalog.GetArchiveId(mappedLevel, mappedSemester);
                    if (subjectArchiveId == null) { processedCount++; continue; }
                    var url = $"{ArchiveCatalog.GetDownloadUrl(subjectArchiveId, subject.Path)}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                    var json = await _http.GetStringAsync(url);
                    if (!string.IsNullOrEmpty(json))
                    {
                        await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, json);

                        var metaKey = $"nmu_q_meta_{subject.Path}";
                        var initialMeta = new QuizMeta
                        {
                            ContentLength = json.Length,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                        };
                        try { await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(initialMeta)); } catch { }

                        newlyDownloadedCount++;
                        _logger.LogInformation("EnsureQuizSyncedAsync: downloaded {Path} ({Length} chars)", subject.Path, json.Length);
                    }
                    else
                    {
                        _logger.LogInformation("EnsureQuizSyncedAsync: empty response for {Path}", subject.Path);
                        allDownloaded = false;
                    }
                }
                else
                {
                    _logger.LogInformation("EnsureQuizSyncedAsync: already cached {Path}", subject.Path);
                }
                processedCount++;
                SyncProgressChanged?.Invoke(new SyncProgress { Current = processedCount, Total = subjects.Count, SubjectName = processedCount < subjects.Count ? subjects[processedCount].Name : "", IsDownloading = false });
            }

            SyncProgressChanged?.Invoke(new SyncProgress { Current = subjects.Count, Total = subjects.Count, IsComplete = true, IsDownloading = false });

            if (allDownloaded)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", syncFlagKey, "true");
                _logger.LogInformation("EnsureQuizSyncedAsync: sync complete, downloaded {Count} new files", newlyDownloadedCount);
                if (newlyDownloadedCount > 0)
                {
                    _toast.ShowToast("تم تجهيز جميع كويزات الترم للعمل بدون إنترنت", ToastType.Success);
                }
            }
            else
            {
                _logger.LogInformation("EnsureQuizSyncedAsync: some downloads failed, will retry on next open");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnsureQuizSyncedAsync error: {Message}", ex.Message);
        }
    }

    public static string GetDownloadUrl(string filePath, string? level = null, string? semester = null, string? archiveId = null)
    {
        archiveId ??= (level != null && semester != null) ? ArchiveCatalog.GetArchiveId(level, semester) : null;
        if (archiveId == null) return filePath;
        return ArchiveCatalog.GetDownloadUrl(archiveId, filePath);
    }

    public static string GetDownloadUrl(QuizSubject subject)
    {
        var archiveId = !string.IsNullOrEmpty(subject.ArchiveId)
            ? subject.ArchiveId
            : ArchiveCatalog.GetArchiveId(subject.Level, subject.Semester);
        if (archiveId == null) return subject.Path;
        return ArchiveCatalog.GetDownloadUrl(archiveId, subject.Path);
    }

    /// <summary>
    /// Returns the Quizzes folder file names for a semester. The big archive metadata is
    /// parsed in JS (native JSON.parse) so it never blocks the .NET thread.
    /// </summary>
    private async Task<List<string>> GetQuizFileNamesAsync(string level, string semester)
    {
        level = MapLevel(level);
        semester = MapSemester(semester);
        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.getQuizFiles", level, semester);
            if (!string.IsNullOrEmpty(json))
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(json);
                if (parsed != null)
                    return parsed;
            }
        }
        catch { }

        // Fallback (older cached app.js, or JS fetch failed): fetch the full metadata
        // JSON and filter the Quizzes file names in .NET.
        try
        {
            var fallbackArchiveId = ArchiveCatalog.GetArchiveId(level, semester);
            if (fallbackArchiveId == null) return new List<string>();
            var fullJson = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", ArchiveCatalog.GetMetadataUrl(fallbackArchiveId));
            if (!string.IsNullOrEmpty(fullJson))
            {
                var data = JsonSerializer.Deserialize<ArchiveMetadata>(fullJson);
                var names = data?.Files?
                    .Where(f => f.Name.StartsWith("Data/", StringComparison.Ordinal)
                                && f.Name.Contains("/Quizzes/", StringComparison.OrdinalIgnoreCase)
                                && f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Name)
                    .ToList() ?? new List<string>();
                return names;
            }
        }
        catch { }
        return new List<string>();
    }
}