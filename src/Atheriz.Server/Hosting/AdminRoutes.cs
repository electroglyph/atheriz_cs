using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Plugins;

namespace Atheriz.Server.Hosting;

public static class AdminRoutes
{
    // Shared oversized-body limit for the hot_reload/shutdown admin routes
    // (create_account caps through ReadCappedJsonBodyAsync instead).
    private const long MaxAdminBodyBytes = 4096;

    // Nullable comparison: a missing Content-Length (chunked) is never
    // greater, so chunked bodies fall through exactly as before.
    private static bool IsBodyTooLarge(HttpContext ctx) => ctx.Request.ContentLength > MaxAdminBodyBytes;

    public static void MapAdminRoutes(this WebApplication app, AtherizSettings settings)
    {
        bool RequireAdmin(HttpContext ctx, string action, out string? error)
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
            var provided = ctx.Request.Headers["X-Admin-Token"].FirstOrDefault() ?? string.Empty;
            var err = AdminToken.CheckAdmin(settings.SecretPath, remoteIp, provided, action);
            error = err;
            return err is null;
        }

        // Port of atheriz.py:348-350 — auth failures are HTTP 200 with
        // {status: error} so the CLI reads data.status (IsSuccess path).
        // Single guard-result shape shared by the three admin endpoints;
        // endpoints keep their own response shapes otherwise.
        static IResult AdminError(string? message) => Results.Json(new { status = "error", message });
        static IResult AdminOk(string? message) => Results.Json(new { status = "ok", message });

        app.MapPost("/_internal/hot_reload", async (HttpContext ctx) =>
        {
            if (!RequireAdmin(ctx, "reload", out var err))
                return AdminError(err);
            if (IsBodyTooLarge(ctx))
                return Results.Json(new { status = "error", message = "Request body too large." });
            try
            {
                // Watchdog: a hung reload must not pin the worker forever.
                // On timeout the background reload keeps running; the caller
                // retries or inspects the log (same 200+{status:error} contract).
                // single thread-pool hop — the work runs as a named
                // local function, not an async lambda (same one hop).
                async Task<string> DoReloadWork()
                {
                    string msg;
                    try
                    {
                        var ticker = GlobalServices.GetAsyncTicker();
                        var pool = GlobalServices.GetAsyncThreadPool();
                        msg = await PluginReloader.ReloadGameLogicAsync(ticker, pool, settings).ConfigureAwait(false);
                        try { ServerLifecycle.DoReload(settings); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] DoReload failed: {ex.Message}"); }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[HotReload] PluginReloader failed: {ex.Message}, falling back to DoReload");
                        ServerLifecycle.DoReload(settings);
                        msg = "Reload completed (fallback).";
                    }
                    return msg;
                }
                var work = Task.Run(DoReloadWork);
                var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                if (done != work)
                {
                    Console.Error.WriteLine("[HotReload] Reload exceeded 60s watchdog; continuing in background.");
                    return AdminError("Reload timed out after 60s; still running in background.");
                }
                return AdminOk(await work.ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", message = ex.Message });
            }
        });

        app.MapPost("/_internal/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (!RequireAdmin(ctx, "shutdown", out var err))
                return AdminError(err);
            if (IsBodyTooLarge(ctx))
                return Results.Json(new { status = "error", message = "Request body too large." });

            Console.Error.WriteLine("Internal shutdown request received. Running shutdown tasks...");

            // single thread-pool hop for shutdown. The watchdog is a
            // pure delay — a Timer, not a pooled thread. The shutdown work
            // runs on one pooled thread (DoShutdown is synchronous; the old
            // shape nested a second hop inside an async lambda).
            var watchdog = new System.Threading.Timer(_ =>
            {
                Console.Error.WriteLine("Shutdown watchdog: forcing exit.");
                lifetime.StopApplication();
            }, null, TimeSpan.FromSeconds(60), System.Threading.Timeout.InfiniteTimeSpan);

            _ = Task.Run(() =>
            {
                try { ServerLifecycle.DoShutdown(settings); }
                finally
                {
                    try { watchdog.Dispose(); } catch { }
                    lifetime.StopApplication();
                }
            });

            return AdminOk("Shutdown tasks queued.");
        });

        app.MapPost("/_internal/create_account", async (HttpContext ctx) =>
        {
            if (!RequireAdmin(ctx, "account creation", out var err))
                return AdminError(err);

            // Size-capped body read: reject oversized payloads without allocating them.
            using var doc = await ReadCappedJsonBodyAsync(ctx, 64 * 1024).ConfigureAwait(false);
            if (doc is null)
            {
                return Results.Json(new { status = "error", message = "Invalid JSON body." });
            }
            var root = doc.RootElement;
            string? accountName = root.TryGetProperty("account_name", out var a) ? a.GetString() : null;
            string? charName = root.TryGetProperty("char_name", out var c) ? c.GetString() : null;
            string? password = root.TryGetProperty("password", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(password))
                return Results.Json(new { status = "error", message = "account_name, char_name and password are required." });

            string? vErr = Atheriz.Core.Commands.UnloggedIn.Validation.ValidateAccountName(accountName, settings)
                ?? Atheriz.Core.Commands.UnloggedIn.Validation.ValidateCharacterName(charName, settings)
                ?? Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword(password, settings);
            if (vErr is not null) return Results.Json(new { status = "error", message = vErr });

            try
            {
                // Port of atheriz.py create_account_endpoint: run at_char_create and
                // return its printed output (StringWriter = redirect_stdout).
                var sb = new StringBuilder();
                using var sw = new StringWriter(sb);
                ServerEvents.AtCharCreate(accountName, charName, password, sw);
                var message = sb.ToString().Trim();
                if (string.IsNullOrEmpty(message)) message = "Account created.";
                return AdminOk(message);
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", message = ex.Message });
            }
        });
    }

    /// <summary>
    /// Reads the request body as JSON with a hard size cap. Returns null when the body
    /// is missing, oversized, or not valid JSON. Chunked bodies (no Content-Length)
    /// are copied through a bounded buffer so they cannot OOM the server.
    /// </summary>
    private static async Task<JsonDocument?> ReadCappedJsonBodyAsync(HttpContext ctx, long maxBytes)
    {
        try
        {
            if (ctx.Request.ContentLength > maxBytes) return null;
            // Content-Length is client-declared — never hand the raw
            // stream to the parser (a lying length over-allocates inside
            // JsonDocument). Copy through a bounded buffer in all cases.
            using var ms = new MemoryStream();
            var buf = new byte[8192];
            int n;
            long total = 0;
            while ((n = await ctx.Request.Body.ReadAsync(buf).ConfigureAwait(false)) > 0)
            {
                total += n;
                if (total > maxBytes) return null;
                ms.Write(buf, 0, n);
            }
            ms.Position = 0;
            return await JsonDocument.ParseAsync(ms).ConfigureAwait(false);
        }
        catch { return null; }
    }
}
