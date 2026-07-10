using System.Text.Json.Serialization;

namespace NMU.Platform.Components.Models;

public class QuizChapter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("questions")]
    public List<QuizQuestion> Questions { get; set; } = new();
}
