
namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnsetCommand : Command
{
    public override string Key => "unset";
    public override string Desc => "Delete an attribute from an object.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target, help: "Object to modify (name, #id, 'me', or 'here').");
        p.AddArgument(ParsedArgKeys.Attribute, help: "Attribute name to delete.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var targetStr = pa.GetString(ParsedArgKeys.Target) ?? "";
        var attr = pa.GetString(ParsedArgKeys.Attribute) ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target is null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
// only the shared protected set is checked.
        if (SetHelper.IsProtected(attr))
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is protected and cannot be removed."); return; }
        }
        if (SetHelper.MoveGate.Contains(attr)) { go.Msg($"'{attr}' cannot be removed directly."); return; }
        try
        {
            if (SetHelper.HasKnownProp(target, attr)) throw new InvalidOperationException();
            bool had = SetHelper.HasAttr(target, attr);
            // also check _extra directly via SetHelper
            if (!had)
            {
                // Fallback direct check already done in HasAttr; just verify
                go.Msg($"{target.Name} has no attribute '{attr}'.");
                return;
            }
            // Try extra removal via helper
            bool removed = SetHelper.TryRemoveExtra(target, attr);
            if (!removed) { go.Msg($"{target.Name} has no attribute '{attr}'."); return; }
            target.IsModified = true;
        }
        catch { go.Msg($"'{attr}' is a read-only attribute and cannot be removed."); return; }
        go.Msg($"Deleted {target.Name}.{attr}");
    }
}
