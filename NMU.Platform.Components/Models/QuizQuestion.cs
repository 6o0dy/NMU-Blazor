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

    private List<QuizOptionItem>? _optionsCache;

    [JsonIgnore]
    public string Explanation => !string.IsNullOrEmpty(ExplanationAr) ? ExplanationAr : ExplanationEn;

    [JsonIgnore]
    public List<QuizOptionItem> Options
    {
        get
        {
            if (_optionsCache != null)
                return _optionsCache;

            _optionsCache = new List<QuizOptionItem>();
            if (OptionsRaw.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in OptionsRaw.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        var text = el.GetString() ?? "";
                        _optionsCache.Add(new QuizOptionItem { Text = text, RawJson = text });
                    }
                    else if (el.ValueKind == JsonValueKind.Object)
                    {
                        var text = el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        var gType = el.TryGetProperty("graphType", out var gt) ? gt.GetString() ?? "" : "";
                        var gFn = el.TryGetProperty("graphFn", out var gf) ? gf.GetString() ?? "" : "";
                        var gData = el.TryGetProperty("graphData", out var gd) ? gd.GetString() ?? "" : "";
                        _optionsCache.Add(new QuizOptionItem
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
                        var raw = el.GetRawText();
                        _optionsCache.Add(new QuizOptionItem { Text = raw, RawJson = raw });
                    }
                }
            }
            return _optionsCache;
        }
    }

    [JsonIgnore]
    public string CorrectAnswerSerialized
    {
        get
        {
            if (CorrectAnswerRaw.ValueKind == JsonValueKind.String)
            {
                var str = CorrectAnswerRaw.GetString() ?? "";
                if (!string.IsNullOrEmpty(str) && (str[0] == '{' || str[0] == '['))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(str);
                        return doc.RootElement.GetRawText();
                    }
                    catch { }
                }
                return str;
            }
            return CorrectAnswerRaw.GetRawText();
        }
    }

    public bool IsOptionCorrect(QuizOptionItem opt)
    {
        // Quick direct string match (for simple text options)
        if (opt.RawJson == CorrectAnswerSerialized)
            return true;

        if (CorrectAnswerRaw.ValueKind == JsonValueKind.Object)
        {
            // correct_answer is a native object — compare decoded field values
            return FieldsMatch(opt, CorrectAnswerRaw);
        }

        if (CorrectAnswerRaw.ValueKind == JsonValueKind.String)
        {
            var str = CorrectAnswerRaw.GetString() ?? "";
            if (string.IsNullOrEmpty(str))
                return false;

            // If it looks like a nested JSON object, parse and compare field by field
            if (str[0] == '{')
            {
                try
                {
                    using var doc = JsonDocument.Parse(str);
                    return FieldsMatch(opt, doc.RootElement);
                }
                catch { }
            }

            // Plain string option: compare directly
            return str == opt.RawJson || str == opt.Text;
        }

        return false;
    }

    private static bool FieldsMatch(QuizOptionItem opt, JsonElement correctEl)
    {
        // Compare graphType
        if (correctEl.TryGetProperty("graphType", out var gt))
        {
            var gtStr = gt.GetString() ?? "";
            if (!string.Equals(gtStr, opt.GraphType, StringComparison.Ordinal))
                return false;
        }
        else
        {
            if (!string.IsNullOrEmpty(opt.GraphType))
                return false;
        }

        // Compare graphFn
        if (correctEl.TryGetProperty("graphFn", out var gf))
        {
            var gfStr = gf.GetString() ?? "";
            if (!string.Equals(gfStr, opt.GraphFn, StringComparison.Ordinal))
                return false;
        }
        else
        {
            if (!string.IsNullOrEmpty(opt.GraphFn))
                return false;
        }

        // Compare graphData
        if (correctEl.TryGetProperty("graphData", out var gd))
        {
            var gdStr = gd.GetString() ?? "";
            if (!string.Equals(gdStr, opt.GraphData, StringComparison.Ordinal))
                return false;
        }
        else
        {
            if (!string.IsNullOrEmpty(opt.GraphData))
                return false;
        }

        // Compare text
        if (correctEl.TryGetProperty("text", out var t))
        {
            var tStr = t.GetString() ?? "";
            if (!string.Equals(tStr, opt.Text, StringComparison.Ordinal))
                return false;
        }
        else
        {
            if (!string.IsNullOrEmpty(opt.Text))
                return false;
        }

        return true;
    }

    public string? GetCorrectOptionText()
    {
        var correctOpt = Options.FirstOrDefault(o => IsOptionCorrect(o));
        return correctOpt?.Text;
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
