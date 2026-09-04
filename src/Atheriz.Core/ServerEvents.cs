// Port of atheriz/server_events.py:8-96
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core;

// Port of atheriz/server_events.py:8 static hook points
public static class ServerEvents
{
    // Port of server_events.py:8 def at_server_start()
    public static void AtServerStart() => AtServerStart(null);
    // Port of server_events.py:8 preserve hook signature at_server_start(sender)
    public static void AtServerStart(object? sender)
    {
        AtherizLogger.LogInformation("Server starting..."); // Port of server_events.py:8 minimal log
        TryBroadcast("Server is starting...");
        if (sender != null) InvokeHooks("at_server_start", sender); // Port of base_obj hookable iteration
        else InvokeHooks("at_server_start");
    }

    // Port of server_events.py:12 def at_server_stop()
    public static void AtServerStop() => AtServerStop(null);
    // Port of server_events.py:12 preserve hook signature at_server_stop(sender)
    public static void AtServerStop(object? sender)
    {
        AtherizLogger.LogInformation("Server stopping..."); // Port of server_events.py:12
        TryBroadcast("Server is shutting down...");
        if (sender != null) InvokeHooks("at_server_stop", sender);
        else InvokeHooks("at_server_stop");
    }

    // Port of server_events.py:16 def at_server_reload()
    public static void AtServerReload() => AtServerReload(null);
    // Port of server_events.py:16 preserve hook signature at_server_reload(sender)
    public static void AtServerReload(object? sender)
    {
        AtherizLogger.LogInformation("Server reloading..."); // Port of server_events.py:16
        TryBroadcast("Server is reloading...");
        if (sender != null) InvokeHooks("at_server_reload", sender);
        else InvokeHooks("at_server_reload");
    }

    // Port of server_events.py:19 def at_char_create(account_name, char_name, password) CLI helper.
    // The optional output mirrors Python's redirect_stdout capture in the
    // /_internal/create_account endpoint: null keeps Console output (CLI/tests).
    public static void AtCharCreate(string accountName, string charName, string password, TextWriter? output = null)
    {
        void Out(string s) => (output ?? Console.Out).WriteLine(s);
        // Port of server_events.py:19-96 faithful validation + creation, console output replaces print
        var err = Commands.UnloggedIn.Validation.ValidatePassword(password);
        if (err != null) { Out(err); return; }
        err = Commands.UnloggedIn.Validation.ValidateCharacterName(charName);
        if (err != null) { Out(err); return; }
        var existsLc = charName.ToLowerInvariant();
        if (ObjectRegistry.FilterBy(o => o.IsPc && (o.Name ?? "").ToLowerInvariant() == existsLc).Count > 0)
        {
            Out($"Character name '{charName}' already exists.");
            return;
        }
        var settings = AtherizSettings.Global;
        var results = ObjectRegistry.FilterBy(o => o.IsAccount && (o.Name ?? "").ToLowerInvariant() == accountName.ToLowerInvariant());
        // Port of server_events.py:47 get_node_handler + DEFAULT_HOME (direct call, errors surface).
        // C# tests use real Nodes without handler indexing (no mocks), so also consult the live registry.
        Node? home = GlobalServices.GetNodeHandler().GetNode(settings.DefaultHome);
        home ??= ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Equals(settings.DefaultHome)).FirstOrDefault() as Node;
        if (home == null)
        {
            Out($"Default home {settings.DefaultHome} not found; aborting char create");
            return;
        }
        if (results.Count > 0)
        {
            foreach (var r in results)
            {
                if (r is not Account acc) continue;
                if (!acc.CheckPassword(password))
                {
                    Out($"Account '{accountName}' already exists with a different password...");
                    return;
                }
                if (acc.Characters.Count >= settings.MaxCharacters)
                {
                    Out($"Account '{accountName}' already has {settings.MaxCharacters} characters...");
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
                    Out($"Character name '{charName}' already exists.");
                    return;
                }
                character.MoveTo(home);
                ObjectRegistry.SaveObjects();
                acc.IsModified = true; // Port of server_events.py object.__setattr__(r, "is_modified", True) after save
                Out("Success! Character created.");
                // Port of hook invocation for at_char_create
                AtCharCreate(ObjectRegistry.FilterBy(o => o.Name == charName && o.IsPc).FirstOrDefault()!, results[0] as Account ?? new Account { Name = accountName });
                return;
            }
        }
        err = Commands.UnloggedIn.Validation.ValidateAccountName(accountName);
        if (err != null) { Out(err); return; }
        Out($"Creating account '{accountName}'...");
        Account account;
        try { account = Account.Create(accountName, password); }
        catch (InvalidOperationException) { Out($"Account '{accountName}' already exists."); return; }
        if (account == null) { Out($"Account '{accountName}' already exists."); return; }
        ObjectRegistry.AddObject(account);
        Out($"Creating character '{charName}'...");
        var ch2 = GameObject.Create(charName, isPc: true);
        ObjectRegistry.AddObject(ch2);
        if (LostPcNameRace(existsLc, ch2.Id))
        {
            ObjectRegistry.RemoveObject(ch2);
            Out($"Character name '{charName}' already exists.");
            return;
        }
        ch2.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
        account.AddCharacter(ch2);
        ch2.MoveTo(home);
        ObjectRegistry.SaveObjects();
        account.IsModified = true; // Port of server_events.py object.__setattr__(account, "is_modified", True) after save
        Out("Success! Account and character created.");
        AtCharCreate(ch2, account);
    }

    // Port of server_events.py:19 _lost_pc_name_race — lowest id wins so concurrent
    // creators converge deterministically no matter how the re-checks interleave.
    private static bool LostPcNameRace(string charNameLower, int myId)
    {
        var dupes = ObjectRegistry.FilterBy(o => o.IsPc && (o.Name ?? "").ToLowerInvariant() == charNameLower && o.Id != myId);
        return dupes.Any(d => d.Id < myId);
    }

    // Spec overload: AtCharCreate(GameObject character, Account account)
    public static void AtCharCreate(GameObject character, Account account)
    {
        if (character == null || account == null) return;
        AtherizLogger.LogInformation($"Character '{character.Name}' created for account '{account.Name}'."); // Port of server_events.py:19 hook
        TryBroadcast($"{character.Name} has been created.");
        InvokeHooks("at_char_create", character, account);
        // Also try virtual override if subclass overrides AtCharCreate
        TryInvokeVirtual("AtCharCreate", character, account);
    }

    private static void TryBroadcast(string msg)
    {
        try
        {
            var ch = GlobalServices.GetServerChannel();
            if (ch != null) try { ch.Msg(msg); } catch { }
        }
        catch { }
    }

    private static void InvokeHooks(string hookName, params object?[] args)
    {
        args ??= Array.Empty<object?>();
        try
        {
            var targets = ObjectRegistry.FilterBy(o => o.HasHook(hookName));
            foreach (var o in targets)
            {
                try { o.Hookable(hookName, () => 0, args); } catch { }
            }
        }
        catch { }
        // Also try virtual overrides named AtServerStart etc (PascalCase)
        var pascal = ToPascal(hookName);
        TryInvokeVirtual(pascal, args);
    }

    private static void TryInvokeVirtual(string methodName, params object?[] args)
    {
        args ??= Array.Empty<object?>();
        try
        {
            foreach (var o in ObjectRegistry.FilterBy(_ => true))
            {
                var mi = o.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;
                if (mi.DeclaringType == typeof(GameObject) || mi.DeclaringType == typeof(object)) continue;
                try { mi.Invoke(o, args.Length == 0 ? null : args); } catch { }
            }
        }
        catch { }
    }

    private static string ToPascal(string snake)
    {
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));
    }
}
