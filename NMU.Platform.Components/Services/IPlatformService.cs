namespace NMU.Platform.Components.Services;

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
    Task DownloadFileAsync(string url, string fileName);
}
