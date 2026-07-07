using System.Runtime.InteropServices;
using NMU.Platform.Components.Services;

namespace NMU.Platform;

public class DesktopPlatformService : IPlatformService
{
    public bool IsWeb => false;
    public bool IsDesktop
    {
        get
        {
#if WINDOWS || MACCATALYST
            return true;
#else
            return false;
#endif
        }
    }
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
                    App.HideTitleBarLogo(nw));
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

    public async Task OpenPdfAsync(byte[] pdfData, string fileName)
    {
#if ANDROID
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);
        await File.WriteAllBytesAsync(path, pdfData);
        await Launcher.OpenAsync(new OpenFileRequest
        {
            File = new ReadOnlyFile(path, "application/pdf")
        });
#endif
    }

    public Task DragMoveAsync()
    {
#if WINDOWS
        var nw = GetNativeWindow();
        if (nw == null) return Task.CompletedTask;
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nw);
            SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }
        catch { }
#endif
        return Task.CompletedTask;
    }

    public async Task DownloadFileAsync(string url, string fileName)
    {
#if ANDROID
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(url);

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
            intent.AddCategory(Android.Content.Intent.CategoryOpenable);
            intent.SetType("application/pdf");
            intent.PutExtra(Android.Content.Intent.ExtraTitle, fileName ?? "document.pdf");

            var resultUri = await MainActivity.StartSaveFileIntent(intent);
            if (resultUri == null) return;

            using var stream = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver?.OpenOutputStream(resultUri);
            if (stream == null) return;
            await stream.WriteAsync(bytes, 0, bytes.Length);
        }
        catch { }
#endif
    }

#if WINDOWS
    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    const uint WM_NCLBUTTONDOWN = 0x00A1;
    static readonly IntPtr HTCAPTION = new IntPtr(2);

    static Microsoft.UI.Xaml.Window? GetNativeWindow()
        => Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
#endif
}
