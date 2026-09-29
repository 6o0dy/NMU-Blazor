namespace NMU.Platform.Components.Services;

public enum DownloadResult
{
    Success,
    Cancelled,
    Error
}

public interface IPlatformService
{
    bool IsDesktop { get; }
    bool IsWeb { get; }
    /// <summary>Update-config key: "android" | "ios" | "windows" | "macos" | "web".</summary>
    string PlatformKey { get; }
    /// <summary>Installed app version (e.g. "1.0"). Empty when unavailable (web).</summary>
    string AppVersion { get; }
    bool IsFullScreen { get; }
    event Action? FullScreenChanged;
    Task DragMoveAsync();
    Task ToggleMaximizeAsync();
    Task ToggleFullScreenAsync();
    Task MinimizeAsync();
    Task CloseAsync();
    Task OpenPdfAsync(byte[] pdfData, string fileName);
    Task<DownloadResult> DownloadFileAsync(string url, string fileName);
    Task<DownloadResult> SaveFileAsync(byte[] data, string fileName);
}
