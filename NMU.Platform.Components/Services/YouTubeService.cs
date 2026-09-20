using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class YouTubeService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly StudentService _studentService;

    private const string FirebaseUrl = "https://nmu-ce-default-rtdb.firebaseio.com";
    private const string CacheVersion = "v6_yt_";

    // Master (UNFILTERED) list. Filtering by the CURRENT student happens on
    // every return, so switching modes/subjects can never show stale data.
    private List<YouTubeChannel> _channels = new();
    private string? _scopeKey;
    private readonly Dictionary<string, string> _lastArchiveJson = new();

    public YouTubeService(IJSRuntime js, HttpClient http, StudentService studentService)
    {
        _js = js;
        _http = http;
        _studentService = studentService;
    }

    public List<YouTubeChannel> Channels => _channels;

    public async Task<List<YouTubeChannel>> GetChannelsAsync()
    {
        var (student, studentLevel, studentSemester, rawLevel, rawSemester) = await ResolveStudentFoldersAsync();
        var custom = IsCustom(student);
        // Scope changes (mode switch, different selections/term) invalidate memory.
        var scopeKey = custom ? CustomScopeKey(student!) : $"{studentLevel}_{studentSemester}";

        if (_channels.Count == 0 || _scopeKey != scopeKey)
        {
            _scopeKey = scopeKey;
            if (custom)
                _channels = await LoadCustomMasterAsync(student!);
            else
                _channels = await LoadTermMasterAsync(studentLevel, studentSemester, rawLevel, rawSemester);
        }

        return FilterBySelections(_channels, student);
    }

    private static bool IsCustom(StudentProfile? student)
        => student?.CustomSubjectsMode == true && student.CustomSubjects != null && student.CustomSubjects.Count > 0;

    private static string CustomScopeKey(StudentProfile student)
    {
        var parts = student.CustomSubjects!
            .Select(s => $"{s.Level}|{s.Semester}|{s.Subject}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);
        return "custom:" + string.Join(";", parts);
    }

    /// <summary>
    /// Custom mode: the student's subjects may live in DIFFERENT terms, so
    /// load every involved archive and merge (deduped by GroupKey).
    /// </summary>
    private async Task<List<YouTubeChannel>> LoadCustomMasterAsync(StudentProfile student)
    {
        var merged = new List<YouTubeChannel>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var grp in student.CustomSubjects!
            .GroupBy(s => (s.Level, s.Semester))
            .OrderBy(g => g.Key.Level, StringComparer.OrdinalIgnoreCase)
            .ThenBy(g => g.Key.Semester, StringComparer.OrdinalIgnoreCase))
        {
            var level = ArchiveCatalog.NormalizeLevel(grp.Key.Level) ?? grp.Key.Level;
            var semester = ArchiveCatalog.NormalizeSemester(grp.Key.Semester) ?? grp.Key.Semester;
            foreach (var c in await LoadTermMasterAsync(level, semester, grp.Key.Level, grp.Key.Semester))
            {
                if (!string.IsNullOrEmpty(c.GroupKey) && seen.Add(c.GroupKey))
                    merged.Add(c);
            }
        }
        return merged;
    }

    /// <summary>Full master list for ONE term archive (localStorage → archive → Firebase).</summary>
    private async Task<List<YouTubeChannel>> LoadTermMasterAsync(
        string studentLevel, string studentSemester, string rawLevel, string rawSemester)
    {
        var cacheKey = $"{CacheVersion}{studentLevel}_{studentSemester}";
        var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<YouTubeChannel>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }

        var fetched = await FetchArchiveChannelsAsync(studentLevel, studentSemester);
        if (fetched.Count == 0)
            fetched = await FetchFirebaseChannelsAsync(studentLevel, studentSemester, rawLevel, rawSemester);
        if (fetched.Count > 0)
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(fetched));
        return fetched;
    }

    /// <summary>
    /// Custom-subjects mode: show only channels whose subject is one of the
    /// student's registered subjects (exact archive-folder match, fuzzy fallback).
    /// Normal mode returns everything.
    /// </summary>
    public static List<YouTubeChannel> FilterBySelections(
        List<YouTubeChannel> channels, StudentProfile? student)
    {
        if (student?.CustomSubjectsMode != true || student.CustomSubjects == null
            || student.CustomSubjects.Count == 0)
            return channels;
        var wanted = student.CustomSubjects
            .Select(s => s.Subject)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return channels.Where(c =>
        {
            var folder = c.SubjectFolder;
            if (string.IsNullOrEmpty(folder) && c.GroupKey.Contains("||", StringComparison.Ordinal))
                folder = c.GroupKey.Split(new[] { "||" }, StringSplitOptions.None)[0];
            return wanted.Any(w =>
                string.Equals(w, folder, StringComparison.OrdinalIgnoreCase) ||
                SubjectMatcher.Matches(w, folder ?? "") ||
                SubjectMatcher.Matches(w, c.Subject));
        }).ToList();
    }

    private async Task<(StudentProfile? Student, string Level, string Semester, string RawLevel, string RawSemester)> ResolveStudentFoldersAsync()
    {
        var student = await _studentService.GetStudentAsync();
        var rawLevel = student?.AcademicLevel?.Replace(" ", "_") ?? "Level_1";
        var rawSemester = student?.Semester?.Replace(" ", "_") ?? "First_Term";
        // Canonical archive folders (Level_1/Semester_1). Firebase actually
        // stores Semester_1, not First_Term — the old raw path returned null.
        var studentLevel = ArchiveCatalog.NormalizeLevel(rawLevel) ?? rawLevel;
        var studentSemester = ArchiveCatalog.NormalizeSemester(rawSemester) ?? rawSemester;
        return (student, studentLevel, studentSemester, rawLevel, rawSemester);
    }

    /// <summary>
    /// Background revalidation (same pattern as Materials/Recorded): re-fetch
    /// the archive youtube.json; if it changed, refresh cache + notify caller.
    /// Custom mode revalidates every involved archive, then merges.
    /// </summary>
    public async Task CheckAndUpdateAsync(Func<List<YouTubeChannel>, StudentProfile?, Task>? onUpdated = null)
    {
        try
        {
            var (student, studentLevel, studentSemester, _, _) = await ResolveStudentFoldersAsync();
            List<(string Level, string Semester)> archives;
            if (IsCustom(student))
            {
                archives = student!.CustomSubjects!
                    .GroupBy(s => (s.Level, s.Semester))
                    .Select(g => (
                        ArchiveCatalog.NormalizeLevel(g.Key.Level) ?? g.Key.Level,
                        ArchiveCatalog.NormalizeSemester(g.Key.Semester) ?? g.Key.Semester))
                    .Distinct()
                    .ToList();
            }
            else
            {
                archives = new() { (studentLevel, studentSemester) };
            }

            var merged = new List<YouTubeChannel>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var (lvl, sem) in archives)
            {
                var akey = $"{lvl}_{sem}";
                var before = _lastArchiveJson.TryGetValue(akey, out var prev) ? prev : null;
                var fresh = await FetchArchiveJsonTextAsync(lvl, sem);
                if (fresh == null || fresh == before) continue;
                changed = true;
                _lastArchiveJson[akey] = fresh;
                var parsed = ParseArchiveJson(fresh);
                if (parsed.Count == 0) continue;
                var cacheKey = $"{CacheVersion}{akey}";
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(parsed));
                foreach (var c in parsed)
                {
                    if (!string.IsNullOrEmpty(c.GroupKey) && seen.Add(c.GroupKey))
                        merged.Add(c);
                }
            }

            if (!changed || merged.Count == 0) return;
            _channels = merged;
            _scopeKey = IsCustom(student) ? CustomScopeKey(student!) : $"{studentLevel}_{studentSemester}";
            if (onUpdated != null)
                await onUpdated(FilterBySelections(_channels, student), student);
        }
        catch { }
    }
    public async Task CheckAndUpdateAsync(string level, string semester,
        Func<List<YouTubeChannel>, StudentProfile?, Task>? onUpdated = null)
    {
        try
        {
            var akey = $"{level}_{semester}";
            var before = _lastArchiveJson.TryGetValue(akey, out var prev) ? prev : null;
            var json = await FetchArchiveJsonTextAsync(level, semester);
            if (string.IsNullOrEmpty(json) || json == before) return;
            _lastArchiveJson[akey] = json;
            var parsed = ParseArchiveJson(json);
            if (parsed.Count == 0) return;

            var student = await _studentService.GetStudentAsync();
            _channels = parsed;
            var cacheKey = $"{CacheVersion}{level}_{semester}";
            await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(_channels));
            if (onUpdated != null)
                await onUpdated(FilterBySelections(_channels, student), student);
        }
        catch { }
    }

    private async Task<string?> FetchArchiveJsonTextAsync(string level, string semester)
    {
        try
        {
            var archiveId = ArchiveCatalog.GetArchiveId(level, semester);
            if (archiveId == null) return null;
            var urls = new[]
            {
                $"https://archive.org/download/{archiveId}/Data/youtube_{archiveId}.json",
                $"https://archive.org/download/{archiveId}/youtube_{archiveId}.json"
            };
            foreach (var url in urls)
            {
                try
                {
                    using var response = await _http.GetAsync(url);
                    if (!response.IsSuccessStatusCode) continue;
                    return await response.Content.ReadAsStringAsync();
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Primary source: youtube_{archiveId}.json hosted INSIDE the archive item
    /// itself (same place as the PDFs/recordings/quizzes metadata), e.g.
    /// https://archive.org/download/NMU.CE_1.1/Data/youtube_NMU.CE_1.1.json
    /// (root-level URL also tried as fallback). Falls through silently when
    /// the file isn't uploaded yet.
    /// </summary>
    private async Task<List<YouTubeChannel>> FetchArchiveChannelsAsync(string studentLevel, string studentSemester)
    {
        var json = await FetchArchiveJsonTextAsync(studentLevel, studentSemester);
        if (string.IsNullOrEmpty(json)) return new();
        _lastArchiveJson[$"{studentLevel}_{studentSemester}"] = json;
        return ParseArchiveJson(json);
    }

    public static List<YouTubeChannel> ParseArchiveJson(string json)
    {
        var result = new List<YouTubeChannel>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("subjects", out var subjects)) return result;
            foreach (var subj in subjects.EnumerateArray())
            {
                var folder = subj.TryGetProperty("subjectFolder", out var sf) ? sf.GetString() ?? "" : "";
                if (string.IsNullOrEmpty(folder)) continue;
                if (!subj.TryGetProperty("channels", out var channels) &&
                    !subj.TryGetProperty("lecturers", out channels)) continue;
                foreach (var ch in channels.EnumerateArray())
                {
                    // New shape: {name, handle, avatar}. Old shape: {key, name, avatar}.
                    var key = ch.TryGetProperty("handle", out var h) ? h.GetString() ?? ""
                        : ch.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    var name = ch.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(name)) name = key;
                    var avatar = ch.TryGetProperty("avatar", out var a) ? a.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(avatar)) avatar = GenerateAvatarUrl(key);
                    var videos = new List<YouTubeVideo>();
                    if (ch.TryGetProperty("videos", out var vids))
                    {
                        foreach (var v in vids.EnumerateArray())
                        {
                            var url = v.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                            if (string.IsNullOrEmpty(url)) continue;
                            videos.Add(new YouTubeVideo
                            {
                                Title = v.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                                Url = url,
                                Img = v.TryGetProperty("img", out var i) ? i.GetString() ?? "" : "",
                                VideoId = ExtractYouTubeId(url) ?? ""
                            });
                        }
                    }
                    videos.Reverse();
                    result.Add(new YouTubeChannel
                    {
                        ChannelName = name,
                        Subject = SubjectDisplayName(folder),
                        SubjectFolder = folder,
                        AvatarUrl = avatar,
                        GroupKey = $"{folder}||{key}",
                        Videos = videos
                    });
                }
            }
        }
        catch { }
        return result;
    }

    private async Task<List<YouTubeChannel>> FetchFirebaseChannelsAsync(string studentLevel, string studentSemester,
        string rawLevel, string rawSemester)
    {
        // Canonical path first, legacy (pre-migration) path as fallback so the
        // section works both before and after the Firebase migration script.
        var paths = new List<(string Level, string Semester)> { (studentLevel, studentSemester) };
        if (!string.Equals(rawLevel, studentLevel, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(rawSemester, studentSemester, StringComparison.OrdinalIgnoreCase))
            paths.Add((rawLevel, rawSemester));

        foreach (var (lvl, sem) in paths)
        {
            try
            {
                var url = $"{FirebaseUrl}/NMU/{lvl}/{sem}/Channels.json";
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode) continue;

                var json = await response.Content.ReadAsStringAsync();
                if (json == "null" || json == "{}") continue;

                var parsed = ParseChannelsFromJson(json);
                if (parsed.Count == 0) continue;

                return parsed;
            }
            catch { }
        }
        return new();
    }

    private static List<YouTubeChannel> ParseChannelsFromJson(string json)
    {
        var result = new List<YouTubeChannel>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var subjectProp in doc.RootElement.EnumerateObject())
            {
                var subjectKey = subjectProp.Name;
                // Exact archive folder published by the migration script
                // (Firebase keys forbid dots, so "CSE014 - ....(ALL)" lives
                // in this child field instead of the key itself).
                var folder = subjectKey;
                if (subjectProp.Value.ValueKind == JsonValueKind.Object &&
                    subjectProp.Value.TryGetProperty("subjectFolder", out var sf) &&
                    sf.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(sf.GetString()))
                    folder = sf.GetString()!;
                // Display uses the clean name (no code/branch), like every
                // other page in the project.

                foreach (var channelProp in subjectProp.Value.EnumerateObject())
                {
                    var channelKey = channelProp.Name;
                    var channelObj = channelProp.Value;
                    var channelName = "";
                    var videos = new List<YouTubeVideo>();

                    foreach (var prop in channelObj.EnumerateObject())
                    {
                        if (prop.Name == "channelName")
                        {
                            channelName = prop.Value.GetString() ?? channelKey;
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            var video = ParseVideo(prop.Value);
                            if (video != null)
                                videos.Add(video);
                        }
                    }

                    if (string.IsNullOrEmpty(channelName))
                        channelName = channelKey;

                    videos.Reverse();

                    var avatarUrl = GenerateAvatarUrl(channelKey);

                    result.Add(new YouTubeChannel
                    {
                        ChannelName = channelName,
                        Subject = SubjectDisplayName(folder),
                        SubjectFolder = folder,
                        AvatarUrl = avatarUrl,
                        GroupKey = $"{folder}||{channelKey}",
                        Videos = videos
                    });
                }
            }
        }
        catch { }
        return result;
    }

    private static YouTubeVideo? ParseVideo(JsonElement el)
    {
        try
        {
            var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var img = el.TryGetProperty("img", out var i) ? i.GetString() ?? "" : "";
            var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url)) return null;
            return new YouTubeVideo
            {
                Url = url,
                Img = img,
                Title = title,
                VideoId = ExtractYouTubeId(url) ?? ""
            };
        }
        catch { return null; }
    }

    private static readonly Regex _ytIdRegex = new(
        @"^.*(youtu\.be\/|v\/|u\/\w\/|embed\/|watch\?v=|\&v=)([^#\&\?]*).*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ExtractYouTubeId(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var match = _ytIdRegex.Match(url);
        if (match.Success && match.Groups[2].Length == 11)
            return match.Groups[2].Value;
        return "";
    }

    /// <summary>
    /// Clean display name like everywhere else in the project:
    /// "Structured Programming" instead of "CSE014 - Structured Programming.(ALL)".
    /// Falls back to the old underscore replacement for unparseable keys.
    /// </summary>
    private static string SubjectDisplayName(string folder)
    {
        try
        {
            ArchiveCatalog.ParseSubjectFolder(folder, out var code, out var clean, out _);
            if (!string.IsNullOrWhiteSpace(clean) && !string.IsNullOrEmpty(code))
                return clean;
        }
        catch { }
        return (folder ?? "").Replace("_", " ");
    }

    private static string GenerateAvatarUrl(string channelKey)
    {
        if (channelKey.StartsWith("@"))
        {
            var cleanHandle = channelKey.Substring(1).Replace("-dot-", ".");
            return $"https://unavatar.io/youtube/{cleanHandle}?fallback=https://ui-avatars.com/api/?name={cleanHandle}&background=141414&color=00f2ff";
        }
        return "https://ui-avatars.com/api/?name=YT&background=141414&color=00f2ff";
    }
}
