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
    }

    public static void EnsureSecretDirectory(string secretPath)
    {
        GuardSecretPath(secretPath);
        Directory.CreateDirectory(secretPath);
        FsUtil.TryChmod0700(secretPath);
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
}
