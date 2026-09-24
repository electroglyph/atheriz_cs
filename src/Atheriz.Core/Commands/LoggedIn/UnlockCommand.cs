
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnlockCommand : DoorDirectionCommand
{
    public override string Key => "unlock";
    public override string Desc => "Unlock doors.";
    public override string Category => "General";
    protected override string VerbNoun => "unlock";
    protected override void Act(Door d, GameObject go) => d.TryUnlock(go);
}
