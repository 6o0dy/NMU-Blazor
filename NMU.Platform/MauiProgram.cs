using Microsoft.Extensions.Logging;

namespace NMU.Platform;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();
		builder.Services.AddScoped<NMU.Platform.Components.Services.StudentService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.FullscreenService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.NavigationState>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.LayoutState>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.ToastService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.MaterialsService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.RecordedService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.YouTubeService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.QuizService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.QuizStateService>();
		builder.Services.AddScoped<NMU.Platform.Components.Services.IPlatformService, DesktopPlatformService>();
		builder.Services.AddScoped<HttpClient>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
