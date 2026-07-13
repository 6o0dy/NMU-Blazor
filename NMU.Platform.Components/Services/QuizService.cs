using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;
using Microsoft.Extensions.Logging;

namespace NMU.Platform.Components.Services;

public class QuizService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly ILogger<QuizService> _logger;
    private const string ArchiveId = "nmu.ce";

    public QuizService(IJSRuntime js, HttpClient http, ILogger<QuizService> logger)
    {
        _js = js;
        _http = http;
        _logger = logger;
    }

    private static string MapSemester(string sem)
    {
        var s = sem.Replace(" ", "_").ToLower();
        if (s is "semester_1" or "first_term" or "term_1") return "Semester_1";
        if (s is "semester_2" or "second_term" or "term_2") return "Semester_2";
        return sem.Replace(" ", "_");
    }

    public async Task<List<QuizSubject>> GetQuizListAsync(string level, string semester)
    {
        semester = MapSemester(semester);
        var cacheKey = $"nmu_quiz_list_{level}_{semester}_v4";
        try
        {
            var cached = await _js.InvokeAsync<string?>("localStorage.getItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizSubject>>(cached);
                if (parsed != null && parsed.Count > 0)
                    return parsed;
            }
        }
        catch { }

        try
        {
            var quizPath = $"NMU/{level}/{semester}/QUIZE/";
            var metaUrl = $"https://archive.org/metadata/{ArchiveId}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _http.GetStringAsync(metaUrl);
            var data = JsonSerializer.Deserialize<ArchiveMetadata>(json);

            if (data?.Files == null)
                return new List<QuizSubject>();

            var matchedFiles = data.Files
                .Where(f => f.Name.StartsWith(quizPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                .ToList();

            if (matchedFiles.Count == 0)
            {
                var altSemester = semester == "Semester_1" ? "Semester_2" : "Semester_1";
                var altPath = $"NMU/{level}/{altSemester}/QUIZE/";
                matchedFiles = data.Files
                    .Where(f => f.Name.StartsWith(altPath) && f.Name.EndsWith(".json") && !f.Name.EndsWith("order_config.json"))
                    .ToList();
                if (matchedFiles.Count > 0)
                    quizPath = altPath;
            }

            var orderList = new List<string>();
            try
            {
                var orderUrl = $"https://archive.org/download/{ArchiveId}/{quizPath}order_config.json?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                var orderJson = await _http.GetStringAsync(orderUrl);
                var orderConfig = JsonSerializer.Deserialize<OrderConfig>(orderJson);
                if (orderConfig?.Order != null)
                    orderList = orderConfig.Order;
            }
            catch { }

            var files = matchedFiles
                .Select(f =>
                {
                    var rel = f.Name.Substring(quizPath.Length);
                    var name = rel.Split('/')[0].Replace(".json", "").Replace("_", " ");
                    return new QuizSubject { Name = name, Path = f.Name, Rel = rel };
                })
                .ToList();

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

        try
        {
            var cached = await _js.InvokeAsync<string?>("localStorage.getItem", cacheKey);
            if (!string.IsNullOrEmpty(cached))
            {
                var parsed = JsonSerializer.Deserialize<List<QuizChapter>>(cached);
                if (parsed != null && parsed.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var url = $"https://archive.org/download/{ArchiveId}/{filePath}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                            var latestJson = await _http.GetStringAsync(url);
                            if (!string.IsNullOrEmpty(latestJson))
                            {
                                await _js.InvokeVoidAsync("localStorage.setItem", cacheKey, latestJson);
                            }
                        }
                        catch { }
                    });
                    return parsed;
                }
            }
        }
        catch { }

        try
        {
            _logger.LogInformation("QuizService: fetching path: {FilePath}", filePath);
            var url = $"https://archive.org/download/{ArchiveId}/{filePath}?t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var json = await _http.GetStringAsync(url);
            _logger.LogInformation("QuizService: got json length: {Length}", json?.Length ?? 0);

            if (string.IsNullOrEmpty(json))
            {
                _logger.LogWarning("QuizService: empty json response");
                return new List<QuizChapter>();
            }

            await _js.InvokeVoidAsync("localStorage.setItem", cacheKey, json);

            var data = JsonSerializer.Deserialize<List<QuizChapter>>(json);
            if (data == null)
                _logger.LogWarning("QuizService: deserialized null");
            return data ?? new List<QuizChapter>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "QuizService error: {Message}", ex.Message);
            return new List<QuizChapter>();
        }
    }

    public static string GetDownloadUrl(string filePath)
    {
        return $"https://archive.org/download/{ArchiveId}/{filePath}";
    }
}