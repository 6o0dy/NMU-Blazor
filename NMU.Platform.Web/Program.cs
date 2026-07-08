using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using NMU.Platform.Components.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<NMU.Platform.Web.Components.Routes>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<StudentService>();
builder.Services.AddScoped<FullscreenService>();
builder.Services.AddScoped<NavigationState>();
builder.Services.AddScoped<LayoutState>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddScoped<MaterialsService>();
builder.Services.AddScoped<RecordedService>();
builder.Services.AddScoped<IPlatformService, DefaultPlatformService>();

await builder.Build().RunAsync();
