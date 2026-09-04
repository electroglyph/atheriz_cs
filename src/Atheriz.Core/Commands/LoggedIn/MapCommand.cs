// Port of atheriz/commands/loggedin/map.py:30
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class MapCommand : Command
{
    public override string Key => "map";
    public override string Desc => "Toggle map display.";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        go.MapEnabled = !go.MapEnabled;
        if (go.MapEnabled)
        {
            go.Msg("Map enabled.");
            try { go.Session?.Connection?.SendCommand("map_enable", new List<object?> { "" }, null); } catch { }
            if (AtherizSettings.Global.MapEnabled)
            {
                var loc = go.ResolveLocationObject() as Node;
                if (loc != null)
                {
                    try
                    {
                        var mh = GlobalServices.GetMapHandler();
                        var mi = mh.GetMapInfo(loc.Coord.Area, loc.Coord.Z);
                        mi?.Render(true);
                    }
                    catch { }
                }
            }
        }
        else
        {
            go.Msg("Map disabled.");
            try { go.Session?.Connection?.SendCommand("map_disable", new List<object?> { "" }, null); } catch { }
        }
    }
}
