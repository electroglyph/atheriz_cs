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
            UseShellExecute = false,
        };
        // ArgumentList, not string-concatenated Arguments: an arg containing
        // a quote could otherwise break out of its pair and reshape the command.
        psi.ArgumentList.Add("test");
        foreach (var x in a) psi.ArgumentList.Add(x);
        try
        {
            var proc = Process.Start(psi);
            // Unbounded wait is the policy: `atheriz test` is a foreground
            // command that streams the suite to completion. Bounding it would
            // kill long suites mid-run and corrupt their result.
            proc?.WaitForExit();
            return proc?.ExitCode ?? 0;
        }
        catch (Exception ex) { Console.WriteLine($"Failed to run tests: {ex.Message}"); return 1; }
    }
}
