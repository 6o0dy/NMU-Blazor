namespace NMU.Platform;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
#if ANDROID || IOS || MACCATALYST
		ConfigureMediaPlayback();
#endif
	}

#if ANDROID || IOS || MACCATALYST
	private bool _mediaConfigured;

	private void ConfigureMediaPlayback()
	{
		if (_mediaConfigured) return;
		var platform = blazorWebView.Handler?.PlatformView;
#if ANDROID
		if (platform is Android.Webkit.WebView wv)
		{
			wv.Settings.MediaPlaybackRequiresUserGesture = false;
			_mediaConfigured = true;
		}
#endif
#if IOS || MACCATALYST
		if (platform is WebKit.WKWebView wk)
		{
			wk.Configuration.MediaTypesRequiringUserActionForPlayback = WebKit.WKAudiovisualMediaTypes.None;
			_mediaConfigured = true;
		}
#endif
		ApplySafeAreaTop();
		if (!_mediaConfigured)
		{
			blazorWebView.HandlerChanged -= OnBlazorHandlerChanged;
			blazorWebView.HandlerChanged += OnBlazorHandlerChanged;
		}
	}

	private void OnBlazorHandlerChanged(object? sender, EventArgs e)
		=> ConfigureMediaPlayback();

	private void ApplySafeAreaTop()
	{
#if ANDROID
		if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView wv)
		{
			var px = MainActivity.GetTopInsetPx();
			var js = $"document.documentElement.style.setProperty('--safe-area-top', '{px}px');";
			try { wv.EvaluateJavascript(js, null); } catch { }
		}
#endif
	}

	protected override void OnSizeAllocated(double width, double height)
	{
		base.OnSizeAllocated(width, height);
		ApplySafeAreaTop();
	}
#endif

#if ANDROID
	protected override bool OnBackButtonPressed()
	{
		try
		{
			var webView = blazorWebView.Handler?.PlatformView as Android.Webkit.WebView;
			webView?.EvaluateJavascript("window.__goBack()", null);
		}
		catch { }
		return true;
	}
#endif
}
