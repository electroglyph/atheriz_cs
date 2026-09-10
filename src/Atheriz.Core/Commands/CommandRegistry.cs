
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
        cs.Adds([
            new LoggedIn.BanCommand(),
            new LoggedIn.BuildCommand(),
            new LoggedIn.ChannelCommand(),
            new LoggedIn.CloseCommand(),
            new LoggedIn.CreateCommand(),
            new LoggedIn.DeleteCommand(),
            new LoggedIn.DescCommand(),
            new LoggedIn.DoorCommand(),
            new LoggedIn.DrawCommand(),
            new LoggedIn.DropCommand(),
            new LoggedIn.EmoteCommand(),
            new LoggedIn.ExamCommand(),
            new LoggedIn.FollowCommand(),
            new LoggedIn.GetCommand(),
            new LoggedIn.GiveCommand(),
            new LoggedIn.GroupCommand(),
            new LoggedIn.HelpCommand(),
            new LoggedIn.InventoryCommand(),
            new LoggedIn.LockCommand(),
            new LoggedIn.LookCommand(),
            new LoggedIn.MapCommand(),
            new LoggedIn.MazeCommand(),
            new LoggedIn.MoveCommand(),
            new LoggedIn.NofollowCommand(),
            new LoggedIn.NoneCommand(),
            new LoggedIn.NounCommand(),
            new LoggedIn.OpenCommand(),
            new LoggedIn.PuppetCommand(),
            new LoggedIn.PutCommand(),
            new LoggedIn.QuellCommand(),
            new LoggedIn.QuitCommand(),
            new LoggedIn.ReloadCommand(),
            new LoggedIn.SaveCommand(),
            new LoggedIn.SayCommand(),
            new UnloggedIn.ScreenReaderCommand(),
            new LoggedIn.SetCommand(),
            new LoggedIn.ShutdownCommand(),
            new LoggedIn.SocialsCommand(),
            new LoggedIn.SpamCommand(),
            new LoggedIn.TimeCommand(),
            new LoggedIn.UnbanCommand(),
            new LoggedIn.UnfollowCommand(),
            new LoggedIn.UnlockCommand(),
            new LoggedIn.UnpuppetCommand(),
            new LoggedIn.UnquellCommand(),
            new LoggedIn.UnsetCommand(),
            new LoggedIn.WanderCommand(),
        ]);
    }

    private static void RegisterUnloggedIn(CmdSet cs)
    {
        // Mirrors atheriz/commands/unloggedin/cmdset.py:14-26 conditional adds,
        // except all four verbs are ALWAYS registered: the dispatch gate
        // (CommandDispatcher.IsUnloggedInEnabled) demotes disabled verbs to
        // "none", so runtime re-enabling works without a registry reset.
        // One Adds takes the write lock once instead of once per verb.
        cs.Adds([
            new UnloggedIn.ConnectCommand(),
            new UnloggedIn.CreateAccountCommand(),
            new UnloggedIn.NewCharacterCommand(),
            new UnloggedIn.GuestCommand(),
            new UnloggedIn.NoneCommand(),
            new UnloggedIn.ScreenReaderCommand(),
            new UnloggedIn.HelpCommand(),
            new UnloggedIn.QuitCommand(),
        ]);
    }
}
