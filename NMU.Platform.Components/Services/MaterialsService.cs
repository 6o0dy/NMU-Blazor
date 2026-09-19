using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class MaterialsService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<MaterialsService> _logger;
    // Per-semester archives: Level_1/Semester_1 -> NMU.CE_1.1 ... Level_5/Semester_2 -> NMU.CE_5.2
    // Resolved via ArchiveCatalog.GetArchiveId(level, semester). No single ArchiveId anymore.
    private const string CacheVersion = "v70_newarch_semfiles_";
    private const string SubjectsCacheVersion = "v70_newarch_subjects_";
    private const string SubjectFilesCacheVersion = "v70_newarch_subjectfiles_";

    public MaterialsService(IJSRuntime js, ILogger<MaterialsService> logger)
    {
        _js = js;
        _logger = logger;
    }

    private static string SubjectsCacheKey(string level, string semester)
        => $"{SubjectsCacheVersion}{level}_{semester}";

    private static string SubjectFilesCacheKey(string level, string semester, string subject)
        => $"{SubjectFilesCacheVersion}{level}_{semester}_{subject}";

    private static string MetaCacheKey(string level, string semester)
        => $"nmu_mat_meta_v2_{level}_{semester}";

    /// <summary>
    /// Returns the small cached subject list (name + file count) instantly, without
    /// loading the full archive file list or touching IndexedDB. Triggers a background
    /// HEAD-based revalidation so the list stays fresh, same as the quiz page.
    /// </summary>
    public async Task<List<MaterialSubjectInfo>> GetSubjectsInfoAsync(string level, string semester)
    {
        var cached = await GetCachedSubjectsInfoAsync(level, semester);
        if (cached.Count > 0)
        {
            _ = CheckAndUpdateMaterialsAsync(level, semester);
            return cached;
        }

        var files = await GetFilesAsync(level, semester);
        _logger.LogDebug("GetSubjectsInfoAsync: GetFilesAsync returned {Count} files", files.Count);
        var info = BuildSubjectsInfo(files, level, semester);
        _logger.LogDebug("GetSubjectsInfoAsync: BuildSubjectsInfo returned {Count} subjects", info.Count);
        if (info.Count > 0)
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", SubjectsCacheKey(level, semester), JsonSerializer.Serialize(info));
        return info;
    }

    /// <summary>
    /// Returns every subject that exists in the archive across all levels and
    /// semesters (PDF folder catalog). Used to populate the custom-subjects picker
    /// so a credit-hours student can pin subjects from any level/semester.
    /// </summary>
    public async Task<List<SubjectCatalogEntry>> GetSubjectCatalogAsync(bool force = false)
    {
        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.getSubjectCatalog", force);
            if (!string.IsNullOrEmpty(json))
            {
                var parsed = JsonSerializer.Deserialize<List<SubjectCatalogEntry>>(json, CaseInsensitive);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }
        return new List<SubjectCatalogEntry>();
    }

    /// <summary>
    /// Fire-and-forget rebuild of the cross-archive subject catalog after an
    /// archive change was detected (new subjects must reach the picker).
    /// </summary>
    public async Task RefreshSubjectCatalogAsync()
    {
        await RefreshSubjectCatalogForcedAsync();
    }

    private static bool _catalogRefreshedThisSession;

    /// <summary>
    /// True only on the first call per app session: the custom-subjects picker
    /// refreshes once per session, on its first open.
    /// </summary>
    public bool CatalogSessionRefreshDue()
    {
        if (_catalogRefreshedThisSession) return false;
        _catalogRefreshedThisSession = true;
        return true;
    }

    /// <summary>
    /// Force-rebuilds the cross-archive catalog.
    /// Returns the fresh list, or null on failure/empty.
    /// </summary>
    public async Task<List<SubjectCatalogEntry>?> RefreshSubjectCatalogForcedAsync()
    {
        try
        {
            var fresh = await GetSubjectCatalogAsync(force: true);
            return fresh.Count > 0 ? fresh : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "RefreshSubjectCatalogForcedAsync error: {Message}", ex.Message);
            return null;
        }
    }

    public async Task<List<MaterialSubjectInfo>> GetCachedSubjectsInfoAsync(string level, string semester)
    {
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", SubjectsCacheKey(level, semester));
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<MaterialSubjectInfo>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }
        return new List<MaterialSubjectInfo>();
    }

    /// <summary>
    /// Returns the small cached per-subject file list instantly. Falls back to computing
    /// it from the full archive list and caches the result.
    /// </summary>
    public async Task<List<MaterialFile>> GetSubjectFilesAsync(string level, string semester, string subject)
    {
        var cached = await GetCachedSubjectFilesAsync(level, semester, subject);
        if (cached.Count > 0)
        {
            _ = CheckAndUpdateSubjectFilesAsync(level, semester, subject);
            return cached;
        }

        var files = await GetFilesAsync(level, semester);
        var (_, subjectFiles) = GetFilesForSubject(files, level, semester, subject);
        if (subjectFiles.Count > 0)
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", SubjectFilesCacheKey(level, semester, subject), JsonSerializer.Serialize(subjectFiles));
        return subjectFiles;
    }

    public async Task<List<MaterialFile>> GetCachedSubjectFilesAsync(string level, string semester, string subject)
    {
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", SubjectFilesCacheKey(level, semester, subject));
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<MaterialFile>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }
        return new List<MaterialFile>();
    }

    /// <summary>
    /// Background revalidation for the materials subject list: sends a HEAD request to
    /// the archive metadata; only if it changed does it re-fetch and refresh the caches.
    /// </summary>
    public async Task CheckAndUpdateMaterialsAsync(string level, string semester, Action<List<MaterialSubjectInfo>>? onSubjectsUpdated = null)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester)) return;
        try
        {
            var metaKey = MetaCacheKey(level, semester);
            var check = await MetadataChangedAsync(metaKey);
            if (check == null || !check.Value.changed) return;

            _filesMemCache.TryRemove($"{level}_{semester}", out _);
            // Force: bypass the persistent file-list caches so newly added
            // archive files are actually picked up (not re-served stale).
            var files = await GetFilesAsync(level, semester, force: true);
            var subjects = BuildSubjectsInfo(files, level, semester);
            if (subjects.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", SubjectsCacheKey(level, semester), JsonSerializer.Serialize(subjects));
                await SaveMetaAsync(metaKey, check.Value);
                // New subjects may exist -> rebuild the cross-archive catalog
                // in the background for the custom-subjects picker.
                _ = RefreshSubjectCatalogAsync();
                onSubjectsUpdated?.Invoke(subjects);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckAndUpdateMaterialsAsync error: {Message}", ex.Message);
        }
    }

    /// <summary>
    /// Background revalidation for a subject's file list.
    /// </summary>
    public async Task CheckAndUpdateSubjectFilesAsync(string level, string semester, string subject, Action<List<MaterialFile>>? onUpdated = null)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester) || string.IsNullOrEmpty(subject)) return;
        try
        {
            var metaKey = MetaCacheKey(level, semester);
            var check = await MetadataChangedAsync(metaKey);
            if (check == null || !check.Value.changed) return;

            _filesMemCache.TryRemove($"{level}_{semester}", out _);
            // Force: bypass the persistent file-list caches (see above).
            var files = await GetFilesAsync(level, semester, force: true);
            var (_, subjectFiles) = GetFilesForSubject(files, level, semester, subject);
            if (subjectFiles.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", SubjectFilesCacheKey(level, semester, subject), JsonSerializer.Serialize(subjectFiles));
                await SaveMetaAsync(metaKey, check.Value);
                onUpdated?.Invoke(subjectFiles);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckAndUpdateSubjectFilesAsync error: {Message}", ex.Message);
        }
    }

    private async Task<(bool changed, long length, string etag, string lastMod)?> MetadataChangedAsync(string metaKey)
    {
        var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
        if (!online) return null;

        // archive.org's search index does not list the NMU.CE_* identifiers, so
        // no remote size signal exists. Freshness is decided by age instead:
        // at most one background refresh per RevalidateAfter window. This check
        // itself performs zero network calls.
        string? cachedMetaJson = null;
        try { cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey); } catch { }

        if (!string.IsNullOrEmpty(cachedMetaJson))
        {
            try
            {
                var cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson);
                if (cachedMeta != null && cachedMeta.Timestamp > 0)
                {
                    var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - cachedMeta.Timestamp;
                    if (ageMs < ArchiveCatalog.RevalidateAfter.TotalMilliseconds)
                        return (false, cachedMeta.ContentLength, cachedMeta.Etag, cachedMeta.LastModified);
                }
            }
            catch { }
        }

        return (true, 0, "", "");
    }

    private async Task SaveMetaAsync(string metaKey, (bool changed, long length, string etag, string lastMod) check)
    {
        var meta = new QuizMeta
        {
            ContentLength = check.length,
            Etag = check.etag,
            LastModified = check.lastMod,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        try { await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(meta)); } catch { }
    }

    /// <summary>
    /// True when the student has Physics 2 among their subjects: directly
    /// selected in custom mode, or present in their level/semester catalog.
    /// Used to gate the Virtual Lab entry points.
    /// </summary>
    public async Task<bool> HasPhysics2Async(StudentProfile? student)
    {
        try
        {
            if (student == null) return false;
            if (student.CustomSubjectsMode && student.CustomSubjects != null && student.CustomSubjects.Count > 0)
                return student.CustomSubjects.Any(s => ArchiveCatalog.IsPhysics2Subject(s.Subject));
            var level = student.AcademicLevel.Replace(" ", "_");
            var semester = student.Semester.Replace(" ", "_");
            if (await HasPhysics2CachedAsync(level, semester)) return true;
            // Caches may predate the upload: run the time-gated background
            // refresh now (no-op when freshly checked) and re-check.
            await CheckAndUpdateMaterialsAsync(level, semester, null);
            return await HasPhysics2CachedAsync(level, semester);
        }
        catch { return false; }
    }

    private async Task<bool> HasPhysics2CachedAsync(string level, string semester)
    {
        var subjects = await GetCachedSubjectsInfoAsync(level, semester);
        if (subjects.Count == 0)
            subjects = await GetSubjectsInfoAsync(level, semester);
        return subjects.Any(s => ArchiveCatalog.IsPhysics2Subject(s.Name));
    }

    private static List<MaterialSubjectInfo> BuildSubjectsInfo(List<ArchiveFile> files, string level, string semester)
    {
        // New layout: Data/{SubjectFolder}/PDFs/{Lecturer}/{Folder}/*.pdf
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (!f.Name.StartsWith("Data/", StringComparison.Ordinal)) continue;
            if (!f.Name.Contains("/PDFs/", StringComparison.OrdinalIgnoreCase)) continue;
            if (!f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Name.ToLowerInvariant().EndsWith("_text.pdf")) continue;
            if (f.Name.EndsWith("order_config.json", StringComparison.OrdinalIgnoreCase)) continue;
            var segs = f.Name.Split('/');
            if (segs.Length < 2) continue;
            var subjectFull = segs[1];
            if (string.IsNullOrWhiteSpace(subjectFull) || subjectFull.Equals("order_config.json", StringComparison.OrdinalIgnoreCase)) continue;
            map.TryGetValue(subjectFull, out var c);
            map[subjectFull] = c + 1;
        }
        return map
            .Select(kv =>
            {
                ArchiveCatalog.ParseSubjectFolder(kv.Key, out var code, out var clean, out var branch);
                return new MaterialSubjectInfo
                {
                    Name = kv.Key,
                    FileCount = kv.Value,
                    Code = code,
                    DisplayName = string.IsNullOrEmpty(clean) ? kv.Key : clean,
                    Branch = branch
                };
            })
            .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Orders folders (lec/tut/lab/quiz first, then alphabetical) from an already
    /// filtered per-subject file list.
    /// </summary>
    public static List<string> GetFolderOrder(List<MaterialFile> files)
    {
        var folderSet = new HashSet<string>(files.Select(f => f.Folder));
        var folderOrder = new[] { "lec", "tut", "lab", "quiz" };
        var list = folderSet.ToList();
        list.Sort((a, b) =>
        {
            var aName = a.ToLower();
            var bName = b.ToLower();
            var iA = Array.FindIndex(folderOrder, o => aName.Contains(o));
            var iB = Array.FindIndex(folderOrder, o => bName.Contains(o));
            if (iA == -1) iA = 99;
            if (iB == -1) iB = 99;
            var cmp = iA.CompareTo(iB);
            return cmp != 0 ? cmp : string.Compare(aName, bName, StringComparison.Ordinal);
        });
        return list;
    }

    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<ArchiveFile>> _filesMemCache = new();

    public async Task<List<ArchiveFile>> GetFilesAsync(string level, string semester, bool force = false)
    {
        var memKey = $"{level}_{semester}";
        if (!force && _filesMemCache.TryGetValue(memKey, out var mem) && mem != null)
            return mem;

        var cacheKey = $"{CacheVersion}{level}_{semester}";
        if (!force)
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<List<ArchiveFile>>(cached, CaseInsensitive);
                    if (parsed != null && parsed.Count > 0)
                    {
                        _filesMemCache[memKey] = parsed;
                        return parsed;
                    }
                }
                catch { }
            }
        }

        try
        {
            // Parse + filter the big metadata in JS and receive only this semester's
            // compact {name,size} list (~150 KB) — keeps the 2.3 MB off the .NET thread.
            // force:true re-fetches the raw metadata and overwrites the sem cache.
            var json = await _js.InvokeAsync<string>("nmuFunctions.getSemesterFiles", level, semester, force);
            _logger.LogDebug("GetFilesAsync: getSemesterFiles returned {Len} chars", json?.Length ?? 0);
            if (!string.IsNullOrEmpty(json))
            {
                var files = JsonSerializer.Deserialize<List<ArchiveFile>>(json, CaseInsensitive) ?? new List<ArchiveFile>();
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

        // Fallback (older cached app.js, or JS fetch failed): fetch the full metadata
        // JSON and filter it in .NET. Slower, but guarantees the page isn't empty.
        try
        {
            var archiveId = ArchiveCatalog.GetArchiveId(level, semester);
            if (archiveId == null) return new List<ArchiveFile>();
            var fullJson = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", ArchiveCatalog.GetMetadataUrl(archiveId));
            _logger.LogInformation("GetFilesAsync: fetchJson returned {Len} chars", fullJson?.Length ?? 0);
            if (!string.IsNullOrEmpty(fullJson))
            {
                var data = JsonSerializer.Deserialize<ArchiveMetadata>(fullJson);
                var files = data?.Files?
                    .Where(f => f.Name.StartsWith("Data/", StringComparison.Ordinal)
                        && !ArchiveCatalog.IsDerivativeFile(f.Name)
                        && !f.Name.StartsWith($"{archiveId}.thumbs/", StringComparison.Ordinal))
                    .Select(f => new ArchiveFile { Name = f.Name, Size = long.TryParse(f.Size, out var s) ? s : null })
                    .ToList() ?? new List<ArchiveFile>();
                _logger.LogInformation("GetFilesAsync: fallback found {Count} files", files.Count);
                if (files.Count > 0)
                {
                    _filesMemCache[memKey] = files;
                    await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", cacheKey, JsonSerializer.Serialize(files));
                }
                return files;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetFilesAsync: fallback failed: {Message}", ex.Message);
        }

        return new List<ArchiveFile>();
    }

    /// <summary>
    /// New layout: Data/{subject}/PDFs/{lecturer}/{folder}/*.pdf
    /// subject is the FULL folder name (e.g. "CSE014 - Structured Programming.(ALL)").
    /// Returns (folders across all lecturers, files with Lecturer populated).
    /// </summary>
    public static (List<string> folders, List<MaterialFile> files) GetFilesForSubject(
        List<ArchiveFile> allFiles, string level, string semester, string subject)
    {
        var prefix = $"Data/{subject}/PDFs/";
        var folderSet = new HashSet<string>();
        var result = new List<MaterialFile>();

        foreach (var f in allFiles)
        {
            if (!f.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Name.ToLowerInvariant().EndsWith("_text.pdf")) continue;
            var rel = f.Name[prefix.Length..];
            var parts = rel.Split('/');
            if (parts.Length >= 3)
            {
                var lecturer = parts[0];
                var folder = parts[1];
                folderSet.Add(folder);
                result.Add(new MaterialFile
                {
                    Name = parts[^1],
                    Path = f.Name,
                    Folder = folder,
                    Lecturer = lecturer,
                    Size = f.Size
                });
            }
            else if (parts.Length == 2)
            {
                var lecturer = parts[0];
                result.Add(new MaterialFile
                {
                    Name = parts[1],
                    Path = f.Name,
                    Folder = "ROOT",
                    Lecturer = lecturer,
                    Size = f.Size
                });
            }
        }

        var folderOrder = new[] { "lec", "tut", "lab", "quiz" };
        var foldersList = folderSet.ToList();
        foldersList.Sort((a, b) =>
        {
            var aName = a.ToLower();
            var bName = b.ToLower();
            var iA = Array.FindIndex(folderOrder, o => aName.Contains(o));
            var iB = Array.FindIndex(folderOrder, o => bName.Contains(o));
            if (iA == -1) iA = 99;
            if (iB == -1) iB = 99;
            var cmp = iA.CompareTo(iB);
            return cmp != 0 ? cmp : string.Compare(aName, bName, StringComparison.Ordinal);
        });

        return (foldersList, result);
    }

    /// <summary>Distinct lecturers for a subject (Material &gt; Doctor level).</summary>
    public static List<string> GetLecturersForSubject(List<ArchiveFile> allFiles, string subject)
    {
        var prefix = $"Data/{subject}/PDFs/";
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in allFiles)
        {
            if (!f.Name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!f.Name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Name.ToLowerInvariant().EndsWith("_text.pdf")) continue;
            var rel = f.Name[prefix.Length..];
            var parts = rel.Split('/');
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[0]))
                set.Add(parts[0]);
        }
        return set.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Folders for a specific subject+lecturer.</summary>
    public static List<string> GetFoldersForLecturer(List<MaterialFile> subjectFiles, string lecturer)
    {
        var subset = subjectFiles.Where(f => string.Equals(f.Lecturer, lecturer, StringComparison.Ordinal)).ToList();
        return GetFolderOrder(subset);
    }

    public async Task<List<string>> GetFolderOrderAsync(string dirPath, string? level = null, string? semester = null, string? archiveId = null)
    {
        archiveId ??= (level != null && semester != null) ? ArchiveCatalog.GetArchiveId(level, semester) : null;
        if (archiveId == null) return new List<string>();
        // Pass RAW path: GetDownloadUrl encodes each segment exactly once.
        var url = $"{ArchiveCatalog.GetDownloadUrl(archiveId, dirPath)}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", url);
            var data = JsonSerializer.Deserialize<OrderConfig>(json);
            return data?.Order ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static string GetDownloadUrl(string filePath, string? level = null, string? semester = null, string? archiveId = null)
    {
        archiveId ??= (level != null && semester != null) ? ArchiveCatalog.GetArchiveId(level, semester) : null;
        if (archiveId == null) return filePath;
        return ArchiveCatalog.GetDownloadUrl(archiveId, filePath);
    }

    public static (string icon, string colorClass) GetMaterialStyle(string name)
    {
        // Unified catalog: exact code match first, keyword fallback otherwise.
        var s = SubjectIcons.Resolve(null, name);
        return (s.Icon, s.ColorClass);
    }
}

