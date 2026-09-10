// Port of atheriz/commands/loggedin/set.py:243

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
    private static bool MatchesWordAt(string s, int pos, string word)
    {
        if (pos + word.Length > s.Length) return false;
        if (!s.Substring(pos, word.Length).Equals(word, StringComparison.Ordinal)) return false;
        bool leftOk = pos == 0 || (!char.IsLetterOrDigit(s[pos - 1]) && s[pos - 1] != '_');
        int end = pos + word.Length;
        bool rightOk = end >= s.Length || (!char.IsLetterOrDigit(s[end]) && s[end] != '_');
        return leftOk && rightOk;
    }
    private static string ReplaceWordOutsideQuotes(string input, string word, string replacement)
    {
        var sb = new System.Text.StringBuilder(input.Length);
        int i = 0;
        while (i < input.Length)
        {
            char c = input[i];
            if (c == '"' || c == '\'')
            {
                char q = c;
                sb.Append(c); i++;
                while (i < input.Length)
                {
                    char d = input[i];
                    sb.Append(d);
                    if (d == '\\' && i + 1 < input.Length) { sb.Append(input[i + 1]); i += 2; continue; }
                    i++;
                    if (d == q) break;
                }
            }
            else if (MatchesWordAt(input, i, word)) { sb.Append(replacement); i += word.Length; }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }
    private static string ConvertSingleQuotesOutsideDoubleQuotes(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        bool inDouble = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"' && (i == 0 || s[i - 1] != '\\')) { inDouble = !inDouble; sb.Append(c); }
            else if (c == '\'' && !inDouble) sb.Append('"');
            else sb.Append(c);
        }
        return sb.ToString();
    }
    private static string NormalizeValueText(string text)
    {
        string s = ConvertSingleQuotesOutsideDoubleQuotes(text);
        s = ReplaceWordOutsideQuotes(s, "True", "true");
        s = ReplaceWordOutsideQuotes(s, "False", "false");
        s = ReplaceWordOutsideQuotes(s, "None", "null");
        return s;
    }
    private static string ReprJson(object? value)
    {
        string repr;
        try { repr = JsonSerializer.Serialize(value); }
        catch { repr = value?.ToString() ?? "None"; }
        // Json gives lower-case true/false/null, map to Python
        if (repr == "true") repr = "True";
        else if (repr == "false") repr = "False";
        else if (repr == "null") repr = "None";
        return repr;
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { go.Msg(PrintHelp()); return; }
        var targetStr = pa.GetString("target") ?? "";
        var attr = pa.GetString("attribute") ?? "";
        var raw = pa.GetString("value") ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target is null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        object? value;
        string trimmed = raw.Trim();
        string trimStart = raw.TrimStart();
        try
        {
            // tuple handling: Python ast.literal_eval supports tuples '(1,2)' -> treat as array
            if (trimmed.StartsWith("(", StringComparison.Ordinal) && trimmed.EndsWith(")", StringComparison.Ordinal))
            {
                string inner = trimmed.Substring(1, trimmed.Length - 2).Trim();
                if (inner.EndsWith(",", StringComparison.Ordinal)) inner = inner.Substring(0, inner.Length - 1).TrimEnd();
                string norm = "[" + inner + "]";
                norm = NormalizeValueText(norm);
                try
                {
                    var je2 = JsonSerializer.Deserialize<JsonElement>(norm);
                    value = ConvertJsonElement(je2);
                }
                catch { value = raw; }
            }
            // Port of set.py:141-143 unconditional literal_eval: a leading
            // sign or dot still denotes a number (JSON parses "-5" natively;
            // "+5"/".5" are normalized first since JSON rejects them).
            else if (trimStart.StartsWith("\"", StringComparison.Ordinal) || trimStart.StartsWith("'", StringComparison.Ordinal) || trimmed == "True" || trimmed == "False" || trimmed == "None" || (trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] == '-' || trimmed[0] == '+' || trimmed[0] == '.')) || trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                string candidate = raw;
                string candTrim = candidate.TrimStart();
                if (candTrim.StartsWith("+", StringComparison.Ordinal)) candidate = candTrim.Substring(1);
                else if (candTrim.StartsWith(".", StringComparison.Ordinal)) candidate = "0" + trimStart;
                try { value = JsonSerializer.Deserialize<JsonElement>(candidate); }
                catch { value = null; }
                if (value is null)
                {
                    string norm = NormalizeValueText(raw);
                    try { value = JsonSerializer.Deserialize<JsonElement>(norm); }
                    catch { value = raw; }
                }
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
        }
        catch { value = raw; }
        if (value is null && trimmed != "None" && trimmed != "null") value = raw;
        if (SetHelper.IsProtected(attr))
        {
            if (!go.IsSuperUser) { go.Msg($"'{attr}' is protected and cannot be set."); return; }
        }
        if (SetHelper.MoveGate.Contains(attr)) { go.Msg($"'{attr}' cannot be set directly; use move/teleport instead."); return; }
        bool had = SetHelper.HasAttr(target, attr);
        if (!had) go.Msg($"Warning: '{attr}' is a new attribute on {target.Name}.");
        try
        {
            SetHelper.SetAttr(target, attr, value);
            target.IsModified = true;
        }
        catch (InvalidOperationException) { go.Msg($"'{attr}' is a read-only attribute and cannot be set."); return; }
        // A property whose type can never convert from text (e.g. LocationRef)
        // is unsettable from the command line, so it gets its own message
        // rather than the read-only one: the attribute exists, the text just
        // cannot become its type. Malformed values for convertible types fall
        // through to the conversion message below.
        catch (InvalidCastException) { go.Msg($"'{attr}' cannot be set from text."); return; }
        catch (Exception ex) { go.Msg($"Could not set '{attr}': {ex.Message}"); return; }
        // First-match order mirrors the old if/else chain (null, string,
        // bool, then JSON with Python True/False/None spellings).
        string repr = value switch
        {
            null => "None",
            string s => $"'{s}'",
            bool b => b ? "True" : "False",
            _ => ReprJson(value),
        };
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
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { go.Msg(PrintHelp()); return; }
        var targetStr = pa.GetString("target") ?? "";
        var attr = pa.GetString("attribute") ?? "";
        var target = SetHelper.ResolveTarget(go, targetStr);
        if (target is null) return;
        if (target != go && target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot modify an object of equal or higher privilege."); return; }
        // Port of unset.py:226 — only the shared protected set is checked.
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
