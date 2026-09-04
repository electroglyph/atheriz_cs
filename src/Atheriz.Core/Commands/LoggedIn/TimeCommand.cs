// Port of atheriz/commands/loggedin/time.py:17
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class TimeCommand : Command
{
    public override string Key => "time";
    public override string Desc => "Show the current time.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        try
        {
            var gt = GlobalServices.GetGameTime();
            var info = gt.GetTime();
            go.Msg(info.Formatted);
        }
        catch { go.Msg("Time system unavailable."); }
    }
}
