using System.Net.Http.Headers;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseWebRoot("wwwroot");

var app = builder.Build();

var webRootPath = app.Environment.WebRootPath;

// Map requested _framework files to their hashed-on-disk names (e.g., dotnet.js → dotnet.rmkowhzo5h.js)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/_framework"))
    {
        var relPath = ctx.Request.Path.Value!.Substring("/_framework/".Length);
        var filePath = Path.Combine(webRootPath, "_framework", relPath);
        if (!File.Exists(filePath))
        {
            var dir = Path.GetDirectoryName(filePath)!;
            if (Directory.Exists(dir))
            {
                var stem = Path.GetFileNameWithoutExtension(relPath);
                var ext = Path.GetExtension(relPath);
                var match = Directory.GetFiles(dir, $"{stem}.*{ext}")
                    .FirstOrDefault(f =>
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        return !name.Substring(stem.Length + 1).Contains('.');
                    });
                if (match != null)
                    ctx.Request.Path = "/_framework/" + Path.GetFileName(match);
            }
        }
    }
    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    ServeUnknownFileTypes = true,
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.EndsWith(".wasm"))
            ctx.Context.Response.ContentType = "application/wasm";
        else if (path.EndsWith(".dll"))
            ctx.Context.Response.ContentType = "application/octet-stream";
        else if (path.EndsWith(".dat"))
            ctx.Context.Response.ContentType = "application/octet-stream";
    }
});

app.MapGet("/api/proxy", async (HttpContext ctx) =>
{
    var url = ctx.Request.Query["url"].FirstOrDefault();
    if (string.IsNullOrEmpty(url)) { ctx.Response.StatusCode = 400; return; }
    if (!url.StartsWith("https://archive.org/", StringComparison.OrdinalIgnoreCase)) { ctx.Response.StatusCode = 403; return; }
    try
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        var resp = await client.GetAsync(url);
        if (!resp.IsSuccessStatusCode) { ctx.Response.StatusCode = 502; return; }
        ctx.Response.ContentType = resp.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        ctx.Response.Headers["Cache-Control"] = "public, max-age=3600";
        await resp.Content.CopyToAsync(ctx.Response.Body);
    }
    catch { ctx.Response.StatusCode = 502; }
});

app.MapFallbackToFile("index.html");
app.Run();
