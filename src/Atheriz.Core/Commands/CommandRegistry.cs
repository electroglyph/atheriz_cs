using Atheriz.Core.Globals;

namespace Atheriz.Core.Commands;

/// <summary>
/// Global command set registries. Mirrors <c>atheriz/globals/get.py:get_loggedin_cmdset/get_unloggedin_cmdset</c>.
/// Thread-safe double-checked via lock.
/// </summary>
public static class CommandRegistry
{
    private static readonly object Lock = new();
    private static CmdSet? _loggedIn;
    private static CmdSet? _unloggedIn;

    public static CmdSet LoggedIn
    {
        get
        {
            if (_loggedIn is not null) return _loggedIn;
            lock (Lock)
            {
                if (_loggedIn is not null) return _loggedIn;
                var cs = new CmdSet();
                RegisterLoggedIn(cs);
                _loggedIn = cs;
                return _loggedIn;
            }
        }
    }

    public static CmdSet UnloggedIn
    {
        get
        {
            if (_unloggedIn is not null) return _unloggedIn;
            lock (Lock)
            {
                if (_unloggedIn is not null) return _unloggedIn;
                var cs = new CmdSet();
                RegisterUnloggedIn(cs);
                _unloggedIn = cs;
                return _unloggedIn;
            }
        }
    }

    public static void ResetForTesting()
    {
        lock (Lock) { _loggedIn = null; _unloggedIn = null; }
    }

    private static void RegisterLoggedIn(CmdSet cs)
    {
        // wontfix py.py:592 sandbox — excluded per plan.md; use Roslyn if re-enabled
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
        // Mirrors atheriz/commands/unloggedin/cmdset.py:14-26 conditional adds
        cs.Add(new UnloggedIn.ConnectCommand());
        if (Atheriz.Core.Settings.AtherizSettings.Global.AccountCreationEnabled)
            cs.Add(new UnloggedIn.CreateAccountCommand());
        if (Atheriz.Core.Settings.AtherizSettings.Global.CharCreationEnabled)
            cs.Add(new UnloggedIn.NewCharacterCommand());
        if (Atheriz.Core.Settings.AtherizSettings.Global.GuestEnabled)
            cs.Add(new UnloggedIn.GuestCommand());
        cs.Add(new UnloggedIn.NoneCommand());
        cs.Add(new UnloggedIn.ScreenReaderCommand());
        cs.Add(new UnloggedIn.HelpCommand());
        cs.Add(new UnloggedIn.QuitCommand());
    }
}
