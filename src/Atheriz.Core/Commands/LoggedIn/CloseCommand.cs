
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class CloseCommand : DoorDirectionCommand
{
    public override string Key => "close";
    public override string Desc => "Close doors.";
    public override string Category => "General";
    protected override string VerbNoun => "close";
    protected override void Act(Door d, GameObject go) => d.TryClose(go);
}
