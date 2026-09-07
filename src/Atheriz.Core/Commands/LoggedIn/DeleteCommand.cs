// Port of atheriz/commands/loggedin/delete.py:111
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class DeleteCommand : Command
{
    public override string Key => "delete";
    public override string Desc => "Delete an object permanently.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target").Help("Object to delete.").Nargs("+");
        p.AddArgument("-r", "--recursive").Help("Delete contents recursively.").Action(GameArgumentParser.ArgAction.StoreTrue);
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var targets = pa.GetList("target");
        if (targets.Count == 0) { go.Msg("Delete what?"); return; }
        string targetName = string.Join(" ", targets).Trim();
        var target = CommandHelpers.ResolveObject(go, targetName);
        if (target == null) return;
        if (!target.Access(go, "delete")) { go.Msg("You do not have permission to delete that."); return; }
        // Port of delete.py:92-94 _privilege_denied (set.py:58-70): self-exempt, target >= caller denied.
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot delete an object of equal or higher privilege."); return; }
        var fullName = target.GetDisplayName(go);
        var result = target.Delete(go, pa.GetBool("recursive"));
        if (result == null) { go.Msg("Deletion aborted."); return; }
        int count = result.Value.count;
        if (count > 1) go.Msg($"Deleted or moved {fullName}, {count} objects total.");
        else go.Msg($"Deleted {fullName}.");
    }
}
