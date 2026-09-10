namespace Atheriz.Server.Cli;

public static class NewHandler
{
    public static async Task<bool> HandleNewAsync(string[] a)
    {
        bool overwrite = ArgumentParser.HasFlag(a, "--overwrite", null) || ArgumentParser.HasFlag(a, "--force", null);
        var filtered = a.Where((v, i) =>
        {
            // A trailing bare flag carries no value — keep it (minus
            // consumed-value/prefix/glued shapes, which cannot dangle) so the
            // folder check below rejects it instead of it becoming the folder.
            if (i + 1 >= a.Length)
                return !(i > 0 && (a[i - 1] == "--port" || a[i - 1] == "--telnet-port" || a[i - 1] == "-p" || a[i - 1] == "--host"))
                    && !v.StartsWith("--port=", StringComparison.Ordinal) && !v.StartsWith("--telnet-port=", StringComparison.Ordinal) && !ArgumentParser.IsGluedShortPort(v)
                    && v != "--overwrite" && v != "--force" && v != "--foreground" && v != "-f";
            return !(v == "--port" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "--port") && !v.StartsWith("--port=", StringComparison.Ordinal) &&
            !(v == "--telnet-port" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "--telnet-port") && !v.StartsWith("--telnet-port=", StringComparison.Ordinal) &&
            !(v == "-p" && i + 1 < a.Length) && !(i > 0 && a[i - 1] == "-p") && !ArgumentParser.IsGluedShortPort(v) &&
            v != "--host" && !(i > 0 && a[i - 1] == "--host") && !v.StartsWith("--host=", StringComparison.Ordinal) &&
            v != "--foreground" && v != "-f" &&
            v != "--overwrite" && v != "--force";
        }).ToArray();
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
