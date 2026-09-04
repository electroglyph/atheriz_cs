using Atheriz.Core.Settings;
using Atheriz.Server.Infrastructure;
using Microsoft.AspNetCore.StaticFiles;

namespace Atheriz.Server.Hosting;

public static class StaticFileConfig
{
    public static (string? staticCandidate, string? templatesCandidate) Configure(WebApplication app, AtherizSettings settings)
    {
        var staticCandidate = AssetPathResolver.ResolveWwwRoot(app.Environment.ContentRootPath, AppContext.BaseDirectory);
        var templatesCandidate = AssetPathResolver.ResolveTemplates(app.Environment.ContentRootPath, AppContext.BaseDirectory);
        if (staticCandidate != null)
        {
            Console.WriteLine($"Serving static files from: {staticCandidate}");
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            contentTypeProvider.Mappings[".wasm"] = "application/wasm";
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(staticCandidate)),
                RequestPath = "/static",
                ContentTypeProvider = contentTypeProvider,
                OnPrepareResponse = ctx =>
                {
                    var path = ctx.Context.Request.Path.Value ?? string.Empty;
                    if (path.StartsWith("/static/assets/", StringComparison.OrdinalIgnoreCase))
                        ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                    else if (path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
                        ctx.Context.Response.Headers.CacheControl = "public, max-age=86400";
                }
            });
            var drawEntrypoint = Path.Combine(staticCandidate, "atheriz_draw", "index.html");
            if (File.Exists(drawEntrypoint)) Console.WriteLine("AtheriZ Draw available at /atheriz_draw/");
            else Console.WriteLine("Warning: AtheriZ Draw build not found at /atheriz_draw/");
            if (File.Exists(Path.Combine(staticCandidate, "webclient", "index.html")))
                Console.WriteLine("Webclient available at /webclient/index.html");
            else
                Console.WriteLine("Webclient available at /webclient/index.html (fallback template if compiled missing)");
            try
            {
                var syncSummary = WebclientSyncChecker.CheckSync(Directory.GetCurrentDirectory(), app.Environment.ContentRootPath, null);
                if (syncSummary != null)
                    Console.WriteLine(WebclientSyncChecker.FormatWarning(syncSummary, Directory.GetCurrentDirectory(), app.Environment.ContentRootPath, null, null));
            }
            catch (Exception ex) { Console.Error.WriteLine($"Webclient sync check failed: {ex.Message}"); }
        }
        else
        {
            Console.WriteLine($"Warning: Static directory not found: {Path.Combine(app.Environment.ContentRootPath, "wwwroot")}");
        }

        app.MapGet("/", () =>
        {
            if (templatesCandidate != null)
            {
                var tpl = Path.Combine(templatesCandidate, "index.html");
                if (File.Exists(tpl)) return Results.File(tpl, contentType: "text/html");
            }
            if (staticCandidate != null)
            {
                var idx = Path.Combine(staticCandidate, "index.html");
                if (File.Exists(idx)) return Results.File(idx, contentType: "text/html");
            }
            return Results.Content($"<h1>{settings.ServerName}</h1><p><a href=\"/webclient/index.html\">Play</a></p>", "text/html");
        });
        app.MapGet("/webclient/index.html", () =>
        {
            if (staticCandidate != null)
            {
                var compiled = Path.Combine(staticCandidate, "webclient", "index.html");
                if (File.Exists(compiled)) return Results.File(compiled, contentType: "text/html");
            }
            if (templatesCandidate != null)
            {
                var tpl = Path.Combine(templatesCandidate, "webclient", "index.html");
                if (File.Exists(tpl)) return Results.File(tpl, contentType: "text/html");
            }
            return Results.NotFound("Webclient not built — run webclient build and deploy.");
        });
        app.MapGet("/webclient", () => Results.Redirect("/webclient/index.html"));
        app.MapGet("/webclient/", () => Results.Redirect("/webclient/index.html"));
        IResult ServeDraw()
        {
            if (staticCandidate != null)
            {
                var compiledDraw = Path.Combine(staticCandidate, "atheriz_draw", "index.html");
                if (File.Exists(compiledDraw)) return Results.File(compiledDraw, contentType: "text/html");
            }
            return Results.Content("AtheriZ Draw not built — run `npm run build` in webclient/ and deploy.", "text/html", statusCode: 404);
        }
        app.MapGet("/atheriz_draw", ServeDraw);
        app.MapGet("/atheriz_draw/", ServeDraw);
        app.MapGet("/atheriz_draw/index.html", ServeDraw);
        app.MapGet("/health", () => Results.Json(new { status = "ok", server = settings.ServerName }));
        // Readiness probe: /health stays unconditional liveness (webclient relies on it);
        // /ready reports whether DoStartup ran to completion.
        app.MapGet("/ready", () => ServerLifecycle.StartupSucceeded
            ? Results.Json(new { status = "ok", server = settings.ServerName })
            : Results.Json(new { status = "starting", server = settings.ServerName }, statusCode: 503));
        return (staticCandidate, templatesCandidate);
    }
}
