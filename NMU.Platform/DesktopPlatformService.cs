using System.Reflection;
using System.Runtime.InteropServices;
using NMU.Platform.Components.Services;

namespace NMU.Platform;

public class DesktopPlatformService : IPlatformService
{
    public bool IsWeb => false;
    public string PlatformKey
    {
        get
        {
            try
            {
                var p = Microsoft.Maui.Devices.DeviceInfo.Current.Platform;
                if (p == Microsoft.Maui.Devices.DevicePlatform.Android) return "android";
                if (p == Microsoft.Maui.Devices.DevicePlatform.iOS) return "ios";
                if (p == Microsoft.Maui.Devices.DevicePlatform.WinUI) return "windows";
                if (p == Microsoft.Maui.Devices.DevicePlatform.MacCatalyst) return "macos";
            }
            catch { }
            return "";
        }
    }
    public string AppVersion
    {
        get
        {
            // AppInfo is wrong on unpackaged Windows (stale default, unaware of
            // the build), which used to make the update dialog reappear forever
            // after updating. Prefer the build-stamped assembly version whenever
            // the two sources disagree.
            string? fromAppInfo = null;
            try { fromAppInfo = NormalizeVersion(Microsoft.Maui.ApplicationModel.AppInfo.Current.VersionString); }
            catch { }
            string? fromAssembly = null;
            try
            {
                var entry = System.Reflection.Assembly.GetEntryAssembly();
                var informational = entry?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                fromAssembly = NormalizeVersion(!string.IsNullOrWhiteSpace(informational)
                    ? informational.Split('+')[0]
                    : entry?.GetName().Version?.ToString());
            }
            catch { }
            if (!string.IsNullOrEmpty(fromAssembly) &&
                !string.Equals(fromAssembly, fromAppInfo, StringComparison.OrdinalIgnoreCase))
                return fromAssembly;
            return fromAppInfo ?? fromAssembly ?? "";
        }
    }

    /// <summary>"1.1.0.0" -&gt; "1.1" so build-stamped and display versions compare cleanly.</summary>
    private static string? NormalizeVersion(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().TrimStart('v', 'V');
        var parts = v.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        var end = parts.Length;
        while (end > 1)
        {
            var last = parts[end - 1].Trim();
            if (last.Trim('0') == string.Empty && last.All(char.IsDigit))
                end--;
            else
                break;
        }
        return string.Join(".", parts.Take(end));
    }
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
    /// <summary>
    /// Opens a URL in the system browser, outside the app WebView.
    /// update/download links must never go through window.open inside the
    /// WebView: it mishandles binary downloads (APK) and freezes/crashes the
    /// app on some devices.
    /// </summary>
    public async Task<bool> OpenExternalAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
#if ANDROID
        try
        {
            var intent = new Android.Content.Intent(
                Android.Content.Intent.ActionView,
                Android.Net.Uri.Parse(url));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.StartActivity(intent);
            return true;
        }
        catch { return false; }
#elif IOS
        try
        {
            var nsUrl = Foundation.NSUrl.FromString(url);
            if (nsUrl == null) return false;
            return await UIKit.UIApplication.SharedApplication.OpenUrlAsync(nsUrl, new UIKit.UIOpenUrlOptions());
        }
        catch { return false; }
#elif MACCATALYST
        try
        {
            var nsUrl = Foundation.NSUrl.FromString(url);
            if (nsUrl == null) return false;
            return AppKit.NSWorkspace.SharedWorkspace.OpenUrl(nsUrl);
        }
        catch { return false; }
#elif WINDOWS
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
#else
        return false;
#endif
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
                ".apk" => "application/vnd.android.package-archive",
                _ => "*/*"
            };

            var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
            intent.AddCategory(Android.Content.Intent.CategoryOpenable);
            intent.SetType(mime);
            intent.PutExtra(Android.Content.Intent.ExtraTitle, fileName ?? "document");

            var resultUri = await MainActivity.StartSaveFileIntent(intent);
            if (resultUri == null) return DownloadResult.Cancelled;

            // Stream directly to storage: buffering a whole APK/video with
            // GetByteArrayAsync used to OOM-kill the app on phones.
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            using var netStream = await response.Content.ReadAsStreamAsync();

            using var stream = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.ContentResolver?.OpenOutputStream(resultUri);
            if (stream == null) return DownloadResult.Error;
            await netStream.CopyToAsync(stream);

            return DownloadResult.Success;
        }
        catch { return DownloadResult.Error; }
#elif IOS
        try
        {
            // Stream to disk instead of buffering the whole file in RAM.
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var tempPath = Path.Combine(FileSystem.CacheDirectory, fileName ?? "document.pdf");
            using var fileStream = File.OpenWrite(tempPath);
            using var netStream = await response.Content.ReadAsStreamAsync();
            await netStream.CopyToAsync(fileStream);

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
                ".apk" => "application/vnd.android.package-archive",
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
