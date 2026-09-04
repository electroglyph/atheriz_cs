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

    // Port of server_events.py:19 def at_char_create(account_name, char_name, password) CLI helper
    public static void AtCharCreate(string accountName, string charName, string password)
    {
        // Port of server_events.py:19-96 faithful validation + creation, console output replaces print
        var err = Commands.UnloggedIn.Validation.ValidatePassword(password);
        if (err != null) { Console.WriteLine(err); return; }
        err = Commands.UnloggedIn.Validation.ValidateCharacterName(charName);
        if (err != null) { Console.WriteLine(err); return; }
        var existsLc = charName.ToLowerInvariant();
        if (ObjectRegistry.FilterBy(o => o.IsPc && (o.Name ?? "").ToLowerInvariant() == existsLc).Count > 0)
        {
            Console.WriteLine($"Character name '{charName}' already exists.");
            return;
        }
        var settings = AtherizSettings.Default;
        var results = ObjectRegistry.FilterBy(o => o.IsAccount && (o.Name ?? "").ToLowerInvariant() == accountName.ToLowerInvariant());
        // Port of server_events.py:47 get_node_handler + DEFAULT_HOME
        Node? home = null;
        try { home = GlobalServices.GetNodeHandler().GetNode(settings.DefaultHome); } catch { }
        if (home == null)
        {
            try
            {
                var cands = ObjectRegistry.FilterBy(o => o is Node n && n.Coord.Equals(settings.DefaultHome));
                home = cands.FirstOrDefault() as Node;
            }
            catch { }
        }
        if (home == null)
        {
            Console.WriteLine($"Default home {settings.DefaultHome} not found; aborting char create");
            return;
        }
        if (results.Count > 0)
        {
            foreach (var r in results)
            {
                if (r is not Account acc) continue;
                if (!acc.CheckPassword(password))
                {
                    Console.WriteLine($"Account '{accountName}' already exists with a different password...");
                    return;
                }
                if (acc.Characters.Count >= settings.MaxCharacters)
                {
                    Console.WriteLine($"Account '{accountName}' already has {settings.MaxCharacters} characters...");
                    return;
                }
                var character = GameObject.Create(charName, isPc: true);
                character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
                acc.AddCharacter(character);
                try { character.MoveTo(home); } catch { try { home.AddObject(character); } catch { } }
                ObjectRegistry.AddObject(character);
                try { ObjectRegistry.SaveObjects(settings.SavePath); } catch { }
                Console.WriteLine("Success! Character created.");
                // Port of hook invocation for at_char_create
                AtCharCreate(ObjectRegistry.FilterBy(o => o.Name == charName && o.IsPc).FirstOrDefault()!, results[0] as Account ?? new Account { Name = accountName });
                return;
            }
        }
        err = Commands.UnloggedIn.Validation.ValidateAccountName(accountName);
        if (err != null) { Console.WriteLine(err); return; }
        Console.WriteLine($"Creating account '{accountName}'...");
        Account account;
        try { account = Account.Create(accountName, password); }
        catch (InvalidOperationException) { Console.WriteLine($"Account '{accountName}' already exists."); return; }
        if (account == null) { Console.WriteLine($"Account '{accountName}' already exists."); return; }
        ObjectRegistry.AddObject(account);
        Console.WriteLine($"Creating character '{charName}'...");
        var ch2 = GameObject.Create(charName, isPc: true);
        ch2.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
        account.AddCharacter(ch2);
        try { ch2.MoveTo(home); } catch { try { home.AddObject(ch2); } catch { } }
        ObjectRegistry.AddObject(ch2);
        try { ObjectRegistry.SaveObjects(settings.SavePath); } catch { }
        Console.WriteLine("Success! Account and character created.");
        AtCharCreate(ch2, account);
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
