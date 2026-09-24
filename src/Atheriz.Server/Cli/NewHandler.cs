using Atheriz.Server.Hosting;

namespace Atheriz.Server.Cli;

public static class NewHandler
{
    public static async Task<int> NewAsync(string folder, int? port, string? host, int? telnetPort, bool overwrite, bool foreground = true)
    {
        if (!TryCreateFolder(folder, overwrite, out var folderAbs)) return 1;

        if (!foreground) return await DaemonSpawner.SpawnStart(folderAbs, port, host, telnetPort).ConfigureAwait(false);

        // Foreground start resolves the game folder from the process CWD.
        Console.WriteLine($"\nChanging directory to '{folder}'...");
        try { Directory.SetCurrentDirectory(folderAbs); } catch (Exception ex) { Console.Error.WriteLine($"Failed to change directory: {ex.Message}"); return 1; }
        StopHandler.InvalidateEffectiveSettings();
        Console.WriteLine("Starting server...");
        return await ServerHost.RunForegroundAsync(port, host, telnetPort).ConfigureAwait(false);
    }

    public static bool TryCreateFolder(string folder, bool overwrite, out string folderAbs)
    {
        var gameName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(gameName)) gameName = folder;
        folderAbs = Path.GetFullPath(folder);
        return Infrastructure.GameTemplateGenerator.CreateGameFolder(folderAbs, gameName, overwrite);
    }
}
