using System.Diagnostics;

namespace Atheriz.Server.Cli;

public static class TestHandler
{
    // Port of atheriz.py test: leading 'core' selects the core suite (here: the solution tests).
    // Returns the process exit code; the host entry point applies it. Never Environment.Exit here.
    public static int HandleTest(string[] a)
    {
        if (a.Length > 0 && a[0] == "core") a = a.Skip(1).ToArray();
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test " + string.Join(" ", a.Select(x => $"\"{x}\"")),
            UseShellExecute = false,
        };
        try
        {
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            return proc?.ExitCode ?? 0;
        }
        catch (Exception ex) { Console.WriteLine($"Failed to run tests: {ex.Message}"); return 1; }
    }
}
