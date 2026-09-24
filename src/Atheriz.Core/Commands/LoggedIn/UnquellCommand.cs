
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnquellCommand : Command
{
    public override string Key => "unquell";
    public override IReadOnlyList<string> Aliases => ["unq"];
    public override string Desc => "Unquell your privileges.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => caller is GameObject g && g.PrivilegeLevel >= Privilege.Builder;
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!go.Quelled) go.Msg("You are not quelled!");
        else { go.Quelled = false; go.Msg("You are now unquelled."); }
    }
}
