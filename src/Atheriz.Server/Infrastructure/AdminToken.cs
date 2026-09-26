using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Mirrors:
///   <c>token = secrets.token_hex(32)</c> (32 bytes → 64 hex chars)
///   <c>secret_path.mkdir(parents=True, exist_ok=True); secret_path.chmod(0o700)</c>
///   <c>fd = os.open(token_file, O_WRONLY|O_CREAT|O_EXCL, 0o600); fdopen write</c>
///   <c>token_file.chmod(0o600)</c>
/// and <c>_check_admin:50-63 hmac.compare_digest</c>.
/// </summary>
public static class AdminToken
{
    private const string TokenFileName = "admin.token";

    // Shared token-file read for the five admin-token sites. Returns the raw
    // trimmed content, or null when missing/unreadable. Empty is NOT mapped
    // to null here — each site keeps its own empty semantics.
    internal static string? TryReadTokenFile(string tokenFile)
    {
        try { return File.ReadAllText(tokenFile, Encoding.UTF8).Trim(); }
        catch { return null; }
    }

    /// <summary>
    /// Ensures the secret directory exists (guard + 0o700) and returns the admin token.
    /// If token file exists, reads it; otherwise atomically creates it with 0o600.
    /// Mirrors <c>atheriz/atheriz.py:557-602</c>.
    /// </summary>
    public static string EnsureToken(string secretPath)
    {
        // Guard — atheriz.py:559-563 (Core; Server wrapper deleted as redundant)
        Atheriz.Core.Utils.PathGuards.GuardSecretPath(secretPath);
        Atheriz.Core.Utils.PathGuards.EnsureSecretDirectory(secretPath);

        var tokenFile = Path.Combine(secretPath, TokenFileName);

        // If exists, read — similar to reading after creation.
        // A zero-byte file is poison: a crash between CreateNew and
        // Flush leaves it permanently wedged (every future call collides on
        // CreateNew, re-reads empty, throws). Tokens are never empty, so
        // after a bounded wait for a concurrent writer, delete and
        // regenerate instead of throwing forever.
        if (File.Exists(tokenFile))
        {
            var existing = TryReadTokenFile(tokenFile);
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }
            if (existing is not null)
            {
                // Bounded wait for a concurrent writer to finish flushing:
                // spin until the file reads non-empty or ~100ms elapse, then
                // take one final read (no Thread.Sleep — hygiene rule).
                if (string.IsNullOrEmpty(existing)
                    && SpinWait.SpinUntil(() => !string.IsNullOrEmpty(TryReadTokenFile(tokenFile)), TimeSpan.FromMilliseconds(100)))
                    existing = TryReadTokenFile(tokenFile);
                if (!string.IsNullOrEmpty(existing)) return existing;
                try { File.Delete(tokenFile); }
                catch (Exception logEx) { Atheriz.Core.AtherizLogger.LogDebug("Suppressed AdminToken poison cleanup: " + logEx.Message, "AdminToken"); }
            }
        }

        // Generate token — mirrors secrets.token_hex(32) at atheriz.py:557
        var token = CryptoRandom.HexToken(32); // 64 hex

        // Atomic create with FileMode.CreateNew mirroring os.open O_EXCL 0o600 — atheriz.py:572
        try
        {
            using var fs = new FileStream(tokenFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var data = Encoding.UTF8.GetBytes(token);
            fs.Write(data, 0, data.Length);
            fs.Flush(true); // fsync before return — token must survive a crash
            // single chmod at creation (the old fallthrough duplicate is gone).
            FsUtil.TryChmod0600(tokenFile);
            return token;
        }
        catch (IOException) when (File.Exists(tokenFile))
        {
            // Race: another process created it — read back the winner, never truncate
            // a valid token (truncating here would DoS the running server's token).
            // Bounded wait: the winner may still be mid-write (empty-read).
            string? raced = null;
            SpinWait.SpinUntil(() => !string.IsNullOrEmpty(raced = TryReadTokenFile(tokenFile)), TimeSpan.FromMilliseconds(100));
            if (!string.IsNullOrEmpty(raced)) return raced;
            var late = TryReadTokenFile(tokenFile);
            if (!string.IsNullOrEmpty(late)) return late;
            throw new InvalidOperationException($"Admin token file already exists at {tokenFile} but could not be read.");
        }
    }

    /// <summary>
    /// Validates a provided token against expected using constant-time compare.
    /// Mirrors <c>hmac.compare_digest((token or "").encode(), expected_token.encode())</c> at atheriz.py:61.
    /// Blank on either side never validates: hashing alone would not close
    /// that hole (<c>SHA256("") == SHA256("")</c>), so blank is rejected
    /// first and the hash comparison then runs over fixed 32-byte digests,
    /// which is also what makes the compare constant-time across lengths
    /// (audit 10 findings 1 and 14).
    /// </summary>
    public static bool ValidateToken(string? provided, string expected)
    {
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(expected))
            return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Reads the token from file if present, else returns null.
    /// </summary>
    public static string? ReadToken(string secretPath)
    {
        var tokenFile = Path.Combine(secretPath, TokenFileName);
        if (!File.Exists(tokenFile)) return null;
        return TryReadTokenFile(tokenFile);
    }

    /// <summary>
    /// Deletes the token file — mirrors <c>atheriz/atheriz.py:683-685 token_file.unlink()</c> on shutdown.
    /// </summary>
    public static void DeleteToken(string secretPath)
    {
        var tokenFile = Path.Combine(secretPath, TokenFileName);
        try { if (File.Exists(tokenFile)) File.Delete(tokenFile); } catch { }
    }

    /// <summary>
    /// Checks admin request — mirrors <c>atheriz/atheriz.py:50-63 _check_admin</c>.
    /// Returns null if allowed, error string otherwise.
    /// Caller should check RemoteIp loopback + FixedTimeEquals.
    /// This helper works with HttpContext.
    /// the 64B token file is re-read per request (no mtime/length
    /// cache — a read of 64 bytes is cheaper than cache-coherence doubt).
    /// </summary>

    public static string? CheckAdmin(string secretPath, string? remoteIp, string? providedToken, string action)
    {
        if (!IsLoopbackIp(remoteIp))
            return $"Remote {action} not allowed.";

        var tokenFile = Path.Combine(secretPath, TokenFileName);
        if (!File.Exists(tokenFile))
            return "Token file not found.";
        var expected = TryReadTokenFile(tokenFile);
        if (string.IsNullOrWhiteSpace(expected))
            return "Token file not found.";

        if (!ValidateToken(providedToken, expected))
            return "Invalid token.";
        return null;
    }

    /// <summary>
    /// Loopback check covering the whole <c>127/8</c> range plus IPv4-mapped forms
    /// (the old string list missed <c>127.0.0.2</c>, <c>::ffff:7f00:1</c>, etc.).
    /// </summary>
    internal static bool IsLoopbackIp(string? remoteIp)
    {
        if (remoteIp is null) return false;
        if (!System.Net.IPAddress.TryParse(remoteIp, out var ip)) return false;
        if (System.Net.IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv4MappedToIPv6) { var v4 = ip.MapToIPv4(); if (System.Net.IPAddress.IsLoopback(v4) || v4.GetAddressBytes()[0] == 127) return true; }
        // Whole 127/8 (IsLoopback is exact-match only: 127.0.0.1 / ::1).
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && ip.GetAddressBytes()[0] == 127) return true;
        return false;
    }
}
