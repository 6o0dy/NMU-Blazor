using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class RecordedService
{
    private readonly IJSRuntime _js;
    private readonly ILogger<RecordedService> _logger;
    private const string CacheVersion = "v70_newarch_recorded_";
    private const string GroupsCacheVersion = "v70_newarch_recgroups_";

    public RecordedService(IJSRuntime js, ILogger<RecordedService> logger)
    {
        _js = js;
        _logger = logger;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, List<RecordedFile>> _filesMemCache = new();

    public async Task<List<RecordedFile>> GetFilesAsync(string level, string semester, bool force = false)
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
                    var parsed = JsonSerializer.Deserialize<List<RecordedFile>>(cached, CaseInsensitive);
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
            // Parse the big metadata in JS and receive only this semester's recorded
            // list (with resolved thumbnails) as a compact PascalCase JSON string.
            // force:true re-fetches the raw metadata and overwrites the rec cache.
            var json = await _js.InvokeAsync<string>("nmuFunctions.getRecordedFiles", level, semester, force);
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
            var archiveId = ArchiveCatalog.GetArchiveId(level, semester);
            if (archiveId == null) return new List<RecordedFile>();
            string? fullJson;
            if (force)
            {
                // Always-fresh network fetch + refresh the raw metadata cache.
                fullJson = await _js.InvokeAsync<string>("nmuFunctions.fetchText", ArchiveCatalog.GetMetadataUrl(archiveId));
                if (!string.IsNullOrEmpty(fullJson))
                {
                    try { await _js.InvokeVoidAsync("nmuFunctions.setRawMetadata", archiveId, fullJson); } catch { }
                }
            }
            else
                fullJson = await GetRawMetadataAsync(level, semester);
            if (string.IsNullOrEmpty(fullJson))
                return new List<RecordedFile>();
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(fullJson);
            var thumbsPrefix = $"{archiveId}.thumbs/Data/";

            var thumbNames = data?.Files?
                .Where(f => f.Name.StartsWith(thumbsPrefix, StringComparison.Ordinal) && f.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Name)
                .ToHashSet() ?? new HashSet<string>();

            var files = data?.Files?
                .Where(f => f.Name.StartsWith("Data/", StringComparison.Ordinal)
                    && f.Name.Contains("/Records/", StringComparison.OrdinalIgnoreCase)
                    && ArchiveCatalog.IsRecordedMedia(f.Name))
                .Select(f =>
                {
                    var lower = f.Name.ToLower();
                    var fileNoExt = System.IO.Path.GetFileNameWithoutExtension(f.Name);
                    ParseRecordedPath(f.Name, out var subjectFull, out var lecturer);
                    return new RecordedFile
                    {
                        Name = f.Name,
                        Size = long.TryParse(f.Size, out var s) ? s : null,
                        ThumbName = thumbNames.FirstOrDefault(t => t.Contains(fileNoExt)),
                        IsAudio = ArchiveCatalog.IsAudioFile(f.Name),
                        Lecturer = lecturer,
                        SubjectFullName = subjectFull,
                        ArchiveId = archiveId,
                        DisplayName = fileNoExt.Replace("_", " "),
                        SubFolder = string.IsNullOrEmpty(lecturer) ? "General" : lecturer
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
        var info = BuildGroupsInfo(files, level, semester, groups);

        if (info.Count > 0)
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", GroupsCacheKey(level, semester), JsonSerializer.Serialize(info, CaseInsensitive));
        return info;
    }

    private static string GroupsCacheKey(string level, string semester)
        => $"{GroupsCacheVersion}{level}_{semester}";

    /// <summary>
    /// Background revalidation for the recorded lectures groups: at most one
    /// re-fetch per RevalidateAfter window (no network call in the check itself,
    /// since archive.org's search index does not list the NMU.CE_* identifiers).
    /// </summary>
    public async Task CheckAndUpdateRecordedAsync(string level, string semester, Action<List<RecordedGroupInfo>>? onGroupsUpdated = null)
    {
        if (string.IsNullOrEmpty(level) || string.IsNullOrEmpty(semester)) return;
        try
        {
            var metaKey = $"{GroupsCacheVersion}meta_{level}_{semester}";

            var online = await _js.InvokeAsync<bool>("nmuFunctions.isOnline");
            if (!online) return;

            if (!await IsRefreshDueAsync(metaKey)) return;

            _filesMemCache.TryRemove($"{level}_{semester}", out _);
            // Force: bypass the persistent recorded-list cache so newly added
            // archive videos are actually picked up (not re-served stale).
            var files = await GetFilesAsync(level, semester, force: true);
            var groups = GetGroups(files, level, semester);
            var info = BuildGroupsInfo(files, level, semester, groups);

            if (info.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", GroupsCacheKey(level, semester), JsonSerializer.Serialize(info, CaseInsensitive));
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", metaKey, JsonSerializer.Serialize(new QuizMeta
                {
                    ContentLength = files.Count,
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

    /// <summary>
    /// True when no successful refresh happened within the revalidation window
    /// (or never). Reads only the local timestamp — zero network calls.
    /// </summary>
    private async Task<bool> IsRefreshDueAsync(string metaKey)
    {
        try
        {
            var cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey);
            if (!string.IsNullOrEmpty(cachedMetaJson))
            {
                var cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson, CaseInsensitive);
                if (cachedMeta != null && cachedMeta.Timestamp > 0)
                {
                    var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - cachedMeta.Timestamp;
                    if (ageMs < ArchiveCatalog.RevalidateAfter.TotalMilliseconds)
                        return false;
                }
            }
        }
        catch { }
        return true;
    }

    /// <summary>
    /// Splits "Dr. Nourhan (LAB)" into clean name + parenthetical tag ("LAB").
    /// Lecturers without parentheses return an empty tag.
    /// </summary>
    public static (string Name, string Tag) SplitLecturerTag(string lecturer)
    {
        if (string.IsNullOrEmpty(lecturer)) return ("", "");
        var s = lecturer.IndexOf('(');
        var e = s >= 0 ? lecturer.IndexOf(')', s + 1) : -1;
        if (s > 0 && e > s + 1)
            return (lecturer.Substring(0, s).Trim(), lecturer.Substring(s + 1, e - s - 1).Trim());
        return (lecturer, "");
    }

    /// <summary>
    /// Parses "Data/{Subject}/Records/{Lecturer}/{file}" into subject + lecturer.
    /// Returns empty strings when the path does not match the new layout.
    /// </summary>
    public static void ParseRecordedPath(string fullPath, out string subjectFull, out string lecturer)
    {
        subjectFull = "";
        lecturer = "";
        if (string.IsNullOrEmpty(fullPath)) return;
        var segs = fullPath.Split('/');
        // Expect at least Data/{Subject}/Records/{Lecturer}/...
        if (segs.Length < 4) return;
        if (!segs[0].Equals("Data", StringComparison.Ordinal)) return;
        subjectFull = segs[1];
        var recIdx = Array.FindIndex(segs, s => s.Equals("Records", StringComparison.OrdinalIgnoreCase));
        if (recIdx < 0 || recIdx + 1 >= segs.Length) return;
        lecturer = segs[recIdx + 1];
    }

    public static List<RecordedGroupInfo> BuildGroupsInfo(List<RecordedFile> files, string level, string semester, List<string> groups)
    {
        return groups.Select(g =>
        {
            ArchiveCatalog.ParseSubjectFolder(g, out var code, out var clean, out var branch);
            return new RecordedGroupInfo
            {
                Name = g,
                Count = GetFilesForGroup(files, level, semester, g).Count,
                Code = code,
                DisplayName = string.IsNullOrEmpty(clean) ? g : clean,
                Branch = branch
            };
        }).ToList();
    }

    /// <summary>Distinct lecturers inside one subject group (Recorded &gt; Doctor level).</summary>
    public static List<string> GetLecturersForGroup(List<RecordedFile> allFiles, string level, string semester, string group)
    {
        var files = GetFilesForGroup(allFiles, level, semester, group);
        return files.Select(f => f.Lecturer)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<string> GetGroups(List<RecordedFile> files, string level, string semester)
    {
        // New layout: groups are subjects -> Data/{Subject}/Records/...
        var groups = new HashSet<string>(StringComparer.Ordinal);
        foreach (var f in files)
        {
            ParseRecordedPath(f.Name, out var subjectFull, out _);
            if (!string.IsNullOrEmpty(subjectFull))
                groups.Add(subjectFull);
        }
        // Order by clean display name for a stable UI.
        return groups.OrderBy(g =>
        {
            ArchiveCatalog.ParseSubjectFolder(g, out _, out var clean, out _);
            return string.IsNullOrEmpty(clean) ? g : clean;
        }, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<RecordedFile> GetFilesForGroup(List<RecordedFile> allFiles, string level, string semester, string group, string? lecturer = null)
    {
        var prefix = $"Data/{group}/Records/";
        var result = new List<RecordedFile>();

        foreach (var f in allFiles)
        {
            if (!f.Name.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            ParseRecordedPath(f.Name, out _, out var fileLecturer);
            if (!string.IsNullOrEmpty(lecturer)
                && !string.Equals(fileLecturer, lecturer, StringComparison.Ordinal))
                continue;

            var lower = f.Name.ToLower();
            if (lower.EndsWith(".ia.mp4")) continue;
            if (!ArchiveCatalog.IsRecordedMedia(f.Name))
                continue;

            var fileNoExt = System.IO.Path.GetFileNameWithoutExtension(f.Name);
            var displayName = fileNoExt.Replace("_", " ");

            result.Add(new RecordedFile
            {
                Name = f.Name,
                Size = f.Size,
                DisplayName = displayName,
                SubFolder = string.IsNullOrEmpty(fileLecturer) ? "General" : fileLecturer.Replace("_", " "),
                IsAudio = f.IsAudio,
                ThumbName = f.ThumbName,
                Lecturer = fileLecturer,
                SubjectFullName = group,
                ArchiveId = f.ArchiveId
            });
        }

        return result.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string GetDownloadUrl(string filePath, string? level = null, string? semester = null, string? archiveId = null)
    {
        if (!string.IsNullOrEmpty(archiveId))
            return ArchiveCatalog.GetDownloadUrl(archiveId, filePath);
        archiveId = (level != null && semester != null) ? ArchiveCatalog.GetArchiveId(level, semester) : null;
        if (archiveId == null) return filePath;
        return ArchiveCatalog.GetDownloadUrl(archiveId, filePath);
    }

    public static string GetDownloadUrl(RecordedFile file)
    {
        var archiveId = !string.IsNullOrEmpty(file.ArchiveId)
            ? file.ArchiveId
            : null;
        if (archiveId != null)
            return ArchiveCatalog.GetDownloadUrl(archiveId, file.Name);
        return file.Name;
    }

    /// <summary>
    /// Returns the full archive metadata JSON for one semester archive.
    /// Cached per-archive in IndexedDB so each feature shares the same copy.
    /// </summary>
    public async Task<string?> GetRawMetadataAsync(string? level = null, string? semester = null, string? archiveId = null)
    {
        archiveId ??= (level != null && semester != null) ? ArchiveCatalog.GetArchiveId(level, semester) : null;
        if (archiveId == null) return null;
        try
        {
            var cached = await _js.InvokeAsync<string>("nmuFunctions.getRawMetadata", archiveId);
            if (!string.IsNullOrEmpty(cached))
                return cached;
        }
        catch { }

        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchText", ArchiveCatalog.GetMetadataUrl(archiveId));
            if (!string.IsNullOrEmpty(json))
            {
                try { await _js.InvokeVoidAsync("nmuFunctions.setRawMetadata", archiveId, json); } catch { }
                return json;
            }
        }
        catch { }
        return null;
    }

    public static string GetIconClass(string name)
    {
        // Unified catalog (same icon on every page); keyword fallback inside.
        return SubjectIcons.Resolve(null, name).Icon;
    }
}
