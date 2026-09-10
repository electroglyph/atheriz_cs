using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Commands;

/// <summary>
/// Faithful port of <c>atheriz/inputfuncs.py:dispatch_loggedin / _resolve_unloggedin</c>.
/// </summary>
public static class CommandDispatcher
{
    private static readonly string[] NoAliasCommands = ["n", "s", "e", "w", "u", "d"];

    // Hot-path parse (ParseRaw runs on every dispatched command): one shared
    // whitespace table instead of per-call ToCharArray/collection allocations.
    private static readonly char[] WhitespaceChars = [' ', '\t', '\r', '\n'];

    private static AtherizSettings _settings = new();
    public static void SetSettings(AtherizSettings s) => _settings = s;
    private static AsyncThreadPool? _pool;
    public static void SetThreadPool(AsyncThreadPool pool) => _pool = pool;

    // Lag gate hook — set by GrottoLagGate.Install without reflection; mirrors Python monkey-patch
    // F010 note: BOTH this and Command.GlobalLagCheck point at the same predicate (GrottoLagGate.CheckLag)
    // by design, not accident — this check runs at dispatch time (pre-queue), the Execute wrapper re-checks
    // at run time after queue wait (lag may accrue in between). Idempotent boolean, keep both.
    public static Func<IMessageTarget, bool>? LagCheck { get; set; }

    // for tests: expose last dispatch result
    public sealed record Job(Action<IMessageTarget, object?> Func, IMessageTarget Caller, object? Args);

    // F010: shared raw-line parse (verbatim inputfuncs.py: stripped/split(None,1)/lower).
    // Returns null for null/empty/whitespace input (both callers return null there).
    internal sealed record ParsedInput(string Stripped, string RawCmdKey, string CmdArgs, string MatchedAlias);

    internal static ParsedInput? ParseRaw(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var stripped = text.Trim(WhitespaceChars);
        if (string.IsNullOrEmpty(stripped)) return null;
        // preserve split None logic: first token is raw_cmd_key lower
        int firstSpace = stripped.IndexOfAny(WhitespaceChars);
        string rawCmdKey;
        string cmdArgs;
        if (firstSpace < 0) { rawCmdKey = stripped.ToLowerInvariant(); cmdArgs = ""; }
        else { rawCmdKey = stripped[..firstSpace].ToLowerInvariant(); cmdArgs = stripped[(firstSpace + 1)..].TrimStart(WhitespaceChars); }
        return new ParsedInput(stripped, rawCmdKey, cmdArgs, rawCmdKey);
    }

    // F010: shared auto-alias prefix scan (sorted keys, ignored-keys skip).
    // socialsFallback=true reproduces the Q5 two-pass rule (non-socials first, social fallback);
    // false is the plain scan for cmdsets without socials (unlogged-in path).
    internal static (Command? cmd, string matchedAlias) AutoAlias(CmdSet cmdset, string rawCmdKey, bool socialsFallback)
    {
        Command? socialFallback = null;
        string socialFallbackKey = "";
        foreach (var key in cmdset.GetKeys().OrderBy(k => k, StringComparer.Ordinal))
        {
            if (_settings.AutoAliasIgnoredKeys.Contains(key)) continue;
            // case-insensitive prefix. Input is lowercased but
            // registered keys may be mixed-case (channel/exit SetKey);
            // Ordinal never matched those. Dict lookups are already
            // OrdinalIgnoreCase, so this aligns the scan with Get.
            if (key.StartsWith(rawCmdKey, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = cmdset.Get(key);
                if (socialsFallback && candidate is LoggedIn.SocialsCommand)
                {
                    socialFallback ??= candidate;
                    if (socialFallbackKey == "") socialFallbackKey = key;
                    continue;
                }
                return (candidate, key);
            }
        }
        if (socialsFallback && socialFallback is not null) return (socialFallback, socialFallbackKey);
        return (null, rawCmdKey);
    }

    // Tail of the resolution chain, shared by the normal path and the glued
    // path: location/inventory verbs first, then the location's own external
    // set. Kept tail-only on purpose — the internal-first/global-second head
    // above differs per path and must not be reordered.
    private static Command? TryResolveLocal(GameObject puppet, string key)
    {
        foreach (var set in CommandHelpers.LocalVerbSets(puppet))
        {
            Command? found = set.Get(key);
            if (found is not null) return found;
        }
        GameObject? loc = puppet.ResolveLocationObject();
        if (loc is not null && loc.ExternalCmdSet is not null) return loc.ExternalCmdSet.Get(key);
        return null;
    }

    /// <summary>
    /// Mirrors <c>dispatch_loggedin(puppet, text, immediate)</c> (inputfuncs.py:88).
    /// If <paramref name="immediate"/> is false, queues on threadpool and returns null.
    /// </summary>
    public static Job? DispatchLoggedIn(GameObject puppet, string text, bool immediate = false)
    {
        var parsed = ParseRaw(text);
        if (parsed is null) return null;
        var stripped = parsed.Stripped;
        var rawCmdKey = parsed.RawCmdKey;
        var cmdArgs = parsed.CmdArgs;
        string matchedAlias = parsed.MatchedAlias;

        Command? cmd = null;
        if (puppet.InternalCmdSet is not null) cmd = puppet.InternalCmdSet.Get(rawCmdKey);
        if (cmd is null) cmd = CommandRegistry.LoggedIn.Get(rawCmdKey);
        if (cmd is null)
        {
            // glued single-char non-alpha: a lone leading symbol with text stuck to
            // it retries as its first character (so `'hello` can reach `'`).
            var first = rawCmdKey.Length > 0 ? rawCmdKey[..1] : "";
            if (!string.IsNullOrEmpty(first) && !char.IsLetter(first[0]))
            {
                // Glued lookup prefers internal-first, same as the normal path above:
                // an internal single-char verb shadows the global one on glued input too.
                cmd = puppet.InternalCmdSet?.Get(first);
                cmd ??= CommandRegistry.LoggedIn.Get(first);
                cmd ??= TryResolveLocal(puppet, first);
                if (cmd is not null)
                {
                    matchedAlias = first;
                    // glued args: parts[0][1:] + remainder
                    int ws = stripped.IndexOfAny(WhitespaceChars);
                    string rawFirstToken = ws < 0 ? stripped : stripped[..ws];
                    string gluedRemainder = rawFirstToken.Length > 1 ? rawFirstToken[1..] : "";
                    if (!string.IsNullOrEmpty(cmdArgs)) gluedRemainder = gluedRemainder.Length > 0 ? gluedRemainder + " " + cmdArgs : cmdArgs;
                    cmdArgs = gluedRemainder.TrimStart(WhitespaceChars);
                }
            }
            if (cmd is null)
            {
                // check location and inventory external cmdsets — faithful to inputfuncs.py loc.contents + puppet.contents
                cmd = TryResolveLocal(puppet, rawCmdKey);
            }
            if (cmd is null && _settings.AutoCommandAliasing)
            {
                if (rawCmdKey.Length == 1 && NoAliasCommands.Contains(rawCmdKey.ToLowerInvariant()))
                {
                    puppet.Msg("You can't do that.");
                    return null;
                }
                // Deliberate divergence from Python: non-social commands take priority over socials
                // (else "sa"→salute would shadow "say", etc.).
                (cmd, matchedAlias) = AutoAlias(CommandRegistry.LoggedIn, rawCmdKey, socialsFallback: true);
            }
            if (cmd is null)
            {
                cmd = CommandRegistry.LoggedIn.Get("none");
                matchedAlias = "none";
                cmdArgs = stripped;
            }
        }
        if (cmd is null) return null;
        if (!cmd.Access(puppet))
        {
            puppet.Msg("You can't do that.");
            return null;
        }
        var (func, caller, eargs) = cmd.Execute(puppet, cmdArgs, matchedAlias);
        if (func is null) return null;
        if (LagCheck is not null && caller is not null && LagCheck(caller)) return null;
        if (immediate) return new Job(func, caller!, eargs);
        // queue
        var pool = _pool;
        if (pool is not null)
        {
            if (!pool.AddTask(() => func(caller!, eargs))) { AtherizLogger.LogWarning($"[Dispatch] threadpool full; dropping command '{rawCmdKey}'."); }
        }
        else
        {
            // fallback immediate if no pool
            func(caller!, eargs);
        }
        return null;
    }

    // Port of unloggedin/cmdset.py:14-26 conditionals, evaluated at dispatch
    // time so settings flips take effect without a registry reset. Single
    // effective source: a verb is gated off when EITHER the dispatch settings
    // (_settings, honored by the aliasing paths above) or the live Global
    // settings disables it (both default enabled; Global flips and
    // SetSettings flips both take effect).
    // shared with the creation commands' Run methods so a direct
    // Run honors the same gate as dispatch (SetSettings snapshot && Global).
    internal static bool IsUnloggedInEnabled(Command cmd)
    {
        var g = AtherizSettings.Global;
        if (cmd is UnloggedIn.CreateAccountCommand) return _settings.AccountCreationEnabled && g.AccountCreationEnabled;
        if (cmd is UnloggedIn.NewCharacterCommand) return _settings.CharCreationEnabled && g.CharCreationEnabled;
        if (cmd is UnloggedIn.GuestCommand) return _settings.GuestEnabled && g.GuestEnabled;
        return true;
    }

    public static Job? ResolveUnloggedIn(IMessageTarget connection, string text)
    {
        var parsed = ParseRaw(text);
        if (parsed is null) return null;
        var stripped = parsed.Stripped;
        var rawCmdKey = parsed.RawCmdKey;
        var cmdArgs = parsed.CmdArgs;
        string matchedAlias = parsed.MatchedAlias;
        var cmdset = CommandRegistry.UnloggedIn;
        var cmd = cmdset.Get(rawCmdKey);
        if (cmd is null)
        {
            if (_settings.AutoCommandAliasing)
            {
                (cmd, matchedAlias) = AutoAlias(cmdset, rawCmdKey, socialsFallback: false);
            }
            if (cmd is null)
            {
                cmd = cmdset.Get("none");
                matchedAlias = "none";
                cmdArgs = stripped;
            }
        }
        // cmdset.py:14-26 conditionals are evaluated at registration; settings
        // flips afterwards must take effect without a reset. A disabled verb
        // demotes to `none` exactly like an unknown verb above :
        // full stripped input, so the suggestion is based on "create foo",
        // not the already-split args "foo".
        if (cmd is not null && !IsUnloggedInEnabled(cmd))
        {
            var none = cmdset.Get("none");
            if (none is not null)
            {
                cmd = none;
                matchedAlias = "none";
                cmdArgs = stripped;
            }
        }
        if (cmd is null) return null;
        if (!cmd.Access(connection))
        {
            connection.Msg("You can't do that.");
            return null;
        }
        var (func, caller, eargs) = cmd.Execute(connection, cmdArgs, matchedAlias);
        return func is null ? null : new Job(func, caller!, eargs);
    }
}
