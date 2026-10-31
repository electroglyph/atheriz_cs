namespace Atheriz.Server.Cli;

// File trace for a dying daemon child (its console is nulled, so the log is
// the only trace). Installed only when ATHERIZ_DAEMON=1; never throws.
public static class DaemonCrashLog
{
    private static int _installed;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try
            {
                var dir = ResolveSaveDir();
                if (string.IsNullOrWhiteSpace(dir)) return;
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "server.log"),
                    $"{DateTime.UtcNow:O} FATAL unhandled exception: {e.ExceptionObject}{Environment.NewLine}");
            }
            catch { }
        };
    }

    private static string? ResolveSaveDir()
    {
        string? path = null;
        try { path = AtherizSettings.Global.SavePath; }
        catch { }
        if (string.IsNullOrWhiteSpace(path))
            try { path = StopHandler.EffectiveSettingsValue.SavePath; } catch { return null; }
        if (string.IsNullOrWhiteSpace(path)) return null;
        // Never plant a relative save folder outside a game folder: the
        // engine checkout itself is not a game, so there is no crash log to
        // write there.
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(path); }
        catch { return null; }
        return path;
    }
}
