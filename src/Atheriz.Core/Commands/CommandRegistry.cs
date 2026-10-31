
namespace Atheriz.Core.Commands;

/// <summary>
/// Global command set registries. Mirrors <c>atheriz/globals/get.py:get_loggedin_cmdset/get_unloggedin_cmdset</c>.
/// Thread-safe double-checked via lock.
/// </summary>
public static class CommandRegistry
{
    private static readonly Lock Lock = new();
    private static CmdSet? _loggedIn;
    private static CmdSet? _unloggedIn;

    // One home for the double-checked lazy init: the fast path reads via
    // Volatile (a plain read can observe a half-published instance on
    // weak-memory hardware — C10); the lock re-checks before building so
    // concurrent first touches still register exactly once.
    private static CmdSet GetOrCreate(ref CmdSet? field, Action<CmdSet> register)
    {
        var snap = Volatile.Read(ref field);
        if (snap is not null) return snap;
        lock (Lock)
        {
            if (field is not null) return field;
            var cs = new CmdSet();
            register(cs);
            field = cs;
            return field;
        }
    }

    public static CmdSet LoggedIn
    {
        get => GetOrCreate(ref _loggedIn, RegisterLoggedIn);
    }

    public static CmdSet UnloggedIn
    {
        get => GetOrCreate(ref _unloggedIn, RegisterUnloggedIn);
    }

    public static void Reset()
    {
        lock (Lock) { _loggedIn = null; _unloggedIn = null; }
    }

    private static void RegisterLoggedIn(CmdSet cs)
    {
        // wontfix py.py sandbox (atheriz/commands/loggedin/py.py) — intentionally never ported, ever
        // (AGENTS.md hard rule; C# will never execute Python). No Roslyn/scripting backdoor either.
        // 44+ commands sorted by key (py excluded)
        // Family builders group the explicit adds by category/function; each
        // family keeps its original relative order. The flattened insertion
        // order regroups by family, which is safe: CmdSet is key-addressed
        // and every listing path (help tables, dispatch) sorts before display.
        cs.Adds([
            ..BuildAdminSet(),
            ..BuildBuildingSet(),
            ..BuildCommunicationSet(),
            ..BuildSocialSet(),
            ..BuildItemSet(),
            ..BuildMovementSet(),
            ..BuildInfoSet(),
        ]);
    }

    // Server administration: the Admin category plus superuser-gated reload.
    private static IEnumerable<Command> BuildAdminSet() =>
    [
        new LoggedIn.AccountsCommand(),
        new LoggedIn.BootCommand(),
        new LoggedIn.PermCommand(),
        new LoggedIn.ReloadCommand(),
        new LoggedIn.ResetCommand(),
        new LoggedIn.RestartCommand(),
        new LoggedIn.SaveCommand(),
        new LoggedIn.ShutdownCommand(),
        new LoggedIn.SpamCommand(),
        new LoggedIn.UserpasswordCommand(),
        new LoggedIn.WallCommand(),
    ];

    // World building and moderation (Category "Building").
    private static IEnumerable<Command> BuildBuildingSet() =>
    [
        new LoggedIn.AliasCommand(),
        new LoggedIn.BanCommand(),
        new LoggedIn.BuildCommand(),
        new LoggedIn.CopyCommand(),
        new LoggedIn.CreateCommand(),
        new LoggedIn.DeleteCommand(),
        new LoggedIn.DescCommand(),
        new LoggedIn.DoorCommand(),
        new LoggedIn.EmitCommand(),
        new LoggedIn.ExamCommand(),
        new LoggedIn.FindCommand(),
        new LoggedIn.ForceCommand(),
        new LoggedIn.MazeCommand(),
        new LoggedIn.MoveCommand(),
        new LoggedIn.NameCommand(),
        new LoggedIn.NounCommand(),
        new LoggedIn.ObjectsCommand(),
        new LoggedIn.PuppetCommand(),
        new LoggedIn.QuellCommand(),
        new LoggedIn.ScriptsCommand(),
        new LoggedIn.SetCommand(),
        new LoggedIn.TagCommand(),
        new LoggedIn.TeleportCommand(),
        new LoggedIn.UnbanCommand(),
        new LoggedIn.UnpuppetCommand(),
        new LoggedIn.UnquellCommand(),
        new LoggedIn.UnsetCommand(),
        new LoggedIn.WanderCommand(),
        new LoggedIn.WipeCommand(),
    ];

    // Chat and channels (Category "Communication", shared screenreader).
    private static IEnumerable<Command> BuildCommunicationSet() =>
    [
        new LoggedIn.ChannelCommand(),
        new LoggedIn.EmoteCommand(),
        new LoggedIn.GroupCommand(),
        new LoggedIn.PageCommand(),
        new LoggedIn.SayCommand(),
        new LoggedIn.YellCommand(),
        new Common.ScreenReaderCommand(),
        new LoggedIn.WhisperCommand(),
    ];

    // Social verbs (Category "Socials").
    private static IEnumerable<Command> BuildSocialSet() =>
    [
        new LoggedIn.SocialsCommand(),
    ];

    // Item handling: containment verbs plus inventory.
    private static IEnumerable<Command> BuildItemSet() =>
    [
        new LoggedIn.DropCommand(),
        new LoggedIn.GetCommand(),
        new LoggedIn.GiveCommand(),
        new LoggedIn.InventoryCommand(),
        new LoggedIn.PutCommand(),
    ];

    // Doors plus follow/unfollow (movement between rooms and with others).
    private static IEnumerable<Command> BuildMovementSet() =>
    [
        new LoggedIn.CloseCommand(),
        new LoggedIn.FollowCommand(),
        new LoggedIn.LockCommand(),
        new LoggedIn.NofollowCommand(),
        new LoggedIn.OpenCommand(),
        new LoggedIn.UnfollowCommand(),
        new LoggedIn.UnlockCommand(),
    ];

    // Information, session, and fallback verbs.
    private static IEnumerable<Command> BuildInfoSet() =>
    [
        new LoggedIn.AboutCommand(),
        new LoggedIn.AccessCommand(),
        new LoggedIn.ChardeleteCommand(),
        new LoggedIn.ColorCommand(),
        new LoggedIn.DrawCommand(),
        new LoggedIn.HelpCommand(),
        new LoggedIn.HomeCommand(),
        new LoggedIn.InfoCommand(),
        new LoggedIn.LookCommand(),
        new LoggedIn.MapCommand(),
        new LoggedIn.NickCommand(),
        new LoggedIn.NoneCommand(),
        new LoggedIn.PasswordCommand(),
        new LoggedIn.QuitCommand(),
        new LoggedIn.SessionsCommand(),
        new LoggedIn.TimeCommand(),
        new LoggedIn.WhoCommand(),
    ];

    private static void RegisterUnloggedIn(CmdSet cs)
    {
        // Mirrors atheriz/commands/unloggedin/cmdset.py:14-26 conditional adds,
        // except all four verbs are ALWAYS registered: the dispatch gate
        // (CommandDispatcher.IsUnloggedInEnabled) demotes disabled verbs to
        // "none", so runtime re-enabling works without a registry reset.
        // One Adds takes the write lock once instead of once per verb.
        cs.Adds([..BuildAccountSet(), ..BuildSessionSet()]);
    }

    // Account creation and login verbs.
    private static IEnumerable<Command> BuildAccountSet() =>
    [
        new UnloggedIn.ConnectCommand(),
        new UnloggedIn.CreateAccountCommand(),
        new UnloggedIn.NewCharacterCommand(),
        new UnloggedIn.GuestCommand(),
    ];

    // Session verbs: fallback, screenreader, help, quit, info.
    private static IEnumerable<Command> BuildSessionSet() =>
    [
        new UnloggedIn.NoneCommand(),
        new UnloggedIn.ScreenReaderCommand(),
        new UnloggedIn.HelpCommand(),
        new UnloggedIn.InfoCommand(),
        new UnloggedIn.QuitCommand(),
    ];
}
