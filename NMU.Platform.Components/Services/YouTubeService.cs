using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class YouTubeService
{
    private readonly IJSRuntime _js;
    private readonly HttpClient _http;
    private readonly StudentService _studentService;

    private const string FirebaseUrl = "https://nmu-ce-default-rtdb.firebaseio.com";
    private const string CacheVersion = "v1_yt_";

    private List<YouTubeChannel> _channels = new();

    public YouTubeService(IJSRuntime js, HttpClient http, StudentService studentService)
    {
        _js = js;
        _http = http;
        _studentService = studentService;
    }

    public List<YouTubeChannel> Channels => _channels;

    public async Task<List<YouTubeChannel>> GetChannelsAsync()
    {
        if (_channels.Count > 0) return _channels;

        var student = await _studentService.GetStudentAsync();
        var studentLevel = student?.AcademicLevel?.Replace(" ", "_") ?? "Level_1";
        var studentSemester = student?.Semester?.Replace(" ", "_") ?? "First_Term";

        var cacheKey = $"{CacheVersion}{studentLevel}_{studentSemester}";
        var cached = await _js.InvokeAsync<string>("nmuFunctions.safeGetItem", cacheKey);

        if (!string.IsNullOrEmpty(cached))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<YouTubeChannel>>(cached);
                if (parsed != null && parsed.Count > 0)
                {
                    _channels = parsed;
                    return _channels;
                }
            }
            catch { }
        }

        await FetchFromFirebase(studentLevel, studentSemester, cacheKey);
        return _channels;
    }

    private async Task FetchFromFirebase(string studentLevel, string studentSemester, string cacheKey)
    {
        try
        {
            var url = $"{FirebaseUrl}/NMU/{studentLevel}/{studentSemester}/Channels.json";
            var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync();
            if (json == "null" || json == "{}") return;

            _channels = ParseChannelsFromJson(json);

            if (_channels.Count > 0)
                await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", cacheKey, JsonSerializer.Serialize(_channels));
        }
        catch { }
    }

    private static List<YouTubeChannel> ParseChannelsFromJson(string json)
    {
        var result = new List<YouTubeChannel>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var subjectProp in doc.RootElement.EnumerateObject())
            {
                var subjectKey = subjectProp.Name;
                var subjectDisplay = subjectKey.Replace("_", " ");

                foreach (var channelProp in subjectProp.Value.EnumerateObject())
                {
                    var channelKey = channelProp.Name;
                    var channelObj = channelProp.Value;
                    var channelName = "";
                    var videos = new List<YouTubeVideo>();

                    foreach (var prop in channelObj.EnumerateObject())
                    {
                        if (prop.Name == "channelName")
                        {
                            channelName = prop.Value.GetString() ?? channelKey;
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            var video = ParseVideo(prop.Value);
                            if (video != null)
                                videos.Add(video);
                        }
                    }

                    if (string.IsNullOrEmpty(channelName))
                        channelName = channelKey;

                    videos.Reverse();

                    var avatarUrl = GenerateAvatarUrl(channelKey);

                    result.Add(new YouTubeChannel
                    {
                        ChannelName = channelName,
                        Subject = subjectDisplay,
                        AvatarUrl = avatarUrl,
                        GroupKey = $"{subjectKey}||{channelKey}",
                        Videos = videos
                    });
                }
            }
        }
        catch { }
        return result;
    }

    private static YouTubeVideo? ParseVideo(JsonElement el)
    {
        try
        {
            var url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var img = el.TryGetProperty("img", out var i) ? i.GetString() ?? "" : "";
            var title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url)) return null;
            return new YouTubeVideo
            {
                Url = url,
                Img = img,
                Title = title,
                VideoId = ExtractYouTubeId(url) ?? ""
            };
        }
        catch { return null; }
    }

    private static readonly Regex _ytIdRegex = new(
        @"^.*(youtu\.be\/|v\/|u\/\w\/|embed\/|watch\?v=|\&v=)([^#\&\?]*).*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static string ExtractYouTubeId(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";
        var match = _ytIdRegex.Match(url);
        if (match.Success && match.Groups[2].Length == 11)
            return match.Groups[2].Value;
        return "";
    }

    private static string GenerateAvatarUrl(string channelKey)
    {
        if (channelKey.StartsWith("@"))
        {
            var cleanHandle = channelKey.Substring(1).Replace("-dot-", ".");
            return $"https://unavatar.io/youtube/{cleanHandle}?fallback=https://ui-avatars.com/api/?name={cleanHandle}&background=141414&color=00f2ff";
        }
        return "https://ui-avatars.com/api/?name=YT&background=141414&color=00f2ff";
    }
}
