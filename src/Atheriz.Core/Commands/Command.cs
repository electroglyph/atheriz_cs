namespace Atheriz.Core.Commands;

/// <summary>
/// Base command. Faithful to <c>atheriz/commands/base_cmd.py:Command</c> (209 LOC).
/// </summary>
public abstract class Command
{
    private static readonly AsyncLocal<(Command cmd, GameArgumentParser parser)?> ParserBuilding = new();

    // Volatile publication (R1): the getter's outer null check is an
    // unsynchronized read, so the constructing thread's SetupParser writes
    // must be visible to any thread observing a non-null _parser.
    private volatile GameArgumentParser? _parser;
    private readonly Lock _parserLock = new();

    public virtual string Key => "base";
    public virtual IReadOnlyList<string> Aliases => [];
    public virtual string Desc => "Base command";
    public virtual string ExtraDesc => "";
    public virtual string Category => "General";
    public virtual string Tag { get; set; } = "";
    public virtual bool Hide => false;
    public virtual bool UseParser => true;

    public virtual bool Access(IMessageTarget caller) => true;

    public virtual GameArgumentParser? Parser
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
        if (Parser is null) return HelpHelper.FormatNoParser(this);
        return Parser.FormatHelp() + $"\nAliases: {HelpHelper.FormatAliasList(this)}\n" + ExtraDesc;
    }

    /// <summary>
    /// Untyped entry. Builds a <see cref="CommandContext"/> from the dual-type
    /// <paramref name="args"/> (<c>ParsedArgs</c> vs raw <c>string</c>) and
    /// forwards to <see cref="Run(CommandContext)"/>, so direct
    /// <c>Run(caller, pa-or-string)</c> calls keep working. New commands
    /// override <see cref="Run(CommandContext)"/> (or <see cref="RunParsed"/>
    /// / <see cref="RunRaw"/>) instead. Stays virtual so existing overrides of
    /// this shape keep compiling; production commands all sit on the context.
    /// </summary>
    // Re-entrancy depth for the adapter below: the RunParsed/RunRaw defaults
    // bridge back to Run(caller, args), so a command overriding only one of
    // them and receiving the other input shape would ping-pong adapter ->
    // context -> default -> adapter until the stack overflows. The defaults
    // go terminal (PrintHelp) instead once re-entered. Only the base adapter
    // body counts (overrides replace it outright), so decorator forwarding
    // (LagGateCommand -> inner Run(ctx)) and nested command calls are
    // unaffected: their first bridge back still runs.
    private static readonly AsyncLocal<int> _runDepth = new();

    public virtual void Run(IMessageTarget caller, object? args)
    {
        _runDepth.Value++;
        try
        {
            Run(new CommandContext(caller, null, args as GameArgumentParser.ParsedArgs, args as string ?? "", CancellationToken.None));
        }
        finally { _runDepth.Value--; }
    }

    /// <summary>
    /// Primary entry point: the typed invocation for a command. The default
    /// splits parsed vs raw input (non-null <see cref="CommandContext.Args"/>
    /// runs <see cref="RunParsed"/>, otherwise <see cref="RunRaw"/>).
    /// </summary>
    public virtual void Run(CommandContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Args is not null) RunParsed(ctx);
        else RunRaw(ctx);
    }

    /// <summary>
    /// Async entry surface (sync dispatch stays). Override to await
    /// wizard-style flows under <paramref name="ct"/>; the default runs the
    /// sync body inline.
    /// </summary>
    public virtual Task RunAsync(CommandContext ctx, CancellationToken ct)
    {
        Run(ctx);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Parsed-args entry: default forwards to <see cref="Run"/>, or sends
    /// help when re-entered (a RunRaw-only command receiving parsed args).
    /// Commands on the <see cref="LoggedInCommand"/> base never touch
    /// <c>object?</c> here.
    /// </summary>
    public virtual void RunParsed(CommandContext ctx)
    {
        if (_runDepth.Value > 1) { ctx.Caller.Msg(PrintHelp()); return; }
        Run(ctx.Caller, ctx.Args);
    }

    /// <summary>
    /// Raw-text entry: default forwards to <see cref="Run"/>, or sends help
    /// when re-entered (a RunParsed-only command receiving raw text).
    /// </summary>
    public virtual void RunRaw(CommandContext ctx)
    {
        if (_runDepth.Value > 1) { ctx.Caller.Msg(PrintHelp()); return; }
        Run(ctx.Caller, ctx.RawText);
    }

    /// <summary>Forwarder so decorators (e.g. <see cref="LagGateCommand"/>) can drive setup.</summary>
    internal void SetupParserForwarder(GameArgumentParser parser) => SetupParser(parser);

    // Shlex helper — mirrors Python's shlex.split( posix=True ) with escaping for Windows backslashes
    internal static List<string> SplitArgs(string argsString)
    {
        var (head, _, _, balanced) = Tokenize(argsString);
        if (!balanced) throw new ArgumentException("Unbalanced quote");
        return head;
    }

    // Single tokenizer behind `SplitArgs` and the creation stubs: one
    // escape-aware span walk yields head values plus verbatim-tail offsets, so
    // the head and the tail can never disagree on quoting. `headCount`
    // selects how many tokens go to `Head`; `Tail` is the verbatim rest after
    // them (leading separators stripped, interior spacing intact — passwords
    // and descs are credential/text bytes, not re-joined tokens).
    // Unbalanced quotes keep the legacy stub contract bit-for-bit: head falls
    // back to a plain whitespace split while the tail uses the old
    // quote-scan, exactly as `SplitStubArgs` + `RemainderAfterTokens` did.
    internal static (List<string> Head, string Tail) SplitHeadTail(string text, int headCount)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (headCount < 0) throw new ArgumentOutOfRangeException(nameof(headCount));
        // headCount 0 selects no head tokens: the whole text is the tail.
        // Without this, ends[headCount - 1] below indexes ends[-1].
        if (headCount == 0) return ([], text);
        var (values, ends, origin, balanced) = Tokenize(text);
        if (!balanced)
        {
            // Legacy fallback: whitespace head + old quote-scan tail.
            var head = text.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries).Take(headCount).ToList();
            return (head, RemainderScan(text, headCount));
        }
        List<string> head2 = values.Take(headCount).ToList();
        string tail = "";
        if (values.Count > headCount)
        {
            int origEnd = ends[headCount - 1] < origin.Count ? origin[ends[headCount - 1]] : text.Length;
            // The mapped offset lands on the delimiter run after the head
            // token (escaped/head spans end at the separator); skip only the
            // following separator run, like the old scan did.
            int k = origEnd;
            while (k < text.Length && char.IsWhiteSpace(text[k])) k++;
            tail = k < text.Length ? text[k..] : "";
        }
        return (head2, tail);
    }

    // One escape-aware span walk behind `SplitArgs` (values) and
    // `SplitHeadTail` (values plus escaped end-offsets per token, with the
    // escaped-to-original origin map for verbatim tail slicing). The walk
    // mirrors the old SplitArgs body exactly; `balanced` reports whether any
    // quote was left open.
    private static (List<string> Values, List<int> Ends, List<int> Origin, bool Balanced) Tokenize(string text)
    {
        // Pre-pass with origin map: replicate Python
        // re.sub(r'\\(?![\"\'\\])', r'\\\\', s) while remembering which
        // original offset each escaped char came from (doubled chars map to
        // the backslash's offset). Tail slicing runs on original offsets;
        // head values run on escaped text, exactly like SplitArgs always did.
        var escaped = new System.Text.StringBuilder(text.Length + 8);
        var origin = new List<int>(text.Length + 8);
        for (int o = 0; o < text.Length; o++)
        {
            char c = text[o];
            if (c != '\\') { escaped.Append(c); origin.Add(o); continue; }
            char next = o + 1 < text.Length ? text[o + 1] : '\0';
            escaped.Append('\\'); origin.Add(o);
            if (next is not ('"' or '\'' or '\\')) { escaped.Append('\\'); origin.Add(o); }
        }
        string e = escaped.ToString();
        List<string> values = [];
        List<int> ends = [];
        var cur = new System.Text.StringBuilder();
        bool inSingle = false, inDouble = false, escapedNext = false;
        for (int i = 0; i < e.Length; i++)
        {
            char c = e[i];
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
                if (cur.Length > 0) { values.Add(cur.ToString()); ends.Add(i); cur.Clear(); }
                continue;
            }
            cur.Append(c);
        }
        if (escapedNext) cur.Append('\\');
        bool balanced = !inSingle && !inDouble;
        if (balanced && cur.Length > 0) { values.Add(cur.ToString()); ends.Add(e.Length); }
        return (values, ends, origin, balanced);
    }

    // Old quote-scan kept for the unbalanced fallback only: skips `count`
    // leading tokens (quotes delimit, whitespace separates, no escape
    // processing) and returns the rest verbatim.
    private static string RemainderScan(string text, int count)
    {
        int i = 0;
        for (int t = 0; t < count; t++)
        {
            while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
            if (i >= text.Length) return "";
            if (text[i] == '"' || text[i] == '\'')
            {
                char q = text[i++];
                while (i < text.Length && text[i] != q) i++;
                if (i < text.Length) i++;
            }
            else
            {
                while (i < text.Length && !char.IsWhiteSpace(text[i])) i++;
            }
        }
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i < text.Length ? text[i..] : "";
    }

    // Lag gate global hook — mirrors grotto/lag_gate.py monkey-patch of BaseCommand.execute
    // Install sets this via typed delegate, no reflection. Execute wraps returned func with gate check.
    public static Func<IMessageTarget, bool>? GlobalLagCheck { get; set; }

    // One home for the lag-gate wrapper applied to both Execute paths: with
    // no gate installed the action runs as-is, otherwise a lagged caller
    // returns early without running the wrapped action.
    private static Action<IMessageTarget, object?> WrapWithLagCheck(Action<IMessageTarget, object?> orig)
    {
        // Capture once: re-reading GlobalLagCheck at invoke time lets
        // an install/remove landing between wrap and run throw NRE on a
        // nulled gate or apply the wrong generation.
        var gate = GlobalLagCheck;
        if (gate is null) return orig;
        return (c, a) => { if (gate(c)) return; orig(c, a); };
    }

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
            raw = WrapWithLagCheck(raw);
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
        catch (CommandHelpException che)
        {
            // Explicit --help (or PrintHelp/PrintUsage): the message already
            // IS the help text, so surface it once. Real parse errors below
            // keep the diagnosis+help shape.
            if (!string.IsNullOrEmpty(che.Message)) caller.Msg(che.Message);
            return (null, null, null);
        }
        catch (CommandError ce)
        {
            // surface the diagnosis WITH the help. (Python
            // base_cmd.py:189 shows help only — this deliberately diverges so
            // callers learn what failed instead of guessing.)
            if (!string.IsNullOrEmpty(ce.Message)) caller.Msg(ce.Message);
            caller.Msg(PrintHelp());
            return (null, null, null);
        }
        // wrap Run to match Python's (func, caller, eargs) triple
        Action<IMessageTarget, object?> fn = (c, a) => Run(c, a);
        fn = WrapWithLagCheck(fn);
        return (fn, caller, parsed);
    }
}
