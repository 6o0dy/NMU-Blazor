namespace NMU.Platform.Components.Services;

public class DefaultPlatformService : IPlatformService
{
    public bool IsDesktop => false;
    public bool IsFullScreen => false;
    public event Action? FullScreenChanged { add { } remove { } }
    public Task ToggleMaximizeAsync() => Task.CompletedTask;
    public Task ToggleFullScreenAsync() => Task.CompletedTask;
    public Task MinimizeAsync() => Task.CompletedTask;
    public Task CloseAsync() => Task.CompletedTask;
}
