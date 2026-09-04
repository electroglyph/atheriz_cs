using Atheriz.Core.Network;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class QuitCommand : Command
{
    public override string Key => "quit";
    public override IReadOnlyList<string> Aliases => ["exit", "logout", "disconnect"];
    public override string Desc => "Quit.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        caller.Msg("Goodbye!");
        try
        {
            var sess = ((dynamic)caller).Session as Session;
            sess?.Connection?.Close();
        }
        catch { }
        if (caller is GameObject go)
        {
            try { go.Session?.Connection?.Close(); } catch { }
        }
        if (caller is BaseConnection bc)
        {
            try { bc.Close(); } catch { }
        }
    }
}

