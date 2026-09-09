
namespace Atheriz.Core.Globals;

/// <summary>
/// Port of <c>atheriz/globals/salt.py:get_salt</c>.
/// Global static salt shared by all accounts (intentional wontfix).
/// Uses absolute-path guard matching <c>database_setup.py:66</c>.
/// </summary>
public static class SaltProvider
{
    // Default-invocation cache: kept as a plain static string slot because the
    // test harness pins it via reflection (save/clear/restore `_salt`, plus a
    // wontfix marker asserting the shared static field). Explicit-path calls
    // bypass it and use the per-path dict below: a single global slot would
    // serve pathA's salt for a later GetSalt(pathB) (Python has no such
    // hazard — it reads one global SECRET_PATH).
    private static string? _salt;
    private static readonly Dictionary<string, string> _salts = new(StringComparer.Ordinal);
    private static readonly object _lock = new();

    // Full-path keying for explicit arguments; the default invocation uses a
    // fixed key (single static salt is an intentional wontfix).
    private const string DefaultSaltKey = "secret";

    public static string GetSalt(string secretPath = DefaultSaltKey)
    {
        // Equality with the default identifies the default invocation (the
        // production Account path); any explicit argument is path-keyed.
        bool isDefault = secretPath == DefaultSaltKey;
        string key = isDefault ? DefaultSaltKey : Path.GetFullPath(secretPath);
        lock (_lock)
        {
            if (isDefault)
            {
                if (_salt is not null) return _salt;
            }
            else if (_salts.TryGetValue(key, out var cached)) return cached;
        }
        // RNG runs outside the global lock (RNG is thread-safe;
        // holding _lock over it serializes all salt callers for no reason).
        var preVal = CryptoRandom.UInt64String();
        lock (_lock)
        {
            if (isDefault)
            {
                if (_salt is not null) return _salt;
            }
            else if (_salts.TryGetValue(key, out var cached)) return cached;
            var isAbs = Path.IsPathRooted(secretPath);
            if (!isAbs && !GameUtils.IsInGameFolder())
                throw new InvalidOperationException(
                    $"Cannot determine salt: SECRET_PATH ({secretPath}) is not absolute and we're not in a game folder. Run 'atheriz new' or set SECRET_PATH.");

            var saltFile = Path.Combine(secretPath, "salt.txt");
            if (File.Exists(saltFile))
            {
                FsUtil.TryChmod0600(saltFile);
                var raw = File.ReadAllText(saltFile).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    throw new InvalidOperationException($"Corrupt salt file {saltFile}: empty/whitespace. Restore secret/salt.txt from backup.");
                Store(key, isDefault, raw);
                return raw;
            }

            var val = preVal;
            // Ensure parent exists
            Directory.CreateDirectory(secretPath);
            FsUtil.TryChmod0700(secretPath);
            // atomic create O_EXCL 0o600
            try
            {
                using var fs = new FileStream(saltFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs);
                sw.Write(val);
                // fsync before the file becomes the live salt: a crash between
                // write and OS flush must not leave a truncated salt behind.
                sw.Flush();
                fs.Flush(true);
            }
            catch (IOException) // FileExists
            {
                var raw = File.ReadAllText(saltFile).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    throw new InvalidOperationException($"Corrupt salt file {saltFile} after concurrent create.");
                Store(key, isDefault, raw);
                return raw;
            }
            catch (UnauthorizedAccessException)
            {
                // Port of salt.py:65-66 except OSError fallback: a non-race OS
                // error (permissions/FS) falls back to a plain write rather
                // than propagating. Read any peer-persisted salt
                // BEFORE overwriting — a concurrent process may have created
                // the file between our failed O_EXCL create and now, and
                // overwriting it would fork the salt. Then verify: a
                // swallowed failed write would cache a salt that is not on
                // disk, silently invalidating every password hash on restart.
                // Re-read (or throw) instead of trusting the write.
                var peer = TryReadSalt(saltFile);
                if (peer is not null) { Store(key, isDefault, peer); return peer; }
                try { File.WriteAllText(saltFile, val); } catch (Exception) { }
                var back = TryReadSalt(saltFile);
                if (back is null || !CryptographicEquals(back, val))
                    throw new InvalidOperationException($"Salt fallback write to {saltFile} could not be verified; refusing to cache an unverified salt.");
                Store(key, isDefault, val);
                return val;
            }
            FsUtil.TryChmod0600(saltFile);
            Store(key, isDefault, val);
            return val;
        }
    }

    private static void Store(string key, bool isDefault, string val)
    {
        if (isDefault) _salt = val;
        else _salts[key] = val;
    }

    private static string? TryReadSalt(string saltFile)
    {
        try
        {
            var raw = File.ReadAllText(saltFile).Trim();
            return string.IsNullOrWhiteSpace(raw) ? null : raw;
        }
        catch (Exception) { return null; }
    }

    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        int diff = 0;
        for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }

    /// <summary>For tests: inject fixed salt without touching disk.</summary>
    public static void SetSalt(string? salt)
    {
        lock (_lock) _salt = salt;
    }

    public static void Clear() { lock (_lock) { _salt = null; _salts.Clear(); } }
}
