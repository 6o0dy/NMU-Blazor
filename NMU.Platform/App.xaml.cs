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
	const int WS_CAPTION = 0x00C00000;
	const int WS_SYSMENU = 0x00080000;

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
		var window = new Window(new MainPage()) { Title = "NMU.Platform" };

		window.HandlerChanged += (s, e) =>
		{
#if WINDOWS
			if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
			{
				RemoveTitleBar(nativeWindow);

				var t = new Microsoft.UI.Xaml.DispatcherTimer();
				t.Interval = TimeSpan.FromMilliseconds(800);
				t.Tick += (_, _) => { t.Stop(); RemoveTitleBar(nativeWindow); };
				t.Start();
			}
#endif
		};

		return window;
	}

#if WINDOWS
	internal static void RemoveTitleBar(Microsoft.UI.Xaml.Window? nativeWindow)
	{
		try
		{
			if (nativeWindow == null) return;
			nativeWindow.ExtendsContentIntoTitleBar = true;

			var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);

			var style = GetWindowLong(hwnd, GWL_STYLE);
			style &= ~WS_CAPTION;
			style &= ~WS_SYSMENU;
			SetWindowLong(hwnd, GWL_STYLE, style);

			SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
		}
		catch { }
	}
#endif
}
