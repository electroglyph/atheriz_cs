// Port of atheriz/globals/salt.py:35,50,68 chmod try/except OSError and atheriz/atheriz.py:566-568,600-602 POSIX 0o600/0o700 best-effort
namespace Atheriz.Core.Utils;

/// <summary>
/// Port of POSIX <c>chmod 0o600/0o700</c> best-effort helpers.
/// Mirrors <c>try: path.chmod(0o600/0o700) except OSError: pass</c> at
/// <c>atheriz/globals/salt.py:35,50,68</c> and <c>atheriz/atheriz.py:566,600</c>.
/// No Windows ACL usage — POSIX <c>File.SetUnixFileMode</c> via try/catch per AGENTS.
/// </summary>
public static class FsUtil
{
    /// <summary>Best-effort <c>chmod 0o600</c> via <c>File.SetUnixFileMode(UserRead|UserWrite)</c>.</summary>
    public static void TryChmod0600(string path)
    {
        try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); } catch { }
    }

    /// <summary>Best-effort <c>chmod 0o700</c> via <c>File.SetUnixFileMode(UserRead|UserWrite|UserExecute)</c>.</summary>
    public static void TryChmod0700(string path)
    {
        try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); } catch { }
    }

    // Aliases per task spec (TrySet0600/TrySet0700)
    public static void TrySet0600(string path) => TryChmod0600(path);
    public static void TrySet0700(string path) => TryChmod0700(path);
}
