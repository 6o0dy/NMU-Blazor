using Android.Views;
using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;

namespace NMU.Platform;

/// <summary>
/// Android WebView tuning for the media player:
/// - MediaPlaybackRequiresUserGesture = false so the app can autoplay video/audio
///   without an initial user gesture (Blazor hybrid limitation).
/// - Consumes long-press so Android does not trigger text selection / context
///   menu, which produces haptic feedback (vibration) on every long press.
/// </summary>
public class CustomBlazorWebViewHandler : BlazorWebViewHandler
{
    private sealed class ConsumeLongClick : Java.Lang.Object, Android.Views.View.IOnLongClickListener
    {
        public bool OnLongClick(Android.Views.View? v) => true;
    }

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView == null) return;
        platformView.Settings.MediaPlaybackRequiresUserGesture = false;
        platformView.HapticFeedbackEnabled = false;
        platformView.SetOnLongClickListener(new ConsumeLongClick());
    }
}
