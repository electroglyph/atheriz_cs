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
                return Results.Json(new { status = "error", message = err }, statusCode: 403);
            try
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
                return Results.Json(new { status = "ok", message = msg });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", message = ex.Message });
            }
        });

        app.MapPost("/_internal/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (!CheckAdmin(ctx, "shutdown", out var err))
                return Results.Json(new { status = "error", message = err }, statusCode: 403);

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
                return Results.Json(new { status = "error", message = err }, statusCode: 403);

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
        => AccountValidation.ValidateAccountName(name, s);

    private static string? ValidateCharacterName(string name, AtherizSettings s)
        => AccountValidation.ValidateCharacterName(name, s);

    private static string? ValidatePassword(string pw, AtherizSettings s)
        => AccountValidation.ValidatePassword(pw, s);
}
