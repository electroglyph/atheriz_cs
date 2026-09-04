using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands;

/// <summary>
/// Faithful port of <c>atheriz/inputfuncs.py:dispatch_loggedin / _resolve_unloggedin</c>.
/// </summary>
public static class CommandDispatcher
{
    private static readonly string[] NoAliasCommands = ["n", "s", "e", "w", "u", "d"];

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
        var stripped = text.Trim(" \t\r\n".ToCharArray());
        if (string.IsNullOrEmpty(stripped)) return null;
        // preserve split None logic: first token is raw_cmd_key lower
        int firstSpace = stripped.IndexOfAny([' ', '\t', '\r', '\n']);
        string rawCmdKey;
        string cmdArgs;
        if (firstSpace < 0) { rawCmdKey = stripped.ToLowerInvariant(); cmdArgs = ""; }
        else { rawCmdKey = stripped[..firstSpace].ToLowerInvariant(); cmdArgs = stripped[(firstSpace + 1)..].TrimStart(" \t\r\n".ToCharArray()); }
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
            if (key.StartsWith(rawCmdKey, StringComparison.Ordinal))
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
            // glued single-char non-alpha: mirrors intentional shadowing at inputfuncs.py:123
            var first = rawCmdKey.Length > 0 ? rawCmdKey[..1] : "";
            if (!string.IsNullOrEmpty(first) && !char.IsLetter(first[0]))
            {
                cmd = CommandRegistry.LoggedIn.Get(first);
                if (cmd is not null)
                {
                    matchedAlias = first;
                    // glued args: parts[0][1:] + remainder
                    int ws = stripped.IndexOfAny([' ', '\t', '\r', '\n']);
                    string rawFirstToken = ws < 0 ? stripped : stripped[..ws];
                    string gluedRemainder = rawFirstToken.Length > 1 ? rawFirstToken[1..] : "";
                    if (!string.IsNullOrEmpty(cmdArgs)) gluedRemainder = gluedRemainder.Length > 0 ? gluedRemainder + " " + cmdArgs : cmdArgs;
                    cmdArgs = gluedRemainder.TrimStart(" \t\r\n".ToCharArray());
                }
            }
            if (cmd is null)
            {
                // check location and inventory external cmdsets — faithful to inputfuncs.py loc.contents + puppet.contents
                // location: both ObjectLocation and CoordLocation
                GameObject? locObj = puppet.ResolveLocationObject();
                if (locObj != null)
                {
                    // scan loc's contents for external commands (props in room)
                    foreach (var cid in locObj.ContentsSnapshot)
                    {
                        var obj = ObjectRegistry.Get(cid).FirstOrDefault();
                        if (obj?.ExternalCmdSet is not null && (cmd = obj.ExternalCmdSet.Get(rawCmdKey)) is not null) break;
                    }
                    // also if loc itself has external (unlikely but for completeness)
                    if (cmd is null && locObj is not null && locObj.ExternalCmdSet is not null)
                        cmd = locObj.ExternalCmdSet.Get(rawCmdKey);
                }
                if (cmd is null)
                {
                    foreach (var cid in puppet.ContentsSnapshot)
                    {
                        var obj = ObjectRegistry.Get(cid).FirstOrDefault();
                        if (obj?.ExternalCmdSet is not null && (cmd = obj.ExternalCmdSet.Get(rawCmdKey)) is not null) break;
                    }
                }
            }
            if (cmd is null && _settings.AutoCommandAliasing)
            {
                if (rawCmdKey.Length == 1 && NoAliasCommands.Contains(rawCmdKey.ToLowerInvariant()))
                {
                    puppet.Msg("You can't do that.");
                    return null;
                }
                // Deliberate divergence from Python: non-social commands take priority over socials
                // (Else "sa"→salute would shadow "say", etc. Owner decision 2026-09-04, audit Appendix B Q5.)
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
        if (LagCheck != null && caller != null && LagCheck(caller)) return null;
        if (immediate) return new Job(func, caller!, eargs);
        // queue
        var pool = _pool;
        if (pool is not null)
        {
            if (!pool.AddTask(() => func(caller!, eargs))) { /* log warning */ }
        }
        else
        {
            // fallback immediate if no pool
            func(caller!, eargs);
        }
        return null;
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
