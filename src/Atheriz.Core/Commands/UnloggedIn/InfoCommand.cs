namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class InfoCommand : Command
{
    public override string Key => "info";
    public override string Desc => "Show server info.";
    public override string Category => "General";

    // Same block as the logged-in twin, before and after login.
    public override void Run(CommandContext ctx)
        => ctx.Caller.Msg(Atheriz.Core.Commands.LoggedIn.InfoCommand.BuildInfoBlock());
}
