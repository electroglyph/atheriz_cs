// Port of atheriz/commands/loggedin/open.py:345
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class OpenCommand : DoorDirectionCommand
{
    public override string Key => "open";
    public override string Desc => "Open doors.";
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";
    public override string Category => "General";
    protected override string VerbNoun => "open";
    protected override void Act(Door d, GameObject go) => d.TryOpen(go);
}

public sealed class CloseCommand : DoorDirectionCommand
{
    public override string Key => "close";
    public override string Desc => "Close doors.";
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";
    public override string Category => "General";
    protected override string VerbNoun => "close";
    protected override void Act(Door d, GameObject go) => d.TryClose(go);
}

public sealed class LockCommand : DoorDirectionCommand
{
    public override string Key => "lock";
    public override string Desc => "Lock doors.";
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";
    public override string Category => "General";
    protected override string VerbNoun => "lock";
    protected override void Act(Door d, GameObject go) => d.TryLock(go);
}

public sealed class UnlockCommand : DoorDirectionCommand
{
    public override string Key => "unlock";
    public override string Desc => "Unlock doors.";
    public override string ExtraDesc => "Also accepts n,s,e,w,u,d as arguments.";
    public override string Category => "General";
    protected override string VerbNoun => "unlock";
    protected override void Act(Door d, GameObject go) => d.TryUnlock(go);
}
