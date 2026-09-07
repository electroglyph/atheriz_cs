namespace Atheriz.Server.Cli;

public static class ArgumentParser
{
    private const string PortPrefix = "--port=";
    private const string TelnetPortPrefix = "--telnet-port=";
    private const string HostPrefix = "--host=";

    private static string? GetOptionValue(string[] a, string longFlag, string? shortFlag, string prefix)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == longFlag || (shortFlag != null && a[i] == shortFlag))
                // Missing value is invalid (argparse: "expected one argument"),
                // not a silent fall back to the default port.
                return i + 1 < a.Length ? a[i + 1] : string.Empty;
            // Glued short form: -p1234 / -p=1234 (argparse allows -p1234).
            if (shortFlag != null && a[i].Length > shortFlag.Length + 1 && a[i].StartsWith(shortFlag, StringComparison.Ordinal))
            {
                var rest = a[i].Substring(shortFlag.Length);
                if (rest.StartsWith("=", StringComparison.Ordinal)) rest = rest.Substring(1);
                return rest;
            }
        }
        foreach (var s in a)
        {
            if (s.StartsWith(prefix, StringComparison.Ordinal))
                return s.Substring(prefix.Length);
        }
        return null;
    }

    public static int? ParsePort(string[] a)
    {
        var v = GetOptionValue(a, "--port", "-p", PortPrefix);
        if (v != null && int.TryParse(v, out var p)) return p;
        return null;
    }

    // Port of argparse type=int failure for --port (exit 2): raw value present but not an int.
    public static string? InvalidPortValue(string[] a)
    {
        var v = GetOptionValue(a, "--port", "-p", PortPrefix);
        if (v != null && !int.TryParse(v, out _)) return v;
        return null;
    }

    public static string? InvalidTelnetPortValue(string[] a)
    {
        var v = GetOptionValue(a, "--telnet-port", null, TelnetPortPrefix);
        if (v != null && !int.TryParse(v, out _)) return v;
        return null;
    }

    public static int? ParseTelnetPort(string[] a)
    {
        var v = GetOptionValue(a, "--telnet-port", null, TelnetPortPrefix);
        if (v != null && int.TryParse(v, out var p)) return p;
        var env = Environment.GetEnvironmentVariable("ATHERIZ_TELNET_PORT") ?? Environment.GetEnvironmentVariable("Atheriz__TelnetPort");
        if (int.TryParse(env, out var ep)) return ep;
        return null;
    }

    public static string? ParseHost(string[] a)
    {
        var v = GetOptionValue(a, "--host", null, HostPrefix);
        return v;
    }

    public static bool HasFlag(string[] a, string longFlag, string? shortFlag = null)
        => a.Contains(longFlag, StringComparer.Ordinal) || (shortFlag != null && a.Contains(shortFlag, StringComparer.Ordinal));

    // Glued short-port form (-p1234 / -p=1234), for stripping port flags out of
    // positional filters in the create/new handlers.
    internal static bool IsGluedShortPort(string v)
        => v.Length > 2 && v[0] == '-' && v[1] == 'p' && (v[2] == '=' || char.IsDigit(v[2]));
}
