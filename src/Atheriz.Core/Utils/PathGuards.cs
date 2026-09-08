// Port of atheriz/database_setup.py:66-71 + atheriz/atheriz.py:508,559 guard logic
namespace Atheriz.Core.Utils;

/// <summary>
/// Guards for SAVE_PATH / SECRET_PATH — canonical implementation.
/// Mirrors <c>atheriz/database_setup.py:66</c> and <c>atheriz/atheriz.py:508,559</c>.
/// Server's <c>PathGuards</c> delegates here (Core cannot reference Server).
/// </summary>
public static class PathGuards
{
    /// <summary>
    /// Mirrors <c>atheriz/database_setup.py:66-71</c>:
    /// <c>save_path = Path(settings.SAVE_PATH); if not (save_path.is_absolute() or is_in_game_folder()): raise RuntimeError(...)</c>
    /// </summary>
    public static void GuardSavePath(string savePath)
    {
        if (!Path.IsPathRooted(savePath) && !GameUtils.IsInGameFolder())
            throw new InvalidOperationException(
                $"Cannot determine database path: SAVE_PATH ({savePath}) is not absolute and we're not in a game folder. Run 'atheriz new' or set SAVE_PATH.");
    }

    /// <summary>
    /// Mirrors <c>atheriz/atheriz.py:559-563</c> secret guard.
    /// </summary>
    public static void GuardSecretPath(string secretPath)
    {
        if (!Path.IsPathRooted(secretPath) && !GameUtils.IsInGameFolder())
            throw new InvalidOperationException(
                $"Cannot determine secret path: SECRET_PATH ({secretPath}) is not absolute and we're not in a game folder. Run 'atheriz new' or set SECRET_PATH.");
    }

    /// <summary>
    /// Guard + ensure directory exists with POSIX 0o700 where supported — mirrors <c>atheriz.py:514 + 564-568</c>.
    /// </summary>
    public static void EnsureSaveDirectory(string savePath)
    {
        GuardSavePath(savePath);
        Directory.CreateDirectory(savePath);
        FsUtil.TryChmod0700(savePath);
        ProbeWritable(savePath, "save");
    }

    public static void EnsureSecretDirectory(string secretPath)
    {
        GuardSecretPath(secretPath);
        Directory.CreateDirectory(secretPath);
        FsUtil.TryChmod0700(secretPath);
        ProbeWritable(secretPath, "secret");
    }

    /// <summary>
    /// Fail loud here instead of mid-save/mid-token-write: proves the directory
    /// is actually writable (CreateDirectory succeeds on read-only mounts when
    /// the dir already exists, deferring the failure to first write).
    /// </summary>
    private static void ProbeWritable(string dir, string kind)
    {
        var probe = Path.Combine(dir, ".atheriz_write_probe");
        try
        {
            File.WriteAllText(probe, "w");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            throw new UnauthorizedAccessException(
                $"Game {kind} directory '{dir}' is not writable: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Backwards-compatible wrapper for legacy <c>PathHelpers.EnsureSavePathValid</c> — mirrors same guard with kind param.
    /// </summary>
    public static void EnsureSavePathValid(string savePath, string kind = "database")
    {
        var isAbs = Path.IsPathRooted(savePath);
        if (!isAbs && !GameUtils.IsInGameFolder())
            throw new InvalidOperationException(
                $"Cannot determine {kind} path: SAVE_PATH ({savePath}) is not absolute and we're not in a game folder. Run 'atheriz new' or set SAVE_PATH.");
    }

    public static void EnsureSecretPathValid(string secretPath) => GuardSecretPath(secretPath);

    /// <summary>
    /// Refuses filesystem roots: wiping <c>/</c>, <c>C:\</c> etc. is never a valid game operation.
    /// </summary>
    public static void DenyRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        var normFull = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(normFull, normRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to wipe filesystem root: {path}");
    }

    /// <summary>
    /// Containment for destructive wipes (<c>reset</c>).
    /// The target must be a previously-initialized world (<c>server.pid</c> /
    //  <c>database.sqlite3*</c> markers); anything else needs an explicit
    /// <c>--force</c> inside a game folder. A bare <c>save</c> leaf is NOT
    /// sufficient on its own : <c>new /tmp --overwrite</c> must not
    /// wipe <c>/tmp/save</c> contents. Overwrite scaffolding (<c>new</c>)
    /// performs its own confined save-leaf wipe with explicit operator intent
    /// instead of consulting this world-membership gate.
    /// </summary>
    public static void GuardWipePath(string path, bool force)
    {
        DenyRoot(path);
        var full = Path.GetFullPath(path);
        if (Directory.Exists(full))
        {
            if (File.Exists(Path.Combine(full, "server.pid"))) return;
            foreach (var marker in new[] { "database.sqlite3", "database.sqlite3-wal", "database.sqlite3-shm", "database.sqlite3.journal" })
                if (File.Exists(Path.Combine(full, marker))) return;
        }
        if (force && GameUtils.IsInGameFolder()) return;
        throw new InvalidOperationException(
            $"Refusing to wipe '{path}': not an initialized world (expected world markers). Pass --force inside a game folder to override.");
    }
}
