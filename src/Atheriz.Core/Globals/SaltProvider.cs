using Atheriz.Core.Utils;

namespace Atheriz.Core.Globals;

/// <summary>
/// Port of <c>atheriz/globals/salt.py:get_salt</c>.
/// Global static salt shared by all accounts (intentional wontfix).
/// Uses absolute-path guard matching <c>database_setup.py:66</c>.
/// </summary>
public static class SaltProvider
{
    private static string? _salt;
    private static readonly object _lock = new();

    public static string GetSalt(string secretPath = "secret")
    {
        if (_salt is not null) return _salt;
        lock (_lock)
        {
            if (_salt is not null) return _salt;
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
                _salt = raw;
                return _salt;
            }

            var val = CryptoRandom.UInt64String();
            // Ensure parent exists
            Directory.CreateDirectory(secretPath);
            FsUtil.TryChmod0700(secretPath);
            // atomic create O_EXCL 0o600
            try
            {
                using var fs = new FileStream(saltFile, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var sw = new StreamWriter(fs);
                sw.Write(val);
            }
            catch (IOException) // FileExists
            {
                var raw = File.ReadAllText(saltFile).Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    throw new InvalidOperationException($"Corrupt salt file {saltFile} after concurrent create.");
                _salt = raw;
                return _salt;
            }
            FsUtil.TryChmod0600(saltFile);
            _salt = val;
            return _salt;
        }
    }

    /// <summary>For tests: inject fixed salt without touching disk.</summary>
    public static void SetSaltForTesting(string? salt)
    {
        lock (_lock) _salt = salt;
    }

    public static void Clear() { lock (_lock) _salt = null; }
}
