using System.Text.Json.Serialization;

namespace NMU.Platform.Components.Models;

public class ArchiveMetadata
{
    [JsonPropertyName("files")]
    public List<ArchiveFileEntry>? Files { get; set; }
}

public class ArchiveFileEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("size")]
    public string? Size { get; set; }
}

public class ArchiveFile
{
    public string Name { get; set; } = "";
    public long? Size { get; set; }
}

public class MaterialFile
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Folder { get; set; } = "";
    public long? Size { get; set; }
    /// <summary>Lecturer folder under Data/{Subject}/PDFs/{Lecturer}/... (new archive layout).</summary>
    public string Lecturer { get; set; } = "";
}

public class MaterialSubjectInfo
{
    /// <summary>Full archive subject folder, e.g. "CSE014 - Structured Programming.(ALL)". Used for routing/lookups.</summary>
    public string Name { get; set; } = "";
    public int FileCount { get; set; }
    /// <summary>Subject code, e.g. "CSE014".</summary>
    public string Code { get; set; } = "";
    /// <summary>Clean display name without code/branch, e.g. "Structured Programming".</summary>
    public string DisplayName { get; set; } = "";
    /// <summary>Department branch tag: ALL / CE / AIE.</summary>
    public string Branch { get; set; } = "ALL";
}

/// <summary>
/// One individual subject pinned to the level + semester where its content lives.
/// Used by the "Custom subjects" (credit-hours) mode to show only the subjects a
/// student is actually registered in, from any level/semester.
/// </summary>
public class CustomSubjectSelection
{
    public string Level { get; set; } = "";     // archive folder form, e.g. "Level_1"
    public string Semester { get; set; } = "";  // archive folder form, e.g. "Semester_1"
    public string Subject { get; set; } = "";   // subject folder name, e.g. "Mathematics"

    public static string Key(string level, string semester, string subject)
        => $"{level}|{semester}|{subject}";

    public bool Matches(string level, string semester, string subject)
        => string.Equals(Level, level, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Semester, semester, StringComparison.OrdinalIgnoreCase)
           && string.Equals(Subject, subject, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// A subject available in the archive (built from the PDF folder list), used to
/// populate the subject picker in custom mode across all levels and semesters.
/// Subject is the FULL archive folder (e.g. "CSE014 - Structured Programming.(ALL)");
/// Code/DisplayName/Branch are parsed helpers for display + search + dept filter.
/// </summary>
public class SubjectCatalogEntry
{
    public string Level { get; set; } = "";
    public string Semester { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Branch { get; set; } = "ALL";

    public string LevelFolder => Level.Replace(" ", "_");
    public string SemesterFolder => Semester.Replace(" ", "_");
}

/// <summary>
/// Matching helpers for subject names between content sources (PDF folders vs
/// QUIZE json names vs video group folders), which are not always identical.
/// </summary>
public static class SubjectMatcher
{
    public static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True if the two subject names refer to the same subject, tolerating small
    /// naming differences like "Mathematics" vs "Mathematics I", "Object-Oriented
    /// Programming" vs "Object Oriented Programming (OOP)".
    /// </summary>
    public static bool Matches(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        var na = Normalize(a);
        var nb = Normalize(b);
        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;

        // One is a prefix of the other AND the longer one is only slightly longer
        // (handles "Mathematics" vs "Mathematics I", "Physics" vs "Physics II").
        string shorter, longer;
        if (na.Length <= nb.Length) { shorter = na; longer = nb; }
        else { shorter = nb; longer = na; }
        if (shorter.Length >= 3 && longer.StartsWith(shorter, StringComparison.Ordinal))
            return (longer.Length - shorter.Length) <= 12;

        // Token overlap: "Object-Oriented Programming" vs "Object Oriented Programming (OOP)".
        var tokensA = Tokenize(a);
        var tokensB = Tokenize(b);
        if (tokensA.Count == 0 || tokensB.Count == 0) return false;
        var small = tokensA.Count <= tokensB.Count ? tokensA : tokensB;
        var large = tokensA.Count <= tokensB.Count ? tokensB : tokensA;
        if (small.Count == 0) return false;
        return small.All(large.Contains);
    }

    private static HashSet<string> Tokenize(string s)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in s.Split(new[] { ' ', '_', '-', '&', '(', ')', ',', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = part.Trim();
            if (t.Length >= 2 && t.Any(char.IsLetter)) set.Add(t);
        }
        return set;
    }
}

public class OrderConfig
{
    [JsonPropertyName("order")]
    public List<string>? Order { get; set; }
}

public class ArchiveDirectMetadata
{
    [JsonPropertyName("d1")]
    public string? D1 { get; set; }

    [JsonPropertyName("workable_servers")]
    public List<string>? WorkableServers { get; set; }

    [JsonPropertyName("dir")]
    public string? Dir { get; set; }
}

public class Review
{
    [JsonPropertyName("serial")]
    public string Serial { get; set; } = "";
    [JsonPropertyName("review")]
    public string ReviewRating { get; set; } = "";
    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("isVerified")]
    public bool IsVerified { get; set; }
    [JsonPropertyName("level")]
    public string Level { get; set; } = "";
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}

public class YouTubeChannel
{
    public string ChannelName { get; set; } = "";
    public string Subject { get; set; } = "";
    public string AvatarUrl { get; set; } = "";
    public string GroupKey { get; set; } = "";
    /// <summary>Exact archive folder, e.g. "CSE014 - Structured Programming.(ALL)". Used to match custom subject selections.</summary>
    public string? SubjectFolder { get; set; }
    public List<YouTubeVideo> Videos { get; set; } = new();
}

public class YouTubeVideo
{
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string Img { get; set; } = "";
    public string VideoId { get; set; } = "";
}

public class VideoFile
{
    public string Name { get; set; } = "";
    public long? Size { get; set; }
    public string? ThumbName { get; set; }
    public string DisplayName { get; set; } = "";
    public string SubFolder { get; set; } = "";
    public bool IsAudio { get; set; }
    /// <summary>Lecturer folder under Data/{Subject}/Records/{Lecturer}/... (new layout).</summary>
    public string Lecturer { get; set; } = "";
    /// <summary>Full subject folder, e.g. "CSE014 - Structured Programming.(ALL)".</summary>
    public string SubjectFullName { get; set; } = "";
    /// <summary>Archive id that owns this file, e.g. "NMU.CE_1.1".</summary>
    public string ArchiveId { get; set; } = "";
}

public class VideoGroupInfo
{
    /// <summary>Full subject folder (routing key).</summary>
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Branch { get; set; } = "ALL";
}
