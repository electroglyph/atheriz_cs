
namespace Atheriz.Core.Objects;

public sealed class ExitCommand : Command
{
    public int CallerId { get; set; }
    public Coord Location { get; set; }
    public Coord Destination { get; set; }
    private string _key = "";
    public override string Key => _key;
    public string ExitName { get; set; } = "";
    private List<string> _aliases = [];
    public override IReadOnlyList<string> Aliases => [.. _aliases];
    public void SetKey(string k) { _key = k; ExitName = k; }
    // Copies: the caller (Node.AddExits) passes a live-list snapshot it keeps
    // mutating, so the command must own its list.
    public void SetAliases(List<string> a) => _aliases = a is null ? [] : [.. a];
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is GameObject go)
        {
            var dest = NodeHandler.GetCurrent()?.GetNode(Destination);
            if (dest is not null)
            {
                // through an exit breaks following like any other move.
                try { Commands.LoggedIn.LoggedInExitCommand.ClearFollowing(go); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ExitCommand.Run: " + logEx.Message, "ExitCommand"); }
                go.MoveTo(dest);
            }
            else go.Msg("You can't go that way.");
        }
    }
}
