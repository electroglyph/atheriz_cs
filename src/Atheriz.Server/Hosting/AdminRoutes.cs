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
// Auth failures are HTTP 401 with {status:"error"} (AdminAuth); the CLI
// reads data.status. Single guard-result shape shared by the three admin
// endpoints; endpoints keep their own response shapes otherwise.
        static IResult AdminError(string? message) => TypedResults.Json(new AdminResult("error", message));
        static IResult AdminOk(string? message) => TypedResults.Json(new AdminResult("ok", message));

        app.MapPost("/_internal/hot_reload", async (HttpContext ctx) =>
        {
            if (IsBodyTooLarge(ctx))
                return AdminError("Request body too large.");
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
                        try { ServerLifecycle.DoReload(settings); } catch (Exception ex) { AtherizLogger.LogError($"[HotReload] DoReload failed: {ex.Message}"); }
                    }
                    catch (Exception ex)
                    {
                        AtherizLogger.LogError($"[HotReload] PluginReloader failed: {ex.Message}, falling back to DoReload");
                        ServerLifecycle.DoReload(settings);
                        msg = "Reload completed (fallback).";
                    }
                    return msg;
                }
                var work = Task.Run(DoReloadWork);
                var done = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(60))).ConfigureAwait(false);
                if (done != work)
                {
                    AtherizLogger.LogError("[HotReload] Reload exceeded 60s watchdog; continuing in background.");
                    return AdminError("Reload timed out after 60s; still running in background.");
                }
                return AdminOk(await work.ConfigureAwait(false));
            }
            catch (Exception ex)
            {
                return AdminError(ex.Message);
            }
        }).RequireAuthorization(AdminAuthServices.Policy);

        app.MapPost("/_internal/shutdown", (HttpContext ctx, IHostApplicationLifetime lifetime) =>
        {
            if (IsBodyTooLarge(ctx))
                return AdminError("Request body too large.");

            AtherizLogger.LogInformation("Internal shutdown request received. Running shutdown tasks...");

            // single thread-pool hop for shutdown. The watchdog is a
            // pure delay — a Timer, not a pooled thread. The shutdown work
            // runs on one pooled thread (DoShutdown is synchronous; the old
            // shape nested a second hop inside an async lambda).
            var watchdog = new System.Threading.Timer(_ =>
            {
                AtherizLogger.LogError("Shutdown watchdog: forcing exit.");
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
        }).RequireAuthorization(AdminAuthServices.Policy);

        app.MapPost("/_internal/create_account", async (HttpContext ctx) =>
        {
            // Size-capped body read: reject oversized payloads without allocating them.
            using var doc = await ReadCappedJsonBodyAsync(ctx, 64 * 1024).ConfigureAwait(false);
            if (doc is null)
            {
                return AdminError("Invalid JSON body.");
            }
            var request = doc.RootElement.Deserialize<CreateAccountRequest>();
            string? accountName = request?.AccountName;
            string? charName = request?.CharName;
            string? password = request?.Password;
            if (string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(password))
                return AdminError("account_name, char_name and password are required.");

            string? vErr = Atheriz.Core.Commands.UnloggedIn.Validation.ValidateAccountName(accountName, settings)
                ?? Atheriz.Core.Commands.UnloggedIn.Validation.ValidateCharacterName(charName, settings)
                ?? Atheriz.Core.Commands.UnloggedIn.Validation.ValidatePassword(password, settings);
            if (vErr is not null) return AdminError(vErr);

            try
            {
                // Offloaded to the pool with a watchdog: AtCharCreate
                // runs validation, PBKDF2, MoveTo and SaveObjects disk I/O
                // inline — parking a Kestrel worker 100 ms+ per create lets a
                // burst of creates starve legit admin traffic (head-of-line
                // blocking). Same 60 s watchdog shape as hot_reload above.
                string message = await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    using var sw = new StringWriter(sb);
                    ServerEvents.AtCharCreate(accountName, charName, password, sw);
                    var m = sb.ToString().Trim();
                    return string.IsNullOrEmpty(m) ? "Account created." : m;
                }).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
                return AdminOk(message);
            }
            catch (TimeoutException)
            {
                AtherizLogger.LogError("[CreateAccount] Create exceeded 60s watchdog; continuing in background.");
                return AdminError("Create timed out after 60s; still running in background.");
            }
            catch (Exception ex)
            {
                return AdminError(ex.Message);
            }
        }).RequireAuthorization(AdminAuthServices.Policy);
    }

    /// <summary>
    /// Typed create_account body. Property names match the JSON contract.
    /// </summary>
    public sealed record CreateAccountRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("account_name")] string? AccountName,
        [property: System.Text.Json.Serialization.JsonPropertyName("char_name")] string? CharName,
        [property: System.Text.Json.Serialization.JsonPropertyName("password")] string? Password);

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
