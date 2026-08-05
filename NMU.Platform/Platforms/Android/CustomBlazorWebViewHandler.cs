using Android.Webkit;
using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Maui.Handlers;

namespace NMU.Platform;

/// <summary>
/// Android WebView tuning for the media player:
/// - MediaPlaybackRequiresUserGesture = false so the app can autoplay video/audio
///   without an initial user gesture (Blazor hybrid limitation).
/// </summary>
public class CustomBlazorWebViewHandler : BlazorWebViewHandler
{
    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        if (platformView == null) return;
        platformView.Settings.MediaPlaybackRequiresUserGesture = false;
    }
}
