// Port of atheriz/commands/loggedin/set.py:243
using System.Text.Json;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class SetCommand : Command
{
    public override string Key => "set";
    public override string Desc => "Set an attribute on an object.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target", help: "Object to modify (name, #id, 'me', or 'here').");
        p.AddArgument("attribute", help: "Attribute name to set.");
        p.AddArgument("value", help: "Value to set (evaluated with ast.literal_eval).");
    }
    private static object? ConvertJsonElement(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.TryGetInt32(out var i) ? i : je.TryGetDouble(out var d) ? d : (object)je.GetRawText(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Array => je.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => je.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => je.GetRawText()
        };
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var targetStr = pa.GetString("target") ?? "";
        var attr = pa.GetString("attribute") ?? "";
        var raw = pa.GetString("value") ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target == null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        object? value;
        try
        {
            string trimmed = raw.Trim();
            // tuple handling: Python ast.literal_eval supports tuples '(1,2)' -> treat as array
            if (trimmed.StartsWith("(") && trimmed.EndsWith(")"))
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inner.EndsWith(",")) inner = inner.Substring(0, inner.Length - 1).TrimEnd();
                string norm = "[" + inner + "]";
                norm = norm.Replace("'", "\"").Replace("True", "true").Replace("False", "false").Replace("None", "null");
                try
                {
                    var je2 = JsonSerializer.Deserialize<JsonElement>(norm);
                    value = ConvertJsonElement(je2);
                }
                catch { value = raw; }
            }
            else if (raw.TrimStart().StartsWith("\"") || raw.TrimStart().StartsWith("'") || trimmed == "True" || trimmed == "False" || trimmed == "None" || (trimmed.Length > 0 && char.IsDigit(trimmed[0])) || trimmed.StartsWith("[") || trimmed.StartsWith("{"))
            {
                string norm = raw.Replace("'", "\"").Replace("True", "true").Replace("False", "false").Replace("None", "null");
                try { value = JsonSerializer.Deserialize<JsonElement>(norm); }
                catch { value = raw; }
                if (value is JsonElement je)
                {
                    value = ConvertJsonElement(je);
                    if (value is string rawFallback && je.ValueKind != JsonValueKind.String && je.ValueKind != JsonValueKind.Number && je.ValueKind != JsonValueKind.True && je.ValueKind != JsonValueKind.False && je.ValueKind != JsonValueKind.Null)
                    {
                        // ConvertJsonElement returns raw string for unsupported array/object if fallback, but we want preserved lists/dicts
                        // Actually Convert handles arrays/objects; if it fell back to raw, keep raw
                        if (rawFallback == raw) value = rawFallback;
                    }
                }
            }
            else value = raw;
            JsonSerializer.Serialize(value);
        }
        catch { value = raw; }
        if (value == null && raw.Trim() != "None" && raw.Trim() != "null") value = raw;
        if (attr.StartsWith("_") || SetHelper.Protected.Contains(attr))
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is protected and cannot be set."); return; }
        }
        if (new[] { "location","home","_contents","group_channel","contents" }.Contains(attr)) { go.Msg($"'{attr}' cannot be set directly; use move/teleport instead."); return; }
        bool had = SetHelper.HasAttr(target, attr);
        if (!had) go.Msg($"Warning: '{attr}' is a new attribute on {target.Name}.");
        try
        {
            SetHelper.SetAttr(target, attr, value);
            target.IsModified = true;
        }
        catch { go.Msg($"'{attr}' is a read-only attribute and cannot be set."); return; }
        string repr;
        if (value == null) repr = "None";
        else if (value is string s) repr = $"'{s}'";
        else if (value is bool b) repr = b ? "True" : "False";
        else
        {
            try { repr = JsonSerializer.Serialize(value); }
            catch { repr = value.ToString() ?? "None"; }
            // Json gives lower-case true/false/null, map to Python
            if (repr == "true") repr = "True";
            else if (repr == "false") repr = "False";
            else if (repr == "null") repr = "None";
        }
        go.Msg($"Set {target.Name}.{attr} = {repr}");
    }
}

public sealed class UnsetCommand : Command
{
    public override string Key => "unset";
    public override string Desc => "Delete an attribute from an object.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target", help: "Object to modify (name, #id, 'me', or 'here').");
        p.AddArgument("attribute", help: "Attribute name to delete.");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg(PrintHelp()); return; }
        var targetStr = pa.GetString("target") ?? "";
        var attr = pa.GetString("attribute") ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target == null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        if (attr.StartsWith("_") || SetHelper.Protected.Contains(attr) || attr == "is_builder" || attr == "is_superuser")
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is a read-only attribute and cannot be removed."); return; }
        }
        if (new[] { "location","home","_contents","group_channel","contents" }.Contains(attr)) { go.Msg($"'{attr}' cannot be removed directly."); return; }
        try
        {
            var prop = SetHelper.FindPropUnset(target, attr);
            if (prop != null) throw new InvalidOperationException();
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
