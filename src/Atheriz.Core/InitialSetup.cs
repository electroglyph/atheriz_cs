// Port of atheriz/initial_setup.py:48 do_setup
using Microsoft.EntityFrameworkCore;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Persistence;

namespace Atheriz.Core;

/// <summary>
/// Faithful port of <c>atheriz/initial_setup.py:do_setup</c>.
/// Builds 9x9x9 limbo cube, map placeholders, alarm/dashboard, superuser account.
/// Mirrors Python constants LIMBO_AREA=limbo, LIMBO_GRID=9, LIMBO_DESC etc.
/// </summary>
public static class InitialSetup
{
    public const string LIMBO_AREA = "limbo";
    public const int LIMBO_GRID = 9;
    public static int LIMBO_CENTER => LIMBO_GRID / 2; // 4
    public const string LIMBO_DESC = "You are in a vast nothingness.";

    // Port of initial_setup.py:20 PushCommand
    public sealed class PushCommand : Command
    {
        public override string Key => "push";
        public override string Desc => "It's a button, you can push it.";
        public override string Category => "Danger?";
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args)
        {
            if (caller is GameObject go)
            {
                var loc = go.ResolveLocationObject();
                loc?.MsgContents("BEEEEEP!", go);
            }
        }
    }

    // Port of initial_setup.py:32 AlarmObject
    public sealed class AlarmObject : GameObject
    {
        public AlarmObject() { IsItem = true; }
        public override void AtAlarm(Globals.GameTime.GameTimeInfo time, Dictionary<string, System.Text.Json.JsonElement>? data)
        {
            EmitSound(
                "A robotic voice intones: ",
                "Hands to ACTION STATIONS! Hands to ACTION STATIONS! Assume damage control state one condition ZULU. This is not a drill!",
                130.0, true);
        }
    }

    public static void DoSetup(string savePath, string? username = null, string? password = null, string? secretPath = null, bool prompt = true, TextReader? input = null)
    {
        // Port of initial_setup.py:49 logger.info — not duplicated to stdout (new.py:740 already prints)
        // Ensure savePath absolute for guard
        var absSave = Path.GetFullPath(savePath);
        var absSecret = secretPath is not null ? Path.GetFullPath(secretPath) : Path.Combine(Path.GetDirectoryName(absSave) ?? ".", "secret");
        Directory.CreateDirectory(absSecret);
        Utils.FsUtil.TryChmod0700(absSecret);

        // Port of initial_setup.py:50 do_db_setup()
        AtherizDbContextFactory.DoSetup(absSave);
        // Ensure salt exists in game's secret folder (mirrors Python SECRET_PATH override)
        try
        {
            // Force salt creation with explicit path
            SaltProvider.GetSalt(absSecret);
        }
        catch (Exception ex) { Console.Error.WriteLine($"Salt setup warning: {ex.Message}"); }

        // Reset registries for fresh world (mirrors globals cleared by conftest).
        // (ClearAll already resets the Id generator — no separate SetId.)
        ObjectRegistry.ClearAll();
        // Clear any existing node/map/time singletons that might cache old save path
        try { Globals.GlobalServices.Reset(); } catch (Exception ex) { Console.Error.WriteLine($"Singleton reset warning: {ex.Message}"); }
        SaltProvider.Clear();
        // Re-seed salt after clear
        try { SaltProvider.GetSalt(absSecret); } catch (Exception ex) { Console.Error.WriteLine($"Salt re-seed warning: {ex.Message}"); }
        // Seed the default slot with the same value: runtime password checks
        // call GetSalt() with no path, which otherwise reads CWD-relative
        // "secret/salt.txt" (a different file, or a throw outside a game
        // folder) instead of this game's salt.
        try { SaltProvider.SetSalt(SaltProvider.GetSalt(absSecret)); } catch (Exception ex) { Console.Error.WriteLine($"Default salt seed warning: {ex.Message}"); }

        var settings = AtherizSettings.Global;
        // Build NodeArea 9x9x9
        var nh = new NodeHandler(autoLoad: false);
        // the setup world becomes current explicitly (the ctor no
        // longer publishes helpers as current). Published through the
        // singleton setter so GlobalServices and GetCurrent agree —
        // publishing only one slot would fork same-process readers.
        Globals.GlobalServices.SetNodeHandler(nh);
        var area = new NodeArea(LIMBO_AREA);
        for (int z = 0; z < LIMBO_GRID; z++)
        {
            var grid = new NodeGrid(LIMBO_AREA, z);
            for (int x = 0; x < LIMBO_GRID; x++)
                for (int y = 0; y < LIMBO_GRID; y++)
                {
                    var coord = new Coord(LIMBO_AREA, x, y, z);
                    var node = new Node(coord, desc: LIMBO_DESC);
                    grid.Nodes[(x, y)] = node;
                    // the Node ctor no longer publishes to the
                    // registry — register explicitly so ResolveLocationObject
                    // finds limbo nodes.
                    Atheriz.Core.Globals.ObjectRegistry.AddObject(node);
                }
            area.AddGrid(grid);
        }
        // Directions mirrors Python DIRS
        var dirs = new (string name, string alias, int dx, int dy, int dz, string revName, string revAlias)[]
        {
            ("North","n",0,1,0,"South","s"),
            ("East","e",1,0,0,"West","w"),
            ("Up","u",0,0,1,"Down","d"),
        };
        for (int z = 0; z < LIMBO_GRID; z++)
        {
            var grid = area.GetGrid(z);
            if (grid is null) continue;
            for (int x = 0; x < LIMBO_GRID; x++)
                for (int y = 0; y < LIMBO_GRID; y++)
                {
                    if (!grid.Nodes.TryGetValue((x, y), out var node)) continue;
                    foreach (var d in dirs)
                    {
                        int nx = x + d.dx, ny = y + d.dy, nz = z + d.dz;
                        if (nx < 0 || nx >= LIMBO_GRID || ny < 0 || ny >= LIMBO_GRID || nz < 0 || nz >= LIMBO_GRID) continue;
                        var ng = d.dz == 0 ? grid : area.GetGrid(nz);
                        if (ng is null) continue;
                        if (!ng.Nodes.TryGetValue((nx, ny), out var neighbor)) continue;
                        // add_link both directions (mirrors Python double add_link)
                        node.AddLink(new NodeLink(d.name, new Coord(LIMBO_AREA, nx, ny, nz), new List<string>{d.alias}));
                        neighbor.AddLink(new NodeLink(d.revName, new Coord(LIMBO_AREA, x, y, z), new List<string>{d.revAlias}));
                    }
                }
        }
        nh.AddArea(area);

        var mh = new MapHandler(settings, autoLoad: false);
        for (int z = 0; z < LIMBO_GRID; z++)
        {
            var mi = new MapInfo(LIMBO_AREA) { Settings = settings };
            for (int x = 0; x < LIMBO_GRID; x++)
                for (int y = 0; y < LIMBO_GRID; y++)
                {
                    mi.PreGrid[(x, y)] = settings.RoomPlaceholder;
                    mi.PlaceWalls((x, y), settings.SingleWallPlaceholder);
                }
            mi.PreRender();
            mh.SetMapInfo(LIMBO_AREA, z, mi);
        }
        // Same twin-slot publish as the node handler above: readers through
        // GlobalServices must see the seed world, not a stale handler.
        Globals.GlobalServices.SetMapHandler(mh);
        // Persist nodes and map.
        // Resolve credentials BEFORE taking the write gate or opening the seed
        // transaction. Holding either across interactive ReadLine/ReadKey turned
        // every slow operator into a TimeoutException (and a pinned DB) for all
        // concurrent savers. The gate/tx span below covers EnsureCreated +
        // saves only.
        // Resolve username/password — mirrors initial_setup.py:98-123
        string? u = username;
        string? p = password;
        // An explicitly provided reader is used as-is (test/console-redirect
        // seams); otherwise prompts need a real console like before.
        TextReader? promptInput = input ?? (prompt && !Console.IsInputRedirected ? Console.In : null);
        if (string.IsNullOrWhiteSpace(u))
        {
            u = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME")?.Trim();
            if (string.IsNullOrWhiteSpace(u))
            {
                if (promptInput is not null)
                {
                    // Explicit Console.Out (not Logger): interactive prompts must stay
                    // on stdout interleaved with stdin reads, mirroring print()/input().
                    Console.Out.Write("Enter superuser username: ");
                    u = promptInput.ReadLine()?.Trim();
                }
            }
            else u = u!.Trim();
        }
        else if (u is not null) u = u.Trim();

        bool skipSuperuser = false;
        if (string.IsNullOrWhiteSpace(u))
        {
            Console.Error.WriteLine("Error: Username cannot be empty.");
            // Port of initial_setup.py: mh.save()/nh.save() precede the
            // credential prompts — limbo is durable even with no superuser.
            // (Committed below, after the gate/tx open.)
            skipSuperuser = true;
        }

        if (!skipSuperuser && string.IsNullOrWhiteSpace(p))
        {
            p = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD")?.Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                if (promptInput is not null)
                {
                    Console.Out.Write("Enter superuser password: ");
                    // simple no-echo fallback
                    try
                    {
                        if (ReferenceEquals(promptInput, Console.In))
                        {
                            var sb = new System.Text.StringBuilder();
                            ConsoleKeyInfo k;
                            while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
                            {
                                if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
                                else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
                            }
                            Console.Out.WriteLine();
                            p = sb.ToString().Trim();
                        }
                        else p = promptInput.ReadLine()?.Trim();
                    }
                    catch { p = promptInput.ReadLine()?.Trim(); }
                }
            }
            else p = p!.Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                Console.Error.WriteLine("Error: Password cannot be empty.");
                // Limbo commits (see username branch above).
                skipSuperuser = true;
            }
        }
        else if (p is not null) p = p.Trim();

        if (!skipSuperuser)
        {
            var errU = Validation.ValidateAccountName(u!);
            if (errU is not null) throw new ArgumentException($"Invalid superuser username: {errU}");
            var errP = Validation.ValidatePassword(p!);
            if (errP is not null) throw new ArgumentException($"Invalid superuser password: {errP}");
        }

        // Single shared context + transaction for the whole seed (atomic checkpoint):
        // previously five separate contexts committed independently, so a mid-setup
        // failure left a half-built world. Any early return / throw below disposes the
        // transaction uncommitted (rollback); only the Commit at the end persists.
        using var db = AtherizDbContextFactory.Create(absSave);
        // The seed holds the write gate for its whole span so no concurrent save can
        // interleave with the single transaction below (same-flow re-entry, bounded).
        if (!Atheriz.Core.Persistence.DbWriteGate.TryEnter(TimeSpan.FromSeconds(30)))
            throw new TimeoutException("DbWriteGate held for over 30s; refusing to hang initial setup.");
        try
        {
        db.Database.EnsureCreated();
        // Scoped durability: global pragmas use synchronous=NORMAL (steady-state perf);
        // the one-time seed upgrades this connection to FULL so the initial world
        // state is fsync'd through the OS buffers, not just WAL-buffered.
        try { db.Database.ExecuteSqlRaw("PRAGMA synchronous=FULL;"); }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed InitialSetup.DoSetup fsync pragma: " + logEx.Message, "InitialSetup"); }
        using var setupTx = db.Database.BeginTransaction();
        mh.Save(db);
        nh.Save(db);

        if (skipSuperuser)
        {
            setupTx.Commit();
            Console.Out.WriteLine("Initial world (limbo) created without superuser — run `create` to add account.");
            return;
        }

        // Alarm object at 0,0,8
        var alarmCoord = new Coord(LIMBO_AREA, 0, 0, LIMBO_GRID - 1);
        var alarmNode = nh.GetNode(alarmCoord);
        // If not found via handler, fallback to area grid directly
        if (alarmNode is null)
        {
            var g = area.GetGrid(LIMBO_GRID - 1);
            g?.Nodes.TryGetValue((0,0), out alarmNode);
        }
        var alarmObj = new AlarmObject();
        alarmObj.Id = IdGenerator.GetUniqueId();
        alarmObj.Name = "A flashing dashboard";
        alarmObj.Desc = "A large display showing a multitude of plots and status readouts.";
        alarmObj.IsItem = true;
        alarmObj.Aliases = new List<string>{"dashboard"};
        alarmObj.IsModified = true;
        ObjectRegistry.AddObject(alarmObj);
        try { alarmObj.MoveTo(alarmNode); } catch {}

        // seed the clock from the operator's time settings (the same
        // AtherizSettings.Global the nodes/map above use), not defaults —
        // custom TickMinutes/SecondsPerMinute/calendar must apply to the
        // seed clock too. (Setup persists via the explicit db handle, so no
        // SavePath override is needed here.)
        var gt = new Globals.GameTime(settings, autoLoad: false);
        gt.AddAlarm("?", "0", alarmObj, repeat: true);
        gt.Save(db);

        // Account + character
        // Ensure salt with explicit secret path
        string saltVal;
        try { saltVal = SaltProvider.GetSalt(absSecret); } catch { saltVal = SaltProvider.GetSalt(absSecret); }
        var account = Account.Create(u!, p!, saltOverride: saltVal);
        Console.Out.WriteLine($"Creating character '{u!}'...");
        var character = GameObject.Create(u!, isPc: true);
        character.Desc = "";
        // Port of Object.create add_object — faithful registry add before save
        ObjectRegistry.AddObject(character);
        var homeCoord = settings.DefaultHome; // limbo 4,4,4
        var home = nh.GetNode(homeCoord);
        if (home is null)
        {
            var hg = area.GetGrid(homeCoord.Z);
            hg?.Nodes.TryGetValue((homeCoord.X, homeCoord.Y), out home);
        }
        if (home is not null)
        {
            character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
            character.PrivilegeLevel = Privilege.Admin;
            try { character.MoveTo(home); } catch {}
        }
        else character.PrivilegeLevel = Privilege.Admin;

        var button = GameObject.Create("A big red button", isItem: true);
        button.Desc = "A large button that glows with an ominous red light. Wonder if it does anything...";
        button.Aliases = new List<string>{"button"};
        button.AddLock("get", (GameObject x) => x.IsBuilder, LockPolicies.Builder);
        if (button.ExternalCmdSet is null) button.ExternalCmdSet = new Commands.CmdSet();
        try { button.ExternalCmdSet.Add(new PushCommand()); } catch { }
        ObjectRegistry.AddObject(button);
        if (home is not null) try { button.MoveTo(home); } catch {}

        account.AddCharacter(character);
        var chan = Channel.Create("Server");
        chan.AddLock("send", (GameObject x) => x.IsBuilder, LockPolicies.Builder);
        chan.AddLock("view", (GameObject x) => x.IsBuilder, LockPolicies.Builder);
        chan.Desc = "for server announcements";
        chan.AddListener(character);
        if (!character.ChannelsSnapshot.Contains(chan.Id))
            character.Subscribe(chan);

        // Port of initial_setup.py:171 save_objects() — default force=False:
        // persist only modified objects.
        ObjectRegistry.SaveObjects(db, force: false);
        nh.Save(db);
        setupTx.Commit();
        Console.Error.WriteLine("Initial world state set up.");
        }
        finally { Atheriz.Core.Persistence.DbWriteGate.Exit(); }
    }
}
