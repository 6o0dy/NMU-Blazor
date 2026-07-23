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

            appWindow.Changed -= OnAppWindowChanged;
            appWindow.Changed += OnAppWindowChanged;

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

#if WINDOWS
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (args.DidPresenterChange &&
            sender.Presenter.Kind != Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen &&
            IsFullScreen)
        {
            IsFullScreen = false;
            var nw = GetNativeWindow();
            if (nw != null)
            {
                nw.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    App.HideTitleBarLogo(nw));
            }
            FullScreenChanged?.Invoke();
        }
    }
#endif
    
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
#elif ANDROID
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity != null)
                activity.FinishAffinity();
        }
        catch { }
#elif IOS
        try { System.Threading.Thread.CurrentThread.Abort(); } catch { }
        try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
        try { Environment.Exit(0); } catch { }
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
    public async Task<DownloadResult> DownloadFileAsync(string url, string fileName)
    {
#if ANDROID
        try
        {
            var ext = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
            var mime = ext switch
            {
                ".mp4" or ".mkv" or ".webm" => "video/*",
                ".mp3" or ".wav" or ".m4a" => "audio/*",
                ".pdf" => "application/pdf",
                _ => "*/*"
            };

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
            intent.AddCategory(Android.Content.Intent.CategoryOpenable);
            intent.SetType(mime);
            intent.PutExtra(Android.Content.Intent.ExtraTitle, fileName ?? "document");

            var resultUri = await MainActivity.StartSaveFileIntent(intent);
            if (resultUri == null) return DownloadResult.Cancelled;

            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(url);

            using var stream = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver?.OpenOutputStream(resultUri);
            if (stream == null) return DownloadResult.Error;
            await stream.WriteAsync(bytes, 0, bytes.Length);

            return DownloadResult.Success;
        }
        catch { return DownloadResult.Error; }
#elif IOS
        try
        {
            using var client = new HttpClient();
            var bytes = await client.GetByteArrayAsync(url);

            var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName ?? "document.pdf");
            await File.WriteAllBytesAsync(tempPath, bytes);

            var urlObj = Foundation.NSUrl.FromFilename(tempPath);
            var picker = new UIKit.UIDocumentPickerViewController(
                new Foundation.NSUrl[] { urlObj },
                UIKit.UIDocumentPickerMode.ExportToService);

            TaskCompletionSource<DownloadResult> tcs = new();
            picker.DidPickDocument += (_, _) => tcs.TrySetResult(DownloadResult.Success);
            picker.WasCancelled += (_, _) => tcs.TrySetResult(DownloadResult.Cancelled);

            var vc = UIKit.UIApplication.SharedApplication.KeyWindow?.RootViewController;
            while (vc?.PresentedViewController != null)
                vc = vc.PresentedViewController;

            if (vc != null)
            {
                await vc.PresentViewControllerAsync(picker, true);
                return await tcs.Task;
            }
            return DownloadResult.Error;
        }
        catch { return DownloadResult.Error; }
#else
        return DownloadResult.Error;
#endif
    }

    public async Task<DownloadResult> SaveFileAsync(byte[] data, string fileName)
    {
#if ANDROID
        try
        {
            var ext = System.IO.Path.GetExtension(fileName ?? "").ToLowerInvariant();
            var mime = ext switch
            {
                ".mp4" or ".mkv" or ".webm" => "video/*",
                ".mp3" or ".wav" or ".m4a" => "audio/*",
                ".pdf" => "application/pdf",
                _ => "*/*"
            };

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
            intent.AddCategory(Android.Content.Intent.CategoryOpenable);
            intent.SetType(mime);
            intent.PutExtra(Android.Content.Intent.ExtraTitle, fileName ?? "document");

            var resultUri = await MainActivity.StartSaveFileIntent(intent);
            if (resultUri == null) return DownloadResult.Cancelled;

            using var stream = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver?.OpenOutputStream(resultUri);
            if (stream == null) return DownloadResult.Error;
            await stream.WriteAsync(data, 0, data.Length);

            return DownloadResult.Success;
        }
        catch { return DownloadResult.Error; }
#elif IOS
        try
        {
            var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName ?? "document.pdf");
            await File.WriteAllBytesAsync(tempPath, data);

            var urlObj = Foundation.NSUrl.FromFilename(tempPath);
            var picker = new UIKit.UIDocumentPickerViewController(
                new Foundation.NSUrl[] { urlObj },
                UIKit.UIDocumentPickerMode.ExportToService);

            TaskCompletionSource<DownloadResult> tcs = new();
            picker.DidPickDocument += (_, _) => tcs.TrySetResult(DownloadResult.Success);
            picker.WasCancelled += (_, _) => tcs.TrySetResult(DownloadResult.Cancelled);

            var vc = UIKit.UIApplication.SharedApplication.KeyWindow?.RootViewController;
            while (vc?.PresentedViewController != null)
                vc = vc.PresentedViewController;

            if (vc != null)
            {
                await vc.PresentViewControllerAsync(picker, true);
                return await tcs.Task;
            }
            return DownloadResult.Error;
        }
        catch { return DownloadResult.Error; }
#else
        return DownloadResult.Error;
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
