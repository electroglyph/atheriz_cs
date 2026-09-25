using Atheriz.Core.Concurrency;

namespace Atheriz.Core.Commands;

/// <summary>
/// </summary>
public static class CommandDispatcher
{
    private static readonly string[] NoAliasCommands = ["n", "s", "e", "w", "u", "d"];

    // Hot-path parse (ParseRaw runs on every dispatched command): one shared
    // whitespace table instead of per-call ToCharArray/collection allocations.
    private static readonly char[] WhitespaceChars = [' ', '\t', '\r', '\n'];

    private static AtherizSettings _settings = new();
    public static void SetSettings(AtherizSettings s) => _settings = s;
    // Test seam: capture the dispatch generation so a render/gate
    // agreement test can restore it. The slot is swap-only (never mutated in
    // place), so the reference is the generation.
    internal static AtherizSettings SnapshotSettings() => _settings;
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
    // NOTE: this stays as one GetSortedKeys snapshot plus one Get per
    // prefix-matching candidate (a lock take each) rather than a single pass
    // under one read scope. CmdSet exposes no read scope — its lock is
    // private — so a single scope would need either a new scoped lock
    // accessor or an in-CmdSet scan that bypasses the virtual Get override
    // point, and holding one read across the sort + scan would stall Adds /
    // Remove writers longer than these short discrete takes. The saving would
    // apply to the typo path only.
    internal static (Command? cmd, string matchedAlias) AutoAlias(CmdSet cmdset, string rawCmdKey, bool socialsFallback, IMessageTarget? caller = null)
    {
        Command? socialFallback = null;
        string socialFallbackKey = "";
        foreach (var key in cmdset.GetSortedKeys())
        {
            if (_settings.AutoAliasIgnoredKeys.Contains(key)) continue;
            // case-insensitive prefix. Input is lowercased but
            // registered keys may be mixed-case (channel/exit SetKey);
            // Ordinal never matched those. Dict lookups are already
            // OrdinalIgnoreCase, so this aligns the scan with Get.
            if (key.StartsWith(rawCmdKey, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = cmdset.Get(key);
                // Access gate: a near-miss of a forbidden verb must not
                // resolve to it (dispatch would deny with "You can't do
                // that." while genuine gibberish falls to NoneCommand —
                // a closeness oracle). Skipped candidates fall through
                // to NoneCommand exactly like gibberish.
                if (caller is not null && candidate is not null && !candidate.Access(caller)) continue;
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

    // Shared internal-first/global-second head for the normal vs glued
    // lookups inside DispatchLoggedIn: an internal single-char verb shadows
    // the global one on glued input exactly as on the normal path. Scoped to
    // these two lookups only — the dispatch tails differ (Job-always vs
    // LagCheck+queue) and stay separate.
    private static Command? TryResolveGlobal(GameObject puppet, string key)
        => puppet.InternalCmdSet?.Get(key) ?? CommandRegistry.LoggedIn.Get(key);

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

    // Auto-alias tail of the resolution chain: the global-registry prefix
    // scan behind the AutoCommandAliasing setting. A refused single-char
    // no-alias verb reports through <paramref name="refused"/> so the caller
    // aborts before the none-fallback (a refusal is not "unknown").
    private static Command? TryResolveAutoAlias(GameObject puppet, string rawCmdKey, ref string matchedAlias, out bool refused)
    {
        refused = false;
        if (!_settings.AutoCommandAliasing) return null;
        if (rawCmdKey.Length == 1 && NoAliasCommands.Contains(rawCmdKey))
        {
            puppet.Msg("You can't do that.");
            refused = true;
            return null;
        }
        // Deliberate divergence from Python: non-social commands take priority over socials
        // (else "sa"→salute would shadow "say", etc.).
        Command? cmd;
        (cmd, matchedAlias) = AutoAlias(CommandRegistry.LoggedIn, rawCmdKey, socialsFallback: true, caller: puppet);
        return cmd;
    }

    // Glued single-char non-alpha: a lone leading symbol with text stuck to
    // it retries as its first character (so `'hello` can reach `'`). The
    // retry keeps the internal-first/global-second head: an internal
    // single-char verb shadows the global one on glued input too.
    private static Command? TryResolveGlued(GameObject puppet, string stripped, string rawCmdKey, ref string matchedAlias, ref string cmdArgs)
    {
        var first = rawCmdKey.Length > 0 ? rawCmdKey[..1] : "";
        if (string.IsNullOrEmpty(first) || char.IsLetter(first[0])) return null;
        var cmd = TryResolveGlobal(puppet, first);
        cmd ??= TryResolveLocal(puppet, first);
        if (cmd is null) return null;
        matchedAlias = first;
        // glued args: parts[0][1:] + remainder
        int ws = stripped.IndexOfAny(WhitespaceChars);
        string rawFirstToken = ws < 0 ? stripped : stripped[..ws];
        string gluedRemainder = rawFirstToken.Length > 1 ? rawFirstToken[1..] : "";
        if (!string.IsNullOrEmpty(cmdArgs)) gluedRemainder = gluedRemainder.Length > 0 ? gluedRemainder + " " + cmdArgs : cmdArgs;
        cmdArgs = gluedRemainder.TrimStart(WhitespaceChars);
        return cmd;
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

        // Resolution precedence, one link per resolver: global, then the
        // glued single-char retry, then local, then auto-alias, then the
        // none-fallback. Each resolver runs only when every earlier one
        // missed — no nested fallthroughs.
        // Short-circuit note: when an earlier resolver hits,
        // TryResolveAutoAlias never runs and refused keeps its
        // initializer (false) below — a hit is never a refusal.
        bool refused = false;
        Command? cmd = TryResolveGlobal(puppet, rawCmdKey)
            ?? TryResolveGlued(puppet, stripped, rawCmdKey, ref matchedAlias, ref cmdArgs)
            ?? TryResolveLocal(puppet, rawCmdKey)
            ?? TryResolveAutoAlias(puppet, rawCmdKey, ref matchedAlias, out refused);
        if (refused) return null;
        if (cmd is null)
        {
            cmd = CommandRegistry.LoggedIn.Get("none");
            matchedAlias = "none";
            cmdArgs = stripped;
        }
        if (cmd is null) return null;
        if (!cmd.Access(puppet))
        {
            puppet.Msg("You can't do that.");
            return null;
        }
        var (func, caller, eargs) = cmd.Execute(puppet, cmdArgs, matchedAlias);
        if (func is null) return null;
        // Snapshot the gate: separate null-check and invoke reads race a
        // concurrent LagCheck swap (stale non-null invoke after a null swap
        // throws; a missed swap dispatches under the wrong gate).
        var lagCheck = LagCheck;
        if (lagCheck is not null && caller is not null && lagCheck(caller)) return null;
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

    // time so settings flips take effect without a registry reset. Single
    // effective source: a verb is gated off when EITHER the dispatch settings
    // (_settings, honored by the aliasing paths above) or the live Global
    // settings disables it (both default enabled; Global flips and
    // SetSettings flips both take effect).
    // shared with the creation commands' Run methods so a direct
    // Run honors the same gate as dispatch (SetSettings snapshot && Global).
    internal static bool IsUnloggedInEnabled(Command cmd)
    {
        return IsUnloggedInEnabled(cmd, _settings, AtherizSettings.Global);
    }
    // Single-generation overload: the gate's two legs share one captured
    // pair, so a swap landing inside the probe cannot tear the hint.
    // Dispatch keeps reading live (a swap between hint and dispatch can
    // still differ for one render — the hint is internally consistent,
    // never torn).
    internal static bool IsUnloggedInEnabled(Command cmd, AtherizSettings dispatchSettings, AtherizSettings live)
    {
        return cmd switch
        {
            UnloggedIn.CreateAccountCommand => dispatchSettings.AccountCreationEnabled && live.AccountCreationEnabled,
            UnloggedIn.NewCharacterCommand => dispatchSettings.CharCreationEnabled && live.CharCreationEnabled,
            UnloggedIn.GuestCommand => dispatchSettings.GuestEnabled && live.GuestEnabled,
            _ => true,
        };
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
                (cmd, matchedAlias) = AutoAlias(cmdset, rawCmdKey, socialsFallback: false, caller: connection);
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
