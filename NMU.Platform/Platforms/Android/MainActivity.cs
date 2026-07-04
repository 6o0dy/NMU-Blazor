using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace NMU.Platform;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    private const int DownloadRequestCode = 1001;
    private static TaskCompletionSource<Android.Net.Uri?>? _downloadTcs;

    public static Task<Android.Net.Uri?> StartSaveFileIntent(Intent intent)
    {
        _downloadTcs = new TaskCompletionSource<Android.Net.Uri?>();
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        activity?.StartActivityForResult(intent, DownloadRequestCode);
        return _downloadTcs.Task;
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == DownloadRequestCode)
        {
            if (resultCode == Result.Ok && data?.Data != null)
                _downloadTcs?.TrySetResult(data.Data);
            else
                _downloadTcs?.TrySetResult(null);
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        if (Window == null) return;
        HideStatusBar();
    }

    private void HideStatusBar()
    {
        if (Window?.DecorView == null) return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
        {
            Window.SetDecorFitsSystemWindows(false);
            Window.InsetsController?.Hide(WindowInsets.Type.StatusBars());
            Window.InsetsController?.SystemBarsBehavior =
                (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
        }
        else
        {
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)(
                SystemUiFlags.Fullscreen |
                SystemUiFlags.ImmersiveSticky |
                SystemUiFlags.LayoutStable |
                SystemUiFlags.LayoutFullscreen);
        }
    }
}
