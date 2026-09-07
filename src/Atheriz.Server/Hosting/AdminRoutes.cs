using System.Text;
using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Server.Hosting;

public static class AdminRoutes
{
    public static void MapAdminRoutes(this WebApplication app, AtherizSettings settings)
    {
        bool CheckAdmin(HttpContext ctx, string action, out string? error)
        {
            var remoteIp = ctx.Connection.RemoteIpAddress?.ToString();
            var provided = ctx.Request.Headers["X-Admin-Token"].FirstOrDefault() ?? string.Empty;
            var err = AdminToken.CheckAdmin(settings.SecretPath, remoteIp, provided, action);
            error = err;
            return err == null;
        }

        app.MapPost("/_internal/hot_reload", async (HttpContext ctx) =>
        {
            if (!CheckAdmin(ctx, "reload", out var err))
                // Port of atheriz.py:348-350 — auth failures are HTTP 200 with
                // {status: error} so the CLI reads data.status (IsSuccess path).
                return Results.Json(new { status = "error", message = err });
            if (ctx.Request.ContentLength > 4096)
                return Results.Json(new { status = "error", message = "Request body too large." });
            try
            {
                // Watchdog: a hung reload must not pin the worker forever.
                // On timeout the background reload keeps running; the caller
                // retries or inspects the log (same 200+{status:error} contract).
                var work = Task.Run(async () =>
                {
                    string msg;
                    try
                    {
                        var ticker = GlobalServices.GetAsyncTicker();
                        var pool = GlobalServices.GetAsyncThreadPool();
                        msg = await PluginReloader.ReloadGameLogicAsync(ticker, pool, settings);
                        try { ServerLifecycle.DoReload(settings); } catch (Exception ex) { Console.Error.WriteLine($"[HotReload] DoReload failed: {ex.Message}"); }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[HotReload] PluginReloader failed: {ex.Message}, falling back to DoReload");
                        ServerLifecycle.DoReload(settings);
                        msg = "Reload completed (fallback).";
                    }
                    return msg;
                });
                var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(60)));
                if (done != work)
                {
                    Console.Error.WriteLine("[HotReload] Reload exceeded 60s watchdog; continuing in background.");
                    return Results.Json(new { status = "error", message = "Reload timed out after 60s; still running in background." });
                }
                return Results.Json(new { status = "ok", message = await work });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", message = ex.Message });
            }
        });

        app.MapPost("/_internal/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (!CheckAdmin(ctx, "shutdown", out var err))
                // Port of atheriz.py:348-350 — auth failures are HTTP 200 with
                // {status: error} so the CLI reads data.status (IsSuccess path).
                return Results.Json(new { status = "error", message = err });
            if (ctx.Request.ContentLength > 4096)
                return Results.Json(new { status = "error", message = "Request body too large." });

            Console.Error.WriteLine("Internal shutdown request received. Running shutdown tasks...");

            var watchdogCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(60), watchdogCts.Token); }
                catch (OperationCanceledException) { return; }
                Console.Error.WriteLine("Shutdown watchdog: forcing exit.");
                lifetime.StopApplication();
            });

            _ = Task.Run(async () =>
            {
                try { await Task.Run(() => ServerLifecycle.DoShutdown(settings)); }
                finally
                {
                    try { watchdogCts.Cancel(); } catch { }
                    lifetime.StopApplication();
                }
            });

            return Results.Json(new { status = "ok", message = "Shutdown tasks queued." });
        });

        app.MapPost("/_internal/create_account", async (HttpContext ctx) =>
        {
            if (!CheckAdmin(ctx, "account creation", out var err))
                // Port of atheriz.py:348-350 — auth failures are HTTP 200 with
                // {status: error} so the CLI reads data.status (IsSuccess path).
                return Results.Json(new { status = "error", message = err });

            // Size-capped body read: reject oversized payloads without allocating them.
            using var doc = await ReadCappedJsonBodyAsync(ctx, 64 * 1024);
            if (doc == null)
            {
                return Results.Json(new { status = "error", message = "Invalid JSON body." });
            }
            var root = doc.RootElement;
            string? accountName = root.TryGetProperty("account_name", out var a) ? a.GetString() : null;
            string? charName = root.TryGetProperty("char_name", out var c) ? c.GetString() : null;
            string? password = root.TryGetProperty("password", out var p) ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(password))
                return Results.Json(new { status = "error", message = "account_name, char_name and password are required." });

            string? vErr = ValidateAccountName(accountName, settings) ?? ValidateCharacterName(charName, settings) ?? ValidatePassword(password, settings);
            if (vErr != null) return Results.Json(new { status = "error", message = vErr });

            try
            {
                // Port of atheriz.py create_account_endpoint: run at_char_create and
                // return its printed output (StringWriter = redirect_stdout).
                var sb = new StringBuilder();
                using var sw = new StringWriter(sb);
                ServerEvents.AtCharCreate(accountName, charName, password, sw);
                var message = sb.ToString().Trim();
                if (string.IsNullOrEmpty(message)) message = "Account created.";
                return Results.Json(new { status = "ok", message });
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
            if (ctx.Request.ContentLength is null)
            {
                using var ms = new MemoryStream();
                var buf = new byte[8192];
                int n;
                long total = 0;
                while ((n = await ctx.Request.Body.ReadAsync(buf)) > 0)
                {
                    total += n;
                    if (total > maxBytes) return null;
                    ms.Write(buf, 0, n);
                }
                ms.Position = 0;
                return await JsonDocument.ParseAsync(ms);
            }
            return await JsonDocument.ParseAsync(ctx.Request.Body);
        }
        catch { return null; }
    }

    private static string? ValidateAccountName(string name, AtherizSettings s)
        => Atheriz.Core.Commands.UnloggedIn.Validation.ValidateAccountName(name, s);

    private static string? ValidateCharacterName(string name, AtherizSettings s)
        => Atheriz.Core.Commands.UnloggedIn.Validation.ValidateCharacterName(name, s);

    private static string? ValidatePassword(string pw, AtherizSettings s)
        => Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword(pw, s);
}
