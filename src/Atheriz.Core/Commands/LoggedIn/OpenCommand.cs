
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class OpenCommand : DoorDirectionCommand
{
    public override string Key => "open";
    public override string Desc => "Open doors.";
    public override string Category => "General";
    protected override string VerbNoun => "open";
    protected override void Act(Door d, GameObject go) => d.TryOpen(go);
}
