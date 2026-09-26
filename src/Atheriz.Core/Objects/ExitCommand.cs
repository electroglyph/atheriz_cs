
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
    public override void Run(CommandContext ctx)
    {
        if (ctx.Caller is GameObject go)
        {
            var nh = NodeHandler.GetCurrent();
            var dest = nh?.GetNode(Destination);
            if (dest is null)
            {
                go.Msg("You can't go that way.");
                return;
            }
            // Delegate to the door-aware state machine: AddExits installs
            // this type for its per-exit Key/Aliases/Tag support, while
            // LoggedInExitCommand owns the TryOpen/move/TryClose sequence
            // (auto-open closed-unlocked doors with announces, refuse locked
            // ones with the door's own message). A closed-unlocked door must
            // traverse, not refuse.
            var twin = new Commands.LoggedIn.LoggedInExitCommand
            {
                Location = Location,
                Destination = Destination,
                ExitName = ExitName,
            };
            twin.DoMove(go);
        }
    }
}
