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
            if (a[i] == longFlag || (shortFlag is not null && a[i] == shortFlag))
                // Missing value is invalid (argparse: "expected one argument"),
                // not a silent fall back to the default port.
                return i + 1 < a.Length ? a[i + 1] : string.Empty;
            // Glued short form: -p1234 / -p=1234 (argparse allows -p1234).
            // single-char values count — "-p1" (len 3) must parse.
            if (shortFlag is not null && a[i].Length > shortFlag.Length && a[i].StartsWith(shortFlag, StringComparison.Ordinal))
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
        => ParseIntOption(a, "--port", "-p", PortPrefix);

    // Port of argparse type=int failure for --port (exit 2): raw value present but not an int.
    public static string? InvalidPortValue(string[] a)
        => InvalidIntOption(a, "--port", "-p", PortPrefix);

    public static string? InvalidTelnetPortValue(string[] a)
        => InvalidIntOption(a, "--telnet-port", null, TelnetPortPrefix);

    public static int? ParseTelnetPort(string[] a)
    {
        var p = ParseIntOption(a, "--telnet-port", null, TelnetPortPrefix);
        if (p is not null) return p;
        var env = Environment.GetEnvironmentVariable("ATHERIZ_TELNET_PORT") ?? Environment.GetEnvironmentVariable("Atheriz__TelnetPort");
        if (int.TryParse(env, out var ep)) return ep;
        return null;
    }

    // Shared int-option core for the Parse*/Invalid* port pairs, which differ
    // only in option name (+ the telnet env fallback, kept at its call site).
    // Public methods keep their names and messages byte-identical.
    private static int? ParseIntOption(string[] a, string longFlag, string? shortFlag, string prefix)
    {
        var v = GetOptionValue(a, longFlag, shortFlag, prefix);
        if (v is not null && int.TryParse(v, out var p)) return p;
        return null;
    }

    private static string? InvalidIntOption(string[] a, string longFlag, string? shortFlag, string prefix)
    {
        var v = GetOptionValue(a, longFlag, shortFlag, prefix);
        if (v is not null && !int.TryParse(v, out _)) return v;
        return null;
    }

    public static string? ParseHost(string[] a)
    {
        var v = GetOptionValue(a, "--host", null, HostPrefix);
        return v;
    }

    // Port of argparse "expected one argument" for --host (exit 2): the flag
    // is present but carries no value (bare `--host` / `--host=`). Without
    // this the empty string leaks into per-command parsing (foreground binds
    // WebserverInterface="", create eats the flag as a positional).
    public static bool HasBareHost(string[] a)
    {
        var v = GetOptionValue(a, "--host", null, HostPrefix);
        return v is not null && v.Length == 0;
    }

    public static bool HasFlag(string[] a, string longFlag, string? shortFlag = null)
        => a.Contains(longFlag, StringComparer.Ordinal) || (shortFlag is not null && a.Contains(shortFlag, StringComparer.Ordinal));

    // Pure OR over HasFlag — identical short-circuit in flag order. Collapses
    // the force-like flag sets (new --overwrite/--force, reset
    // --force/-f/--yes/-y) to one call each.
    public static bool HasAnyFlag(string[] a, params string[] flags)
    {
        foreach (var f in flags)
            if (a.Contains(f, StringComparer.Ordinal)) return true;
        return false;
    }

    // Glued short-port form (-p1234 / -p=1234), for stripping port flags out of
    // positional filters in the create/new handlers.
    internal static bool IsGluedShortPort(string v)
        => v.Length > 2 && v[0] == '-' && v[1] == 'p' && (v[2] == '=' || char.IsDigit(v[2]));

    // Shared positional filter for the create/new handlers: strips bare,
    // consumed-value, --opt=value prefix and glued (-p1234) shapes of the
    // value-taking options. The create path strips the port family only;
    // new additionally strips --telnet-port/--host/--overwrite/--force/
    // --foreground — one core so a new glued shape cannot be learned by one
    // site and missed by the other. The filter produces no output; only
    // positional passthrough equality is pinned.
    internal static string[] StripPortOptions(string[] a) => StripOptions(a, full: false);
    internal static string[] StripKnownOptions(string[] a) => StripOptions(a, full: true);

    private static string[] StripOptions(string[] a, bool full)
    {
        return a.Where((v, i) =>
        {
            bool prevIsPort = i > 0 && a[i - 1] == "--port";
            bool prevIsShortPort = i > 0 && a[i - 1] == "-p";
            bool isPortValue = v == "--port" || prevIsPort || v.StartsWith(PortPrefix, StringComparison.Ordinal)
                || v == "-p" || prevIsShortPort || IsGluedShortPort(v);
            // A trailing bare flag carries no value — keep it (minus
            // consumed-value/prefix/glued shapes, which cannot dangle) so
            // the missing-value path reports it instead of a neighbor.
            bool trailing = i + 1 >= a.Length;
            if (!full)
            {
                if (trailing)
                    return !prevIsPort && !prevIsShortPort && !v.StartsWith(PortPrefix, StringComparison.Ordinal) && !IsGluedShortPort(v);
                return !isPortValue;
            }
            bool prevIsTelnet = i > 0 && a[i - 1] == "--telnet-port";
            bool prevIsHost = i > 0 && a[i - 1] == "--host";
            bool isTelnetValue = (v == "--telnet-port" && !trailing) || prevIsTelnet || v.StartsWith(TelnetPortPrefix, StringComparison.Ordinal);
            bool isShortPortValue = (v == "-p" && !trailing) || prevIsShortPort || IsGluedShortPort(v);
            bool isPortLongValue = (v == "--port" && !trailing) || prevIsPort || v.StartsWith(PortPrefix, StringComparison.Ordinal);
            bool isHostValue = v == "--host" || prevIsHost || v.StartsWith(HostPrefix, StringComparison.Ordinal);
            if (trailing)
                return !prevIsPort && !prevIsTelnet && !prevIsShortPort && !prevIsHost
                    && !v.StartsWith(PortPrefix, StringComparison.Ordinal) && !v.StartsWith(TelnetPortPrefix, StringComparison.Ordinal) && !IsGluedShortPort(v)
                    && v != "--overwrite" && v != "--force" && v != "--foreground" && v != "-f";
            return !isPortLongValue && !isTelnetValue && !isShortPortValue && !isHostValue
                && v != "--foreground" && v != "-f" && v != "--overwrite" && v != "--force";
        }).ToArray();
    }
}
