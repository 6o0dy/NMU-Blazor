using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class QuizService
{
    private readonly IJSRuntime _js;
    private const string ArchiveId = "nmu.ce";

    public QuizService(IJSRuntime js)
    {
        _js = js;
    }

    private static string MapSemester(string sem)
    {
        var s = sem.Replace(" ", "_").ToLower();
        if (s is "semester_1" or "first_term" or "term_1") return "First_Term";
        if (s is "semester_2" or "second_term" or "term_2") return "Second_Term";
        return sem.Replace(" ", "_");
    }

    public async Task<List<QuizSubject>> GetQuizListAsync(string level, string semester)
    {
        semester = MapSemester(semester);
        var cacheKey = $"nmu_quiz_list_{level}_{semester}_v4";
        var cached = await _js.InvokeAsync<string>("localStorage.getItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<QuizSubject>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }

        try
        {
            var quizPath = $"NMU/{level}/{semester}/QUIZE/";
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", $"https://archive.org/metadata/{ArchiveId}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);

            var orderList = new List<string>();
            try
            {
                var orderJson = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", $"https://archive.org/download/{ArchiveId}/{quizPath}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
                var orderConfig = JsonSerializer.Deserialize<OrderConfig>(orderJson);
                if (orderConfig?.Order != null)
                    orderList = orderConfig.Order;
            }
            catch { }

            var files = data?.Files?
                .Where(f => f.Name.StartsWith(quizPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                .Select(f =>
                {
                    var rel = f.Name.Substring(quizPath.Length);
                    var name = rel.Split('/')[0].Replace(".json", "").Replace("_", " ");
                    return new QuizSubject { Name = name, Path = f.Name, Rel = rel };
                })
                .ToList() ?? new List<QuizSubject>();

            if (orderList.Count > 0)
            {
                files = files.OrderBy(f =>
                {
                    var idx = orderList.FindIndex(k => f.Rel.Contains(k));
                    return idx == -1 ? 999 : idx;
                }).ThenBy(f => f.Name).ToList();
            }
            else
            {
                files = files.OrderBy(f => f.Name).ToList();
            }

            if (files.Count > 0)
                await _js.InvokeVoidAsync("localStorage.setItem", cacheKey, JsonSerializer.Serialize(files));

            return files;
        }
        catch
        {
            return new List<QuizSubject>();
        }
    }

    public async Task<List<QuizChapter>> GetQuizDataAsync(string filePath)
    {
        var cacheKey = $"nmu_q_content_{filePath}";
        var cached = await _js.InvokeAsync<string>("localStorage.getItem", cacheKey);
        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<QuizChapter>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }

        try
        {
            var url = $"https://archive.org/download/{ArchiveId}/{filePath}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _js.InvokeAsync<string>("nmuFunctions.fetchJson", url);
            var data = JsonSerializer.Deserialize<List<QuizChapter>>(json);

            if (data != null && data.Count > 0)
                await _js.InvokeVoidAsync("localStorage.setItem", cacheKey, JsonSerializer.Serialize(data));

            return data ?? new List<QuizChapter>();
        }
        catch
        {
            return new List<QuizChapter>();
        }
    }

    public static string GetDownloadUrl(string filePath)
    {
        return $"https://archive.org/download/{ArchiveId}/{filePath}";
    }
}
