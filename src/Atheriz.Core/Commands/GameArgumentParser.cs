using System.Text;

namespace Atheriz.Core.Commands;

/// <summary>
/// Faithful port of <c>atheriz/commands/base_cmd.py:GameArgumentParser</c>.
/// Throws <see cref="CommandError"/> instead of exiting.
/// Subset of argparse sufficient for Atheriz commands (mirrors Python opts).
/// </summary>
public sealed class GameArgumentParser
{
    public string Prog { get; }
    public string Description { get; }
    public bool AddHelp { get; }

    private readonly List<ArgumentDef> _defs = new();

    public GameArgumentParser(string prog = "", string description = "", bool addHelp = true)
    {
        Prog = prog;
        Description = description;
        AddHelp = addHelp;
        if (addHelp)
        {
            _defs.Add(new ArgumentDef
            {
                Names = ["-h", "--help"],
                Dest = "help",
                Action = ArgAction.StoreTrue,
                Help = "show this help message and exit",
                IsHelp = true,
            });
        }
    }

    public enum ArgAction { Store, StoreTrue, StoreFalse, Append }
    public enum NargsKind { None, Optional, ZeroOrMore, OneOrMore, Remainder }

    private static bool IsOptionLike(string? s) =>
        !string.IsNullOrEmpty(s) && s.Length > 1 && s[0] == '-' &&
        (char.IsLetter(s[1]) || (s[1] == '-' && s.Length > 2 && char.IsLetter(s[2])));

    public sealed class ArgumentDef
    {
        public List<string> Names = new();
        public string Dest = "";
        public string Help = "";
        public ArgAction Action = ArgAction.Store;
        public NargsKind Nargs = NargsKind.None;
        public Type? Type;
        public object? DefaultValue;
        public object? ConstValue;
        public string[]? Choices;
        public bool Required;
        // tracks an explicit required: opt-out/in (argparse forces
        // Nargs.None positionals required; an explicit required:false opts out).
        public bool RequiredExplicit;
        public bool IsHelp;
    }

    public sealed class Builder
    {
        private readonly ArgumentDef _def;
        public Builder(ArgumentDef def) => _def = def;
        public Builder Help(string h) { _def.Help = h; return this; }
        public Builder Required(bool v = true) { _def.Required = v; _def.RequiredExplicit = true; return this; }
        public Builder Action(ArgAction a) { _def.Action = a; return this; }
        public Builder Nargs(NargsKind k) { _def.Nargs = k; return this; }
        public Builder Nargs(string s) => Nargs(ParseNargs(s));
        public Builder Type<T>() { _def.Type = typeof(T); return this; }
        public Builder Type(Type t) { _def.Type = t; return this; }
        public Builder Default(object? v) { _def.DefaultValue = v; return this; }
        public Builder Const(object? v) { _def.ConstValue = v; return this; }
        public Builder Choices(params string[] c) { _def.Choices = c; return this; }
        private static NargsKind ParseNargs(string s) => s switch
        {
            "?" => NargsKind.Optional,
            "*" => NargsKind.ZeroOrMore,
            "+" => NargsKind.OneOrMore,
            "REMAINDER" or "..." => NargsKind.Remainder,
            _ => NargsKind.None
        };
    }

    // Python-compatible AddArgument overloads
    public Builder AddArgument(params string[] names)
    {
        var def = new ArgumentDef { Names = names.ToList() };
        // dest: like argparse, prefer long option (--) for optional args
        string raw = names[0];
        if (names.Length > 1)
        {
            var longOpt = names.FirstOrDefault(n => n.StartsWith("--", StringComparison.Ordinal));
            if (longOpt is not null) raw = longOpt;
        }
        if (raw.StartsWith("-", StringComparison.Ordinal)) raw = raw.TrimStart('-').Replace("-", "_");
        def.Dest = raw;
        _defs.Add(def);
        return new Builder(def);
    }

    public Builder AddArgument(string name, string help = "", string nargs = "", string action = "", Type? type = null, object? defaultValue = null, string[]? choices = null, bool? required = null)
    {
        // Handle case where caller passed two option strings positionally: AddArgument("-f","--flag")
        // In that case 'help' looks like an option (starts with -), treat as second alias rather than help text.
        // Guard requires BOTH strings to be option-like so genuine help text
        // starting with '-' on a positional is never misread as an alias.
        if (name.StartsWith("-", StringComparison.Ordinal) && IsOptionLike(help) && string.IsNullOrEmpty(nargs) && string.IsNullOrEmpty(action) && type is null && defaultValue is null && choices is null && required != true)
        {
            // treat as AddArgument(params ["-f","--flag"])
            var names = new List<string> { name, help };
            var def2 = new ArgumentDef { Names = names };
            string raw2 = names.FirstOrDefault(n => n.StartsWith("--", StringComparison.Ordinal)) ?? names[0];
            if (raw2.StartsWith("-", StringComparison.Ordinal)) raw2 = raw2.TrimStart('-').Replace("-", "_");
            def2.Dest = raw2;
            _defs.Add(def2);
            return new Builder(def2);
        }
        var def = new ArgumentDef
        {
            Names = [name],
            Help = help,
            Required = required ?? false,
            RequiredExplicit = required.HasValue,
            Choices = choices,
            DefaultValue = defaultValue,
            Type = type,
        };
        if (!string.IsNullOrEmpty(nargs)) def.Nargs = nargs switch { "?" => NargsKind.Optional, "*" => NargsKind.ZeroOrMore, "+" => NargsKind.OneOrMore, "REMAINDER" => NargsKind.Remainder, _ => NargsKind.None };
        if (!string.IsNullOrEmpty(action)) def.Action = action switch { "store_true" => ArgAction.StoreTrue, "store_false" => ArgAction.StoreFalse, "append" => ArgAction.Append, _ => ArgAction.Store };
        // dest
        string raw = name;
        if (raw.StartsWith("-", StringComparison.Ordinal)) raw = raw.TrimStart('-').Replace("-", "_");
        def.Dest = raw;
        if (type is not null) def.Type = type;
        _defs.Add(def);
        return new Builder(def);
    }

    // usage names positionals (argparse shape), shared by help/usage.
    private string BuildUsage()
    {
        var sb = new StringBuilder($"usage: {Prog}");
        if (AddHelp) sb.Append(" [-h]");
        foreach (var d in _defs)
        {
            if (d.IsHelp || d.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal))) continue;
            sb.Append(d.Nargs switch
            {
                NargsKind.Optional => $" [{d.Dest}]",
                NargsKind.ZeroOrMore => $" [{d.Dest} ...]",
                NargsKind.OneOrMore => $" {d.Dest} [...]",
                NargsKind.Remainder => " ...",
                _ => $" {d.Dest}",
            });
        }
        return sb.ToString();
    }

    public string FormatHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine(BuildUsage());
        if (!string.IsNullOrEmpty(Description)) sb.AppendLine(Description);
        sb.AppendLine("options:");
        foreach (var d in _defs)
        {
            var names = string.Join(", ", d.Names);
            sb.AppendLine($"  {names,-20} {d.Help}");
        }
        return sb.ToString();
    }

    public string FormatUsage() => BuildUsage() + "\n";

    public void PrintHelp() => throw new CommandError(FormatHelp());
    // the file overload honors file (argparse print_help(file=...));
    // null/absent file keeps the Msg-surface throw.
    public void PrintHelp(object? file)
    {
        if (file is System.IO.TextWriter w) { w.Write(FormatHelp()); return; }
        throw new CommandError(FormatHelp());
    }
    public void PrintUsage() => throw new CommandError(FormatUsage());
    public void PrintUsage(object? file)
    {
        if (file is System.IO.TextWriter w) { w.Write(FormatUsage()); return; }
        throw new CommandError(FormatUsage());
    }
    public void Error(string message) => throw new CommandError(message);
    public void Exit(int status = 0, string? message = null)
    {
        if (message is not null) throw new CommandError(message);
    }

    // Parsed result: dict dest -> object, plus CmdString
    public sealed class ParsedArgs
    {
        private readonly Dictionary<string, object?> _map = new();
        public string CmdString { get; set; } = "";
        public object? this[string key] { get => _map.TryGetValue(key, out var v) ? v : null; set => _map[key] = value; }
        public bool Has(string key) => _map.ContainsKey(key);
        public T Get<T>(string key, T fallback = default!) => _map.TryGetValue(key, out var v) && v is T t ? t : fallback;
        public string? GetString(string key) => _map.TryGetValue(key, out var v) ? v?.ToString() : null;
        public List<string> GetList(string key) => _map.TryGetValue(key, out var v) ? v as List<string> ?? new() : new();
        public bool GetBool(string key) => _map.TryGetValue(key, out var v) && v is bool b && b;
        public IReadOnlyDictionary<string, object?> AsDict() => _map;
        internal void Set(string k, object? v) => _map[k] = v;
    }

    // Shared scalar conversion for options and single-value positionals
    // : int/float/double convert uniformly and throw like
    // argparse's "invalid <type> value" instead of keeping silent strings.
    private static object ConvertTypedValue(Type? type, string display, string val)
    {
        if (type == typeof(int))
        {
            if (int.TryParse(val, out var iv)) return iv;
            throw new CommandError($"argument {display}: invalid int value: '{val}'");
        }
        if (type == typeof(float))
        {
            if (float.TryParse(val, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var fv)) return fv;
            throw new CommandError($"argument {display}: invalid float value: '{val}'");
        }
        if (type == typeof(double))
        {
            if (double.TryParse(val, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, System.Globalization.CultureInfo.InvariantCulture, out var dv)) return dv;
            throw new CommandError($"argument {display}: invalid double value: '{val}'");
        }
        return val;
    }

    private static void CheckChoices(string[]? choices, string display, string val)
    {
        if (choices is not null && !choices.Contains(val))
            throw new CommandError($"argument {display}: invalid choice: '{val}' (choose from {string.Join(", ", choices)})");
    }

    // argparse's negative-number matcher (also the C# negative fast path).
    private static bool IsNegativeNumber(string tok)
        // trailing-dot forms ("-5.") also parse as values. DELIBERATE
        // extension beyond argparse's _negative_number_matcher
        // (^-\d+$|^-\d*\.\d+$), which rejects them: with no digit-options
        // defined the token is unambiguous, and float("-5.") is valid.
        => System.Text.RegularExpressions.Regex.IsMatch(tok, @"^-\d+\.?$|^-\d*\.\d+$");

    // An unknown -flag: starts with '-' but is not a bare '-' or a negative
    // number. List consumers must stop at these (argparse treats them as
    // unknown optionals) instead of swallowing them as values .
    private static bool LooksLikeUnknownOption(string tok)
        => tok.StartsWith("-", StringComparison.Ordinal) && tok.Length > 1 && !IsNegativeNumber(tok);

    public ParsedArgs ParseArgs(IReadOnlyList<string> argList)
    {
        // No blanket -h/--help pre-scan: help tokens inside free-text values
        // must stay data . A standalone --help still reaches the
        // IsHelp branch in the main loop (argparse likewise lets REMAINDER
        // swallow --help once a positional has started).
        // init defaults
        var result = new ParsedArgs();
        foreach (var d in _defs)
        {
            if (d.DefaultValue is not null) result.Set(d.Dest, d.DefaultValue);
            else if (d.Action == ArgAction.StoreTrue) result.Set(d.Dest, false);
            else if (d.Action == ArgAction.StoreFalse) result.Set(d.Dest, true);
            else if (d.Nargs == NargsKind.ZeroOrMore || d.Nargs == NargsKind.OneOrMore || d.Nargs == NargsKind.Remainder)
                result.Set(d.Dest, new List<string>());
            else if (d.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal)))
            {
                // optional with store: default null
                result.Set(d.Dest, null);
            }
            else
            {
                // required positional without nargs: default null
                if (d.Nargs == NargsKind.None) result.Set(d.Dest, null);
            }
        }

        var positionalDefs = _defs.Where(d => !d.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal)) && !d.IsHelp).ToList();
        Dictionary<string, ArgumentDef> optionalMap = [];
        foreach (var d in _defs.Where(d => d.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal))))
            foreach (var n in d.Names) optionalMap[n] = d;

        int posIdx = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < argList.Count; )
        {
            string tok = argList[i];
            if (optionalMap.TryGetValue(tok, out var opt))
            {
                // A pending REMAINDER positional absorbs even help flags: free-text
                // commands speak them (owner decision 2026-09-08). Other commands
                // keep standard --help behavior.
                if (opt.IsHelp && posIdx < positionalDefs.Count && positionalDefs[posIdx].Nargs == NargsKind.Remainder)
                {
                    var pdHelp = positionalDefs[posIdx];
                    var helpLst = result.GetList(pdHelp.Dest);
                    while (i < argList.Count) helpLst.Add(argList[i++]);
                    result.Set(pdHelp.Dest, helpLst);
                    break;
                }
                if (opt.IsHelp) throw new CommandError(FormatHelp());
                // presence tracking so required flags with non-null
                // bool defaults (store_true/store_false) are detectable.
                seen.Add(opt.Dest);
                if (opt.Action == ArgAction.StoreTrue) { result.Set(opt.Dest, true); i++; }
                else if (opt.Action == ArgAction.StoreFalse) { result.Set(opt.Dest, false); i++; }
                else
                {
                    // store: consume value(s). List consumers stop at unknown
                    // -flags (they are unrecognized optionals, not values);
                    // a single-value store keeps argparse's greedy take.
                    if (opt.Nargs == NargsKind.ZeroOrMore || opt.Nargs == NargsKind.OneOrMore || opt.Nargs == NargsKind.Remainder)
                    {
                        List<string> lst = [];
                        i++;
                        while (i < argList.Count && !optionalMap.ContainsKey(argList[i]) && !LooksLikeUnknownOption(argList[i]))
                        {
                            lst.Add(argList[i++]);
                        }
                        // append or set
                        if (opt.Action == ArgAction.Append)
                        {
                            var cur = result.GetList(opt.Dest);
                            cur.AddRange(lst);
                            result.Set(opt.Dest, cur);
                        }
                        else result.Set(opt.Dest, lst);
                        if (opt.Nargs == NargsKind.OneOrMore && lst.Count == 0)
                            throw new CommandError($"argument {tok}: expected at least one argument");
                    }
                    else
                    {
                        i++;
                        if (i >= argList.Count) throw new CommandError($"argument {tok}: expected one argument");
                        string val = argList[i++];
                        // type conversion (uniform with positionals)
                        object conv = ConvertTypedValue(opt.Type, tok, val);
                        // choices
                        CheckChoices(opt.Choices, tok, val);
                        if (opt.Action == ArgAction.Append)
                        {
                            var cur = result.GetList(opt.Dest);
                            cur.Add(val);
                            result.Set(opt.Dest, cur);
                        }
                        else result.Set(opt.Dest, conv);
                    }
                }
            }
            else if (tok.StartsWith("-", StringComparison.Ordinal) && tok.Length > 1)
            {
                // Port of argparse negative-number handling: when no defined
                // optional looks like a negative number, a "-5"/"-.5" token
                // is positional (so `set me score -5` parses the value).
                // (C# commands never define digit options, like Python's.)
                if (IsNegativeNumber(tok))
                {
                    if (posIdx >= positionalDefs.Count)
                        throw new CommandError($"unrecognized arguments: {tok}");
                    var pdNum = positionalDefs[posIdx];
                    // Same shape as the positional path below : list
                    // positionals collect the token (lists stay string-typed);
                    // single-value positionals convert + validate choices.
                    // List defs do not advance posIdx so following tokens join.
                    if (pdNum.Nargs == NargsKind.ZeroOrMore || pdNum.Nargs == NargsKind.OneOrMore || pdNum.Nargs == NargsKind.Remainder)
                    {
                        var negLst = result.GetList(pdNum.Dest);
                        negLst.Add(tok);
                        result.Set(pdNum.Dest, negLst);
                    }
                    else
                    {
                        CheckChoices(pdNum.Choices, pdNum.Names[0], tok);
                        result.Set(pdNum.Dest, ConvertTypedValue(pdNum.Type, pdNum.Names[0], tok));
                        posIdx++;
                    }
                    i++;
                    continue;
                }
                    // REMAINDER positional consumes everything remaining, including
                    // dash tokens (owner decision 2026-09-08 — the parser's own
                    // consume-all contract; `say --help` must speak, not throw).
                    if (posIdx < positionalDefs.Count && positionalDefs[posIdx].Nargs == NargsKind.Remainder)
                    {
                        var pdRem = positionalDefs[posIdx];
                        var remLst = result.GetList(pdRem.Dest);
                        while (i < argList.Count) remLst.Add(argList[i++]);
                        result.Set(pdRem.Dest, remLst);
                        break;
                    }
                    // unknown optional — no REMAINDER to absorb it, so it is an error.
                throw new CommandError($"unrecognized arguments: {tok}");
            }
            else
            {
                // positional
                if (posIdx >= positionalDefs.Count)
                    throw new CommandError($"unrecognized arguments: {tok}");
                var pd = positionalDefs[posIdx];
                if (pd.Nargs == NargsKind.Remainder)
                {
                    var lst = result.GetList(pd.Dest);
                    while (i < argList.Count) lst.Add(argList[i++]);
                    result.Set(pd.Dest, lst);
                    break;
                }
                else if (pd.Nargs == NargsKind.ZeroOrMore)
                {
                    var lst = result.GetList(pd.Dest);
                    while (i < argList.Count && !optionalMap.ContainsKey(argList[i]) && !LooksLikeUnknownOption(argList[i])) lst.Add(argList[i++]);
                    result.Set(pd.Dest, lst);
                    posIdx++;
                }
                else if (pd.Nargs == NargsKind.OneOrMore)
                {
                    var lst = result.GetList(pd.Dest);
                    while (i < argList.Count && !optionalMap.ContainsKey(argList[i]) && !LooksLikeUnknownOption(argList[i])) lst.Add(argList[i++]);
                    if (lst.Count == 0) throw new CommandError($"the following arguments are required: {pd.Dest}");
                    result.Set(pd.Dest, lst);
                    posIdx++;
                }
                else if (pd.Nargs == NargsKind.Optional)
                {
                    CheckChoices(pd.Choices, pd.Names[0], tok);
                    result.Set(pd.Dest, ConvertTypedValue(pd.Type, pd.Names[0], tok));
                    i++; posIdx++;
                }
                else // None single value
                {
                    CheckChoices(pd.Choices, pd.Names[0], tok);
                    result.Set(pd.Dest, ConvertTypedValue(pd.Type, pd.Names[0], tok));
                    i++; posIdx++;
                }
            }
        }
        // check required positionals
        foreach (var pd in positionalDefs)
        {
            bool isRequired = pd.Required || pd.Nargs == NargsKind.OneOrMore;
            // positional with Nargs.None is required by default in Python argparse,
            // unless explicitly opted out via required:false .
            if (!isRequired && pd.Nargs == NargsKind.None && !pd.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal))
                && !(pd.RequiredExplicit && !pd.Required))
                isRequired = true;
            if (isRequired)
            {
                var v = result[pd.Dest];
                if (v is null || (v is List<string> lst && lst.Count == 0))
                    throw new CommandError($"the following arguments are required: {pd.Dest}");
            }
            if (pd.Nargs == NargsKind.None && !isRequired && result[pd.Dest] is null)
            {
                // keep null
            }
        }
        // check required optionals: presence on the command line is required
        // (bool defaults make a null-check undetectable for store_true).
        foreach (var od in _defs.Where(d => d.Required && d.Names.Any(n => n.StartsWith("-", StringComparison.Ordinal))))
        {
            if (!seen.Contains(od.Dest)) throw new CommandError($"the following arguments are required: {string.Join("/", od.Names)}");
        }
        return result;
    }
}
