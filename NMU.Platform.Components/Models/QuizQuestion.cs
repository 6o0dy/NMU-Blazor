using System.Text.Json;
using System.Text.Json.Serialization;

namespace NMU.Platform.Components.Models;

public class QuizQuestion
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("question")]
    public string Question { get; set; } = "";

    [JsonPropertyName("options")]
    public JsonElement OptionsRaw { get; set; }

    [JsonPropertyName("correct_answer")]
    public JsonElement CorrectAnswerRaw { get; set; }

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
    [JsonConverter(typeof(RawJsonStringConverter))]
    public string GraphData { get; set; } = "";

    [JsonIgnore]
    public string Explanation => !string.IsNullOrEmpty(ExplanationAr) ? ExplanationAr : ExplanationEn;

    [JsonIgnore]
    public List<QuizOptionItem> Options
    {
        get
        {
            var result = new List<QuizOptionItem>();
            if (OptionsRaw.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in OptionsRaw.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        result.Add(new QuizOptionItem { Text = el.GetString() ?? "", RawJson = el.GetString() ?? "" });
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        var gType = el.TryGetProperty("graphType", out var gt) ? gt.GetString() ?? "" : "";
                        var gFn = el.TryGetProperty("graphFn", out var gf) ? gf.GetString() ?? "" : "";
                        var gData = el.TryGetProperty("graphData", out var gd) ? gd.GetRawText() : "";
                        result.Add(new QuizOptionItem
                        {
                            Text = text,
                            RawJson = el.GetRawText(),
                            GraphType = gType,
                            GraphFn = gFn,
                            GraphData = gData,
                            IsObject = true
                        });
                    }
                    else
                    {
                        result.Add(new QuizOptionItem { Text = el.GetRawText(), RawJson = el.GetRawText() });
                    }
                }
            }
            return result;
        }
    }

    [JsonIgnore]
    public string CorrectAnswerSerialized
    {
        get
        {
            if (CorrectAnswerRaw.ValueKind == JsonValueKind.String)
                return CorrectAnswerRaw.GetString() ?? "";
            return CorrectAnswerRaw.GetRawText();
        }
    }
}

public class QuizOptionItem
{
    public string Text { get; set; } = "";
    public string RawJson { get; set; } = "";
    public string GraphType { get; set; } = "";
    public string GraphFn { get; set; } = "";
    public string GraphData { get; set; } = "";
    public bool IsObject { get; set; }
}

public class RawJsonStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return reader.GetString() ?? "";
        if (reader.TokenType == JsonTokenType.Null)
            return "";
        using var doc = JsonDocument.ParseValue(ref reader);
        return doc.RootElement.GetRawText();
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
