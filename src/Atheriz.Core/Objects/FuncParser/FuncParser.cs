// Port of atheriz/objects/funcparser.py:1
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Atheriz.Core;
using Atheriz.Core.Objects.VerbConjugation;namespace Atheriz.Core.Objects;

/// <summary>
/// Port of <c>atheriz/objects/funcparser.py</c> (1723 LOC) compressed to ~500 C#.
/// Faithful: `$` is FUNCPARSER_START_CHAR, `\` escapes, `$$` → literal `$`,
/// `MAX_NESTING=20` guard, quoted args, nesting, inside-out execution, error handling.
/// Supports actor-stance callables `$You/$you/$obj/$Obj/$conj/$pconj/$pron/$Pron`
/// + director <c>{key}</c> via <see cref="FuncParserHelpers.SafeFormatMap"/>.
/// Public API: instance <c>FuncParser</c> with <c>Parse</c>/<c>ParseToAny</c>/<c>Execute</c>
/// plus legacy static <c>Parse</c> for <c>GameObject.Msg</c>.
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
    // Generic fallback for instance callables that accept merged dict
    private delegate object? GenericCallable(string[] args, Dictionary<string, object?> kwargs, ParsedFunc raw);

    public static readonly Dictionary<string, ParserCallable> FuncParserCallables;
    public static readonly Dictionary<string, ParserCallable> ActorStanceCallables;

    // Instance fields
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
                    // multi-arg: each arg must be valid py literal when raiseErrors
                    var converters = Enumerable.Repeat((object)"py", a.Length).ToArray();
                    var conv = FuncParserHelpers.SafeConvertToTypes( (converters, new Dictionary<string,object?>()), a.Cast<object?>().ToArray(), new Dictionary<string,object?>(), ctx.RaiseErrors);
                    // if conversion threw and RaiseErrors, it would have bubbled; otherwise pick from converted
                    var list = conv.args.Select(o=> o?.ToString() ?? "").ToArray();
                    if(list.Length>0) return list[rnd.Next(list.Length)];
                }
                return a[rnd.Next(a.Length)]; },
            ["pad"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; string t=a[0]??""; int w=78; if(k.TryGetValue("width", out var ws)&& long.TryParse(ws,out var wl)) w=(int)Math.Min(wl, FuncParserHelpers.MaxTextWidth); else if(a.Length>1&& long.TryParse(a[1], out var wl2)) w=(int)Math.Min(wl2, FuncParserHelpers.MaxTextWidth); string al="c"; if(k.TryGetValue("align", out var alv)) al=alv; else if(a.Length>2) al=a[2]; string fc=" "; if(k.TryGetValue("fillchar", out var fcv)) fc=fcv; else if(a.Length>3) fc=a[3]; return FuncParserHelpers.Pad(t,w,al,fc); },
            ["crop"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; string t=a[0]??""; int w=78; if(k.TryGetValue("width", out var ws)&& long.TryParse(ws,out var wl)) w=(int)Math.Min(wl, FuncParserHelpers.MaxTextWidth); else if(a.Length>1&& long.TryParse(a[1], out var wl2)) w=(int)Math.Min(wl2, FuncParserHelpers.MaxTextWidth); string suffix="[...]"; if(k.TryGetValue("suffix", out var sv)) suffix=sv; else if(a.Length>2) suffix=a[2]; return FuncParserHelpers.Crop(t,w,suffix); },
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
            ["pluralize"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; // mirroring python logic with raise_errors handling via ctx.RaiseErrors
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
    private static string JustifyHelper(string[] a, Dictionary<string,string> k, ParserContext ctx, string defAlign)
    {
        if(a.Length==0) return "";
        string text=a[0]??""; int width=78; string align=defAlign; int indent=0;
        if(k.TryGetValue("width", out var ws) && long.TryParse(ws, out var wl)) width=(int)Math.Min(wl, FuncParserHelpers.MaxTextWidth); else if(a.Length>1 && long.TryParse(a[1], out var wl2)) width=(int)Math.Min(wl2, FuncParserHelpers.MaxTextWidth);
        if(k.TryGetValue("align", out var alv)) align=alv; else if(a.Length>2) align=a[2];
        if(k.TryGetValue("indent", out var ivs) && int.TryParse(ivs, out var ivi)) indent=ivi; else if(a.Length>3 && int.TryParse(a[3], out var ivi2)) indent=ivi2;
        indent = Math.Max(0, Math.Min(indent, width));
        return FuncParserHelpers.Justify(text, width, align, indent);
    }
    private static string SafePyEval(string s)
    {
        s=s.Trim();
        if(string.IsNullOrEmpty(s)) return "";
        // Try py conversion via helpers for full fidelity
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

    private static object? HandleYou(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        GameObject? caller = ctx.Caller;
        if (args.Length>0 && ctx.Mapping is not null && ctx.Mapping.TryGetValue(args[0], out var mapped) && mapped is GameObject go) caller = go;
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
        GameObject? caller = ctx.Caller;
        if (args.Length>0 && ctx.Mapping is not null && ctx.Mapping.TryGetValue(args[0], out var mapped) && mapped is GameObject go) caller = go;
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
        string? key = args.Length>1? args[1]: null;
        GameObject? obj = ctx.Caller;
        if (key is not null && ctx.Mapping is not null && ctx.Mapping.TryGetValue(key, out var m) && m is GameObject go2) obj = go2;
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
        string? key = args.Length>1? args[1]: null;
        GameObject? obj = ctx.Caller;
        if (key is not null && ctx.Mapping is not null && ctx.Mapping.TryGetValue(key, out var m) && m is GameObject go2) obj = go2;
        bool plural=false;
        if(obj is not null)
        {
            string? g = obj is IGenderProvider gp ? gp.GetGender() : obj.Gender;
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
            string? g = obj is IGenderProvider gp ? gp.GetGender() : obj.Gender;
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


    // Instance constructors
    public FuncParser(IReadOnlyDictionary<string, ParserCallable> callables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
    {
        _callables = new Dictionary<string, ParserCallable>(callables, StringComparer.Ordinal);
        _genericCallables = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        _hasGeneric = false;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs is not null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
    }


    public FuncParser(IDictionary<string, Delegate> genericCallables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
    {
        _callables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal);
        _genericCallables = new Dictionary<string, Delegate>(genericCallables, StringComparer.Ordinal);
        _hasGeneric = true;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs is not null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
        // Wrap each generic in the shared shape-sniffing core (below).
        foreach(var kv in genericCallables) _callables[kv.Key]=BuildGenericWrapper(kv.Value, kv.Key);
        ValidateGenericCallables(genericCallables);
    }
    // Fallback constructor accepting IDictionary<string, object> where values are Delegate or ParserCallable
    public FuncParser(IDictionary<string, object> mixedCallables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
    {
        _callables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal);
        _genericCallables = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        _hasGeneric = false;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs is not null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
        var genDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach(var kv in mixedCallables){
            if(kv.Value is ParserCallable pc) _callables[kv.Key]=pc;
            else if(kv.Value is Delegate d){ genDict[kv.Key]=d; _hasGeneric=true; _genericCallables[kv.Key]=d;
                _callables[kv.Key]=BuildGenericWrapper(d, kv.Key);
            }
        }
        if(_hasGeneric) ValidateGenericCallables(genDict);
    }

    // Convenience for empty dict
    public FuncParser() : this(new Dictionary<string, ParserCallable>(StringComparer.Ordinal)) {}

    public IReadOnlyDictionary<string, ParserCallable> Callables => _callables;
    public char StartCharProp => _startChar;
    public char EscapeCharProp => _escapeChar;
    public int MaxNestingProp => _maxNesting;
    public IReadOnlyDictionary<string, object?> DefaultKwargs => _defaultKwargs;

    // Shared shape-sniffing core for Delegate callables (both generic ctors):
    // 2+ params forward exactly [string[] args, merged kwargs]; 1 array param
    // takes [args]; 0 params take []; anything else spreads args positionally.
    // DelegateInvoker normalizes arity AND type mismatches to
    // TargetParameterCountException, caught below as ParsingError/"".
    private ParserCallable BuildGenericWrapper(Delegate del, string key)
    {
        return (a,k,ctx,raw) => {
            try
            {
                var pars = del.Method.GetParameters();
                if(pars.Length>=2)
                    return DelegateInvoker.Invoke(del, new object?[]{ a, BuildMergedKwargs(k, ctx) });
                if(pars.Length==1 && pars[0].ParameterType.IsArray)
                    return DelegateInvoker.Invoke(del, new object?[]{ a });
                if(pars.Length==0)
                    return DelegateInvoker.Invoke(del, Array.Empty<object?>());
                return DelegateInvoker.Invoke(del, a.Cast<object?>().ToArray());
            }
            catch (System.Reflection.TargetParameterCountException tpe)
            {
                if (ctx.RaiseErrors) throw new ParsingError($"Parse-func callable '{key}' argument mismatch: {tpe.Message}");
                return "";
            }
        };
    }
    // Merged kwargs for Delegate forwarding: parsed string kwargs, then reserved
    // funcparser/raise_errors/caller/receiver/mapping (Execute precedence).
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
            // also consider params array via IsArray as varargs
            bool hasVarKw = pars.Any(p=> p.ParameterType.IsGenericType && (p.ParameterType.GetGenericTypeDefinition()==typeof(Dictionary<,>) || p.ParameterType.GetGenericTypeDefinition()==typeof(IDictionary<,>)) || p.ParameterType == typeof(Dictionary<string, object>) || p.ParameterType == typeof(Dictionary<string, string>) || p.ParameterType == typeof(Dictionary<string, object?>));
            // Heuristic: if delegate has at least one Dictionary param, consider hasVarKw
            // Check for ParamArray for kwargs not typical; we use dict presence.
            if(!hasVarArgs) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support *args.");
            if(!hasVarKw) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support **kwargs.");
        }
    }
    // Instance Execute with merging
    public object? Execute(ParsedFunc pf, bool raiseErrors = false, IDictionary<string, object?>? reservedKwargs = null)
    {
        var funcname = pf.FuncName;
        if (!_callables.TryGetValue(funcname, out var func))
        {
            if(raiseErrors) throw new ParsingError($"Unknown parsed function '{pf}' (available: {string.Join(", ", _callables.Keys.Select(k=>"'"+k+"'"))})");
            return pf.ToString();
        }
        var argsStr = pf.Args.Select(o=> o?.ToString() ?? "").ToArray();
        // Build kwargs dict: defaults < string kwargs < reserved < funcparser/raise_errors
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach(var kv in _defaultKwargs) merged[kv.Key]=kv.Value;
        foreach(var kv in pf.Kwargs) merged[kv.Key]=kv.Value;
        if(reservedKwargs is not null) foreach(var kv in reservedKwargs) merged[kv.Key]=kv.Value;
        merged["funcparser"]=this;
        merged["raise_errors"]=raiseErrors;
        // Extract caller/receiver/mapping for ctx
        var ctx = new ParserContext{ RaiseErrors=raiseErrors };
        if(merged.TryGetValue("caller", out var co) && co is GameObject gco) ctx.Caller=gco;
        if(merged.TryGetValue("receiver", out var ro) && ro is GameObject gro) ctx.Receiver=gro;
        if(merged.TryGetValue("mapping", out var mo) && mo is IDictionary<string, object?> md) ctx.Mapping=md;
        // Also try reserved directly
        if(reservedKwargs is not null){
            if(reservedKwargs.TryGetValue("caller", out var c2) && c2 is GameObject g2) ctx.Caller=g2;
            if(reservedKwargs.TryGetValue("receiver", out var r2) && r2 is GameObject gr2) ctx.Receiver=gr2;
            if(reservedKwargs.TryGetValue("mapping", out var m2) && m2 is IDictionary<string, object?> mm2) ctx.Mapping=mm2;
        }
        // Convert merged to string dict for func signature (but also keep object dict for generic)
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
            // log
            if(raiseErrors) throw;
            return pf.ToString();
        }
    }

    // Instance Parse that returns object? (string or raw)
    public object? Parse(string? text, bool raiseErrors = false, bool escape = false, bool strip = false, bool returnStr = true, IDictionary<string, object?>? reservedKwargs = null)
    {
        if (text is null) return "";
        if (text.Length > MaxMessageSize) throw new ParsingError($"Input too long ({text.Length} chars)");
        if (string.IsNullOrEmpty(text)) return text;
        // need to handle reservedKwargs that may contain caller/receiver/mapping for actor stance later? But instance parse's callables are generic; for actor stance we need to handle via reserved.
        // Use internal parser with instance fields
        return ParseInternal(text, raiseErrors, escape, strip, returnStr, reservedKwargs, _callables, _startChar, _escapeChar, _maxNesting, _defaultKwargs, this);
    }
    // Overload with actor/receiver/mapping convenience
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

    // Legacy static Parse used by GameObject
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
            // use static actor callables for legacy
            var reserved = new Dictionary<string, object?>(StringComparer.Ordinal);
            if(actor is not null) reserved["caller"]=actor;
            if(receiver is not null) reserved["receiver"]=receiver;
            if(mapping is not null) reserved["mapping"]=mapping;
            var obj = ParseInternalStaticLegacy(text, raiseErrors, escape, strip, true, reserved);
            afterFunc = obj?.ToString() ?? "";
        }
        if (hasDirector && mapping is not null && receiver is not null)
        {
            var displayMap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in mapping)
            {
                if (kv.Value is GameObject go) displayMap[kv.Key] = go.GetDisplayName(receiver);
                else displayMap[kv.Key] = kv.Value?.ToString() ?? "";
            }
            var safe = new FuncParserHelpers.SafeFormatMap(displayMap);
            afterFunc = safe.Format(afterFunc);
        }
        else if (hasDirector && mapping is not null)
        {
            var displayMap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in mapping) displayMap[kv.Key] = kv.Value is GameObject go ? go.Name : kv.Value?.ToString() ?? "";
            var safe = new FuncParserHelpers.SafeFormatMap(displayMap);
            afterFunc = safe.Format(afterFunc);
        }
        return afterFunc;
    }
    public static string Parse(string? text, IDictionary<string, object?>? mapping, bool raiseErrors = false)
        => Parse(text, null, null, mapping, raiseErrors);

    // Shared internal parser (instance-like). owner is the instance whose
    // Parse/Execute entry routed here (null on the static legacy path, which
    // genuinely has no instance): generic callables already capture their
    // owning instance at wrap time, and this threads the same reference into
    // the merged kwargs so ParserCallable callables see one consistent value.
    private static object? ParseInternal(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs, IReadOnlyDictionary<string, ParserCallable> callables, char startChar, char escapeChar, int maxNesting, IReadOnlyDictionary<string, object?> defaultKwargs, FuncParser? owner = null)
    {
        return new RecursiveParser(str, raiseErrors, escapeMode, stripMode, returnStr, reservedKwargs, callables, startChar, escapeChar, maxNesting, defaultKwargs, owner).Run();
    }

    // Stage 2b: recursive-descent replacement for the old char-loop scanner.
    // Each open `$name(` is a Frame; a nested `$` recurses instead of pushing
    // onto a manual callstack with snapshot/restore vars. Echo (old FullStr)
    // appends happen at exactly the old append points, so unknown-func echo
    // is byte-identical (pinned by FuncParserCharacterizationTests).
    // Two accepted divergences from the old loop, both unpinned and
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

        // One open `$name(` frame (old currFunc plus its shadow locals).
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

        // Old ExecuteWithCallables local (its ctx parameter was never read).
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
            merged["funcparser"] = owner; // instance entry supplies the live parser, matching Execute; the static legacy path has no instance
            merged["raise_errors"] = re;
            // Build ParserContext from merged
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
                if (escapedTop) { outTop.Append(ch); escapedTop = false; pos++; continue; } // old 567, top level
                if (ch == escapeChar) { if (pos + 1 >= n) { outTop.Append(ch); pos++; continue; } escapedTop = true; pos++; continue; } // old 568
                if (ch == startChar && pos + 1 < n && str[pos + 1] == startChar) { outTop.Append(startChar); pos += 2; continue; } // old 569: no sawTopLiteral, mirroring the old loop
                if (ch == startChar) { ParseTopCall(); continue; } // old 570/589: top level always opens
                outTop.Append(ch); sawTopLiteral = true; pos++; continue; // old 591
            }
            // Old 673-695 end-game. Top level keeps no leftover text (literals
            // append straight to outTop), so only the pending slot matters.
            string pendStr = topPending?.ToString() ?? "";
            if (pendStr != "" && sawTopLiteral) outTop.Append(pendStr);
            if (!returnStr && pendStr != "" && outTop.Length == 0) return topPending;
            return outTop.ToString();
        }

        void ParseTopCall()
        {
            var frame = new Frame(startChar);
            pos++; // consume $
            var (closed, _, echo) = ParseFrame(frame, 1, 0, 0, 0);
            if (!closed) outTop.Append(echo);
            // Closed frames settle themselves (old 642-652): the result is
            // appended to outTop when sawTopLiteral, else kept in topPending.
        }

        // Parses one `$name(...` frame. Returns (closed, value, echo): closed
        // frames carry the executed result; input ending mid-frame returns
        // closed:false with echo composed as echo+text+pending (the old
        // 659-672 single-frame reassembly, matched exactly). Multi-frame
        // pile-ups echo in input order instead of the old inside-out order;
        // that path was never pinned.
        (bool Closed, object? Value, string Echo) ParseFrame(Frame f, int depth, int paren0, int square0, int curly0)
        {
            int n = str.Length;
            bool escaped = false;
            while (pos < n)
            {
                char ch = str[pos];
                if (escaped) { f.Text.Append(ch); escaped = false; pos++; continue; } // old 567
                if (ch == escapeChar) { if (pos + 1 >= n) { f.Text.Append(ch); pos++; continue; } escaped = true; pos++; continue; } // old 568
                if (ch == startChar && pos + 1 < n && str[pos + 1] == startChar) { f.Text.Append(startChar); pos += 2; continue; } // old 569
                if (ch == startChar && f.Quoted < 0)
                {
                    // Old 570-588 nested open.
                    if (depth >= maxNesting)
                    {
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
                    if (outerText.Length > 0) { f.Text.Append(outerText).Append(value?.ToString() ?? ""); f.Pending = null; }
                    else { f.Text.Clear(); f.Pending = value; }
                    continue;
                }
                if (ch != ',' && ch != ')' && ch != '=') FlushPending(f); // old 592-593 exclusions
                if (ch == '"' || ch == '\'')
                {
                    // Old 594-602 quote open/close surgery.
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
                if (f.Quoted >= 0) { f.Text.Append(ch); pos++; continue; } // old 604
                if (ch == '(')
                {
                    // Old 605-607: the first `(` takes the name verbatim.
                    if (f.Name == "") { f.Name = f.Text.ToString(); f.Echo.Append(f.Name).Append(ch); f.Text.Clear(); } else f.Text.Append(ch);
                    f.Paren++; pos++; continue;
                }
                if (ch == '[' || ch == ']') { f.Text.Append(ch); f.Square += ch == ']' ? -1 : 1; pos++; continue; } // old 609
                if (ch == '{' || ch == '}') { f.Text.Append(ch); f.Curly += ch == '}' ? -1 : 1; pos++; continue; } // old 610
                if (ch == '=')
                {
                    // Old 611-615: a pending nested result becomes the name
                    // text (kept for the separator); the name is verbatim.
                    if (f.Pending is string per2 && per2 != "") f.Text = new StringBuilder(per2);
                    else if (f.Pending is not null && f.Pending.ToString() != "") f.Text = new StringBuilder(f.Pending.ToString()!);
                    // Key is trimmed but the echo keeps the verbatim text.
                    string kwname = f.Text.ToString().Trim();
                    f.Kwargs[kwname] = "";
                    f.Echo.Append(f.Text.ToString()).Append(ch);
                    f.Text.Clear(); f.CurrentKwarg = kwname; pos++; continue;
                }
                if (ch == ',' || ch == ')')
                {
                    // Old 617-618: inside inner parens/brackets the separator is literal.
                    if (f.Paren > 1) { f.Text.Append(ch); if (ch == ')') f.Paren--; pos++; continue; }
                    if (f.Square > 0 || f.Curly > 0) { f.Text.Append(ch); pos++; continue; }
                    // Old 619-628 arg finalize. A pending nested result keeps
                    // its raw object for positional args (old 623); everything
                    // else stringifies here, exactly as the old branches did.
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
                    // Old 629-632: echo splices the executed value, then text.
                    string execStr = f.Pending?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(execStr)) f.Echo.Append(execStr);
                    f.Echo.Append(f.Text.ToString()).Append(ch);
                    f.CurrentKwarg = ""; f.Pending = null; f.Text.Clear(); f.QuotedSeen = false;
                    if (ch == ')')
                    {
                        // Old 633-653 close (the old loop's trailing i++
                        // consumed the `)`; advance here since we return).
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
                f.Text.Append(ch); pos++; // old 657: ordinary char
            }
            // End of input with the frame still open: unterminated echo.
            return (false, null, f.Echo.ToString() + f.Text.ToString() + (f.Pending?.ToString() ?? ""));
        }

        // Old 592-593: ordinary chars flush a pending nested result into text.
        static void FlushPending(Frame f)
        {
            if (f.Pending is string ers && ers != "") { f.Text.Append(ers); f.Pending = null; }
            else if (f.Pending is not null && f.Pending.ToString() != "") { f.Text.Append(f.Pending.ToString()!); f.Pending = null; }
        }

        ParsedFunc ToParsedFunc(Frame f, int depth, int paren0, int square0, int curly0)
        {
            // InFuncStr is empty at execution time in the old loop too (it was
            // cleared right after the echo append), so only FullStr is set.
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
        // delegate to ParseInternal using static ActorStanceCallables
        return ParseInternal(str, raiseErrors, escapeMode, stripMode, returnStr, reservedKwargs, ActorStanceCallables, StartChar, EscapeChar, MaxNesting, new Dictionary<string, object?>(StringComparer.Ordinal));
    }
}
