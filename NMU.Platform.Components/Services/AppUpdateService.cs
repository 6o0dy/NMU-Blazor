using System.Text.Json;
using Microsoft.JSInterop;

namespace NMU.Platform.Components.Services;

/// <summary>
/// One platform entry of the admin-controlled update config
/// (Firebase Realtime DB node "AppUpdate").
/// </summary>
public class AppUpdateEntry
{
    /// <summary>Latest published version, e.g. "1.1.0".</summary>
    public string Version { get; set; } = "";
    /// <summary>True = user cannot dismiss, must update.</summary>
    public bool Force { get; set; }
    /// <summary>Download URL of the new package (native only).</summary>
    public string Url { get; set; } = "";
    /// <summary>Download URL of the arm64-only package (android, lighter).</summary>
    public string UrlArm64 { get; set; } = "";
    /// <summary>Release notes shown in the dialog (admin free text).</summary>
    public string Notes { get; set; } = "";
}

/// <summary>
/// Result of an update check that should be surfaced to the user.
/// </summary>
public class AppUpdateInfo
{
    public string PlatformKey { get; set; } = "";
    public string Current { get; set; } = "";
    public AppUpdateEntry Entry { get; set; } = new();
}

/// <summary>
/// Admin-controlled in-app update checks. The admin publishes
/// {platform: {version, force, url, notes}} under the "AppUpdate" node;
/// each client compares its own version and shows a dialog (native) or a
/// banner/dialog (web) when a newer build exists.
/// </summary>
public class AppUpdateService
{
    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private readonly IPlatformService _platform;

    private const string FirebaseUrl = "https://nmu-ce-default-rtdb.firebaseio.com";
    private const string WebVersionKey = "nmu_web_version";

    private static string SkipKey(string platformKey) => $"nmu_skipped_update_{platformKey}";

    private static readonly JsonSerializerOptions CaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AppUpdateService(HttpClient http, IJSRuntime js, IPlatformService platform)
    {
        _http = http;
        _js = js;
        _platform = platform;
    }

    /// <summary>
    /// Checks for a published update. Returns null when up to date, when the
    /// admin published nothing for this platform, when the user skipped this
    /// exact version (optional updates only), or on any failure (silent).
    /// </summary>
    public async Task<AppUpdateInfo?> CheckForUpdateAsync()
    {
        try
        {
            var platformKey = _platform.PlatformKey;
            var entry = await FetchEntryAsync(platformKey);
            if (entry == null || string.IsNullOrWhiteSpace(entry.Version))
                return null;

            var latest = entry.Version.Trim();
            string current;
            if (_platform.IsWeb)
            {
                var stored = await ReadStoredAsync(WebVersionKey);
                if (string.IsNullOrEmpty(stored))
                {
                    // First run with the update system: adopt the published
                    // version silently instead of prompting.
                    await WriteStoredAsync(WebVersionKey, latest);
                    return null;
                }
                current = stored;
            }
            else
            {
                current = (_platform.AppVersion ?? "").Trim();
                if (string.IsNullOrEmpty(current)) return null;
            }

            if (!IsNewer(current, latest)) return null;

            if (!entry.Force)
            {
                var skipped = await ReadStoredAsync(SkipKey(platformKey));
                if (string.Equals(skipped, latest, StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            return new AppUpdateInfo
            {
                PlatformKey = platformKey,
                Current = current,
                Entry = entry
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Remembers that the user skipped this exact version (optional updates only).</summary>
    public async Task SkipVersionAsync(string platformKey, string version)
    {
        try { await WriteStoredAsync(SkipKey(platformKey), version); } catch { }
    }

    /// <summary>
    /// Web update: adopts the new version, wipes the browser-cached app files
    /// and loads a fresh copy (see nmuFunctions.clearSiteCacheAndReload).
    /// </summary>
    public async Task ApplyWebUpdateAsync(string version)
    {
        try { await WriteStoredAsync(WebVersionKey, version); } catch { }
        try { await _js.InvokeVoidAsync("nmuFunctions.clearSiteCacheAndReload", version); } catch { }
    }

    /// <summary>
    /// Loads the whole admin-published update node (per-platform entries).
    /// Used by the download dialog. Returns null on any failure.
    /// </summary>
    public async Task<Dictionary<string, AppUpdateEntry>?> GetEntriesAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{FirebaseUrl}/AppUpdate.json");
            request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
            using var response = await _http.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;
            var json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json == "null")
                return new Dictionary<string, AppUpdateEntry>(StringComparer.OrdinalIgnoreCase);
            // NOTE: dictionary keys deserialize case-sensitively, so rebuild
            // with an OrdinalIgnoreCase comparer for safe lookups.
            var parsed = JsonSerializer.Deserialize<Dictionary<string, AppUpdateEntry>>(json, CaseInsensitive);
            return parsed == null
                ? new Dictionary<string, AppUpdateEntry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, AppUpdateEntry>(parsed, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return null;
        }
    }

    private async Task<AppUpdateEntry?> FetchEntryAsync(string platformKey)
    {
        try
        {
            var all = await GetEntriesAsync();
            if (all != null && all.TryGetValue(platformKey, out var entry))
                return entry;
        }
        catch { }
        return null;
    }

    private async Task<string?> ReadStoredAsync(string key)
    {
        try { return await _js.InvokeAsync<string?>("nmuFunctions.safeGetItem", key); }
        catch { return null; }
    }

    private async Task WriteStoredAsync(string key, string value)
    {
        try { await _js.InvokeVoidAsync("nmuFunctions.safeSetItem", key, value); }
        catch { }
    }

    /// <summary>True when <paramref name="latest"/> is strictly newer than <paramref name="current"/>.</summary>
    public static bool IsNewer(string current, string latest)
    {
        var cur = ParseVersion(current);
        var lat = ParseVersion(latest);
        if (cur == null || lat == null) return false;
        var n = Math.Max(cur.Length, lat.Length);
        for (var i = 0; i < n; i++)
        {
            var c = i < cur.Length ? cur[i] : 0;
            var l = i < lat.Length ? lat[i] : 0;
            if (l > c) return true;
            if (l < c) return false;
        }
        return false;
    }

    private static int[]? ParseVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var parts = v.Trim().TrimStart('v', 'V').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var nums = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            var digitPrefix = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
            if (!int.TryParse(digitPrefix, out nums[i])) nums[i] = 0;
        }
        return nums;
    }
}
