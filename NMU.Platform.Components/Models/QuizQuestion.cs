using System.Text.Json.Serialization;

namespace NMU.Platform.Components.Models;

public class QuizQuestion
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    [JsonPropertyName("correct_answer")]
    public string CorrectAnswer { get; set; } = "";

    [JsonPropertyName("hint")]
    public string Hint { get; set; } = "";

    [JsonPropertyName("explanation_ar")]
    public string ExplanationAr { get; set; } = "";

    [JsonPropertyName("explanation_en")]
    public string ExplanationEn { get; set; } = "";

    [JsonPropertyName("codeSnippet")]
    public string CodeSnippet { get; set; } = "";

    [JsonPropertyName("codeLang")]
    public string CodeLang { get; set; } = "";

    [JsonPropertyName("graphType")]
    public string GraphType { get; set; } = "";

    [JsonPropertyName("graphFn")]
    public string GraphFn { get; set; } = "";

    [JsonPropertyName("graphData")]
    public string GraphData { get; set; } = "";

    [JsonIgnore]
    public string Explanation => !string.IsNullOrEmpty(ExplanationAr) ? ExplanationAr : ExplanationEn;
}
