namespace NMU.Platform;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

#if ANDROID
	protected override bool OnBackButtonPressed()
	{
		try
		{
			var webView = blazorWebView.Handler?.PlatformView as Android.Webkit.WebView;
			webView?.EvaluateJavascript("window.history.back()", null);
		}
		catch { }
		return true;
	}
#endif
}
