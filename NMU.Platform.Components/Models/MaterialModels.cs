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

public class RecordedFile
{
    public string Name { get; set; } = "";
    public long? Size { get; set; }
    public string? ThumbName { get; set; }
    public string DisplayName { get; set; } = "";
    public string SubFolder { get; set; } = "";
    public bool IsAudio { get; set; }
}
