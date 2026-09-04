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
        public bool IsHelp;
    }

    public sealed class Builder
    {
        private readonly ArgumentDef _def;
        public Builder(ArgumentDef def) => _def = def;
        public Builder Help(string h) { _def.Help = h; return this; }
        public Builder Required(bool v = true) { _def.Required = v; return this; }
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
            var longOpt = names.FirstOrDefault(n => n.StartsWith("--"));
            if (longOpt != null) raw = longOpt;
        }
        if (raw.StartsWith("-")) raw = raw.TrimStart('-').Replace("-", "_");
        def.Dest = raw;
        _defs.Add(def);
        return new Builder(def);
    }

    public Builder AddArgument(string name, string help = "", string nargs = "", string action = "", Type? type = null, object? defaultValue = null, string[]? choices = null, bool required = false)
    {
        // Handle case where caller passed two option strings positionally: AddArgument("-f","--flag")
        // In that case 'help' looks like an option (starts with -), treat as second alias rather than help text.
        if (!string.IsNullOrEmpty(help) && help.StartsWith("-") && string.IsNullOrEmpty(nargs) && string.IsNullOrEmpty(action) && type == null && defaultValue == null && choices == null && !required)
        {
            // treat as AddArgument(params ["-f","--flag"])
            var names = new List<string> { name, help };
            var def2 = new ArgumentDef { Names = names };
            string raw2 = names.FirstOrDefault(n => n.StartsWith("--")) ?? names[0];
            if (raw2.StartsWith("-")) raw2 = raw2.TrimStart('-').Replace("-", "_");
            def2.Dest = raw2;
            _defs.Add(def2);
            return new Builder(def2);
        }
        var def = new ArgumentDef
        {
            Names = [name],
            Help = help,
            Required = required,
            Choices = choices,
            DefaultValue = defaultValue,
            Type = type,
        };
        if (!string.IsNullOrEmpty(nargs)) def.Nargs = nargs switch { "?" => NargsKind.Optional, "*" => NargsKind.ZeroOrMore, "+" => NargsKind.OneOrMore, "REMAINDER" => NargsKind.Remainder, _ => NargsKind.None };
        if (!string.IsNullOrEmpty(action)) def.Action = action switch { "store_true" => ArgAction.StoreTrue, "store_false" => ArgAction.StoreFalse, "append" => ArgAction.Append, _ => ArgAction.Store };
        // dest
        string raw = name;
        if (raw.StartsWith("-")) raw = raw.TrimStart('-').Replace("-", "_");
        def.Dest = raw;
        if (type is not null) def.Type = type;
        _defs.Add(def);
        return new Builder(def);
    }

    public string FormatHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"usage: {Prog} [-h] ...");
        if (!string.IsNullOrEmpty(Description)) sb.AppendLine(Description);
        sb.AppendLine("options:");
        foreach (var d in _defs)
        {
            var names = string.Join(", ", d.Names);
            sb.AppendLine($"  {names,-20} {d.Help}");
        }
        return sb.ToString();
    }

    public string FormatUsage() => $"usage: {Prog} ...\n";

    public void PrintHelp() => throw new CommandError(FormatHelp());
    public void PrintHelp(object? file) => throw new CommandError(FormatHelp());
    public void PrintUsage() => throw new CommandError(FormatUsage());
    public void PrintUsage(object? file) => throw new CommandError(FormatUsage());
    public void Error(string message) => throw new CommandError(message);
    public void Exit(int status = 0, string? message = null)
    {
        if (message != null) throw new CommandError(message);
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

    public ParsedArgs ParseArgs(IReadOnlyList<string> argList)
    {
        // help trigger
        if (AddHelp && argList.Any(a => a == "-h" || a == "--help"))
            throw new CommandError(FormatHelp());
        // init defaults
        var result = new ParsedArgs();
        foreach (var d in _defs)
        {
            if (d.DefaultValue is not null) result.Set(d.Dest, d.DefaultValue);
            else if (d.Action == ArgAction.StoreTrue) result.Set(d.Dest, false);
            else if (d.Action == ArgAction.StoreFalse) result.Set(d.Dest, true);
            else if (d.Nargs == NargsKind.ZeroOrMore || d.Nargs == NargsKind.OneOrMore || d.Nargs == NargsKind.Remainder)
                result.Set(d.Dest, new List<string>());
            else if (d.Names.Any(n => n.StartsWith("-")))
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

        var positionalDefs = _defs.Where(d => !d.Names.Any(n => n.StartsWith("-")) && !d.IsHelp).ToList();
        var optionalMap = new Dictionary<string, ArgumentDef>();
        foreach (var d in _defs.Where(d => d.Names.Any(n => n.StartsWith("-"))))
            foreach (var n in d.Names) optionalMap[n] = d;

        int posIdx = 0;
        for (int i = 0; i < argList.Count; )
        {
            string tok = argList[i];
            if (optionalMap.TryGetValue(tok, out var opt))
            {
                if (opt.IsHelp) throw new CommandError(FormatHelp());
                if (opt.Action == ArgAction.StoreTrue) { result.Set(opt.Dest, true); i++; }
                else if (opt.Action == ArgAction.StoreFalse) { result.Set(opt.Dest, false); i++; }
                else
                {
                    // store: consume value(s)
                    if (opt.Nargs == NargsKind.ZeroOrMore || opt.Nargs == NargsKind.OneOrMore || opt.Nargs == NargsKind.Remainder)
                    {
                        var lst = new List<string>();
                        i++;
                        while (i < argList.Count && !optionalMap.ContainsKey(argList[i]))
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
                        // type conversion
                        object conv = val;
                        if (opt.Type == typeof(int) && int.TryParse(val, out var iv)) conv = iv;
                        else if (opt.Type == typeof(float) && float.TryParse(val, out var fv)) conv = fv;
                        // choices
                        if (opt.Choices is not null && !opt.Choices.Contains(val))
                            throw new CommandError($"argument {tok}: invalid choice: '{val}' (choose from {string.Join(", ", opt.Choices)})");
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
            else if (tok.StartsWith("-") && tok.Length > 1)
            {
                // unknown optional — in Python this would error; mirror by throwing CommandError
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
                    while (i < argList.Count && !optionalMap.ContainsKey(argList[i])) lst.Add(argList[i++]);
                    result.Set(pd.Dest, lst);
                    posIdx++;
                }
                else if (pd.Nargs == NargsKind.OneOrMore)
                {
                    var lst = result.GetList(pd.Dest);
                    while (i < argList.Count && !optionalMap.ContainsKey(argList[i])) lst.Add(argList[i++]);
                    if (lst.Count == 0) throw new CommandError($"the following arguments are required: {pd.Dest}");
                    result.Set(pd.Dest, lst);
                    posIdx++;
                }
                else if (pd.Nargs == NargsKind.Optional)
                {
                    result.Set(pd.Dest, tok);
                    i++; posIdx++;
                }
                else // None single value
                {
                    object conv = tok;
                    if (pd.Type == typeof(int) && int.TryParse(tok, out var iv)) conv = iv;
                    if (pd.Choices is not null && !pd.Choices.Contains(tok))
                        throw new CommandError($"argument {pd.Names[0]}: invalid choice: '{tok}'");
                    result.Set(pd.Dest, conv);
                    i++; posIdx++;
                }
            }
        }
        // check required positionals
        foreach (var pd in positionalDefs)
        {
            bool isRequired = pd.Required || pd.Nargs == NargsKind.OneOrMore;
            // positional with Nargs.None is required by default in Python argparse
            if (!isRequired && pd.Nargs == NargsKind.None && !pd.Names.Any(n => n.StartsWith("-")))
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
        // check required optionals
        foreach (var od in _defs.Where(d => d.Required && d.Names.Any(n => n.StartsWith("-"))))
        {
            var v = result[od.Dest];
            if (v is null) throw new CommandError($"the following arguments are required: {string.Join("/", od.Names)}");
        }
        return result;
    }
}
