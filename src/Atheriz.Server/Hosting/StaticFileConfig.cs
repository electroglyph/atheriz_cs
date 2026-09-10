using System.Text.RegularExpressions;
using Microsoft.AspNetCore.StaticFiles;

namespace Atheriz.Server.Hosting;

public static class StaticFileConfig
{
    // Content-hashed bundle filename (e.g. app.ab12cd34.js): compiled once,
    // matched per static-file response for the immutable cache header.
    private static readonly Regex HashedBundlePattern = new(@"\.[0-9a-fA-F]{8,}\.[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Shared no-cache trio for the three entry-HTML shapes (byte-identical).
    private static void SetNoCache(HttpResponse response)
    {
        response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        response.Headers.Pragma = "no-cache";
    }

    // First existing file candidate (static-vs-template fallbacks below).
    private static string? FirstExisting(params string?[] candidates)
    {
        foreach (var c in candidates)
            if (c is not null && File.Exists(c)) return c;
        return null;
    }
    public static (string? staticCandidate, string? templatesCandidate) Configure(WebApplication app, AtherizSettings settings)
    {
        var staticCandidate = AssetPathResolver.ResolveWwwRoot(app.Environment.ContentRootPath, AppContext.BaseDirectory);
        var templatesCandidate = AssetPathResolver.ResolveTemplates(app.Environment.ContentRootPath, AppContext.BaseDirectory);
        if (staticCandidate is not null)
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
                    // Merged immutable branches: the assets-prefix and hashed-
                    // bundle arms assigned the identical value. The wasm guard
                    // on the hash arm is load-bearing, not redundant — the old
                    // chain tested wasm BEFORE the hash, so a hashed .wasm
                    // outside assets/ served 86400, not immutable. Keeping the
                    // guard preserves header bytes for that overlap exactly.
                    bool immutable = path.StartsWith("/static/assets/", StringComparison.OrdinalIgnoreCase)
                        || (!path.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase) && HashedBundlePattern.IsMatch(path));
                    if (immutable)
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
                if (syncSummary is not null)
                    Console.WriteLine(WebclientSyncChecker.FormatWarning(syncSummary, Directory.GetCurrentDirectory(), app.Environment.ContentRootPath, null, null));
            }
            catch (Exception ex) { Console.Error.WriteLine($"Webclient sync check failed: {ex.Message}"); }
        }
        else
        {
            Console.WriteLine($"Warning: Static directory not found: {Path.Combine(app.Environment.ContentRootPath, "wwwroot")}");
        }

        app.MapGet("/", (HttpContext ctx) =>
        {
            SetNoCache(ctx.Response);
            var tpl = templatesCandidate is not null ? Path.Combine(templatesCandidate, "index.html") : null;
            var idx = staticCandidate is not null ? Path.Combine(staticCandidate, "index.html") : null;
            var hit = FirstExisting(tpl, idx);
            if (hit is not null) return Results.File(hit, contentType: "text/html");
            return Results.Content($"<h1>{settings.ServerName}</h1><p><a href=\"/webclient/index.html\">Play</a></p>", "text/html");
        });
        app.MapGet("/webclient/index.html", (HttpContext ctx) =>
        {
            SetNoCache(ctx.Response);
            var compiled = staticCandidate is not null ? Path.Combine(staticCandidate, "webclient", "index.html") : null;
            var tpl = templatesCandidate is not null ? Path.Combine(templatesCandidate, "webclient", "index.html") : null;
            var hit = FirstExisting(compiled, tpl);
            if (hit is not null) return Results.File(hit, contentType: "text/html");
            return Results.NotFound("Webclient not built — run webclient build and deploy.");
        });
        foreach (var route in new[] { "/webclient", "/webclient/" })
            app.MapGet(route, () => Results.Redirect("/webclient/index.html"));
        IResult ServeDraw(HttpContext ctx)
        {
            // Entry HTML is never cached (hashed bundles underneath are immutable).
            SetNoCache(ctx.Response);
            if (staticCandidate is not null)
            {
                var compiledDraw = Path.Combine(staticCandidate, "atheriz_draw", "index.html");
                if (File.Exists(compiledDraw)) return Results.File(compiledDraw, contentType: "text/html");
            }
            return Results.Content("AtheriZ Draw not built — run `npm run build` in webclient/ and deploy.", "text/html", statusCode: 404);
        }
        // Route-table loop for the draw aliases: registration ORDER is
        // preserved (first-match wins in static-file middleware).
        foreach (var route in new[] { "/atheriz_draw", "/atheriz_draw/", "/atheriz_draw/index.html" })
            app.MapGet(route, ServeDraw);
        app.MapGet("/health", () => Results.Json(new { status = "ok", server = settings.ServerName }));
        // Readiness probe: /health stays unconditional liveness (webclient relies on it);
        // /ready reports whether DoStartup ran to completion.
        app.MapGet("/ready", () => ServerLifecycle.StartupSucceeded
            ? Results.Json(new { status = "ok", server = settings.ServerName })
            : Results.Json(new { status = "starting", server = settings.ServerName }, statusCode: 503));
        return (staticCandidate, templatesCandidate);
    }
}
