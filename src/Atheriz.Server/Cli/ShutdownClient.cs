using System.Net.Security;

namespace Atheriz.Server.Cli;

// Outcome of a graceful-shutdown request. Unreachable (no token / server
// down) lets the caller fall back to signals; AuthRejected (a live server
// refused us) must abort — never escalate a refused request into SIGKILL.
internal enum ShutdownRequestResult { Accepted, Unreachable, AuthRejected }

// One HTTP answer from a /_internal/* admin endpoint. Auth failures are
// HTTP 200 with {status:"error"} (AdminRoutes), so callers must inspect the
// body, not just reachability.
internal sealed record AdminResponse(int StatusCode, string Body)
{
    // Shared status/message extraction for the three admin callers. Each
    // caller passes its own missing-property default ("error" for create,
    // "ok" for reload, "" for shutdown) — unifying the default would change
    // behavior. Invalid JSON throws so each caller keeps its own catch path
    // (Body+return vs "Response:"+fallthrough vs Unreachable).
    internal string? GetStatus(string defaultValue)
    {
        using var doc = JsonDocument.Parse(Body);
        return doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : defaultValue;
    }

    internal string? GetMessage()
    {
        using var doc = JsonDocument.Parse(Body);
        return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : Body;
    }
}

public static class ShutdownClient
{
    // The single HTTP admin client for the stop/reload/create paths.
    // Returns null when there is no token or the server cannot be reached
    // (caller falls back); a non-null response — even {status:"error"} —
    // proves a live server answered, and the caller must NOT escalate
    // (no SIGKILL, no offline DB writes).
    internal static async Task<AdminResponse?> PostAdminAsync(int port, string secretPath, string path, string? jsonPayload, bool tlsOn)
    {
        var tokenFile = FindTokenFile(secretPath, port);
        if (tokenFile is null || !File.Exists(tokenFile)) return null;
        // Shared token-file read (empty flows through as a bearer value here;
        // each caller owns its empty semantics — see AdminToken).
        var token = Infrastructure.AdminToken.TryReadTokenFile(tokenFile);
        if (token is null) return null;
        var url = $"{(tlsOn ? "https" : "http")}://localhost:{port}{path}";
        try
        {
            // Loopback only: the token is a bearer secret, but the peer is
            // always localhost here. Self-signed dev certs cannot chain-verify,
            // so chain/name errors are tolerated ONLY on a loopback host —
            // never blind-trust a non-loopback peer. (Inspect SslPolicyErrors
            // instead of returning true unchecked.)
            using var handler = new HttpClientHandler { ServerCertificateCustomValidationCallback = (req, cert, chain, errors) =>
            {
                if (req.RequestUri is not Uri u) return false;
                bool loopback = u.Host == "localhost" || u.Host == "127.0.0.1" || u.Host == "::1";
                if (errors == SslPolicyErrors.None) return true;
                if (!loopback || cert is null) return false;
                const SslPolicyErrors loopbackTolerated =
                    SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch;
                return (errors & ~loopbackTolerated) == 0;
            } };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Add("X-Admin-Token", token);
            if (jsonPayload is not null) req.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var resp = await client.SendAsync(req).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new AdminResponse((int)resp.StatusCode, body);
        }
        catch { return null; }
    }

    // Shared TLS-flipped retry for the create/reload/shutdown admin paths: on
    // a scheme mismatch (settings say https, server speaks plaintext or vice
    // versa) retry once with the flipped scheme, so a null response never
    // falls through against a LIVE server.
    internal static async Task<AdminResponse?> PostAdminWithTlsFallbackAsync(int port, string secretPath, string path, string? jsonPayload, bool tlsOn)
    {
        var resp = await PostAdminAsync(port, secretPath, path, jsonPayload, tlsOn).ConfigureAwait(false);
        resp ??= await PostAdminAsync(port, secretPath, path, jsonPayload, !tlsOn).ConfigureAwait(false);
        return resp;
    }

    internal static async Task<ShutdownRequestResult> TryRequestShutdownAsync(int port, string secretPath, bool tlsOn)
    {
        Console.WriteLine("Requesting graceful shutdown via internal API...");
        // Third caller of the TLS-flipped retry shape (status-only, default
        // ""): on a scheme mismatch retry once flipped, so a null response
        // never falls through against a LIVE server into signals.
        var resp = await PostAdminWithTlsFallbackAsync(port, secretPath, "/_internal/shutdown", null, tlsOn).ConfigureAwait(false);
        if (resp is null)
        {
            Console.WriteLine("Could not contact server for graceful shutdown (server might be hung or stopped).");
            return ShutdownRequestResult.Unreachable;
        }
        // Auth failures arrive as HTTP 200 + {status:"error"} by route
        // design (AdminRoutes), never as 401/403 — read the body below.
        try
        {
            var status = resp.GetStatus("");
            Console.WriteLine($"Internal shutdown response: {resp.Body}");
            if (status == "ok") { Console.WriteLine("Server has completed shutdown tasks."); return ShutdownRequestResult.Accepted; }
            // status == "error": a live server refused (bad token / non-loopback).
            Console.WriteLine("Server refused the shutdown request; aborting without touching processes.");
            return ShutdownRequestResult.AuthRejected;
        }
        catch { return ShutdownRequestResult.Unreachable; }
    }

    internal static string? FindTokenFile(string secretPath, int port)
    {
        var cand = Path.Combine(secretPath, "admin.token");
        if (File.Exists(cand)) return cand;
        try
        {
            var cur = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 6 && cur is not null; i++) { var p = Path.Combine(cur.FullName, "secret", "admin.token"); if (File.Exists(p)) return p; var p2 = Path.Combine(cur.FullName, "save", "..", "secret", "admin.token"); if (File.Exists(Path.GetFullPath(p2))) return Path.GetFullPath(p2); cur = cur.Parent; }
        }
        catch { }
        try
        {
            if (Infrastructure.PidFile.TryFindPidListeningOnPort(port, out var lpid))
            {
                try
                {
                    var realCwd = new FileInfo($"/proc/{lpid}/cwd").LinkTarget;
                    if (!string.IsNullOrEmpty(realCwd)) { var p3 = Path.Combine(realCwd, "secret", "admin.token"); if (File.Exists(p3)) return p3; }
                }
                catch { }
            }
        }
        catch { }
        // no blind CWD-tree scan — a planted admin.token in a nested
        // directory would hand CLI control to the wrong server. Lookup is
        // scoped: configured secret dir, upward secret/ walk, /proc probe.
        return null;
    }
}
