namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class QuitCommand : Command
{
    public override string Key => "quit";
    // "exit" alias is verbatim Python quit.py:14 — kept (see LoggedIn QuitCommand note). No room-exit
    // command exists pre-login, so no shadowing applies here at all.
    public override IReadOnlyList<string> Aliases => ["exit", "logout", "disconnect"];
    public override string Desc => "Quit.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        caller.Msg("Goodbye!");
        LoggedIn.ConnectionHelper.CloseQuietly(caller);
    }
}

