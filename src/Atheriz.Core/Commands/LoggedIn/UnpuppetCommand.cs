namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnpuppetCommand : Command
{
    public override string Key => "unpuppet";
    public override string Desc => "Release the puppeted object and return to your previous one.";
    public override string Category => "Building";
    public override bool UseParser => false;
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var sess = go.Session;
        if (sess is null) { go.Msg("You have no active session."); return; }
        bool ok = go.Unpuppet(sess);
        if (!ok) go.Msg("You are not puppeting anything.");
    }
}
