using System.Runtime.InteropServices;

namespace NMU.Platform;

public partial class App : Application
{
	[DllImport("user32.dll")]
	static extern int GetWindowLong(IntPtr hWnd, int nIndex);

	[DllImport("user32.dll")]
	static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

	[DllImport("user32.dll")]
	static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	const int GWL_STYLE = -16;
	const int WS_SYSMENU = 0x00080000;
	const int WS_MINIMIZEBOX = 0x00020000;
	const int WS_MAXIMIZEBOX = 0x00010000;

	const int SWP_NOMOVE = 0x0002;
	const int SWP_NOSIZE = 0x0001;
	const int SWP_NOZORDER = 0x0004;
	const int SWP_FRAMECHANGED = 0x0020;

	public App()
	{
		InitializeComponent();
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new MainPage()) { Title = "" };

		window.MinimumWidth = 415;
		window.MinimumHeight = 700;

		window.HandlerChanged += (s, e) =>
		{
#if WINDOWS
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
			{
				HideTitleBarLogo(nativeWindow);
				MaximizeOnStartup(nativeWindow);

				var t = new Microsoft.UI.Xaml.DispatcherTimer();
				t.Interval = TimeSpan.FromMilliseconds(300);
				t.Tick += (_, _) => { t.Stop(); HideTitleBarLogo(nativeWindow); };
				t.Start();
			}
#endif
		};

		return window;
	}

#if WINDOWS
	internal static void HideTitleBarLogo(Microsoft.UI.Xaml.Window? nativeWindow)
	{
		try
		{
			if (nativeWindow == null) return;
			nativeWindow.ExtendsContentIntoTitleBar = true;

			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
			var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
			var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

			var titleBar = appWindow.TitleBar;
			titleBar.ExtendsContentIntoTitleBar = true;
			titleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Collapsed;

			titleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonForegroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonInactiveForegroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonHoverBackgroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonHoverForegroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonPressedBackgroundColor = Microsoft.UI.Colors.Transparent;
			titleBar.ButtonPressedForegroundColor = Microsoft.UI.Colors.Transparent;

			var style = GetWindowLong(hwnd, GWL_STYLE);
			style &= ~WS_SYSMENU;
			style &= ~WS_MINIMIZEBOX;
			style &= ~WS_MAXIMIZEBOX;
			SetWindowLong(hwnd, GWL_STYLE, style);

			SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
		}
		catch { }
	}

	internal static void MaximizeOnStartup(Microsoft.UI.Xaml.Window? nativeWindow)
	{
		try
		{
			if (nativeWindow == null) return;
			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
			var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
			var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);

			var displayArea = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(windowId, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
			var workArea = displayArea.WorkArea;
			int w = 1200, h = 700;
			appWindow.MoveAndResize(new Windows.Graphics.RectInt32
			{
				X = (workArea.Width - w) / 2,
				Y = (workArea.Height - h) / 4,
				Width = w,
				Height = h
			});

			if (appWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter p)
				p.Maximize();
		}
		catch { }
	}
#endif
}
