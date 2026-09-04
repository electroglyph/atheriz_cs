// Port of atheriz/initial_setup.py:48 do_setup
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Persistence;
using Atheriz.Core.Settings;

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

    public static void DoSetup(string savePath, string? username = null, string? password = null, string? secretPath = null)
    {
        // Port of initial_setup.py:49 logger.info — not duplicated to stdout (new.py:740 already prints)
        // Ensure savePath absolute for guard
        var absSave = Path.GetFullPath(savePath);
        var absSecret = secretPath != null ? Path.GetFullPath(secretPath) : Path.Combine(Path.GetDirectoryName(absSave) ?? ".", "secret");
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

        // Reset registries for fresh world (mirrors globals cleared by conftest)
        ObjectRegistry.ClearAll();
        // Ensure Id generator clean
        IdGenerator.SetId(-1);
        // Clear any existing node/map/time singletons that might cache old save path
        try { Globals.GlobalServices.ResetForTesting(); } catch { }
        SaltProvider.Clear();
        // Re-seed salt after clear
        try { SaltProvider.GetSalt(absSecret); } catch {}

        var settings = AtherizSettings.Default;
        // Build NodeArea 9x9x9
        var nh = new NodeHandler(autoLoad: false);
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
            if (grid == null) continue;
            for (int x = 0; x < LIMBO_GRID; x++)
                for (int y = 0; y < LIMBO_GRID; y++)
                {
                    if (!grid.Nodes.TryGetValue((x, y), out var node)) continue;
                    foreach (var d in dirs)
                    {
                        int nx = x + d.dx, ny = y + d.dy, nz = z + d.dz;
                        if (nx < 0 || nx >= LIMBO_GRID || ny < 0 || ny >= LIMBO_GRID || nz < 0 || nz >= LIMBO_GRID) continue;
                        var ng = d.dz == 0 ? grid : area.GetGrid(nz);
                        if (ng == null) continue;
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
        // Persist nodes and map
        using (var db = AtherizDbContextFactory.Create(absSave))
        {
            mh.Save(db);
        }
        using (var db = AtherizDbContextFactory.Create(absSave))
        {
            nh.Save(db);
        }

        // Resolve username/password — mirrors initial_setup.py:98-123
        string? u = username;
        string? p = password;
        if (string.IsNullOrWhiteSpace(u))
        {
            u = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME")?.Trim();
            if (string.IsNullOrWhiteSpace(u))
            {
                if (!Console.IsInputRedirected)
                {
                    Console.Write("Enter superuser username: ");
                    u = Console.ReadLine()?.Trim();
                }
            }
            else u = u!.Trim();
            if (string.IsNullOrWhiteSpace(u))
            {
                Console.Error.WriteLine("Error: Username cannot be empty.");
                // still save world without account? Python would return early after error during creation — we mimic that
                // but limbo already saved, so return
                Console.WriteLine("Initial world (limbo) created without superuser — run `create` to add account.");
                return;
            }
        }
        else if (u != null) u = u.Trim();

        if (string.IsNullOrWhiteSpace(p))
        {
            p = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD")?.Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                if (!Console.IsInputRedirected)
                {
                    Console.Write("Enter superuser password: ");
                    // simple no-echo fallback
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        ConsoleKeyInfo k;
                        while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
                        {
                            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
                            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
                        }
                        Console.WriteLine();
                        p = sb.ToString().Trim();
                    }
                    catch { p = Console.ReadLine()?.Trim(); }
                }
            }
            else p = p!.Trim();
            if (string.IsNullOrWhiteSpace(p))
            {
                Console.Error.WriteLine("Error: Password cannot be empty.");
                Console.WriteLine("Initial world (limbo) created without superuser — run `create` to add account.");
                return;
            }
        }
        else if (p != null) p = p.Trim();

        var errU = Validation.ValidateAccountName(u!);
        if (errU != null) throw new ArgumentException($"Invalid superuser username: {errU}");
        var errP = Validation.ValidatePassword(p!);
        if (errP != null) throw new ArgumentException($"Invalid superuser password: {errP}");

        // Alarm object at 0,0,8
        var alarmCoord = new Coord(LIMBO_AREA, 0, 0, LIMBO_GRID - 1);
        var alarmNode = nh.GetNode(alarmCoord);
        // If not found via handler, fallback to area grid directly
        if (alarmNode == null)
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

        var gt = new Globals.GameTime();
        gt.AddAlarm("?", "0", alarmObj, repeat: true);
        using (var db = AtherizDbContextFactory.Create(absSave)) gt.Save(db);

        // Account + character
        // Ensure salt with explicit secret path
        string saltVal;
        try { saltVal = SaltProvider.GetSalt(absSecret); } catch { saltVal = SaltProvider.GetSalt(absSecret); }
        var account = Account.Create(u!, p!, saltOverride: saltVal);
        Console.WriteLine($"Creating character '{u!}'...");
        var character = GameObject.Create(u!, isPc: true);
        character.Desc = "";
        // Port of Object.create add_object — faithful registry add before save
        ObjectRegistry.AddObject(character);
        var homeCoord = settings.DefaultHome; // limbo 4,4,4
        var home = nh.GetNode(homeCoord);
        if (home == null)
        {
            var hg = area.GetGrid(homeCoord.Z);
            hg?.Nodes.TryGetValue((homeCoord.X, homeCoord.Y), out home);
        }
        if (home != null)
        {
            character.Home = new Persistence.Dto.LocationRef.CoordLocation(home.Coord);
            character.PrivilegeLevel = Privilege.Admin;
            try { character.MoveTo(home); } catch {}
        }
        else character.PrivilegeLevel = Privilege.Admin;

        var button = GameObject.Create("A big red button", isItem: true);
        button.Desc = "A large button that glows with an ominous red light. Wonder if it does anything...";
        button.Aliases = new List<string>{"button"};
        button.AddLock("get", (GameObject x) => x.IsBuilder);
        if (button.ExternalCmdSet == null) button.ExternalCmdSet = new Commands.CmdSet();
        try { button.ExternalCmdSet.Add(new PushCommand()); } catch { }
        ObjectRegistry.AddObject(button);
        if (home != null) try { button.MoveTo(home); } catch {}

        account.AddCharacter(character);
        var chan = Channel.Create("Server");
        chan.AddLock("send", (GameObject x) => x.IsBuilder);
        chan.AddLock("view", (GameObject x) => x.IsBuilder);
        chan.Desc = "for server announcements";
        chan.AddListener(character);
        if (!character.ChannelsSnapshot.Contains(chan.Id))
            character.Subscribe(chan);

        using (var db = AtherizDbContextFactory.Create(absSave))
        {
            ObjectRegistry.SaveObjects(db, force: true);
        }
        using (var db = AtherizDbContextFactory.Create(absSave))
        {
            nh.Save(db);
        }
        Console.Error.WriteLine("Initial world state set up.");
    }
}
