using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class MaterialsService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly ILogger<MaterialsService> _logger;
    private const string ArchiveId = "nmu.ce";
    private const string BaseFolder = "NMU";
    private const string CacheVersion = "v68_restore_original_logic_";
    private const string SubjectsCacheVersion = "v1_subjects_";
    private const string SubjectFilesCacheVersion = "v1_subject_files_";

    public MaterialsService(IJSRuntime js, HttpClient http, ILogger<MaterialsService> logger)
    {
        _js = js;
        _http = http;
        _logger = logger;
    }

    private static string SubjectsCacheKey(string level, string semester)
        => $"{SubjectsCacheVersion}{level}_{semester}";

    private static string SubjectFilesCacheKey(string level, string semester, string subject)
        => $"{SubjectFilesCacheVersion}{level}_{semester}_{subject}";

    private static string MetaCacheKey(string level, string semester)
        => $"nmu_mat_meta_{level}_{semester}";

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
            var files = await GetFilesAsync(level, semester);
            var subjects = BuildSubjectsInfo(files, level, semester);
            if (subjects.Count > 0)
            {
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItemBoth", SubjectsCacheKey(level, semester), JsonSerializer.Serialize(subjects));
                await SaveMetaAsync(metaKey, check.Value);
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
            var files = await GetFilesAsync(level, semester);
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

        // archive.org rejects HEAD on /metadata (405, no CORS). Instead GET the item
        // size from the search API (CORS-enabled); item_size changes whenever any
        // file is added/removed, which is exactly what revalidation needs to detect.
        var serverLength = 0L;
        using (var request = new HttpRequestMessage(HttpMethod.Get, $"https://archive.org/advancedsearch.php?q=identifier:{ArchiveId}&fl[]=identifier&fl[]=item_size&rows=1&output=json"))
        using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
        {
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SearchResponse>(json);
            serverLength = data?.Response?.Docs?.FirstOrDefault()?.ItemSize ?? 0;
        }
        if (serverLength <= 0) return null;

        string? cachedMetaJson = null;
        try { cachedMetaJson = await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", metaKey); } catch { }

        if (!string.IsNullOrEmpty(cachedMetaJson))
        {
            try
            {
                var cachedMeta = JsonSerializer.Deserialize<QuizMeta>(cachedMetaJson);
                if (cachedMeta != null && cachedMeta.ContentLength > 0)
                {
                    if (serverLength == cachedMeta.ContentLength)
                        return (false, serverLength, "", "");
                }
            }
            catch { }
        }

        return (true, serverLength, "", "");
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

    private static List<MaterialSubjectInfo> BuildSubjectsInfo(List<ArchiveFile> files, string level, string semester)
    {
        var prefix = $"{BaseFolder}/{level}/{semester}/PDF/";
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            if (!f.Name.StartsWith(prefix)) continue;
            if (f.Name.ToLowerInvariant().EndsWith("_text.pdf")) continue;
            var rel = f.Name[prefix.Length..];
            var parts = rel.Split('/');
            if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            {
                map.TryGetValue(parts[0], out var c);
                map[parts[0]] = c + 1;
            }
        }
        return map
            .Select(kv => new MaterialSubjectInfo { Name = kv.Key, FileCount = kv.Value })
            .OrderBy(s => s.Name, StringComparer.Ordinal)
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

        try
        {
            // Parse + filter the big metadata in JS and receive only this semester's
            // compact {name,size} list (~150 KB) — keeps the 2.3 MB off the .NET thread.
            var json = await _js.InvokeAsync<string>("nmuFunctions.getSemesterFiles", level, semester);
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
            var fullJson = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", $"https://archive.org/metadata/{ArchiveId}");
            _logger.LogInformation("GetFilesAsync: fetchJson returned {Len} chars", fullJson?.Length ?? 0);
            if (!string.IsNullOrEmpty(fullJson))
            {
                var data = JsonSerializer.Deserialize<ArchiveMetadata>(fullJson);
                var files = data?.Files?
                    .Where(f => f.Name.StartsWith($"{BaseFolder}/{level}/{semester}/", StringComparison.Ordinal))
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

    public static (List<string> folders, List<MaterialFile> files) GetFilesForSubject(
        List<ArchiveFile> allFiles, string level, string semester, string subject)
    {
        var prefix = $"{BaseFolder}/{level}/{semester}/PDF/{subject}/";
        var folderSet = new HashSet<string>();
        var result = new List<MaterialFile>();

        foreach (var f in allFiles)
        {
            if (!f.Name.StartsWith(prefix)) continue;
            var rel = f.Name[prefix.Length..];
            var parts = rel.Split('/');
            if (parts.Length > 1)
            {
                folderSet.Add(parts[0]);
                result.Add(new MaterialFile
                {
                    Name = parts[^1],
                    Path = f.Name,
                    Folder = parts[0],
                    Size = f.Size
                });
            }
            else
            {
                result.Add(new MaterialFile
                {
                    Name = parts[0],
                    Path = f.Name,
                    Folder = "ROOT",
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

    public async Task<List<string>> GetFolderOrderAsync(string dirPath)
    {
        var encoded = string.Join("/", dirPath.Split('/').Select(Uri.EscapeDataString));
        var url = $"https://archive.org/download/{ArchiveId}/{encoded}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
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

    public static string GetDownloadUrl(string filePath)
    {
        return $"https://archive.org/download/{ArchiveId}/{filePath}";
    }

    public static (string icon, string colorClass) GetMaterialStyle(string name)
    {
        var n = name.ToLower();
        if (n.Contains("arabic")) return ("fa-solid fa-pen-nib", "color-arabic");
        if (n.Contains("english") || n.Contains("communication") || n.Contains("psychology") || n.Contains("history"))
            return ("fa-solid fa-language", "color-english");
        if (n.Contains("university") || n.Contains("social") || n.Contains("management") || n.Contains("marketing") || n.Contains("humanities"))
            return ("fa-solid fa-building-columns", "color-english");
        if (n.Contains("mat") || n.Contains("math") || n.Contains("calc") || n.Contains("algebra") || n.Contains("diff") || n.Contains("stat") || n.Contains("numerical") || n.Contains("discrete") || n.Contains("optimization") || n.Contains("analysis") || n.Contains("probabilit"))
            return ("fa-solid fa-calculator", "color-math");
        if (n.Contains("phy") || n.Contains("phys") || n.Contains("chem") || n.Contains("magnetic") || n.Contains("optic") || n.Contains("field"))
            return ("fa-solid fa-atom", "color-phys");
        if (n.Contains("mec") || n.Contains("mech") || n.Contains("static") || n.Contains("dynamic") || n.Contains("control") || n.Contains("material"))
            return ("fa-solid fa-gears", "color-mech");
        if (n.Contains("draw") || n.Contains("graphic") || n.Contains("vision") || n.Contains("image") || n.Contains("visual") || n.Contains("game") || n.Contains("animation") || n.Contains("reality"))
            return ("fa-solid fa-compass-drafting", "color-draw");
        if (n.Contains("security") || n.Contains("secure") || n.Contains("crypto") || n.Contains("forensic") || n.Contains("cyber"))
            return ("fa-solid fa-shield-halved", "color-tech");
        if (n.Contains("robot") || n.Contains("kinematic") || n.Contains("map") || n.Contains("localiz") || n.Contains("autonomous"))
            return ("fa-solid fa-robot", "color-mech");
        if (n.Contains("ele") || n.Contains("electric") || n.Contains("electronic") || n.Contains("circuit") || n.Contains("embedded") || n.Contains("iot") || n.Contains("internet of things") || n.Contains("signal") || n.Contains("measure") || n.Contains("network") || n.Contains("architect") || n.Contains("organization") || n.Contains("logic") || n.Contains("hardware") || n.Contains("sensor"))
            return ("fa-solid fa-microchip", "color-mech");
        if (n.Contains("aie") || n.Contains("ai") || n.Contains("intelligen") || n.Contains("learning") || n.Contains("neural") || n.Contains("knowledg") || n.Contains("mining") || n.Contains("data") || n.Contains("nlp") || n.Contains("natural") || n.Contains("cognitive") || n.Contains("recommender") || n.Contains("pattern") || n.Contains("evolution") || n.Contains("reasoning") || n.Contains("fuzzy") || n.Contains("bio-inspired") || n.Contains("decision"))
            return ("fa-solid fa-brain", "color-prog");
        if (n.Contains("cse") || n.Contains("prog") || n.Contains("code") || n.Contains("struct") || n.Contains("object") || n.Contains("oop") || n.Contains("web") || n.Contains("soft") || n.Contains("cloud") || n.Contains("parallel") || n.Contains("distribut") || n.Contains("compiler") || n.Contains("comput") || n.Contains("algorithm") || n.Contains("os") || n.Contains("operating") || n.Contains("database") || n.Contains("system") || n.Contains("high performance"))
            return ("fa-solid fa-laptop-code", "color-prog");
        if (n.Contains("report") || n.Contains("tech") || n.Contains("writ") || n.Contains("search") || n.Contains("project") || n.Contains("training") || n.Contains("grad"))
            return ("fa-solid fa-file-lines", "color-tech");
        return ("fa-solid fa-book-open", "color-default");
    }
}
