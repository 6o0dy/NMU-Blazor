using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class MaterialsService
{
    private readonly IJSRuntime _js;
    private const string ArchiveId = "nmu.ce";
    private const string BaseFolder = "NMU";
    private const string CacheVersion = "v68_restore_original_logic_";

    public MaterialsService(IJSRuntime js)
    {
        _js = js;
    }

    public async Task<List<ArchiveFile>> GetFilesAsync(string level, string semester)
    {
        var cacheKey = $"{CacheVersion}{level}_{semester}";
        var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<ArchiveFile>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }

        try
        {
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", $"https://archive.org/metadata/{ArchiveId}");
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);
            var targetPrefix = $"{BaseFolder}/{level}/{semester}/";
            var files = data?.Files?
                .Where(f => f.Name.StartsWith(targetPrefix))
                .Select(f => new ArchiveFile
                {
                    Name = f.Name,
                    Size = long.TryParse(f.Size, out var s) ? s : null
                })
                .ToList() ?? new List<ArchiveFile>();

            if (files.Count > 0)
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(files));

            return files;
        }
        catch
        {
            return new List<ArchiveFile>();
        }
    }

    public static List<string> GetSubjects(List<ArchiveFile> files, string level, string semester)
    {
        var prefix = $"{BaseFolder}/{level}/{semester}/PDF/";
        var subjects = new HashSet<string>();
        foreach (var f in files)
        {
            if (f.Name.StartsWith(prefix))
            {
                var rel = f.Name[prefix.Length..];
                var parts = rel.Split('/');
                if (parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
                    subjects.Add(parts[0]);
            }
        }
        return subjects.OrderBy(s => s).ToList();
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
