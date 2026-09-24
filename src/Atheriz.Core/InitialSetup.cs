using Microsoft.EntityFrameworkCore;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Persistence;

namespace Atheriz.Core;

/// <summary>
/// Builds 9x9x9 limbo cube, map placeholders, alarm/dashboard, superuser account.
/// Mirrors Python constants LIMBO_AREA=limbo, LIMBO_GRID=9, LIMBO_DESC etc.
/// </summary>
public static class InitialSetup
{
    public const string LIMBO_AREA = "limbo";
    public const int LIMBO_GRID = 9;
    public static int LIMBO_CENTER => LIMBO_GRID / 2; // 4
    public const string LIMBO_DESC = "You are in a vast nothingness.";

    public sealed class PushCommand : Command
    {
        public override string Key => "push";
        public override string Desc => "It's a button, you can push it.";
        public override string Category => "Danger?";
        public override bool UseParser => false;
        public override void Run(CommandContext ctx)
        {
            if (ctx.Caller is GameObject go)
            {
                var loc = go.ResolveLocationObject();
                loc?.MsgContents("BEEEEEP!", go);
            }
        }
    }

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

    /// <summary>
    /// Registers the engine-owned <see cref="GameObject"/> subtypes that live
    /// in the world for persistence round-trips. Unregistered they save as
    /// their base kind with a loud log and reload without their overrides
    /// (dead dashboard klaxon, frozen wanderers). Idempotent: call on both
    /// the world-writing (DoSetup) and world-loading (DoStartup) paths —
    /// setup and daemon are separate processes with per-process registries.
    /// FollowScript is save-exempt today via IsTemporary, but registered so
    /// direct serializations stay quiet if that ever changes.
    /// </summary>
    internal static void RegisterPersistedSubtypes()
    {
        GameObject.RegisterPersistedSubtype(typeof(AlarmObject).FullName ?? nameof(AlarmObject), typeof(AlarmObject), () => new AlarmObject());
        GameObject.RegisterPersistedSubtype(typeof(Objects.FollowScript).FullName ?? nameof(Objects.FollowScript), typeof(Objects.FollowScript), () => new Objects.FollowScript());
        GameObject.RegisterPersistedSubtype(typeof(Commands.LoggedIn.WanderCommand.WandererNpc).FullName ?? nameof(Commands.LoggedIn.WanderCommand.WandererNpc), typeof(Commands.LoggedIn.WanderCommand.WandererNpc), () => new Commands.LoggedIn.WanderCommand.WandererNpc());
    }

    /// <summary>
    /// World-creation dispatch shared by <c>reset</c> and <c>new</c>: runs the
    /// game setup handed over from plugin discovery when present, else the
    /// engine template. Without this every game resets into the template
    /// world instead of its own. The game value rides an explicit parameter
    /// — no static slot — because only short-lived CLI processes read it.
    /// </summary>
    public static void RunSetup(SetupOptions options, IGameSetup? game = null)
    {
        if (game is not null)
        {
            AtherizLogger.LogInformation("[Setup] Running game world setup.");
            game.DoSetup(options);
            return;
        }
        AtherizLogger.LogInformation("[Setup] Running template world setup.");
        DoSetup(options);
    }

    /// <summary>Seed-world handles built by <see cref="BuildLimboWorld"/>:
    /// node handler + area plus map handler, published for same-process
    /// readers and handed to <see cref="PersistSeed"/> for the checkpoint.
    /// </summary>
    internal sealed record SeedWorld(NodeHandler Nodes, NodeArea Area, MapHandler Maps);

    /// <summary>Resolved superuser credentials (null = skip the superuser;
    /// limbo still commits).</summary>
    internal sealed record SeedCredentials(string Username, string Password);

    public static void DoSetup(SetupOptions options)
    {
// not duplicated to stdout (new.py:740 already prints)
        var (absSave, absSecret) = PrepareDirectories(options.SavePath, options.SecretPath);
        AtherizDbContextFactory.DoSetup(absSave);
        WarmSalt(absSecret);
        ResetWorldState(absSecret);
        var settings = AtherizSettings.Global;
        var world = BuildLimboWorld(settings);
        var creds = ResolveCredentials(options);
        PersistSeed(world, settings, creds, absSave, absSecret);
    }

    // Directory/salt-path prep: absolute save + secret paths, created with
    // locked-down permissions.
    private static (string AbsSave, string AbsSecret) PrepareDirectories(string savePath, string? secretPath)
    {
        // Ensure savePath absolute for guard
        var absSave = Path.GetFullPath(savePath);
        var absSecret = secretPath is not null ? Path.GetFullPath(secretPath) : Path.Combine(Path.GetDirectoryName(absSave) ?? ".", "secret");
        Directory.CreateDirectory(absSecret);
        Utils.FsUtil.TryChmod0700(absSecret);
        return (absSave, absSecret);
    }

    // Best-effort salt warm-up (mirrors Python SECRET_PATH override).
    private static void WarmSalt(string absSecret)
    {
        try
        {
            // Force salt creation with explicit path
            SaltProvider.GetSalt(absSecret);
        }
        catch (Exception ex) { AtherizLogger.LogWarning($"Salt setup warning: {ex.Message}"); }
    }

    // Reset registries for fresh world (mirrors globals cleared by conftest).
    // (ClearAll already resets the Id generator — no separate SetId.)
    private static void ResetWorldState(string absSecret)
    {
        ObjectRegistry.ClearAll();
        // Clear any existing node/map/time singletons that might cache old save path
        try { Globals.GlobalServices.Reset(); } catch (Exception ex) { AtherizLogger.LogWarning($"Singleton reset warning: {ex.Message}"); }
        SaltProvider.Clear();
        // Re-seed salt after clear (default slot included — see ReseedForGame).
        SaltProvider.ReseedForGame(absSecret);
    }

    /// <summary>
    /// Pure world build: 9x9x9 limbo cube with links plus map placeholders,
    /// published through the singleton setters so GlobalServices and
    /// GetCurrent agree. No prompting, no database writes.
    /// </summary>
    internal static SeedWorld BuildLimboWorld(AtherizSettings settings)
    {
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
                    grid.AddNodeRaw(node);
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
                    if (grid.GetNode(x, y) is not { } node) continue;
                    foreach (var d in dirs)
                    {
                        int nx = x + d.dx, ny = y + d.dy, nz = z + d.dz;
                        if (nx < 0 || nx >= LIMBO_GRID || ny < 0 || ny >= LIMBO_GRID || nz < 0 || nz >= LIMBO_GRID) continue;
                        var ng = d.dz == 0 ? grid : area.GetGrid(nz);
                        if (ng is null) continue;
                        if (ng.GetNode(nx, ny) is not { } neighbor) continue;
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
                    mi.SetPreCell((x, y), settings.RoomPlaceholder);
                    mi.PlaceWalls((x, y), settings.SingleWallPlaceholder);
                }
            mi.PreRender();
            mh.SetMapInfo(LIMBO_AREA, z, mi);
        }
        // Same twin-slot publish as the node handler above: readers through
        // GlobalServices must see the seed world, not a stale handler.
        Globals.GlobalServices.SetMapHandler(mh);
        return new SeedWorld(nh, area, mh);
    }

    // Resolve username/password — mirrors initial_setup.py:98-123.
    // Returns null when nothing usable was provided: limbo commits without
    // a superuser. Resolution stays outside the write gate/transaction in
    // PersistSeed: holding either across interactive reads turned every
    // slow operator into a TimeoutException (and a pinned DB) for all
    // concurrent savers.
    internal static SeedCredentials? ResolveCredentials(SetupOptions options)
    {
        string? u = options.Username;
        string? p = options.Password;
        // An explicitly provided reader is used as-is (test/console-redirect
        // seams); otherwise prompts need a real console like before.
        TextReader? promptInput = options.Input ?? (options.Prompt && !Console.IsInputRedirected ? Console.In : null);
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

        if (string.IsNullOrWhiteSpace(u))
        {
            Console.Error.WriteLine("Error: Username cannot be empty.");
            // credential prompts — limbo is durable even with no superuser.
            return null;
        }

        if (string.IsNullOrWhiteSpace(p))
        {
            // Passwords are verbatim: unlike usernames they are never
            // trimmed (CheckPassword compares the exact string, so a
            // trimmed store would lock out the typed password — C3).
            // Whitespace-only still counts as missing (empty check below).
            p = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
            if (string.IsNullOrWhiteSpace(p))
            {
                if (promptInput is not null)
                {
                    Console.Out.Write("Enter superuser password: ");
                    // simple no-echo fallback
                    try
                    {
                        if (ReferenceEquals(promptInput, Console.In)) p = GameUtils.ReadSecretLine();
                        else p = promptInput.ReadLine();
                    }
                    catch { p = promptInput.ReadLine(); }
                }
            }
            if (string.IsNullOrWhiteSpace(p))
            {
                Console.Error.WriteLine("Error: Password cannot be empty.");
                // Limbo commits (see username branch above).
                return null;
            }
        }

        {
            var errU = Validation.ValidateAccountName(u!);
            if (errU is not null) throw new ArgumentException($"Invalid superuser username: {errU}");
            var errP = Validation.ValidatePassword(p!);
            if (errP is not null) throw new ArgumentException($"Invalid superuser password: {errP}");
        }
        return new SeedCredentials(u!, p!);
    }

    /// <summary>
    /// Atomic checkpoint: one context + transaction for the whole seed
    /// (previously five independent commits left half-built worlds on
    /// mid-setup failure). Any early return / throw below disposes the
    /// transaction uncommitted (rollback); only the Commit persists.
    /// </summary>
    private static void PersistSeed(SeedWorld world, AtherizSettings settings, SeedCredentials? creds, string absSave, string absSecret)
    {
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
        world.Maps.Save(db);
        world.Nodes.Save(db);

        if (creds is null)
        {
            setupTx.Commit();
            Console.Out.WriteLine("Initial world (limbo) created without superuser — run `create` to add account.");
            return;
        }

        SeedAccount(world, settings, creds, absSecret, db);

// default force=False:
        // persist only modified objects. Engine subtypes must be registered
        // first or they save as their base kind with a loud log.
        RegisterPersistedSubtypes();
        ObjectRegistry.SaveObjects(db, force: false);
        world.Nodes.Save(db);
        setupTx.Commit();
        AtherizLogger.LogInformation("Initial world state set up.");
        }
        finally { Atheriz.Core.Persistence.DbWriteGate.Exit(); }
    }

    // Seeded nodes resolve via the handler first, falling back to the area
    // grid directly (the handler may not have the area yet during setup).
    private static Node? ResolveSeedNode(NodeHandler nh, NodeArea area, Coord coord)
    {
        var node = nh.GetNode(coord);
        if (node is null)
        {
            node = area.GetGrid(coord.Z)?.GetNode(coord.X, coord.Y);
        }
        return node;
    }

    /// <summary>
    /// In-registry account content for the seed checkpoint: dashboard alarm,
    /// clock, superuser account + character, button, channel. Called inside
    /// <see cref="PersistSeed"/>'s transaction; persistence itself stays there.
    /// </summary>
    private static void SeedAccount(SeedWorld world, AtherizSettings settings, SeedCredentials creds, string absSecret, AtherizDbContext db)
    {
        var nh = world.Nodes;
        var area = world.Area;
        var u = creds.Username;
        var p = creds.Password;

        // Alarm object at 0,0,8
        var alarmCoord = new Coord(LIMBO_AREA, 0, 0, LIMBO_GRID - 1);
        var alarmNode = ResolveSeedNode(nh, area, alarmCoord);
        var alarmObj = new AlarmObject();
        alarmObj.Name = "A flashing dashboard";
        alarmObj.Desc = "A large display showing a multitude of plots and status readouts.";
        alarmObj.IsItem = true;
        alarmObj.Aliases = new List<string>{"dashboard"};
        alarmObj.IsModified = true;
        ObjectRegistry.AddObject(alarmObj);
        alarmObj.MoveTo(alarmNode);

        // seed the clock from the operator's time settings (the same
        // AtherizSettings.Global the nodes/map above use), not defaults —
        // custom TickMinutes/SecondsPerMinute/calendar must apply to the
        // seed clock too. (Setup persists via the explicit db handle, so no
        // SavePath override is needed here.)
        var gt = new Globals.GameTime(settings, autoLoad: false);
        gt.AddAlarm("?", "0", alarmObj, repeat: true);
        gt.Save(db);

        // Account + character
        // Ensure salt with explicit secret path. Single call: the old
        // catch-retry invoked the identical read with no backoff, no state
        // change and no logging between attempts, so a second call could only
        // succeed on a transient self-healing fault inside one synchronous
        // window (practically nil for a local file read). Failure throws
        // identically either way; the warn above and reseed below own the
        // failure story.
        string saltVal = SaltProvider.GetSalt(absSecret);
        var account = Account.Create(u, p, saltOverride: saltVal);
        Console.Out.WriteLine($"Creating character '{u}'...");
        var character = GameObject.Create(u, isPc: true);
        character.Desc = "";
// faithful registry add before save
        ObjectRegistry.AddObject(character);
        var homeCoord = settings.DefaultHome; // limbo 4,4,4
        var home = ResolveSeedNode(nh, area, homeCoord);
        if (home is not null)
        {
            character.Home = Persistence.Dto.LocationRef.FromCoord(home.Coord);
            character.PrivilegeLevel = Privilege.Admin;
            character.MoveTo(home);
        }
        else character.PrivilegeLevel = Privilege.Admin;

        if (!LockPolicies.TryResolve(LockPolicies.Builder, out var builderPred))
            throw new InvalidOperationException($"Unknown lock policy '{LockPolicies.Builder}' while seeding world locks.");
        var button = GameObject.Create("A big red button", isItem: true);
        button.Desc = "A large button that glows with an ominous red light. Wonder if it does anything...";
        button.Aliases = new List<string>{"button"};
        button.AddLock("get", builderPred, LockPolicies.Builder);
        if (button.ExternalCmdSet is null) button.ExternalCmdSet = new Commands.CmdSet();
        button.ExternalCmdSet.Add(new PushCommand());
        ObjectRegistry.AddObject(button);
        if (home is not null) button.MoveTo(home);

        account.AddCharacter(character);
        var chan = Channel.Create("Server");
        chan.AddLock("send", builderPred, LockPolicies.Builder);
        chan.AddLock("view", builderPred, LockPolicies.Builder);
        chan.Desc = "for server announcements";
        chan.AddListener(character);
        if (!character.ChannelsSnapshot.Contains(chan.Id))
            character.Subscribe(chan);
    }
}
