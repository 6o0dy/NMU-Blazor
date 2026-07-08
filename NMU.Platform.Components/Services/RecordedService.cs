using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class RecordedService
{
    private readonly IJSRuntime _js;
    private const string ArchiveId = "nmu.ce";
    private const string BaseFolder = "NMU";
    private const string CacheVersion = "v1_recorded_";

    public RecordedService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<List<RecordedFile>> GetFilesAsync(string level, string semester)
    {
        var cacheKey = $"{CacheVersion}{level}_{semester}";
        var cached = await _js.InvokeAsync<string>("localStorage.getItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<RecordedFile>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }

        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", $"https://archive.org/metadata/{ArchiveId}");
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);
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

            if (files.Count > 0)
                await _js.InvokeVoidAsync("localStorage.setItem", cacheKey, JsonSerializer.Serialize(files));

            return files;
        }
        catch
        {
            return new List<RecordedFile>();
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
