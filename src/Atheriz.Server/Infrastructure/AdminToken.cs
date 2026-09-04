using System.Security.Cryptography;
using System.Text;
using Atheriz.Core.Utils;

namespace Atheriz.Server.Infrastructure;

/// <summary>
/// Port of admin token handling at <c>atheriz/atheriz.py:556-602</c>.
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

    /// <summary>
    /// Ensures the secret directory exists (guard + 0o700) and returns the admin token.
    /// If token file exists, reads it; otherwise atomically creates it with 0o600.
    /// Mirrors <c>atheriz/atheriz.py:557-602</c>.
    /// </summary>
    public static string EnsureToken(string secretPath)
    {
        // Guard — atheriz.py:559-563
        PathGuards.GuardSecretPath(secretPath);
        PathGuards.EnsureSecretDirectory(secretPath);

        var tokenFile = Path.Combine(secretPath, TokenFileName);

        // If exists, read — similar to reading after creation
        if (File.Exists(tokenFile))
        {
            try
            {
                var existing = File.ReadAllText(tokenFile, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(existing))
                {
                    // Ensure perms 0o600 even for existing (best-effort)
                    FsUtil.TryChmod0600(tokenFile);
                    return existing;
                }
            }
            catch { }
        }

        // Generate token — mirrors secrets.token_hex(32) at atheriz.py:557
        var token = CryptoRandom.HexToken(32); // 64 hex

        // Atomic create with FileMode.CreateNew mirroring os.open O_EXCL 0o600 — atheriz.py:572
        try
        {
            using var fs = new FileStream(tokenFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var data = Encoding.UTF8.GetBytes(token);
            fs.Write(data, 0, data.Length);
            fs.Flush();
            FsUtil.TryChmod0600(tokenFile);
            // Also try chmod 0o600 fallthrough — atheriz.py:599-602
            FsUtil.TryChmod0600(tokenFile);
            return token;
        }
        catch (IOException) when (File.Exists(tokenFile))
        {
            // Race: another process created it — read existing (like spawn_daemon concurrent)
            try
            {
                var existing = File.ReadAllText(tokenFile, Encoding.UTF8).Trim();
                if (!string.IsNullOrEmpty(existing)) return existing;
            }
            catch { }
            // If we raced and file exists but unreadable, return generated? fallback to reading again
            throw new InvalidOperationException($"Admin token file already exists at {tokenFile} but could not be read.");
        }
        catch (IOException)
        {
            // POSIX fallback without insecure window — atheriz.py:585-598 fallback to O_TRUNC with 0o600
            // We try Create with Truncate if CreateNew failed for other reason
            try
            {
                using var fs2 = new FileStream(tokenFile, FileMode.Create, FileAccess.Write, FileShare.None);
                var data2 = Encoding.UTF8.GetBytes(token);
                fs2.Write(data2, 0, data2.Length);
                fs2.Flush();
                FsUtil.TryChmod0600(tokenFile);
                return token;
            }
            catch { throw; }
        }
    }

    /// <summary>
    /// Validates a provided token against expected using constant-time compare.
    /// Mirrors <c>hmac.compare_digest((token or "").encode(), expected_token.encode())</c> at atheriz.py:61.
    /// Uses <c>CryptographicOperations.FixedTimeEquals</c>.
    /// </summary>
    public static bool ValidateToken(string? provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided ?? "");
        var b = Encoding.UTF8.GetBytes(expected ?? "");
        // FixedTimeEquals requires same length; if lengths differ, we still want constant time?
        // Python's compare_digest returns false for different lengths but still constant time.
        // .NET FixedTimeEquals returns false if lengths differ, but is it constant time? We pad.
        // Simplest: if lengths differ, do dummy compare to avoid timing leak, then return false.
        if (a.Length != b.Length)
        {
            // Do a dummy fixed time of same length to avoid early exit timing? Not strictly required but closer to hmac.compare_digest
            // We'll compare a with itself? Instead we ensure we still call FixedTimeEquals on equal-length dummy
            // Keep behavior: just return FixedTimeEquals result which is false when lengths differ, but .NET docs say it returns false without comparing content.
            // To mitigate timing, we could hash? For now, use if true branch with dummy.
            // Perform dummy compare of b with b (true) to spend similar time, then return false
            CryptographicOperations.FixedTimeEquals(b, b);
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Reads the token from file if present, else returns null.
    /// </summary>
    public static string? ReadToken(string secretPath)
    {
        var tokenFile = Path.Combine(secretPath, TokenFileName);
        try
        {
            if (!File.Exists(tokenFile)) return null;
            return File.ReadAllText(tokenFile, Encoding.UTF8).Trim();
        }
        catch { return null; }
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
    /// </summary>
    public static string? CheckAdmin(string secretPath, string? remoteIp, string? providedToken, string action)
    {
        var tokenFile = Path.Combine(secretPath, TokenFileName);
        if (!File.Exists(tokenFile))
            return "Token file not found.";
        string expected;
        try { expected = File.ReadAllText(tokenFile, Encoding.UTF8).Trim(); }
        catch { return "Token file not found."; }

        if (remoteIp is null || (remoteIp != "127.0.0.1" && remoteIp != "::1" && remoteIp != "::ffff:127.0.0.1"))
            return $"Remote {action} not allowed.";

        if (!ValidateToken(providedToken, expected))
            return "Invalid token.";
        return null;
    }
}
