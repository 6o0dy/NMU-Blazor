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
    private const string ArchiveId = "nmu.ce";

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
        var s = sem.Replace(" ", "_").ToLower();
        if (s is "semester_1" or "first_term" or "term_1") return "Semester_1";
        if (s is "semester_2" or "second_term" or "term_2") return "Semester_2";
        return sem.Replace(" ", "_");
    }

    public async Task<List<QuizSubject>> GetQuizListAsync(string level, string semester)
    {
        semester = MapSemester(semester);
        var cacheKey = $"nmu_quiz_list_{level}_{semester}_v4";
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizSubject>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }

        try
        {
            var quizPath = $"NMU/{level}/{semester}/QUIZE/";
            var metaUrl = $"https://archive.org/metadata/{ArchiveId}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _http.GetStringAsync(metaUrl);
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);

            if (data?.Files == null)
                return new List<QuizSubject>();

            var matchedFiles = data.Files
                .Where(f => f.Name.StartsWith(quizPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                .ToList();

            if (matchedFiles.Count == 0)
            {
                var altSemester = semester == "Semester_1" ? "Semester_2" : "Semester_1";
                var altPath = $"NMU/{level}/{altSemester}/QUIZE/";
                matchedFiles = data.Files
                    .Where(f => f.Name.StartsWith(altPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                    .ToList();
                if (matchedFiles.Count > 0)
                    quizPath = altPath;
            }

            var orderList = new List<string>();
            try
            {
                var orderUrl = $"https://archive.org/download/{ArchiveId}/{quizPath}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var orderJson = await _http.GetStringAsync(orderUrl);
                var orderConfig = JsonSerializer.Deserialize<OrderConfig>(orderJson);
                if (orderConfig?.Order != null)
                    orderList = orderConfig.Order;
            }
            catch { }

            var files = matchedFiles
                .Select(f =>
                {
                    var rel = f.Name.Substring(quizPath.Length);
                    var name = rel.Split('/')[0].Replace(".json", "").Replace("_", " ");
                    return new QuizSubject { Name = name, Path = f.Name, Rel = rel };
                })
                .ToList();

            if (orderList.Count > 0)
            {
                files = files.OrderBy(f =>
                {
                    var idx = orderList.FindIndex(k => f.Rel.Contains(k));
                    return idx == -1 ? 999 : idx;
                }).ThenBy(f => f.Name).ToList();
            }
            else
            {
                files = files.OrderBy(f => f.Name).ToList();
            }

            if (files.Count > 0)
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(files));

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
        var mappedSemester = MapSemester(semester);
        var cacheKey = $"nmu_quiz_list_{level}_{mappedSemester}_v4";
        var metaKey = $"nmu_quiz_list_meta_{level}_{mappedSemester}";

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

            var metaUrl = $"https://archive.org/metadata/{ArchiveId}";

            using var request = new HttpRequestMessage(HttpMethod.Head, metaUrl);
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

            var fetchUrl = $"https://archive.org/metadata/{ArchiveId}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _http.GetStringAsync(fetchUrl);
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);

            if (data?.Files == null) return;

            var quizPath = $"NMU/{level}/{mappedSemester}/QUIZE/";
            var matchedFiles = data.Files
                .Where(f => f.Name.StartsWith(quizPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                .ToList();

            if (matchedFiles.Count == 0) return;

            var orderList = new List<string>();
            try
            {
                var orderUrl = $"https://archive.org/download/{ArchiveId}/{quizPath}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var orderJson = await _http.GetStringAsync(orderUrl);
                var orderConfig = JsonSerializer.Deserialize<OrderConfig>(orderJson);
                if (orderConfig?.Order != null)
                    orderList = orderConfig.Order;
            }
            catch { }

            var files = matchedFiles
                .Select(f =>
                {
                    var rel = f.Name.Substring(quizPath.Length);
                    var name = rel.Split('/')[0].Replace(".json", "").Replace("_", " ");
                    return new QuizSubject { Name = name, Path = f.Name, Rel = rel };
                })
                .ToList();

            if (orderList.Count > 0)
            {
                files = files.OrderBy(f =>
                {
                    var idx = orderList.FindIndex(k => f.Rel.Contains(k));
                    return idx == -1 ? 999 : idx;
                }).ThenBy(f => f.Name).ToList();
            }
            else
            {
                files = files.OrderBy(f => f.Name).ToList();
            }

            if (files.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(files));

                var newMeta = new QuizMeta
                {
                    ContentLength = serverLength > 0 ? serverLength : json.Length,
                    Etag = serverEtag,
                    LastModified = serverLastMod,
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

    public async Task<List<QuizChapter>> GetQuizDataAsync(string filePath)
    {
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
                    _ = CheckAndUpdateQuizContentAsync(filePath);
                    return parsed;
                }
            }
        }
        catch { }

        try
        {
            _logger.LogInformation("QuizService: fetching path: {FilePath}", filePath);
            var url = $"https://archive.org/download/{ArchiveId}/{filePath}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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
    public async Task CheckAndUpdateQuizContentAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
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

            var url = $"https://archive.org/download/{ArchiveId}/{filePath}";
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
        var mappedLevel = level.Replace(" ", "_");
        var mappedSemester = MapSemester(semester);
        var syncFlagKey = $"nmu_quiz_sync_done_{mappedLevel}_{mappedSemester}";

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
                    var url = $"https://archive.org/download/{ArchiveId}/{subject.Path}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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

    public static string GetDownloadUrl(string filePath)
    {
        return $"https://archive.org/download/{ArchiveId}/{filePath}";
    }
}