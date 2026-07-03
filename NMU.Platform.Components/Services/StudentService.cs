using System.Text.Json;
using Microsoft.JSInterop;
using NMU.Platform.Components.Models;

namespace NMU.Platform.Components.Services;

public class StudentService
{
    private readonly IJSRuntime _js;
    private const string StorageKey = "nmu_student_v4";

    public StudentService(IJSRuntime js) => _js = js;

    public async Task<StudentProfile?> GetStudentAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string>("localStorage.getItem", StorageKey);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonSerializer.Deserialize<StudentProfile>(json);
        }
        catch { return null; }
    }

    public async Task SaveStudentAsync(StudentProfile profile)
    {
        var json = JsonSerializer.Serialize(profile);
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
        }
        catch { }
    }

    public async Task<bool> HasStudentDataAsync()
    {
        var s = await GetStudentAsync();
        return s != null && !string.IsNullOrEmpty(s.Name);
    }
}
