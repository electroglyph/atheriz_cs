using System.Text.RegularExpressions;

namespace Atheriz.Core.Commands;

/// <summary>
/// Base command. Faithful to <c>atheriz/commands/base_cmd.py:Command</c> (209 LOC).
/// </summary>
public abstract class Command
{
    private static readonly AsyncLocal<(Command cmd, GameArgumentParser parser)?> ParserBuilding = new();

    private GameArgumentParser? _parser;
    private readonly object _parserLock = new();

    public virtual string Key => "base";
    public virtual IReadOnlyList<string> Aliases => [];
    public virtual string Desc => "Base command";
    public virtual string ExtraDesc => "";
    public virtual string Category => "General";
    public virtual string Tag { get; set; } = "";
    public virtual bool Hide => false;
    public virtual bool UseParser => true;

    public virtual bool Access(IMessageTarget caller) => true;

    public GameArgumentParser? Parser
    {
        get
        {
            var building = ParserBuilding.Value;
            if (building is not null && building.Value.cmd == this)
                return building.Value.parser;
            if (_parser is null && UseParser)
            {
                lock (_parserLock)
                {
                    if (_parser is null)
                    {
                        var p = new GameArgumentParser(prog: Key, description: Desc, addHelp: true);
                        ParserBuilding.Value = (this, p);
                        try { SetupParser(p); }
                        finally { ParserBuilding.Value = null; }
                        _parser = p;
                    }
                }
            }
            return _parser;
        }
        set
        {
            lock (_parserLock) { _parser = value; }
        }
    }

    /// <summary>
    /// Override to add arguments via <paramref name="parser"/>.
    /// Mirrors Python <c>setup_parser</c> where <c>self.parser</c> is used.
    /// </summary>
    protected virtual void SetupParser(GameArgumentParser parser) { }

    public virtual string PrintHelp()
    {
        var a = new List<string> { Key };
        a.AddRange(Aliases);
        string aliasStr = Aliases.Count > 0 ? $"{Key}, {string.Join(", ", Aliases)}" : Key;
        if (Parser is null) return $"\n{Desc}\n\nAliases: {aliasStr}\n" + ExtraDesc;
        return Parser.FormatHelp() + $"\naliases: {string.Join(", ", a)}\n" + ExtraDesc;
    }

    /// <summary>
    /// Override to implement logic. <paramref name="args"/> is either ParsedArgs or raw string.
    /// </summary>
    public abstract void Run(IMessageTarget caller, object? args);

    // Shlex helper — mirrors Python's shlex.split( posix=True ) with escaping for Windows backslashes
    private static List<string> SplitArgs(string argsString)
    {
        // keep os.name in source for cross-platform parity (no-op reference)
        _ = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
        // replicate Python: re.sub(r'\\(?![\"\'\\])', r'\\\\', args_string)
        var escaped = Regex.Replace(argsString, @"\\(?![\""\'\\])", @"\\");
        // simple shlex posix split respecting quotes and backslash escapes
        var tokens = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inSingle = false, inDouble = false, escapedNext = false;
        for (int i = 0; i < escaped.Length; i++)
        {
            char c = escaped[i];
            if (escapedNext) { cur.Append(c); escapedNext = false; continue; }
            if (c == '\\')
            {
                if (inSingle) cur.Append(c);
                else escapedNext = true;
                continue;
            }
            if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
            if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
            if (!inSingle && !inDouble && char.IsWhiteSpace(c))
            {
                if (cur.Length > 0) { tokens.Add(cur.ToString()); cur.Clear(); }
                continue;
            }
            cur.Append(c);
        }
        if (escapedNext) cur.Append('\\');
        if (inSingle || inDouble) throw new ArgumentException("Unbalanced quote");
        if (cur.Length > 0) tokens.Add(cur.ToString());
        return tokens;
    }

    // Lag gate global hook — mirrors grotto/lag_gate.py monkey-patch of BaseCommand.execute
    // Install sets this via typed delegate, no reflection. Execute wraps returned func with gate check.
    public static Func<IMessageTarget, bool>? GlobalLagCheck { get; set; }

    /// <summary>
    /// Parses <paramref name="argsString"/> and returns the job tuple (mirrors Python return).
    /// Returns (runAction, caller, parsedArgs) or (null,null,null) on help/error.
    /// </summary>
    public virtual (Action<IMessageTarget, object?>? func, IMessageTarget? caller, object? args) Execute(
        IMessageTarget caller, string argsString, string cmdstring = "")
    {
        if (!UseParser)
        {
            Action<IMessageTarget, object?> raw = (c, a) => Run(c, (object?)a);
            if (GlobalLagCheck != null)
            {
                var orig = raw;
                raw = (c, a) => { if (GlobalLagCheck(c)) return; orig(c, a); };
            }
            return (raw, caller, (object?)argsString);
        }
        List<string> argList;
        if (string.IsNullOrEmpty(argsString)) argList = [];
        else
        {
            try { argList = SplitArgs(argsString); }
            catch (ArgumentException)
            {
                caller.Msg("Unbalanced quote in command.");
                caller.Msg(PrintHelp());
                return (null, null, null);
            }
        }
        GameArgumentParser.ParsedArgs parsed;
        try
        {
            var p = Parser;
            if (p is null) parsed = new GameArgumentParser.ParsedArgs();
            else
            {
                lock (_parserLock) { parsed = p.ParseArgs(argList); }
                parsed.CmdString = cmdstring;
            }
        }
        catch (CommandError)
        {
            caller.Msg(PrintHelp());
            return (null, null, null);
        }
        // wrap Run to match Python's (func, caller, eargs) triple
        Action<IMessageTarget, object?> fn = (c, a) => Run(c, a);
        if (GlobalLagCheck != null)
        {
            var orig = fn;
            fn = (c, a) => { if (GlobalLagCheck(c)) return; orig(c, a); };
        }
        return (fn, caller, parsed);
    }
}
