
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class LockCommand : DoorDirectionCommand
{
    public override string Key => "lock";
    public override string Desc => "Lock doors.";
    public override string Category => "General";
    protected override string VerbNoun => "lock";
    protected override void Act(Door d, GameObject go) => d.TryLock(go);
}
