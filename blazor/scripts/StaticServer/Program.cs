using Microsoft.AspNetCore.StaticFiles;

var root = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
var urls = args.Length > 1 ? args[1] : "http://0.0.0.0:5090";

var builder = WebApplication.CreateBuilder();
builder.WebHost.UseUrls(urls);
var app = builder.Build();
var contentTypes = new FileExtensionContentTypeProvider();

app.Use(async (context, next) =>
{
    var requested = context.Request.Path.Value?.TrimStart('/') ?? string.Empty;
    var file = Path.GetFullPath(Path.Combine(root, requested));
    if (!file.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal) || !File.Exists(file))
    {
        await next();
        return;
    }

    var acceptEncoding = context.Request.Headers.AcceptEncoding.ToString();
    var compressed = acceptEncoding.Contains("br", StringComparison.OrdinalIgnoreCase) && File.Exists(file + ".br")
        ? (Path: file + ".br", Encoding: "br")
        : acceptEncoding.Contains("gzip", StringComparison.OrdinalIgnoreCase) && File.Exists(file + ".gz")
            ? (Path: file + ".gz", Encoding: "gzip")
            : (Path: file, Encoding: (string?)null);

    context.Response.ContentType = contentTypes.TryGetContentType(file, out var type) ? type : "application/octet-stream";
    if (compressed.Encoding is not null)
    {
        context.Response.Headers.ContentEncoding = compressed.Encoding;
        context.Response.Headers.Vary = "Accept-Encoding";
    }

    var immutable = requested.StartsWith("_framework/", StringComparison.OrdinalIgnoreCase)
        || requested.Contains('.', StringComparison.Ordinal) && (requested.Contains(".", StringComparison.Ordinal) &&
            (requested.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) || requested.EndsWith(".js", StringComparison.OrdinalIgnoreCase)));
    context.Response.Headers.CacheControl = requested is "" or "index.html" or "config.json"
        ? "no-cache"
        : immutable ? "public,max-age=31536000,immutable" : "public,max-age=86400";
    await context.Response.SendFileAsync(compressed.Path);
});

app.MapFallback(async context =>
{
    context.Response.ContentType = "text/html; charset=utf-8";
    context.Response.Headers.CacheControl = "no-cache";
    await context.Response.SendFileAsync(Path.Combine(root, "index.html"));
});

await app.RunAsync();
