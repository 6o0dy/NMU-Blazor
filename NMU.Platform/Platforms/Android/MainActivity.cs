using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;

namespace NMU.Platform;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
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
