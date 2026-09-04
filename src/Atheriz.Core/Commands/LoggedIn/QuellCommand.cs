// Port of atheriz/commands/loggedin/quell.py:53
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class QuellCommand : Command
{
    public override string Key => "quell";
    public override IReadOnlyList<string> Aliases => ["q"];
    public override string Desc => "Quell your privileges to the level of a normal player.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        if (go.Quelled) go.Msg("You are already quelled!");
        else { go.Quelled = true; go.Msg("You are now quelled."); }
    }
}

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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        if (!go.Quelled) go.Msg("You are not quelled!");
        else { go.Quelled = false; go.Msg("You are now unquelled."); }
    }
}