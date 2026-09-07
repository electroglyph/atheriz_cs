namespace Atheriz.Server.Cli;

// Thrown by CLI handlers for usage errors so the host entry point
// (Program.cs) can translate them into a process exit code. Library code
// under Cli/ must never call Environment.Exit itself.
public sealed class CliExitException : Exception
{
    public int ExitCode { get; }
    public CliExitException(int exitCode) : base($"CLI exit {exitCode}") { ExitCode = exitCode; }
}
