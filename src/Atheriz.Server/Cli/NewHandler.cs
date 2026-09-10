namespace Atheriz.Server.Cli;

public static class NewHandler
{
    public static async Task<bool> HandleNewAsync(string[] a)
    {
        bool overwrite = ArgumentParser.HasAnyFlag(a, "--overwrite", "--force");
        var filtered = ArgumentParser.StripKnownOptions(a);
        if (filtered.Length < 1 || filtered[0].StartsWith("-", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: atheriz new <foldername> [--port N] [--host HOST] [--foreground|-f] [--overwrite|--force]");
            Console.Error.WriteLine("atheriz: error: the following arguments are required: foldername");
            throw new CliExitException(2);
        }
        var folder = filtered[0];
        var gameName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(gameName)) gameName = folder;
        var folderAbs = Path.GetFullPath(folder);
        if (!Infrastructure.GameTemplateGenerator.CreateGameFolder(folderAbs, gameName, overwrite)) return false;

        bool foreground = ArgumentParser.HasFlag(a, "--foreground", "-f");
        if (foreground)
        {
            // Foreground start resolves the game folder from the process CWD,
            // so change directory only on this path. The daemon path passes
            // folderAbs as the child working directory instead and must not
            // mutate this process (a library-visible global).
            Console.WriteLine($"\nChanging directory to '{folder}'...");
            try { Directory.SetCurrentDirectory(folderAbs); } catch (Exception ex) { Console.Error.WriteLine($"Failed to change directory: {ex.Message}"); return false; }
            Console.WriteLine("Starting server...");
            return true;
        }
        else
        {
            Console.WriteLine("Starting server...");
            await DaemonSpawner.SpawnDaemonAsync(a, folderAbs).ConfigureAwait(false);
            return false;
        }
    }
}
