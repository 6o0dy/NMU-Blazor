using NMU.Platform.Components.Services;

namespace NMU.Platform;

public class DesktopPlatformService : IPlatformService
{
    public bool IsDesktop => true;
    public bool IsFullScreen { get; private set; }
    public event Action? FullScreenChanged;

    public Task ToggleMaximizeAsync()
    {
#if WINDOWS
        var nw = GetNativeWindow();
        if (nw == null) return Task.CompletedTask;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nw);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
            {
                if (p.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
                    p.Restore();
                else
                    p.Maximize();
            }
        }
        catch { }
#endif
        return Task.CompletedTask;
    }

    public Task ToggleFullScreenAsync()
    {
#if WINDOWS
        var nw = GetNativeWindow();
        if (nw == null) return Task.CompletedTask;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nw);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));

            if (appWindow.Presenter.Kind == Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen)
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.Overlapped);
                IsFullScreen = false;
                nw.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    App.RemoveTitleBar(nw));
            }
            else
            {
                appWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
                IsFullScreen = true;
            }
            FullScreenChanged?.Invoke();
        }
        catch { }
#endif
        return Task.CompletedTask;
    }

    public Task MinimizeAsync()
    {
#if WINDOWS
        var nw = GetNativeWindow();
        if (nw == null) return Task.CompletedTask;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nw);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
                p.Minimize();
        }
        catch { }
#endif
        return Task.CompletedTask;
    }

    public Task CloseAsync()
    {
#if WINDOWS
        var nw = GetNativeWindow();
        if (nw == null) return Task.CompletedTask;
        try { nw.Close(); }
        catch { }
#endif
        return Task.CompletedTask;
    }

#if WINDOWS
    static Microsoft.UI.Xaml.Window? GetNativeWindow()
        => Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
#endif
}
