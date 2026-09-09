// Port of atheriz/commands/loggedin/time.py:17

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class TimeCommand : Command
{
    public override string Key => "time";
    public override string Desc => "Show the current time.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        try
        {
            var gt = GlobalServices.GetGameTime();
            var info = gt.GetTime();
            go.Msg(info.Formatted);
        }
        catch { go.Msg("Time system unavailable."); }
    }
}
