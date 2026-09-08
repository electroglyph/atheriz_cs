// Port of atheriz/server_events.py:8-96
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

namespace Atheriz.Core;

// Port of atheriz/server_events.py:8 static hook points
public static class ServerEvents
{
    // serializes concurrent AtCharCreate check-then-insert sequences so two
    // creators cannot both pass the pre-check and insert duplicate PC names. Dedicated
    // root lock (never taken elsewhere, always outermost); LostPcNameRace stays as the
    // deterministic tiebreak for non-AtCharCreate writers.
    private static readonly object _charCreateLock = new();
    // Port of server_events.py:8 def at_server_start()
    public static void AtServerStart() => AtServerStart(null);
    // Port of server_events.py:8 preserve hook signature at_server_start(sender).
    // Python is pass (silent); the hook walk stays as the game-code extension point.
    public static void AtServerStart(object? sender)
    {
        if (sender != null) InvokeHooks("at_server_start", sender); // Port of base_obj hookable iteration
        else InvokeHooks("at_server_start");
    }

    // Port of server_events.py:12 def at_server_stop()
    public static void AtServerStop() => AtServerStop(null);
    // Port of server_events.py:12 preserve hook signature at_server_stop(sender)
    // Python is pass (silent); the hook walk stays as the game-code extension point.
    public static void AtServerStop(object? sender)
    {
        if (sender != null) InvokeHooks("at_server_stop", sender);
        else InvokeHooks("at_server_stop");
    }

    // Port of server_events.py:16 def at_server_reload()
    public static void AtServerReload() => AtServerReload(null);
    // Port of server_events.py:16 preserve hook signature at_server_reload(sender)
    // Python is pass (silent); the hook walk stays as the game-code extension point.
    public static void AtServerReload(object? sender)
    {
        if (sender != null) InvokeHooks("at_server_reload", sender);
        else InvokeHooks("at_server_reload");
    }

    // Port of server_events.py:19 def at_char_create(account_name, char_name, password) CLI helper.
    // The optional output mirrors Python's redirect_stdout capture in the
    // /_internal/create_account endpoint: null keeps Console output (CLI/tests).
    // settingsOverride lets embedders pass explicit settings instead of the Global
    // singleton (which carries SavePath/SecretPath); null keeps Global (CLI/tests).
    public static void AtCharCreate(string accountName, string charName, string password, TextWriter? output = null, AtherizSettings? settingsOverride = null)
    {
        void Out(string s) => (output ?? Console.Out).WriteLine(s);
        // Port of server_events.py:19-96 faithful validation + creation, console output replaces print
        var err = Commands.UnloggedIn.Validation.ValidatePassword(password);
        if (err != null) { Out(err); return; }
        err = Commands.UnloggedIn.Validation.ValidateCharacterName(charName);
        if (err != null) { Out(err); return; }
        var existsLc = charName.ToLowerInvariant();
        // Lock narrowing: the creation lock guards ONLY check-then-insert (registry
        // mutations + race rollback). MoveTo messaging, SaveObjects persistence, console
        // output of the success path, and the hook call all run AFTER the lock releases.
        GameObject? doneChar = null; Account? doneAcc = null; Node? doneHome = null; bool doneNewAccount = false;
        // pre-check + insert are one critical section per creator; concurrent
        // AtCharCreate calls serialize so the second sees the first's committed PC.
        // home resolution runs BEFORE the lock. GetNodeHandler() can
        // do cold-start Load() disk I/O (server_events.py:47 has no creation
        // lock at all); serializing creators on I/O stalls every signup.
        var settings = settingsOverride ?? AtherizSettings.Global;
        // Port of server_events.py:47 get_node_handler + DEFAULT_HOME (direct call, errors surface).
        // C# tests use real Nodes without handler indexing (no mocks), so also consult the live registry.
        Node? home = GlobalServices.GetNodeHandler().GetNode(settings.DefaultHome);
        home ??= ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Equals(settings.DefaultHome)).FirstOrDefault() as Node;
        if (home == null)
        {
            Out($"Default home {settings.DefaultHome} not found; aborting char create");
            return;
        }
        // no console I/O under the creation lock — error paths capture
        // their message and print after the lock releases (CheckPassword I/O
        // stays: it is part of the check-then-insert critical section).
        string? failMsg = null;
        var progressMsgs = new List<string>();
        // Local section: `return` below exits the lock, not the method —
        // captured messages print after the lock releases.
        void CreateUnderLock()
        {
        lock (_charCreateLock)
        {
        if (ObjectRegistry.FilterBy(o => o.IsPc && (o.Name ?? "").ToLowerInvariant() == existsLc).Count > 0)
        {
            failMsg = $"Character name '{charName}' already exists.";
            return;
        }
        var results = ObjectRegistry.FilterBy(o => o.IsAccount && (o.Name ?? "").ToLowerInvariant() == accountName.ToLowerInvariant());
        if (results.Count > 0)
        {
            foreach (var r in results)
            {
                if (r is not Account acc) continue;
                if (!acc.CheckPassword(password))
                {
                    failMsg = $"Account '{accountName}' already exists with a different password...";
                    return;
                }
                if (acc.Characters.Count >= settings.MaxCharacters)
                {
                    failMsg = $"Account '{accountName}' already has {settings.MaxCharacters} characters...";
                    return;
                }
                var character = GameObject.Create(charName, isPc: true);
                // Port of create() auto-add: add-then-move (matches InitialSetup order).
                ObjectRegistry.AddObject(character);
                character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
                acc.AddCharacter(character);
                if (LostPcNameRace(existsLc, character.Id))
                {
                    acc.RemoveCharacter(character);
                    ObjectRegistry.RemoveObject(character);
                    failMsg = $"Character name '{charName}' already exists.";
                    return;
                }
                // Success-path I/O runs after the lock releases (see doneChar below).
                doneChar = character; doneAcc = acc; doneHome = home; doneNewAccount = false;
                return;
            }
        }
        err = Commands.UnloggedIn.Validation.ValidateAccountName(accountName);
        if (err != null) { failMsg = err; return; }
        progressMsgs.Add($"Creating account '{accountName}'...");
        Account account;
        try { account = Account.Create(accountName, password); }
        catch (InvalidOperationException) { failMsg = $"Account '{accountName}' already exists."; return; }
        if (account == null) { failMsg = $"Account '{accountName}' already exists."; return; }
        ObjectRegistry.AddObject(account);
        progressMsgs.Add($"Creating character '{charName}'...");
        var ch2 = GameObject.Create(charName, isPc: true);
        ObjectRegistry.AddObject(ch2);
        if (LostPcNameRace(existsLc, ch2.Id))
        {
            ObjectRegistry.RemoveObject(ch2);
            failMsg = $"Character name '{charName}' already exists.";
            return;
        }
        ch2.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
        account.AddCharacter(ch2);
        // Success-path I/O runs after the lock releases (see doneChar below).
        doneChar = ch2; doneAcc = account; doneHome = home; doneNewAccount = true;
        }
        }
        CreateUnderLock();
        foreach (var m in progressMsgs) Out(m);
        if (failMsg != null) { Out(failMsg); return; }
        if (doneChar != null && doneAcc != null && doneHome != null)
        {
            doneChar.MoveTo(doneHome);
            // honor settingsOverride — the parameterless save lands
            // in the ambient Global DB when explicit settings were passed.
            // the success-path save is failure-captured — a dead disk
            // must not take down the signup flow with an unhandled throw.
            try { ObjectRegistry.SaveObjects(AtherizDbContextFactory.ResolveSavePath(settings)); }
            catch (Exception saveEx) { string saveErr = "Save failed: " + saveEx.Message; Out(saveErr); return; }
            doneAcc.IsModified = true; // Port of server_events.py object.__setattr__(account, "is_modified", True) after save
            Out(doneNewAccount ? "Success! Account and character created." : "Success! Character created.");
            // Pass the live objects (not a re-query of the registry).
            AtCharCreate(doneChar, doneAcc);
        }
    }

    // Port of server_events.py:19 _lost_pc_name_race — lowest id wins so concurrent
    // creators converge deterministically no matter how the re-checks interleave.
    private static bool LostPcNameRace(string charNameLower, int myId)
    {
        var dupes = ObjectRegistry.FilterBy(o => o.IsPc && (o.Name ?? "").ToLowerInvariant() == charNameLower && o.Id != myId);
        return dupes.Any(d => d.Id < myId);
    }

    // Spec overload: AtCharCreate(GameObject character, Account account)
    // Port of server_events.py:96 — Python only prints (the caller already
    // printed "Success! …"); no broadcast, no hook fan-out.
    public static void AtCharCreate(GameObject character, Account account)
    {
        if (character == null || account == null) return;
        AtherizLogger.LogInformation($"Character '{character.Name}' created for account '{account.Name}'.");
    }

    private static void InvokeHooks(string hookName, params object?[] args)
    {
        args ??= Array.Empty<object?>();
        // Also try virtual overrides named AtServerStart etc (PascalCase)
        var pascal = ToPascal(hookName);
        try
        {
            // Single registry walk: hook fan-out + virtual dispatch per object.
            foreach (var o in ObjectRegistry.FilterBy(_ => true))
            {
                if (o.HasHook(hookName))
                {
                    try { o.Hookable(hookName, () => 0, args); } catch (Exception ex) { AtherizLogger.LogWarning($"Hook '{hookName}' failed on '{o.Name}': {ex.Message}"); }
                }
                try
                {
                    switch (pascal)
                    {
                        case "AtServerStart": o.AtServerStart(args.Length > 0 ? args[0] : null); break;
                        case "AtServerStop": o.AtServerStop(args.Length > 0 ? args[0] : null); break;
                        case "AtServerReload": o.AtServerReload(args.Length > 0 ? args[0] : null); break;
                        default: break;
                    }
                }
                catch (Exception ex) { AtherizLogger.LogWarning($"Hook '{hookName}' failed on '{o.Name}': {ex.Message}"); }
            }
        }
        catch (Exception ex) { AtherizLogger.LogWarning($"Hook dispatch '{hookName}' failed: {ex.Message}"); }

        static string ToPascal(string snake)
        {
            var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
        }
    }

}
