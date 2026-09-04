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

            JsonDocument doc;
            try
            {
                doc = await JsonDocument.ParseAsync(ctx.Request.Body);
            }
            catch
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
                var exists = ObjectRegistry.FilterBy(o => o.IsAccount && string.Equals(o.Name, accountName, StringComparison.OrdinalIgnoreCase)).Any();
                if (exists) return Results.Json(new { status = "error", message = $"Account with this name ({accountName}) already exists." });
                var existingChar = ObjectRegistry.FilterBy(o => o.IsPc && string.Equals(o.Name, charName, StringComparison.OrdinalIgnoreCase)).Any();
                if (existingChar) return Results.Json(new { status = "error", message = $"Character with this name ({charName}) already exists." });

                var acc = Account.Create(accountName, password);
                ObjectRegistry.AddObject(acc);
                var hero = GameObject.Create(charName, isPc: true, privilege: Privilege.Player);
                acc.AddCharacter(hero);
                hero.Home = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(new Coord(settings.DefaultHome.Area, settings.DefaultHome.X, settings.DefaultHome.Y, settings.DefaultHome.Z));
                ObjectRegistry.AddObject(hero);
                try
                {
                    using var db = new AtherizDbContext(settings.SavePath);
                    db.Database.EnsureCreated();
                    ObjectRegistry.SaveObjects(db);
                }
                catch { }

                return Results.Json(new { status = "ok", message = $"Account '{accountName}' and character '{charName}' created." });
            }
            catch (Exception ex)
            {
                return Results.Json(new { status = "error", message = ex.Message });
            }
        });
    }

    private static string? ValidateAccountName(string name, AtherizSettings s)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Account name must not be empty.";
        if (name.Length > s.MaxAccountNameLength) return $"Account name too long (max {s.MaxAccountNameLength}).";
        if (name.Length < 2) return "Account name too short.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_]+$")) return "Account name must be alphanumeric/underscore.";
        return null;
    }

    private static string? ValidateCharacterName(string name, AtherizSettings s)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Character name must not be empty.";
        if (name.Length > s.MaxCharacterNameLength) return $"Character name too long (max {s.MaxCharacterNameLength}).";
        if (name.Length < 2) return "Character name too short.";
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z0-9_]+$")) return "Character name must be alphanumeric/underscore.";
        return null;
    }

    private static string? ValidatePassword(string pw, AtherizSettings s)
    {
        if (pw.Length < s.MinPasswordLength) return $"Password too short (min {s.MinPasswordLength}).";
        if (pw.Length > s.MaxPasswordLength) return $"Password too long (max {s.MaxPasswordLength}).";
        return null;
    }
}
