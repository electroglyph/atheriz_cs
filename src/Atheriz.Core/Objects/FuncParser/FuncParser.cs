// Expression parser for `$func(arg, kwarg=val)` substitutions in game text,
// plus director-stance `{key}` formatting (see FuncParserHelpers.SafeFormatMap).
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Atheriz.Core;
using Atheriz.Core.Objects.VerbConjugation;namespace Atheriz.Core.Objects;

/// <summary>
/// Parses and executes `$`-expressions in message text: `$` opens a call,
/// `\` escapes the next char, `$$` is a literal `$`, and calls nested deeper
/// than <see cref="MaxNesting"/> are left unparsed. Calls run inside-out
/// (innermost first) with quoted args; unknown functions and errors echo the
/// raw text unless raising is requested.
/// Ships with actor-stance callables (`$you/$You/$obj/$conj/$pconj/$pron/$Pron`
/// etc.) plus director-stance `{key}` substitution. Each instance carries its
/// own callable table; the static <c>Parse</c> is the shared entry used by
/// <c>GameObject.Msg</c>.
/// </summary>
public class FuncParser
{
    public const char StartChar = '$';
    public const char EscapeChar = '\\';
    public const int MaxNesting = 20;
    public const int MaxMessageSize = 65536 * 2;

    public sealed class ParsingError : Exception
    {
        public ParsingError(string msg) : base(msg) { }
    }

    public sealed class ParserContext
    {
        public GameObject? Caller;
        public GameObject? Receiver;
        public IDictionary<string, object?>? Mapping;
        public bool RaiseErrors;
    }

    public sealed class ParsedFunc
    {
        public char Prefix = StartChar;
        public string FuncName = "";
        public List<object?> Args = new();
        public Dictionary<string, object?> Kwargs = new(StringComparer.Ordinal);
        public StringBuilder FullStr = new();
        public StringBuilder InFuncStr = new();
        public int DoubleQuoted = -1;
        public string QuotedChar = "";
        public string CurrentKwarg = "";
        public int OpenLParens;
        public int OpenLSquare;
        public int OpenLCurly;
        public object? ExecReturn = "";
        public ParsedFunc(char prefix) { Prefix = prefix; FullStr.Append(prefix); }
        public ParsedFunc() { }
        public (string, List<object?>, Dictionary<string, object?>) Get() => (FuncName, Args, Kwargs);
        public override string ToString()
        {
            return FullStr.ToString() + InFuncStr.ToString();
        }
    }

    public delegate object? ParserCallable(string[] args, Dictionary<string, string> kwargs, ParserContext ctx, ParsedFunc raw);
    // Declared callable shape for instance callables taking merged kwargs.
    // Currently unused: plain Delegates are adapted by BuildGenericWrapper.
    private delegate object? GenericCallable(string[] args, Dictionary<string, object?> kwargs, ParsedFunc raw);

    public static readonly Dictionary<string, ParserCallable> FuncParserCallables;
    public static readonly Dictionary<string, ParserCallable> ActorStanceCallables;

    // Per-instance configuration and callable tables.
    private readonly Dictionary<string, ParserCallable> _callables;
    private readonly Dictionary<string, Delegate> _genericCallables;
    private readonly bool _hasGeneric;
    private readonly char _startChar;
    private readonly char _escapeChar;
    private readonly int _maxNesting;
    private readonly Dictionary<string, object?> _defaultKwargs;

    static FuncParser()
    {
        FuncParserCallables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal)
        {
            ["eval"] = (a,k,ctx,raw) => a.Length>0? SafePyEval(a[0]):"",
            ["toint"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; var ev = a.Length>0? SafePyEval(a[0]):a[0]; if(int.TryParse(ev?.ToString(), out var iv)) return iv; try{ var d=Convert.ToDouble(ev); return (int)d; }catch{ return ev?.ToString()??""; } },
            ["int2str"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; if(int.TryParse(a[0], out var n)) return FuncParserHelpers.Int2Str(n); return a[0]; },
            ["an"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; var s=a[0]??""; if(s.Length>0 && "aeiouyAEIOUY".Contains(s[0])) return $"an {s}"; return $"a {s}"; },
            ["add"] = (a,k,ctx,raw) => ApplyOp(a,k,ctx,"+"),
            ["sub"] = (a,k,ctx,raw) => ApplyOp(a,k,ctx,"-"),
            ["mult"] = (a,k,ctx,raw) => ApplyOp(a,k,ctx,"*"),
            ["div"] = (a,k,ctx,raw) => ApplyOp(a,k,ctx,"/"),
            ["round"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; if(!double.TryParse(a[0], out var d)) return ""; int sig=0; if(a.Length>1) int.TryParse(a[1], out sig); var r=Math.Round(d,sig); if(sig==0) return ((int)r).ToString(); return r.ToString(System.Globalization.CultureInfo.InvariantCulture); },
            ["random"] = (a,k,ctx,raw) => { var rnd=Random.Shared; if(a.Length==0) return rnd.Next(0,2); if(a.Length==1){ if(a[0].Contains('.')){ double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mx); return rnd.NextDouble()*mx; } int.TryParse(a[0], out var mx2); return rnd.Next(0,mx2+1); } { double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mn); double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mx); bool isFloat=a[0].Contains('.')||a[1].Contains('.'); if(isFloat) return mn + (mx-mn)*rnd.NextDouble(); return rnd.Next((int)mn,(int)mx+1); } },
            ["randint"] = (a,k,ctx,raw) => { var rnd=Random.Shared; if(a.Length==0) return rnd.Next(0,2); if(a.Length==1){ int.TryParse(a[0], out var mx2); return rnd.Next(0,mx2+1); } int.TryParse(a[0], out var mn2); int.TryParse(a[1], out var mx3); return rnd.Next(mn2,mx3+1); },
            ["choice"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; var rnd=Random.Shared;
                if(a.Length==1){ var single=a[0].Trim(); if(single.StartsWith("[", StringComparison.Ordinal)&&single.EndsWith("]", StringComparison.Ordinal)){ try{ var inner=single.Substring(1,single.Length-2); var items=inner.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToArray(); if(items.Length>0) return items[rnd.Next(items.Length)].Trim('\'','"'); }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ParsedFunc.GenericCallable: " + logEx.Message, "ParsedFunc"); } } try{
                        var conv = FuncParserHelpers.SafeConvertToTypes( (new object[]{"py"}, new Dictionary<string,object?>()), new object?[]{single}, new Dictionary<string,object?>(), ctx.RaiseErrors); if(conv.args.Length>0 && conv.args[0] is System.Collections.IEnumerable en && !(conv.args[0] is string)){ var list=en.Cast<object?>().ToArray(); if(list.Length>0) return list[rnd.Next(list.Length)]?.ToString()??""; } }catch{ if(ctx.RaiseErrors) throw; }
                } else {
                    // Multi-arg: convert each arg through the literal converter, then pick one.
                    var converters = Enumerable.Repeat((object)"py", a.Length).ToArray();
                    var conv = FuncParserHelpers.SafeConvertToTypes( (converters, new Dictionary<string,object?>()), a.Cast<object?>().ToArray(), new Dictionary<string,object?>(), ctx.RaiseErrors);
                    // A failed conversion either threw (raising mode) or passed through; pick from the results.
                    var list = conv.args.Select(o=> o?.ToString() ?? "").ToArray();
                    if(list.Length>0) return list[rnd.Next(list.Length)];
                }
                return a[rnd.Next(a.Length)]; },
            ["pad"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; string t=a[0]??""; int w = ResolveWidth(a, k); string al="c"; if(k.TryGetValue("align", out var alv)) al=alv; else if(a.Length>2) al=a[2]; string fc=" "; if(k.TryGetValue("fillchar", out var fcv)) fc=fcv; else if(a.Length>3) fc=a[3]; return FuncParserHelpers.Pad(t,w,al,fc); },
            ["crop"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; string t=a[0]??""; int w = ResolveWidth(a, k); string suffix="[...]"; if(k.TryGetValue("suffix", out var sv)) suffix=sv; else if(a.Length>2) suffix=a[2]; return FuncParserHelpers.Crop(t,w,suffix); },
            ["space"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; long wLong=1; bool parsed = long.TryParse(a[0], out wLong); if(!parsed) wLong=1; if(wLong<0) wLong=1; wLong=Math.Min(wLong, FuncParserHelpers.MaxTextWidth); return new string(' ', (int)wLong); },
            ["just"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"f"),
            ["ljust"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"l"),
            ["rjust"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"r"),
            ["cjust"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"c"),
            ["justify"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"f"),
            ["justify_left"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"l"),
            ["justify_right"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"r"),
            ["justify_center"] = (a,k,ctx,raw) => JustifyHelper(a,k,ctx,"c"),
            ["clr"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; string start="", text="", end=""; if(a.Length>1){ start=a[0]; text=a.Length>1?a[1]:""; end=a.Length>2?a[2]:""; } else { text=a[0]; start=k.TryGetValue("start", out var sv)?sv:""; end=k.TryGetValue("end", out var ev2)?ev2:""; } start=string.IsNullOrEmpty(start)?"":("|"+start); end=string.IsNullOrEmpty(end)?(string.IsNullOrEmpty(start)?"":"|n"):("|"+end); return $"{start}{text}{end}"; },
            ["pluralize"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; // Bad numbers throw only when raising; otherwise the singular echoes back.
                bool raise = ctx.RaiseErrors;
                if(a.Length>2){ var singular=a[0]??""; var number=a[1]; var plural=a[2]??""; if(!int.TryParse(number?.ToString(), out var nNum)){ if(raise) throw new ParsingError($"pluralize: number '{number}' not an integer"); return singular; } int nn=Math.Abs(nNum); return nn==0||nn==1? singular : plural; }
                if(a.Length>1){ var singular=a[0]??""; var number=a[1]; if(number is null || string.IsNullOrEmpty(number.ToString())){ if(raise) throw new ParsingError($"pluralize: number '{number}' not an integer"); return singular; } if(!int.TryParse(number.ToString(), out var n2)){ if(raise) throw new ParsingError($"pluralize: number '{number}' not an integer"); return singular; } int nn2=Math.Abs(n2); return nn2==0||nn2==1? singular : (singular+"s"); } return a[0]??""; },
        };

        ActorStanceCallables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal)
        {
            ["you"] = HandleYou,
            ["You"] = HandleYou,
            ["your"] = HandleYour,
            ["Your"] = HandleYour,
            ["obj"] = HandleYou,
            ["Obj"] = HandleYou,
            ["conj"] = HandleConj,
            ["pconj"] = HandlePConj,
            ["pron"] = HandlePron,
            ["Pron"] = HandlePron,
        };
        foreach (var kv in FuncParserCallables) ActorStanceCallables[kv.Key] = kv.Value;
    }

    private static string? ApplyOp(string[] a, Dictionary<string,string> k, ParserContext ctx, string op)
    {
        if(a.Length<2) return "";
        double v1 = 0, v2 = 0;
        bool bothNumeric = double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v1) && double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v2);
        if(bothNumeric)
        {
            try{
                double res = op=="+"?v1+v2: op=="-"?v1-v2: op=="*"?v1*v2: op=="/"?v1/v2:0;
                if(op!="/" && !a[0].Contains('.') && !a[1].Contains('.') && a[0].Trim().All(c=>char.IsDigit(c)||c=='-' ) && a[1].Trim().All(c=>char.IsDigit(c)||c=='-')) return ((long)res).ToString();
                return res.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }catch{ return ctx.RaiseErrors? throw new ParsingError("op failed"): ""; }
        }
        if(op=="+" ) return (a[0]??"") + (a[1]??"");
        return "";
    }
    private static int ResolveWidth(string[] a, Dictionary<string, string> k, int dflt = 78)
    {
        if (k.TryGetValue("width", out var ws) && long.TryParse(ws, out var wl)) return (int)Math.Min(wl, FuncParserHelpers.MaxTextWidth);
        if (a.Length > 1 && long.TryParse(a[1], out var wl2)) return (int)Math.Min(wl2, FuncParserHelpers.MaxTextWidth);
        return dflt;
    }
    private static string JustifyHelper(string[] a, Dictionary<string,string> k, ParserContext ctx, string defAlign)
    {
        if(a.Length==0) return "";
        string text=a[0]??""; int width = ResolveWidth(a, k); string align=defAlign; int indent=0;
        if(k.TryGetValue("align", out var alv)) align=alv; else if(a.Length>2) align=a[2];
        if(k.TryGetValue("indent", out var ivs) && int.TryParse(ivs, out var ivi)) indent=ivi; else if(a.Length>3 && int.TryParse(a[3], out var ivi2)) indent=ivi2;
        indent = Math.Max(0, Math.Min(indent, width));
        return FuncParserHelpers.Justify(text, width, align, indent);
    }
    private static string SafePyEval(string s)
    {
        s=s.Trim();
        if(string.IsNullOrEmpty(s)) return "";
        // Try the literal/arithmetic converters first, then plain number parses.
        try{
            var conv = FuncParserHelpers.SafeConvertToTypes( (new object[]{"py"}, new Dictionary<string,object?>()), new object?[]{s}, new Dictionary<string,object?>(), true);
            var v = conv.args[0];
            if(v is int i) return i.ToString();
            if(v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(v is string str) return str;
            if(v is System.Collections.IList list) return "["+string.Join(",", list.Cast<object?>())+"]";
            return v?.ToString()??"";
        }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ParsedFunc.SafePyEval: " + logEx.Message, "ParsedFunc"); }
        if(int.TryParse(s, out var i2)) return i2.ToString();
        if(double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2)) return d2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(s.StartsWith("[", StringComparison.Ordinal)&&s.EndsWith("]", StringComparison.Ordinal)) return s;
        try{ return FuncParserHelpers.SafeArithEval(s).ToString(System.Globalization.CultureInfo.InvariantCulture); }catch{ return s; }
    }

    private static GameObject? ResolveMappedActor(IDictionary<string, object?>? mapping, string? key, GameObject? fallback) => key is not null && mapping is not null && mapping.TryGetValue(key, out var m) && m is GameObject go ? go : fallback;
    private static string? ResolveGender(GameObject? obj) => obj is null ? null : obj is IGenderProvider gp ? gp.GetGender() : obj.Gender;

    private static object? HandleYou(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        GameObject? caller = ResolveMappedActor(ctx.Mapping, args.Length > 0 ? args[0] : null, ctx.Caller);
        if (caller is null || ctx.Receiver is null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller or receiver supplied to $you callable.");
            return raw.ToString();
        }
        bool cap = false;
        if (kwargs.TryGetValue("capitalize", out var capStr)) cap = capStr.Equals("true", StringComparison.OrdinalIgnoreCase) || capStr=="1";
        if (raw.FuncName == "You" || raw.FuncName == "Obj") cap = true;
        if (caller == ctx.Receiver) return cap? "You":"you";
        return caller.GetDisplayName(ctx.Receiver);
    }
    private static object? HandleYour(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        GameObject? caller = ResolveMappedActor(ctx.Mapping, args.Length > 0 ? args[0] : null, ctx.Caller);
        if (caller is null || ctx.Receiver is null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller or receiver supplied to $your callable.");
            return raw.ToString();
        }
        bool cap = false;
        if (raw.FuncName == "Your") cap = true;
        if (kwargs.TryGetValue("capitalize", out var capStr)) cap = capStr.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (caller == ctx.Receiver) return cap? "Your":"your";
        var name = caller.GetDisplayName(ctx.Receiver);
        return name + "'s";
    }

    private static object? HandleConj(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        if(ctx.Caller is null || ctx.Receiver is null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller/receiver supplied to $conj callable");
            return raw.ToString();
        }
        var verb = args[0]??"";
        GameObject? obj = ResolveMappedActor(ctx.Mapping, args.Length > 1 ? args[1] : null, ctx.Caller);
        var (second, third) = Conjugate.VerbActorStanceComponents(verb, plural:false);
        return obj == ctx.Receiver ? second : third;
    }
    private static object? HandlePConj(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        if(ctx.Caller is null || ctx.Receiver is null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller/receiver supplied to $conj callable");
            return raw.ToString();
        }
        var verb = args[0]??"";
        GameObject? obj = ResolveMappedActor(ctx.Mapping, args.Length > 1 ? args[1] : null, ctx.Caller);
        bool plural=false;
        if(obj is not null)
        {
            string? g = ResolveGender(obj);
            if(!string.IsNullOrEmpty(g)) plural = g.Equals("plural", StringComparison.OrdinalIgnoreCase);
        }
        var (second, third) = Conjugate.VerbActorStanceComponents(verb, plural:plural);
        return obj == ctx.Receiver ? second : third;
    }
    private static object? HandlePron(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        var pronoun = args[0]??"";
        List<string> options = [];
        for(int i=1;i<args.Length;i++) options.Add(args[i]??"");
        GameObject? obj = ctx.Caller;
        if(options.Count>0 && ctx.Mapping is not null && ctx.Mapping.ContainsKey(options[^1]))
        {
            var last = options[^1];
            if(ctx.Mapping[last] is GameObject go) obj = go;
            options.RemoveAt(options.Count-1);
        }
        object? optObj = null;
        if(options.Count==1) optObj = options[0];
        else if(options.Count>1) optObj = options;
        string? defaultGender = "neutral";
        if(obj is not null){
            string? g = ResolveGender(obj);
            if(!string.IsNullOrEmpty(g)) defaultGender = g;
        }
        string defaultViewpoint = "2nd person";
        if(kwargs.TryGetValue("viewpoint", out var vp)) defaultViewpoint = vp;
        var (firstSecond, third) = Pronouns.PronounToViewpoints(pronoun, optObj, null, defaultGender, defaultViewpoint);
        bool cap = false;
        if(raw.FuncName=="Pron") cap=true;
        if(cap){ firstSecond = Capitalize(firstSecond); third=Capitalize(third); }
        return obj == ctx.Receiver ? firstSecond : third;
    }
    private static string Capitalize(string s) => string.IsNullOrEmpty(s)?s: char.ToUpperInvariant(s[0]) + (s.Length>1? s[1..]: "");


    // Constructors. Each public ctor prepares only its callable-table triple
    // and delegates to the private core below, which owns the seven
    // field assignments. Construction-time only.
    public FuncParser(IReadOnlyDictionary<string, ParserCallable> callables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
        : this(new Dictionary<string, ParserCallable>(callables, StringComparer.Ordinal), new Dictionary<string, Delegate>(StringComparer.Ordinal), false, startChar, escapeChar, maxNesting, defaultKwargs)
    {
    }


    public FuncParser(IDictionary<string, Delegate> genericCallables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
        : this(new Dictionary<string, ParserCallable>(StringComparer.Ordinal), new Dictionary<string, Delegate>(genericCallables, StringComparer.Ordinal), true, startChar, escapeChar, maxNesting, defaultKwargs)
    {
        // Wrap each generic in the shape-sniffing adapter below.
        foreach(var kv in genericCallables) _callables[kv.Key]=BuildGenericWrapper(kv.Value, kv.Key);
        ValidateGenericCallables(genericCallables);
    }
    // Mixed table: ParserCallables are stored as-is, Delegates are adapted.
    public FuncParser(IDictionary<string, object> mixedCallables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
        : this(new Dictionary<string, ParserCallable>(StringComparer.Ordinal), CollectMixedGenerics(mixedCallables), HasMixedGenerics(mixedCallables), startChar, escapeChar, maxNesting, defaultKwargs)
    {
        foreach(var kv in mixedCallables){
            if(kv.Value is ParserCallable pc) _callables[kv.Key]=pc;
            else if(kv.Value is Delegate d) _callables[kv.Key]=BuildGenericWrapper(d, kv.Key);
        }
        if(_hasGeneric) ValidateGenericCallables(_genericCallables);
    }

    // Single field-init core for the three public ctors above.
    private FuncParser(Dictionary<string, ParserCallable> callables, Dictionary<string, Delegate> genericCallables, bool hasGeneric, char startChar, char escapeChar, int maxNesting, IDictionary<string, object?>? defaultKwargs)
    {
        _callables = callables;
        _genericCallables = genericCallables;
        _hasGeneric = hasGeneric;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs is not null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
    }

    // Construction-time scan of a mixed table: ParserCallable entries are
    // stored as-is, so only plain Delegates land in the generics triple.
    private static Dictionary<string, Delegate> CollectMixedGenerics(IDictionary<string, object> mixedCallables)
    {
        Dictionary<string, Delegate> genDict = new(StringComparer.Ordinal);
        foreach (var kv in mixedCallables)
        {
            if (kv.Value is Delegate d && d is not ParserCallable)
                genDict[kv.Key] = d;
        }
        return genDict;
    }

    private static bool HasMixedGenerics(IDictionary<string, object> mixedCallables)
    {
        foreach (var kv in mixedCallables)
        {
            if (kv.Value is Delegate d && d is not ParserCallable)
                return true;
        }
        return false;
    }

    // Empty table: every `$...` echoes back unparsed.
    public FuncParser() : this(new Dictionary<string, ParserCallable>(StringComparer.Ordinal)) {}

    public IReadOnlyDictionary<string, ParserCallable> Callables => _callables;
    public char StartCharProp => _startChar;
    public char EscapeCharProp => _escapeChar;
    public int MaxNestingProp => _maxNesting;
    public IReadOnlyDictionary<string, object?> DefaultKwargs => _defaultKwargs;

    // Shape decided once at wrap time from the immutable delegate signature.
    private enum GenericWrapperShape { ArgsAndKwargs, ArgsArray, ZeroArgs, Spread }
    // Adapts a plain Delegate to the ParserCallable shape by sniffing its
    // signature: 2+ params get (string[] args, merged kwargs); one array
    // param gets (args); zero params get (); anything else spreads args
    // positionally. DelegateInvoker turns arity/type mismatches into
    // TargetParameterCountException, mapped below to ParsingError/"".
    private ParserCallable BuildGenericWrapper(Delegate del, string key)
    {
        var pars = del.Method.GetParameters();
        var shape = pars.Length >= 2 ? GenericWrapperShape.ArgsAndKwargs
            : pars.Length == 1 && pars[0].ParameterType.IsArray ? GenericWrapperShape.ArgsArray
            : pars.Length == 0 ? GenericWrapperShape.ZeroArgs
            : GenericWrapperShape.Spread;
        return (a,k,ctx,raw) => {
            try
            {
                return shape switch
                {
                    GenericWrapperShape.ArgsAndKwargs => DelegateInvoker.Invoke(del, [a, BuildMergedKwargs(k, ctx)]),
                    GenericWrapperShape.ArgsArray => DelegateInvoker.Invoke(del, [a]),
                    GenericWrapperShape.ZeroArgs => DelegateInvoker.Invoke(del, []),
                    _ => DelegateInvoker.Invoke(del, a.Cast<object?>().ToArray()),
                };
            }
            catch (System.Reflection.TargetParameterCountException tpe)
            {
                if (ctx.RaiseErrors) throw new ParsingError($"Parse-func callable '{key}' argument mismatch: {tpe.Message}");
                return "";
            }
        };
    }
    // Kwargs handed to Delegate callables: the parsed string kwargs plus the
    // reserved entries (funcparser, raise_errors, caller, receiver, mapping),
    // which win over same-named parsed kwargs.
    private Dictionary<string, object?> BuildMergedKwargs(Dictionary<string,string> k, ParserContext ctx)
    {
        var kwargsObj = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach(var kk in k) kwargsObj[kk.Key]=kk.Value;
        kwargsObj["funcparser"] = this;
        kwargsObj["raise_errors"] = ctx.RaiseErrors;
        if(ctx.Caller is not null) kwargsObj["caller"]=ctx.Caller;
        if(ctx.Receiver is not null) kwargsObj["receiver"]=ctx.Receiver;
        if(ctx.Mapping is not null) kwargsObj["mapping"]=ctx.Mapping;
        return kwargsObj;
    }
    public void ValidateGenericCallables(IDictionary<string, Delegate> callables)
    {
        foreach(var kv in callables){
            var del = kv.Value;
            System.Reflection.MethodInfo method;
            try{ method = del.Method; }catch(Exception ex){ try { AtherizLogger.LogError($"Could not run getfullargspec on {kv.Key}: {ex}"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed ParsedFunc.ValidateGenericCallables: " + logEx.Message, "ParsedFunc"); } continue; }
            var pars = method.GetParameters();
            bool hasVarArgs = pars.Any(p=> p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length>0 || p.ParameterType.IsArray);
            // A params-array (or any array) param stands in for *args; any
            // Dictionary param stands in for **kwargs.
            bool hasVarKw = pars.Any(p=> p.ParameterType.IsGenericType && (p.ParameterType.GetGenericTypeDefinition()==typeof(Dictionary<,>) || p.ParameterType.GetGenericTypeDefinition()==typeof(IDictionary<,>)));
            // Heuristic: any Dictionary param counts as **kwargs support.
            if(!hasVarArgs) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support *args.");
            if(!hasVarKw) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support **kwargs.");
        }
    }
    // Runs one parsed call: merges kwargs, builds the context, invokes.
    public object? Execute(ParsedFunc pf, bool raiseErrors = false, IDictionary<string, object?>? reservedKwargs = null)
    {
        var funcname = pf.FuncName;
        if (!_callables.TryGetValue(funcname, out var func))
        {
            if(raiseErrors) throw new ParsingError($"Unknown parsed function '{pf}' (available: {string.Join(", ", _callables.Keys.Select(k=>"'"+k+"'"))})");
            return pf.ToString();
        }
        var argsStr = pf.Args.Select(o=> o?.ToString() ?? "").ToArray();
        // Precedence: defaults, then parsed kwargs, then reserved, then funcparser/raise_errors.
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach(var kv in _defaultKwargs) merged[kv.Key]=kv.Value;
        foreach(var kv in pf.Kwargs) merged[kv.Key]=kv.Value;
        // Reserved entries are folded in last so they take precedence.
        if(reservedKwargs is not null) foreach(var kv in reservedKwargs) merged[kv.Key]=kv.Value;
        merged["funcparser"]=this;
        merged["raise_errors"]=raiseErrors;
        // Context objects come from the merged kwargs.
        var ctx = new ParserContext{ RaiseErrors=raiseErrors };
        if(merged.TryGetValue("caller", out var co) && co is GameObject gco) ctx.Caller=gco;
        if(merged.TryGetValue("receiver", out var ro) && ro is GameObject gro) ctx.Receiver=gro;
        if(merged.TryGetValue("mapping", out var mo) && mo is IDictionary<string, object?> md) ctx.Mapping=md;
        // Callables take string kwargs; live objects travel via the context.
        var kwargsStr = merged.ToDictionary(kv=>kv.Key, kv=> kv.Value?.ToString() ?? "", StringComparer.Ordinal);
        try
        {
            var ret = func(argsStr, kwargsStr, ctx, pf);
            return ret;
        }
        catch (ParsingError)
        {
            if(raiseErrors) throw;
            return pf.ToString();
        }
        catch (Exception)
        {
            // Non-raising mode: fall through to echoing the raw call.
            if(raiseErrors) throw;
            return pf.ToString();
        }
    }

    // Full parse: renders to a string, or hands back the raw call result when returnStr is false.
    public object? Parse(string? text, bool raiseErrors = false, bool escape = false, bool strip = false, bool returnStr = true, IDictionary<string, object?>? reservedKwargs = null)
    {
        if (text is null) return "";
        if (text.Length > MaxMessageSize) throw new ParsingError($"Input too long ({text.Length} chars)");
        if (string.IsNullOrEmpty(text)) return text;
        // Reserved kwargs (caller/receiver/mapping) flow into every call's context.
        return ParseInternal(text, raiseErrors, escape, strip, returnStr, reservedKwargs, _callables, _startChar, _escapeChar, _maxNesting, _defaultKwargs, this);
    }
    // Convenience overload packing caller/receiver/mapping into reserved kwargs.
    public object? Parse(string? text, GameObject? caller, GameObject? receiver, IDictionary<string, object?>? mapping, bool raiseErrors = false, bool escape = false, bool strip = false, bool returnStr = true)
    {
        var reserved = new Dictionary<string, object?>(StringComparer.Ordinal);
        if(caller is not null) reserved["caller"]=caller;
        if(receiver is not null) reserved["receiver"]=receiver;
        if(mapping is not null) reserved["mapping"]=mapping;
        var res = Parse(text, raiseErrors, escape, strip, returnStr, reserved);
        return res;
    }
    public object? ParseToAny(string? text, bool raiseErrors = false, bool escape = false, bool strip = false, IDictionary<string, object?>? reservedKwargs = null)
        => Parse(text, raiseErrors, escape, strip, false, reservedKwargs);

    // Static entry used by GameObject.Msg: runs actor-stance `$` calls, then `{key}` substitution.
    public static string Parse(string? text, GameObject? actor, GameObject? receiver, IDictionary<string, object?>? mapping, bool raiseErrors = false, bool escape = false, bool strip = false)
    {
        if (text is null) return "";
        if (text.Length > MaxMessageSize) throw new ParsingError($"Input too long ({text.Length} chars)");
        if (string.IsNullOrEmpty(text)) return text;
        bool hasFunc = text.Contains(StartChar);
        bool hasDirector = mapping is not null && text.Contains('{') && text.Contains('}');
        if (!hasFunc && !hasDirector) return text;
        string afterFunc = text;
        if(hasFunc){
            // Actor-stance `$` pass over the shared static table.
            var reserved = new Dictionary<string, object?>(StringComparer.Ordinal);
            if(actor is not null) reserved["caller"]=actor;
            if(receiver is not null) reserved["receiver"]=receiver;
            if(mapping is not null) reserved["mapping"]=mapping;
            var obj = ParseInternalStaticLegacy(text, raiseErrors, escape, strip, true, reserved);
            afterFunc = obj?.ToString() ?? "";
        }
        if (hasDirector && mapping is not null)
        {
            var displayMap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in mapping) displayMap[kv.Key] = kv.Value is GameObject go ? (receiver is not null ? go.GetDisplayName(receiver) : go.Name) : kv.Value?.ToString() ?? "";
            var safe = new FuncParserHelpers.SafeFormatMap(displayMap);
            afterFunc = safe.Format(afterFunc);
        }
        return afterFunc;
    }
    public static string Parse(string? text, IDictionary<string, object?>? mapping, bool raiseErrors = false)
        => Parse(text, null, null, mapping, raiseErrors);

    // Common engine behind the instance and static entries. Owner is the
    // instance the call came through (null on the static path, which has no
    // instance): generic callables already capture their owning instance at
    // wrap time, and the same reference is threaded into merged kwargs so
    // callables see one consistent value.
    private static object? ParseInternal(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs, IReadOnlyDictionary<string, ParserCallable> callables, char startChar, char escapeChar, int maxNesting, IReadOnlyDictionary<string, object?> defaultKwargs, FuncParser? owner = null)
    {
        return new RecursiveParser(str, raiseErrors, escapeMode, stripMode, returnStr, reservedKwargs, callables, startChar, escapeChar, maxNesting, defaultKwargs, owner).Run();
    }

    // Recursive-descent parser. Each open `$name(` becomes a Frame; a nested
    // `$` recurses instead of pushing onto a manual stack. Echo text (the raw
    // source of a call, used when it stays unparsed) is appended at the same
    // points as the previous char-loop scanner, so unknown-func echo is
    // byte-identical (pinned by FuncParserCharacterizationTests).
    // Two accepted divergences from the previous scanner, both unpinned and
    // unreachable from production (no src callers use returnStr:false):
    // consecutive top-level calls stringify in order ("12"; the old code
    // leaked the first result as a phantom arg and returned raw 1), and a
    // trailing `$$`/escape after a pure call keeps the result ("4$"; the
    // old code silently dropped it).
    private sealed class RecursiveParser
    {
        readonly string str;
        readonly bool raiseErrors, escapeMode, stripMode, returnStr;
        readonly IDictionary<string, object?>? reservedKwargs;
        readonly IReadOnlyDictionary<string, ParserCallable> callables;
        readonly char startChar, escapeChar;
        readonly int maxNesting;
        readonly IReadOnlyDictionary<string, object?> defaultKwargs;
        readonly FuncParser? owner;
        int pos;
        readonly StringBuilder outTop = new();
        object? topPending;
        bool sawTopLiteral;

        // One open `$name(` frame: the call currently being accumulated.
        sealed class Frame
        {
            public string Name = "";
            public readonly List<object?> Args = new();
            public readonly Dictionary<string, object?> Kwargs = new(StringComparer.Ordinal);
            public readonly StringBuilder Echo = new();
            public StringBuilder Text = new();
            public object? Pending;
            public int Quoted = -1;
            public string QuotedChar = "";
            public bool QuotedSeen = false;
            public int Paren, Square, Curly;
            public string CurrentKwarg = "";
            public Frame(char start) { Echo.Append(start); }
        }

        internal RecursiveParser(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs, IReadOnlyDictionary<string, ParserCallable> callables, char startChar, char escapeChar, int maxNesting, IReadOnlyDictionary<string, object?> defaultKwargs, FuncParser? owner)
        {
            this.str = str; this.raiseErrors = raiseErrors; this.escapeMode = escapeMode; this.stripMode = stripMode; this.returnStr = returnStr;
            this.reservedKwargs = reservedKwargs; this.callables = callables; this.startChar = startChar; this.escapeChar = escapeChar;
            this.maxNesting = maxNesting; this.defaultKwargs = defaultKwargs; this.owner = owner;
        }

        // Runs one closed call against this parser's callable table.
        object? ExecuteFrame(ParsedFunc pf, bool re)
        {
            if (!callables.TryGetValue(pf.FuncName, out var func))
            {
                if (re) throw new ParsingError($"Unknown parsed function '{pf}' (available: {string.Join(", ", callables.Keys.Select(k => "'" + k + "'"))})");
                return pf.ToString();
            }
            var argsStr = pf.Args.Select(o => o?.ToString() ?? "").ToArray();
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in defaultKwargs) merged[kv.Key] = kv.Value;
            foreach (var kv in pf.Kwargs) merged[kv.Key] = kv.Value;
            if (reservedKwargs is not null) foreach (var kv in reservedKwargs) merged[kv.Key] = kv.Value;
            merged["funcparser"] = owner; // instance entry supplies the live parser, matching Execute; the static path has no instance
            merged["raise_errors"] = re;
            // Context objects come from the merged kwargs.
            var c = new ParserContext { RaiseErrors = re };
            if (merged.TryGetValue("caller", out var co2) && co2 is GameObject gco2) c.Caller = gco2;
            if (merged.TryGetValue("receiver", out var ro2) && ro2 is GameObject gro2) c.Receiver = gro2;
            if (merged.TryGetValue("mapping", out var mo2) && mo2 is IDictionary<string, object?> md2) c.Mapping = md2;
            var kwargsStr = merged.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "", StringComparer.Ordinal);
            try
            {
                var ret = func(argsStr, kwargsStr, c, pf);
                return ret;
            }
            catch (ParsingError) { if (re) throw; return pf.ToString(); } catch (Exception) { if (re) throw; return pf.ToString(); }
        }

        internal object? Run()
        {
            sawTopLiteral = returnStr;
            int n = str.Length;
            bool escapedTop = false;
            while (pos < n)
            {
                char ch = str[pos];
                if (escapedTop) { outTop.Append(ch); escapedTop = false; pos++; continue; } // Escaped char: emit literally.
                if (ch == escapeChar) { if (pos + 1 >= n) { outTop.Append(ch); pos++; continue; } escapedTop = true; pos++; continue; } // Backslash escapes the next char; a trailing lone one emits as-is.
                if (ch == startChar && pos + 1 < n && str[pos + 1] == startChar) { outTop.Append(startChar); pos += 2; continue; } // `$$` is a literal `$`.
                if (ch == startChar) { ParseTopCall(); continue; } // A lone `$` always opens a call.
                outTop.Append(ch); sawTopLiteral = true; pos++; continue; // Ordinary char.
            }
            // End of input. Top level keeps no leftover text (literals append
            // straight to outTop), so only the pending call result matters:
            // it joins the output when literal text was seen.
            string pendStr = topPending?.ToString() ?? "";
            if (pendStr != "" && sawTopLiteral) outTop.Append(pendStr);
            if (!returnStr && pendStr != "" && outTop.Length == 0) return topPending; // returnStr:false hands back the raw result when the output is just the call.
            return outTop.ToString();
        }

        void ParseTopCall()
        {
            var frame = new Frame(startChar);
            pos++; // consume $
            var (closed, _, echo) = ParseFrame(frame, 1, 0, 0, 0);
            if (!closed) outTop.Append(echo); // Unclosed input echoes its raw source.
            // A closed frame already settled its result into outTop/topPending.
        }

        // Parses one `$name(...` frame. Returns (closed, value, echo): a frame
        // closed by `)` carries the executed result; input ending mid-frame
        // returns closed:false with the raw source (echo+text+pending) so the
        // caller echoes it verbatim. Pile-ups of unclosed frames echo in input
        // order instead of inside-out; that path was never pinned by tests.
        (bool Closed, object? Value, string Echo) ParseFrame(Frame f, int depth, int paren0, int square0, int curly0)
        {
            int n = str.Length;
            bool escaped = false;
            while (pos < n)
            {
                char ch = str[pos];
                if (escaped) { f.Text.Append(ch); escaped = false; pos++; continue; } // Escaped char: emit literally.
                if (ch == escapeChar) { if (pos + 1 >= n) { f.Text.Append(ch); pos++; continue; } escaped = true; pos++; continue; } // Backslash escapes the next char; a trailing lone one emits as-is.
                if (ch == startChar && pos + 1 < n && str[pos + 1] == startChar) { f.Text.Append(startChar); pos += 2; continue; } // `$$` is a literal `$`.
                if (ch == startChar && f.Quoted < 0)
                {
                    // Nested `$` outside quotes opens an inner call, evaluated first (inside-out).
                    if (depth >= maxNesting)
                    {
                        // Too deep: leave the `$` as literal text (or throw when raising).
                        if (raiseErrors) throw new ParsingError($"Only allows for parsing nesting function defs to a max depth of {maxNesting}.");
                        f.Text.Append(ch); pos++; continue;
                    }
                    FlushPending(f);
                    string outerText = f.Text.ToString();
                    f.Text.Clear(); f.QuotedSeen = false;
                    pos++; // consume $
                    var child = new Frame(startChar);
                    var (closed, value, echo) = ParseFrame(child, depth + 1, f.Paren, f.Square, f.Curly);
                    if (!closed) return (false, null, f.Echo.ToString() + f.Text.ToString() + (f.Pending?.ToString() ?? "") + echo);
                    if (outerText.Length > 0) { f.Text.Append(outerText).Append(value?.ToString() ?? ""); f.Pending = null; } // A nested result beside literal text merges into text ...
                    else { f.Text.Clear(); f.Pending = value; } // ... alone it stays a raw pending value.
                    continue;
                }
                if (ch != ',' && ch != ')' && ch != '=') FlushPending(f); // Ordinary chars absorb a pending nested result, except at separators where it stays addressable.
                if (ch == '"' || ch == '\'')
                {
                    // Quotes: the quote chars are dropped; contents stay verbatim.
                    if (f.Quoted >= 0)
                    {
                        if (ch.ToString() == f.QuotedChar)
                        {
                            if (f.Quoted == 0) { if (f.Text.Length > 0) f.Text.Remove(0, 1); f.Quoted = -1; f.QuotedChar = ""; }
                            else if (f.Quoted > 0) { f.Text.Remove(f.Quoted, 1); f.Quoted = -1; f.QuotedChar = ""; }
                            else { f.Quoted = -1; f.QuotedChar = ""; }
                        }
                        else f.Text.Append(ch);
                    }
                    else { f.Text.Append(ch); f.Quoted = f.Text.Length - 1; f.QuotedChar = ch.ToString(); f.QuotedSeen = true; }
                    pos++; continue;
                }
                if (f.Quoted >= 0) { f.Text.Append(ch); pos++; continue; } // Inside quotes, everything is literal.
                if (ch == '(')
                {
                    // The first `(` ends the function name, taken verbatim; deeper ones are literal.
                    if (f.Name == "") { f.Name = f.Text.ToString(); f.Echo.Append(f.Name).Append(ch); f.Text.Clear(); } else f.Text.Append(ch);
                    f.Paren++; pos++; continue;
                }
                if (ch == '[' || ch == ']') { f.Text.Append(ch); f.Square += ch == ']' ? -1 : 1; pos++; continue; } // Brackets are literal; depth is tracked so separators inside them stay literal.
                if (ch == '{' || ch == '}') { f.Text.Append(ch); f.Curly += ch == '}' ? -1 : 1; pos++; continue; } // Braces likewise (director `{key}` runs as a separate pass).
                if (ch == '=')
                {
                    // `=`: everything so far is the kwarg name. A pending nested
                    // result becomes the name text; the name is trimmed for
                    // lookup but kept verbatim for the echo.
                    if (f.Pending is string per2 && per2 != "") f.Text = new StringBuilder(per2);
                    else if (f.Pending is not null && f.Pending.ToString() != "") f.Text = new StringBuilder(f.Pending.ToString()!);
                    string kwname = f.Text.ToString().Trim();
                    f.Kwargs[kwname] = "";
                    f.Echo.Append(f.Text.ToString()).Append(ch);
                    f.Text.Clear(); f.CurrentKwarg = kwname; pos++; continue;
                }
                if (ch == ',' || ch == ')')
                {
                    // `,` or a call-level `)`: the pending nested result or the
                    // accumulated text becomes the next arg/kwarg value.
                    // Inside inner parens/brackets the separator is literal text.
                    if (f.Paren > 1) { f.Text.Append(ch); if (ch == ')') f.Paren--; pos++; continue; }
                    if (f.Square > 0 || f.Curly > 0) { f.Text.Append(ch); pos++; continue; }
                    // Finalize one arg: a pending nested result keeps its raw
                    // object for positional args; everything else stringifies here.
                    if (f.Pending is string per3 && per3 != "")
                    {
                        if (f.CurrentKwarg != "") f.Kwargs[f.CurrentKwarg] = per3; else f.Args.Add(per3);
                    }
                    else if (f.Pending is not null && f.Pending.ToString() != "")
                    {
                        var sE = f.Pending.ToString()!;
                        if (f.CurrentKwarg != "") f.Kwargs[f.CurrentKwarg] = sE; else f.Args.Add(f.Pending);
                    }
                    else
                    {
                        if (!f.QuotedSeen) f.Text = new StringBuilder(f.Text.ToString().Trim());
                        if (f.CurrentKwarg != "") f.Kwargs[f.CurrentKwarg] = f.Text.ToString();
                        else if (f.QuotedSeen || f.Text.ToString().Trim().Length > 0) f.Args.Add(f.Text.ToString());
                    }
                    // The echo records the executed value plus the raw text, so an unclosed parent echoes faithfully.
                    string execStr = f.Pending?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(execStr)) f.Echo.Append(execStr);
                    f.Echo.Append(f.Text.ToString()).Append(ch);
                    f.CurrentKwarg = ""; f.Pending = null; f.Text.Clear(); f.QuotedSeen = false;
                    if (ch == ')')
                    {
                        // `)`: consume it, run the call (blanked in strip mode,
                        // escaped in escape mode), and settle the result: nested
                        // frames hand the raw value to the parent; top-level
                        // frames append it to the output, or stash it as pending
                        // when no literal text precedes it.
                        f.Paren = 0;
                        pos++;
                        object? exec = stripMode ? (object?)"" : escapeMode ? (object?)(escapeChar + f.Echo.ToString()) : ExecuteFrame(ToParsedFunc(f, depth, paren0, square0, curly0), raiseErrors);
                        if (depth > 1) return (true, exec, "");
                        if (sawTopLiteral) outTop.Append(exec?.ToString() ?? "");
                        else topPending = exec;
                        return (true, null, "");
                    }
                    pos++; continue;
                }
                f.Text.Append(ch); pos++; // Ordinary char.
            }
            // End of input with the frame still open: echo the raw source.
            return (false, null, f.Echo.ToString() + f.Text.ToString() + (f.Pending?.ToString() ?? ""));
        }

        // Folds a pending nested result into the text buffer.
        static void FlushPending(Frame f)
        {
            if (f.Pending is string ers && ers != "") { f.Text.Append(ers); f.Pending = null; }
            else if (f.Pending is not null && f.Pending.ToString() != "") { f.Text.Append(f.Pending.ToString()!); f.Pending = null; }
        }

        ParsedFunc ToParsedFunc(Frame f, int depth, int paren0, int square0, int curly0)
        {
            // Only FullStr is populated: InFuncStr is always empty at execution time.
            var pf = new ParsedFunc { FuncName = f.Name };
            pf.Prefix = startChar;
            pf.Args.AddRange(f.Args);
            foreach (var kv in f.Kwargs) pf.Kwargs[kv.Key] = kv.Value;
            pf.FullStr.Append(f.Echo.ToString());
            pf.CurrentKwarg = f.CurrentKwarg;
            if (depth > 1) { pf.OpenLParens = paren0; pf.OpenLSquare = square0; pf.OpenLCurly = curly0; }
            return pf;
        }
    }

    private static object? ParseInternalStaticLegacy(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs)
    {
        // Static path runs on the shared actor-stance table.
        return ParseInternal(str, raiseErrors, escapeMode, stripMode, returnStr, reservedKwargs, ActorStanceCallables, StartChar, EscapeChar, MaxNesting, new Dictionary<string, object?>(StringComparer.Ordinal));
    }
}
