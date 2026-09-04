// Port of atheriz/commands/loggedin/quit.py:20
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class QuitCommand : Command
{
    public override string Key => "quit";
    // "exit" alias is verbatim Python quit.py:14. It never collides live with the room-exit
    // command: room exits are per-direction ExitCommands in the puppet's InternalCmdSet,
    // which CommandDispatcher consults BEFORE the global registry — so in-room "exit"
    // (if ever keyed literally) wins, otherwise this quit alias fires. Do not remove.
    public override IReadOnlyList<string> Aliases => ["exit", "logout", "disconnect"];
    public override string Desc => "Quit.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        caller.Msg("Goodbye!");
        if (caller is GameObject go)
        {
            try { go.Session?.Connection?.Close(); } catch { }
        }
        else if (caller is Session sess)
        {
            try { sess.Connection?.Close(); } catch { }
        }
    }
}
