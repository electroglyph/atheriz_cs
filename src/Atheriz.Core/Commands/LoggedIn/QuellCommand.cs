
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class QuellCommand : Command
{
    public override string Key => "quell";
    public override IReadOnlyList<string> Aliases => ["q"];
    public override string Desc => "Quell your privileges to the level of a normal player.";
    public override string Category => "Building";
    public override bool UseParser => false;
    // Raw privilege check (no quelled fold): a quelled builder must still
    // reach Run for the already-quelled branch, mirroring UnquellCommand.
    public override bool Access(IMessageTarget caller) => caller is GameObject g && g.PrivilegeLevel >= Privilege.Builder;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (go.Quelled) go.Msg("You are already quelled!");
        else { go.Quelled = true; go.Msg("You are now quelled."); }
    }
}
