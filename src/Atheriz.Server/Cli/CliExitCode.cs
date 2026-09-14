using System.Threading;

namespace Atheriz.Server.Cli;

// Process exit-code signal for CLI handlers. Handlers run in-process (and
// under tests), so they never call Environment.Exit themselves — every
// terminal path records 0 (success) or 1 (failure/abort) here, and the host
// entry point (Program.cs) exits the process with the recorded code. A
// dedicated static (not Environment.ExitCode) so handler tests probing this
// never pollute the test runner's own exit code.
public static class CliExitCode
{
    private static int _code;
    public static int Code => Volatile.Read(ref _code);
    public static void Set(int code) => Volatile.Write(ref _code, code);
}
