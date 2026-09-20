using System.Text.RegularExpressions;

namespace NMU.Platform.Components.Services;

/// <summary>
/// Central mapping for the per-semester archive.org repositories.
///
/// Level_1 + Semester_1 -&gt; NMU.CE_1.1
/// Level_1 + Semester_2 -&gt; NMU.CE_1.2
/// ...
/// Level_5 + Semester_2 -&gt; NMU.CE_5.2
///
/// New repository layout (inside each archive):
///   Data/{SubjectFolder}/PDFs/{Lecturer}/{LEC|LAB|TUT|OTHER|...}/*.pdf
///   Data/{SubjectFolder}/Quizzes/{Lecturer}/*-quize.json
///   Data/{SubjectFolder}/Records/{Lecturer}/*.mp4|mp3|m4a...
///   Thumbs: {ArchiveId}.thumbs/Data/... (*.jpg)
///
/// SubjectFolder format: "{CODE} - {CleanName}.({BRANCH})"
/// e.g. "CSE014 - Structured Programming.(ALL)" -&gt; Code=CSE014, Clean=Structured Programming, Branch=ALL
/// Branch is the department tag: ALL (both), CE, AIE.
/// </summary>
public static class ArchiveCatalog
{
    public static readonly IReadOnlyList<string> AllArchiveIds = new[]
    {
        "NMU.CE_1.1", "NMU.CE_1.2",
        "NMU.CE_2.1", "NMU.CE_2.2",
        "NMU.CE_3.1", "NMU.CE_3.2",
        "NMU.CE_4.1", "NMU.CE_4.2",
        "NMU.CE_5.1", "NMU.CE_5.2",
    };

    private static readonly Regex BranchRegex = new(@"\.\(([^)]+)\)\s*$", RegexOptions.Compiled);

    /// <summary>Normalizes "Level 1" / "Level_1" to "Level_N" (N=1..5). Returns null if invalid.</summary>
    public static string? NormalizeLevel(string? level)
    {
        if (string.IsNullOrWhiteSpace(level)) return null;
        var s = level.Trim().Replace(" ", "_");
        var m = Regex.Match(s, @"^Level_(\d+)$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        if (!int.TryParse(m.Groups[1].Value, out var n) || n < 1 || n > 5) return null;
        return $"Level_{n}";
    }

    /// <summary>
    /// Normalizes "Semester 1" / "Semester_1" / "First_Term" / "Term_1" to "Semester_N" (N=1..2).
    /// Returns null if invalid.
    /// </summary>
    public static string? NormalizeSemester(string? semester)
    {
        if (string.IsNullOrWhiteSpace(semester)) return null;
        var s = semester.Trim().Replace(" ", "_").ToLowerInvariant();
        if (s is "semester_1" or "first_term" or "term_1" or "semester1" or "term1") return "Semester_1";
        if (s is "semester_2" or "second_term" or "term_2" or "semester2" or "term2") return "Semester_2";
        var m = Regex.Match(s, @"^semester_(\d+)$");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && (n == 1 || n == 2))
            return $"Semester_{n}";
        return null;
    }

    /// <summary>Returns e.g. "NMU.CE_1.1" for (Level_1, Semester_1). Null when inputs invalid.</summary>
    public static string? GetArchiveId(string? level, string? semester)
    {
        var lvl = NormalizeLevel(level);
        var sem = NormalizeSemester(semester);
        if (lvl == null || sem == null) return null;
        var levelNum = lvl.Split('_')[1];
        var semNum = sem.Split('_')[1];
        return $"NMU.CE_{levelNum}.{semNum}";
    }

    /// <summary>Reverse mapping: "NMU.CE_2.1" -&gt; (Level_2, Semester_1). Null when invalid.</summary>
    public static (string Level, string Semester)? GetLevelSemester(string? archiveId)
    {
        if (string.IsNullOrWhiteSpace(archiveId)) return null;
        var m = Regex.Match(archiveId.Trim(), @"^NMU\.CE_([1-5])\.([12])$", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        return ($"Level_{m.Groups[1].Value}", $"Semester_{m.Groups[2].Value}");
    }

    public static string GetMetadataUrl(string archiveId)
        => $"https://archive.org/metadata/{archiveId}";

    /// <summary>
    /// Builds a download URL with each path segment percent-encoded.
    /// Raw archive names contain spaces, '&amp;', '#', '+' and non-ASCII text;
    /// a raw '&amp;' would truncate the path as a query separator and archive.org
    /// answers such malformed paths with a CORS-less 302 (blocked download).
    /// Callers must always pass the RAW metadata path (never pre-encoded).
    /// </summary>
    public static string GetDownloadUrl(string archiveId, string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return $"https://archive.org/download/{archiveId}/";
        var encoded = string.Join("/", filePath.Split('/').Select(Uri.EscapeDataString));
        return $"https://archive.org/download/{archiveId}/{encoded}";
    }

    public static string GetThumbsPrefix(string archiveId)
        => $"{archiveId}.thumbs/";

    /// <summary>
    /// Maximum age of a successful background refresh before the archive is
    /// re-fetched. archive.org's search index does not list the NMU.CE_*
    /// identifiers (advancedsearch always returns zero docs), so freshness is
    /// decided by age instead of by a remote size signal.
    /// </summary>
    public static readonly TimeSpan RevalidateAfter = TimeSpan.FromMinutes(15);

    /// <summary>Normalizes department: "ce"/"CE" -&gt; "CE", "aie" -&gt; "AIE", empty/ALL -&gt; "ALL".</summary>
    public static string NormalizeDepartment(string? dept)
    {
        if (string.IsNullOrWhiteSpace(dept)) return "ALL";
        var s = dept.Trim().ToUpperInvariant();
        return s switch
        {
            "CE" => "CE",
            "AIE" => "AIE",
            "ALL" => "ALL",
            "GENERAL" => "ALL",
            _ => "ALL",
        };
    }

    /// <summary>
    /// Parses "CSE014 - Structured Programming.(ALL)" into code/clean/branch.
    /// Falls back gracefully when the folder does not follow the convention.
    /// </summary>
    public static void ParseSubjectFolder(string subjectFolder, out string code, out string cleanName, out string branch)
    {
        code = "";
        cleanName = (subjectFolder ?? "").Trim();
        branch = "ALL";

        if (string.IsNullOrWhiteSpace(subjectFolder))
        {
            cleanName = "";
            return;
        }

        var rest = subjectFolder.Trim();

        var bm = BranchRegex.Match(rest);
        if (bm.Success)
        {
            var b = bm.Groups[1].Value.Trim().ToUpperInvariant();
            if (!string.IsNullOrEmpty(b))
                branch = b;
            rest = rest.Substring(0, bm.Index).Trim();
        }

        var sep = rest.IndexOf(" - ", StringComparison.Ordinal);
        if (sep > 0)
        {
            var left = rest.Substring(0, sep).Trim();
            var right = rest.Substring(sep + 3).Trim();
            if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
            {
                code = left;
                cleanName = right;
                return;
            }
        }

        cleanName = rest;
    }

    /// <summary>
    /// Short "CODE - Clean name" label without the branch tag, e.g.
    /// "CSE014 - Structured Programming.(ALL)" → "CSE014 - Structured Programming".
    /// Used anywhere a selected subject folder is shown to the user.
    /// </summary>
    public static string ShortSubjectLabel(string? subjectFolder)
    {
        var (code, name) = SplitSubjectLabel(subjectFolder);
        if (!string.IsNullOrEmpty(code) && !string.IsNullOrEmpty(name))
            return $"{code} - {name}";
        return name;
    }

    /// <summary>
    /// Split a subject folder into (Code, CleanName) without the branch tag,
    /// e.g. ("CSE014", "Structured Programming"). Name falls back to the
    /// raw folder text when it has no code.
    /// </summary>
    public static (string Code, string Name) SplitSubjectLabel(string? subjectFolder)
    {
        if (string.IsNullOrWhiteSpace(subjectFolder)) return ("", "");
        ParseSubjectFolder(subjectFolder, out var code, out var clean, out _);
        clean = (clean ?? "").Replace("_", " ").Trim();
        if (string.IsNullOrEmpty(clean))
            clean = subjectFolder.Replace("_", " ").Trim();
        return (code?.Trim() ?? "", clean);
    }

    /// <summary>True when a subject with the given branch tag is visible for the student's department.</summary>
    public static bool IsVisibleForDepartment(string? branch, string? studentDept)
    {
        var b = (branch ?? "ALL").Trim().ToUpperInvariant();
        var d = NormalizeDepartment(studentDept);
        if (string.IsNullOrEmpty(b) || b == "ALL") return true;
        if (d == "ALL") return true;
        return b == d;
    }

    /// <summary>
    /// True when the subject is Physics 2 (second physics course), e.g.
    /// "Physics II", "Physics 2", "الفيزياء 2". Physics 1 courses such as
    /// "PHY212 - Introduction to Engineering Physics" return false: the "2"
    /// must appear as a level indicator in the clean name, never in the code.
    /// </summary>
    public static bool IsPhysics2Subject(string? subjectFolder)
    {
        if (string.IsNullOrWhiteSpace(subjectFolder)) return false;
        ParseSubjectFolder(subjectFolder, out _, out var clean, out _);
        var name = string.IsNullOrEmpty(clean) ? subjectFolder : clean;
        var lower = name.ToLowerInvariant();
        var hasPhys = lower.Contains("physic") || lower.Contains("phys") || name.Contains("فيز");
        if (!hasPhys) return false;
        var tokens = Regex.Split(lower, @"[^a-z0-9\u0600-\u06FF]+")
            .Where(t => t.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        if (tokens.Contains("2") || tokens.Contains("ii") || tokens.Contains("second"))
            return true;
        if (name.Contains('٢') || lower.Contains("ثاني") || lower.Contains("التاني"))
            return true;
        return false;
    }

    /// <summary>Returns true for archive.org derivative sidecar files that must be ignored.</summary>
    public static bool IsDerivativeFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        if (name.EndsWith("_djvu.txt", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_djvu.xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("_chocr.html", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("_hocr.html", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("_hocr_pageindex", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.Contains("_hocr_searchtext", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_jp2.zip", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_scandata.xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_page_numbers.json", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith(".ia.mp4", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_meta.xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_files.xml", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("_meta.sqlite", StringComparison.OrdinalIgnoreCase)) return true;
        if (name.EndsWith("__ia_thumb.jpg", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>True for playable video files (video/audio), excluding derivatives and sidecars.</summary>
    public static bool IsVideoMedia(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        if (lower.EndsWith(".ia.mp4")) return false;
        if (lower.EndsWith(".png") || lower.EndsWith(".jpg") || lower.EndsWith(".jpeg")) return false;
        if (lower.EndsWith(".afpk") || lower.EndsWith("_spectrogram.png")) return false;
        if (lower.EndsWith("order_config.json")) return false;
        return lower.EndsWith(".mp4") || lower.EndsWith(".mkv") || lower.EndsWith(".webm")
            || lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".m4a");
    }

    public static bool IsAudioFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var lower = name.ToLowerInvariant();
        return lower.EndsWith(".mp3") || lower.EndsWith(".wav") || lower.EndsWith(".m4a");
    }
}
