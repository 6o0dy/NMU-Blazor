namespace NMU.Platform.Components.Services;

public class DefaultPlatformService : IPlatformService
{
    public bool IsDesktop => false;
    public bool IsWeb => true;
    public bool IsFullScreen => false;
    public event Action? FullScreenChanged { add { } remove { } }
    public Task ToggleMaximizeAsync() => Task.CompletedTask;
    public Task ToggleFullScreenAsync() => Task.CompletedTask;
    public Task MinimizeAsync() => Task.CompletedTask;
    public Task CloseAsync() => Task.CompletedTask;
    public Task OpenPdfAsync(byte[] pdfData, string fileName) => Task.CompletedTask;
    public Task DownloadFileAsync(string url, string fileName) => Task.CompletedTask;
}
