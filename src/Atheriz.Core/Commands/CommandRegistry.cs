
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

    // One home for the double-checked lazy init: first check is the fast
    // path, the lock re-checks before building so concurrent first touches
    // still register exactly once.
    private static CmdSet GetOrCreate(ref CmdSet? field, Action<CmdSet> register)
    {
        if (field is not null) return field;
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
        new LoggedIn.ReloadCommand(),
        new LoggedIn.SaveCommand(),
        new LoggedIn.ShutdownCommand(),
        new LoggedIn.SpamCommand(),
    ];

    // World building and moderation (Category "Building").
    private static IEnumerable<Command> BuildBuildingSet() =>
    [
        new LoggedIn.BanCommand(),
        new LoggedIn.BuildCommand(),
        new LoggedIn.CreateCommand(),
        new LoggedIn.DeleteCommand(),
        new LoggedIn.DescCommand(),
        new LoggedIn.DoorCommand(),
        new LoggedIn.ExamCommand(),
        new LoggedIn.MazeCommand(),
        new LoggedIn.MoveCommand(),
        new LoggedIn.NounCommand(),
        new LoggedIn.PuppetCommand(),
        new LoggedIn.QuellCommand(),
        new LoggedIn.SetCommand(),
        new LoggedIn.UnbanCommand(),
        new LoggedIn.UnpuppetCommand(),
        new LoggedIn.UnquellCommand(),
        new LoggedIn.UnsetCommand(),
        new LoggedIn.WanderCommand(),
    ];

    // Chat and channels (Category "Communication", shared screenreader).
    private static IEnumerable<Command> BuildCommunicationSet() =>
    [
        new LoggedIn.ChannelCommand(),
        new LoggedIn.EmoteCommand(),
        new LoggedIn.GroupCommand(),
        new LoggedIn.SayCommand(),
        new Common.ScreenReaderCommand(),
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
        new LoggedIn.DrawCommand(),
        new LoggedIn.HelpCommand(),
        new LoggedIn.LookCommand(),
        new LoggedIn.MapCommand(),
        new LoggedIn.NoneCommand(),
        new LoggedIn.QuitCommand(),
        new LoggedIn.TimeCommand(),
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

    // Session verbs: fallback, screenreader, help, quit.
    private static IEnumerable<Command> BuildSessionSet() =>
    [
        new UnloggedIn.NoneCommand(),
        new UnloggedIn.ScreenReaderCommand(),
        new UnloggedIn.HelpCommand(),
        new UnloggedIn.QuitCommand(),
    ];
}
