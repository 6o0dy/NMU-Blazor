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
