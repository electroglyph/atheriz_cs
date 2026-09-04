// Port of atheriz/objects/funcparser.py:1
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Atheriz.Core.Objects.VerbConjugation;

namespace Atheriz.Core.Objects;

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
    public const int _MAX_NESTING = 20;
    public const char _START_CHAR = '$';
    public const char _ESCAPE_CHAR = '\\';

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
        public List<char> FullStr = new();
        public List<char> InFuncStr = new();
        public int DoubleQuoted = -1;
        public string QuotedChar = "";
        public string CurrentKwarg = "";
        public int OpenLParens;
        public int OpenLSquare;
        public int OpenLCurly;
        // alias for typo in python source
        public int OpenLsquate { get => OpenLSquare; set => OpenLSquare = value; }
        public object? ExecReturn = "";
        public ParsedFunc(char prefix) { Prefix = prefix; FullStr.Add(prefix); }
        public ParsedFunc() { }
        public (string, List<object?>, Dictionary<string, object?>) Get() => (FuncName, Args, Kwargs);
        public override string ToString()
        {
            var fs = new string(FullStr.ToArray());
            var ins = new string(InFuncStr.ToArray());
            return fs + ins;
        }
    }

    public delegate object? ParserCallable(string[] args, Dictionary<string, string> kwargs, ParserContext ctx, ParsedFunc raw);
    // Generic fallback for instance callables that accept merged dict
    private delegate object? GenericCallable(string[] args, Dictionary<string, object?> kwargs, ParsedFunc raw);

    private static readonly Dictionary<string, ParserCallable> FuncParserCallables;
    private static readonly Dictionary<string, ParserCallable> ActorStanceCallables;

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
            ["random"] = (a,k,ctx,raw) => { var rnd=new Random(); if(a.Length==0) return rnd.Next(0,2); if(a.Length==1){ if(a[0].Contains('.')){ double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mx); return rnd.NextDouble()*mx; } int.TryParse(a[0], out var mx2); return rnd.Next(0,mx2+1); } { double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mn); double.TryParse(a[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var mx); bool isFloat=a[0].Contains('.')||a[1].Contains('.'); if(isFloat) return mn + (mx-mn)*rnd.NextDouble(); return rnd.Next((int)mn,(int)mx+1); } },
            ["randint"] = (a,k,ctx,raw) => { var rnd=new Random(); if(a.Length==0) return rnd.Next(0,2); if(a.Length==1){ int.TryParse(a[0], out var mx2); return rnd.Next(0,mx2+1); } int.TryParse(a[0], out var mn2); int.TryParse(a[1], out var mx3); return rnd.Next(mn2,mx3+1); },
            ["choice"] = (a,k,ctx,raw) => { if(a.Length==0) return ""; var rnd=new Random();
                if(a.Length==1){ var single=a[0].Trim(); if(single.StartsWith("[")&&single.EndsWith("]")){ try{ var inner=single.Substring(1,single.Length-2); var items=inner.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Trim()).ToArray(); if(items.Length>0) return items[rnd.Next(items.Length)].Trim('\'','"'); }catch{} } try{
                        var conv = FuncParserHelpers.SafeConvertToTypes( (new object[]{"py"}, new Dictionary<string,object>()), new object?[]{single}, new Dictionary<string,object?>(), ctx.RaiseErrors); if(conv.args.Length>0 && conv.args[0] is System.Collections.IEnumerable en && !(conv.args[0] is string)){ var list=en.Cast<object?>().ToArray(); if(list.Length>0) return list[rnd.Next(list.Length)]?.ToString()??""; } }catch{ if(ctx.RaiseErrors) throw; } if(ctx.RaiseErrors){
                        // For single non-list like "a", py conversion will have thrown if raiseErrors, so propagate
                        // Check if single was not list and not int, try py conversion for validation
                        var conv2 = FuncParserHelpers.SafeConvertToTypes( (new object[]{"py"}, new Dictionary<string,object>()), new object?[]{single}, new Dictionary<string,object?>(), true);
                        // if we reach here without throw, then single was valid literal, but we already handled list case, so return random from a
                    }
                } else {
                    // multi-arg: each arg must be valid py literal when raiseErrors
                    var converters = Enumerable.Repeat((object)"py", a.Length).ToArray();
                    var conv = FuncParserHelpers.SafeConvertToTypes( (converters, new Dictionary<string,object>()), a.Cast<object?>().ToArray(), new Dictionary<string,object?>(), ctx.RaiseErrors);
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
                if(a.Length>1){ var singular=a[0]??""; var number=a[1]; if(number==null || string.IsNullOrEmpty(number.ToString())){ if(raise) throw new ParsingError($"pluralize: number '{number}' not an integer"); return singular; } if(!int.TryParse(number.ToString(), out var n2)){ if(raise) throw new ParsingError($"pluralize: number '{number}' not an integer"); return singular; } int nn2=Math.Abs(n2); return nn2==0||nn2==1? singular : (singular+"s"); } return a[0]??""; },
        };

        ActorStanceCallables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal)
        {
            ["you"] = HandleYou,
            ["You"] = HandleYouCap,
            ["your"] = HandleYour,
            ["Your"] = HandleYourCap,
            ["obj"] = HandleYou,
            ["Obj"] = HandleYouCap,
            ["conj"] = HandleConj,
            ["pconj"] = HandlePConj,
            ["pron"] = HandlePron,
            ["Pron"] = HandlePronCap,
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
            var conv = FuncParserHelpers.SafeConvertToTypes( (new object[]{"py"}, new Dictionary<string,object>()), new object?[]{s}, new Dictionary<string,object?>(), true);
            var v = conv.args[0];
            if(v is int i) return i.ToString();
            if(v is double d) return d.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if(v is string str) return str;
            if(v is System.Collections.IList list) return "["+string.Join(",", list.Cast<object?>())+"]";
            return v?.ToString()??"";
        }catch{ }
        if(int.TryParse(s, out var i2)) return i2.ToString();
        if(double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d2)) return d2.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(s.StartsWith("[")&&s.EndsWith("]")) return s;
        try{ return FuncParserHelpers.SafeArithEval(s).ToString(System.Globalization.CultureInfo.InvariantCulture); }catch{ return s; }
    }

    private static object? HandleYou(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        GameObject? caller = ctx.Caller;
        if (args.Length>0 && ctx.Mapping != null && ctx.Mapping.TryGetValue(args[0], out var mapped) && mapped is GameObject go) caller = go;
        if (caller==null || ctx.Receiver==null)
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
    private static object? HandleYouCap(string[] a, Dictionary<string,string> k, ParserContext ctx, ParsedFunc raw)
    {
        var res = HandleYou(a, new Dictionary<string,string>(k){{"capitalize","true"}}, ctx, raw);
        return res;
    }
    private static object? HandleYour(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        GameObject? caller = ctx.Caller;
        if (args.Length>0 && ctx.Mapping != null && ctx.Mapping.TryGetValue(args[0], out var mapped) && mapped is GameObject go) caller = go;
        if (caller==null || ctx.Receiver==null)
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
    private static object? HandleYourCap(string[] a, Dictionary<string,string> k, ParserContext ctx, ParsedFunc raw) => HandleYour(a, k, ctx, new ParsedFunc{ FuncName="Your", FullStr=raw.FullStr, InFuncStr=raw.InFuncStr });

    private static object? HandleConj(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        if(ctx.Caller==null || ctx.Receiver==null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller/receiver supplied to $conj callable");
            return raw.ToString();
        }
        var verb = args[0]??"";
        string? key = args.Length>1? args[1]: null;
        GameObject? obj = ctx.Caller;
        if (key!=null && ctx.Mapping!=null && ctx.Mapping.TryGetValue(key, out var m) && m is GameObject go2) obj = go2;
        var (second, third) = Conjugate.VerbActorStanceComponents(verb, plural:false);
        return obj == ctx.Receiver ? second : third;
    }
    private static object? HandlePConj(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        if(ctx.Caller==null || ctx.Receiver==null)
        {
            if(ctx.RaiseErrors) throw new ParsingError("No caller/receiver supplied to $conj callable");
            return raw.ToString();
        }
        var verb = args[0]??"";
        string? key = args.Length>1? args[1]: null;
        GameObject? obj = ctx.Caller;
        if (key!=null && ctx.Mapping!=null && ctx.Mapping.TryGetValue(key, out var m) && m is GameObject go2) obj = go2;
        bool plural=false;
        if(obj!=null)
        {
            var g = obj.Gender;
            if(!string.IsNullOrEmpty(g)) plural = g.Equals("plural", StringComparison.OrdinalIgnoreCase);
            else { try{ foreach(var prop in obj.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)){ if(prop.Name!="Gender") continue; try{ var gv=prop.GetValue(obj); if(gv is Delegate dg){ var r=dg.DynamicInvoke(); if(r is string rs) { plural=rs=="plural"; if(plural) break; } } else if(gv is string gs) { plural=gs=="plural"; if(plural) break; } }catch{} } }catch{} }
        }
        var (second, third) = Conjugate.VerbActorStanceComponents(verb, plural:plural);
        return obj == ctx.Receiver ? second : third;
    }
    private static object? HandlePron(string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw)
    {
        if(args.Length==0) return "";
        var pronoun = args[0]??"";
        var options = new List<string>();
        for(int i=1;i<args.Length;i++) options.Add(args[i]??"");
        GameObject? obj = ctx.Caller;
        if(options.Count>0 && ctx.Mapping!=null && ctx.Mapping.ContainsKey(options[^1]))
        {
            var last = options[^1];
            if(ctx.Mapping[last] is GameObject go) obj = go;
            options.RemoveAt(options.Count-1);
        }
        object? optObj = null;
        if(options.Count==1) optObj = options[0];
        else if(options.Count>1) optObj = options;
        string? defaultGender = "neutral";
        if(obj!=null){
            try{
                var g = obj.Gender;
                if(!string.IsNullOrEmpty(g)) defaultGender = g;
                else {
                    // Look for any Gender property that returns delegate (callable gender) – handles mock hiding base
                    foreach(var pi in obj.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)){
                        if(pi.Name!="Gender") continue;
                        try{
                            var gv = pi.GetValue(obj);
                            if(gv is Delegate d){ var r=d.DynamicInvoke(); if(r is string rs && !string.IsNullOrEmpty(rs)){ defaultGender=rs; break; } }
                            else if(gv is string s && !string.IsNullOrEmpty(s)){ defaultGender=s; break; }
                        }catch{}
                    }
                }
            }catch{}
        }
        string defaultViewpoint = "2nd person";
        if(kwargs.TryGetValue("viewpoint", out var vp)) defaultViewpoint = vp;
        var (firstSecond, third) = Pronouns.PronounToViewpoints(pronoun, optObj, null, defaultGender, defaultViewpoint);
        bool cap = false;
        if(raw.FuncName=="Pron") cap=true;
        if(cap){ firstSecond = Capitalize(firstSecond); third=Capitalize(third); }
        return obj == ctx.Receiver ? firstSecond : third;
    }
    private static object? HandlePronCap(string[] a, Dictionary<string,string> k, ParserContext ctx, ParsedFunc raw)
    {
        var res = HandlePron(a, k, ctx, new ParsedFunc{ FuncName="Pron", FullStr=raw.FullStr, InFuncStr=raw.InFuncStr, Args=raw.Args, Kwargs=raw.Kwargs });
        return res;
    }
    private static string Capitalize(string s) => string.IsNullOrEmpty(s)?s: char.ToUpperInvariant(s[0]) + (s.Length>1? s[1..]: "");

    // Static aliases for python naming
    public static IReadOnlyDictionary<string, ParserCallable> FUNCPARSER_CALLABLES => FuncParserCallables;
    public static IReadOnlyDictionary<string, ParserCallable> ACTOR_STANCE_CALLABLES => ActorStanceCallables;
    public static IReadOnlyDictionary<string, ParserCallable> FuncParserCallablesMap => FuncParserCallables;
    public static IReadOnlyDictionary<string, ParserCallable> ActorStanceCallablesMap => ActorStanceCallables;

    // Instance constructors
    public FuncParser(IReadOnlyDictionary<string, ParserCallable> callables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
    {
        _callables = new Dictionary<string, ParserCallable>(callables, StringComparer.Ordinal);
        _genericCallables = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        _hasGeneric = false;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs != null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
        ValidateCallables(_callables);
    }


    public FuncParser(IDictionary<string, Delegate> genericCallables, char startChar = StartChar, char escapeChar = EscapeChar, int maxNesting = MaxNesting, IDictionary<string, object?>? defaultKwargs = null)
    {
        _callables = new Dictionary<string, ParserCallable>(StringComparer.Ordinal);
        _genericCallables = new Dictionary<string, Delegate>(genericCallables, StringComparer.Ordinal);
        _hasGeneric = true;
        _startChar = startChar;
        _escapeChar = escapeChar;
        _maxNesting = maxNesting;
        _defaultKwargs = defaultKwargs != null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
        // Build wrapper for each generic that validates signature
        foreach(var kv in genericCallables){
            var del = kv.Value;
            // wrap to ParserCallable that forwards via DynamicInvoke
            ParserCallable wrapper = (a,k,ctx,raw) => {
                // Build merged kwargs for forwarding: need to include caller/receiver/mapping etc.
                // For generic callables tests, they expect to receive *args as string[] and **kwargs merged
                // We'll call delegate via reflection with args array and kwargs dict
                try{
                    var method = del.Method;
                    var pars = method.GetParameters();
                    // Try to invoke with (string[] args, Dictionary<string,object?> kwargs) or similar
                    // Fallback: try multiple signatures
                    if(pars.Length==2 && pars[0].ParameterType.IsArray && pars[1].ParameterType.IsGenericType){
                        var kwargsObj = new Dictionary<string, object?>(StringComparer.Ordinal);
                        foreach(var kk in k) kwargsObj[kk.Key]=kk.Value;
                        // inject reserved
                        kwargsObj["funcparser"] = this;
                        kwargsObj["raise_errors"] = ctx.RaiseErrors;
                        if(ctx.Caller!=null) kwargsObj["caller"]=ctx.Caller;
                        if(ctx.Receiver!=null) kwargsObj["receiver"]=ctx.Receiver;
                        if(ctx.Mapping!=null) kwargsObj["mapping"]=ctx.Mapping;
                        return del.DynamicInvoke(new object?[]{ a, kwargsObj });
                    }
                    if(pars.Length==1 && pars[0].ParameterType.IsArray){
                        return del.DynamicInvoke(new object?[]{ a });
                    }
                    if(pars.Length==0){
                        return del.DynamicInvoke();
                    }
                    // generic *args, **kwargs as params object[] ?
                    return del.DynamicInvoke(a.Cast<object?>().ToArray());
                }catch(System.Reflection.TargetInvocationException tie){ throw tie.InnerException ?? tie; }
            };
            _callables[kv.Key]=wrapper;
        }
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
        _defaultKwargs = defaultKwargs != null ? new Dictionary<string, object?>(defaultKwargs, StringComparer.Ordinal) : new Dictionary<string, object?>(StringComparer.Ordinal);
        var genDict = new Dictionary<string, Delegate>(StringComparer.Ordinal);
        foreach(var kv in mixedCallables){
            if(kv.Value is ParserCallable pc) _callables[kv.Key]=pc;
            else if(kv.Value is Delegate d){ genDict[kv.Key]=d; _hasGeneric=true; _genericCallables[kv.Key]=d;
                ParserCallable wrapper = (a,k,ctx,raw) => {
                    try{
                        var method=d.Method;
                        var pars=method.GetParameters();
                        if(pars.Length>=2 ){
                            var kwargsObj = new Dictionary<string, object?>(StringComparer.Ordinal);
                            foreach(var kk in k) kwargsObj[kk.Key]=kk.Value;
                            kwargsObj["funcparser"]=this;
                            kwargsObj["raise_errors"]=ctx.RaiseErrors;
                            if(ctx.Caller!=null) kwargsObj["caller"]=ctx.Caller;
                            if(ctx.Receiver!=null) kwargsObj["receiver"]=ctx.Receiver;
                            if(ctx.Mapping!=null) kwargsObj["mapping"]=ctx.Mapping;
                            // try to match signature that expects string[] + Dictionary
                            return d.DynamicInvoke(new object?[]{ a, kwargsObj });
                        }
                        return d.DynamicInvoke(new object?[]{ a });
                    }catch(System.Reflection.TargetInvocationException tie){ throw tie.InnerException ?? tie; }
                };
                _callables[kv.Key]=wrapper;
            }
        }
        if(_hasGeneric) ValidateGenericCallables(genDict);
        else ValidateCallables(_callables);
    }

    // Convenience for empty dict
    public FuncParser() : this(new Dictionary<string, ParserCallable>(StringComparer.Ordinal)) {}

    public IReadOnlyDictionary<string, ParserCallable> Callables => _callables;
    public char StartCharProp => _startChar;
    public char EscapeCharProp => _escapeChar;
    public int MaxNestingProp => _maxNesting;
    public IReadOnlyDictionary<string, object?> DefaultKwargs => _defaultKwargs;
    // snake_case aliases for python tests
    public char start_char => _startChar;
    public char escape_char => _escapeChar;
    public int max_nesting => _maxNesting;
    public IReadOnlyDictionary<string, object?> default_kwargs => _defaultKwargs;
    public IReadOnlyDictionary<string, ParserCallable> callables => _callables;

    public void ValidateCallables(IDictionary<string, ParserCallable> callables)
    {
        foreach(var kv in callables){
            var del = kv.Value;
            var method = del.Method;
            var pars = method.GetParameters();
            // For ParserCallable type, signature is (string[] args, Dictionary<string,string> kwargs, ParserContext ctx, ParsedFunc raw) -> has array and dict, so passes
            bool hasVarArgs = pars.Any(p=> p.ParameterType.IsArray);
            bool hasVarKw = pars.Any(p=> p.ParameterType.IsGenericType && (p.ParameterType.GetGenericTypeDefinition()==typeof(Dictionary<,>) || p.ParameterType.GetGenericTypeDefinition()==typeof(IDictionary<,>)));
            // ParserCallable always has both, so no error
            // Only check if missing would raise; but ParserCallable won't missing
            if(!hasVarArgs) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support *args.");
            if(!hasVarKw) throw new ParsingError($"Parse-func callable '{kv.Key}' does not support **kwargs.");
        }
    }
    public void ValidateGenericCallables(IDictionary<string, Delegate> callables)
    {
        foreach(var kv in callables){
            var del = kv.Value;
            System.Reflection.MethodInfo method;
            try{ method = del.Method; }catch(Exception ex){ Console.Error.WriteLine($"Could not run getfullargspec on {kv.Key}: {ex}"); continue; }
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
    public static void ValidateCallablesStatic(IDictionary<string, ParserCallable> callables)
    {
        foreach(var kv in callables){
            var del = kv.Value;
            var pars = del.Method.GetParameters();
            bool hasVarArgs = pars.Any(p=> p.ParameterType.IsArray);
            bool hasVarKw = pars.Any(p=> p.ParameterType.IsGenericType);
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
        if(reservedKwargs!=null) foreach(var kv in reservedKwargs) merged[kv.Key]=kv.Value;
        merged["funcparser"]=this;
        merged["raise_errors"]=raiseErrors;
        // Extract caller/receiver/mapping for ctx
        var ctx = new ParserContext{ RaiseErrors=raiseErrors };
        if(merged.TryGetValue("caller", out var co) && co is GameObject gco) ctx.Caller=gco;
        if(merged.TryGetValue("receiver", out var ro) && ro is GameObject gro) ctx.Receiver=gro;
        if(merged.TryGetValue("mapping", out var mo) && mo is IDictionary<string, object?> md) ctx.Mapping=md;
        // Also try reserved directly
        if(reservedKwargs!=null){
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

    // Static Execute for legacy static parse (uses ActorStanceCallables)
    private static object? ExecuteStatic(ParsedFunc pf, bool raiseErrors, ParserContext ctx)
    {
        var funcname = pf.FuncName;
        if (!ActorStanceCallables.TryGetValue(funcname, out var func))
        {
            if(raiseErrors) throw new ParsingError($"Unknown parsed function '{pf}' (available: {string.Join(", ", ActorStanceCallables.Keys.Select(k=>"'"+k+"'"))})");
            return pf.ToString();
        }
        var argsStr = pf.Args.Select(o=> o?.ToString() ?? "").ToArray();
        var kwargsStr = pf.Kwargs.ToDictionary(kv=>kv.Key, kv=> kv.Value?.ToString() ?? "", StringComparer.Ordinal);
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
            if(raiseErrors) throw;
            return pf.ToString();
        }
    }

    // Instance Parse that returns object? (string or raw)
    public object? Parse(string? text, bool raiseErrors = false, bool escape = false, bool strip = false, bool returnStr = true, IDictionary<string, object?>? reservedKwargs = null)
    {
        if (text == null) return "";
        if (text.Length > MaxMessageSize) throw new ParsingError($"Input too long ({text.Length} chars)");
        if (string.IsNullOrEmpty(text)) return text;
        // need to handle reservedKwargs that may contain caller/receiver/mapping for actor stance later? But instance parse's callables are generic; for actor stance we need to handle via reserved.
        // Use internal parser with instance fields
        return ParseInternal(text, raiseErrors, escape, strip, returnStr, reservedKwargs, _callables, _startChar, _escapeChar, _maxNesting, _defaultKwargs);
    }
    // Overload with actor/receiver/mapping convenience
    public object? Parse(string? text, GameObject? caller, GameObject? receiver, IDictionary<string, object?>? mapping, bool raiseErrors = false, bool escape = false, bool strip = false, bool returnStr = true)
    {
        var reserved = new Dictionary<string, object?>(StringComparer.Ordinal);
        if(caller!=null) reserved["caller"]=caller;
        if(receiver!=null) reserved["receiver"]=receiver;
        if(mapping!=null) reserved["mapping"]=mapping;
        var res = Parse(text, raiseErrors, escape, strip, returnStr, reserved);
        return res;
    }
    public object? ParseToAny(string? text, bool raiseErrors = false, bool escape = false, bool strip = false, IDictionary<string, object?>? reservedKwargs = null)
        => Parse(text, raiseErrors, escape, strip, false, reservedKwargs);

    // Legacy static Parse used by GameObject
    public static string Parse(string? text, GameObject? actor, GameObject? receiver, IDictionary<string, object?>? mapping, bool raiseErrors = false, bool escape = false, bool strip = false)
    {
        if (text == null) return "";
        if (text.Length > MaxMessageSize) throw new ParsingError($"Input too long ({text.Length} chars)");
        if (string.IsNullOrEmpty(text)) return text;
        bool hasFunc = text.Contains(StartChar);
        bool hasDirector = mapping!=null && text.Contains('{') && text.Contains('}');
        if (!hasFunc && !hasDirector) return text;
        string afterFunc = text;
        if(hasFunc){
            // use static actor callables for legacy
            var reserved = new Dictionary<string, object?>(StringComparer.Ordinal);
            if(actor!=null) reserved["caller"]=actor;
            if(receiver!=null) reserved["receiver"]=receiver;
            if(mapping!=null) reserved["mapping"]=mapping;
            var obj = ParseInternalStaticLegacy(text, raiseErrors, escape, strip, true, reserved);
            afterFunc = obj?.ToString() ?? "";
        }
        if (hasDirector && mapping != null && receiver != null)
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
        else if (hasDirector && mapping != null)
        {
            var displayMap = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in mapping) displayMap[kv.Key] = kv.Value is GameObject go ? go.Name : kv.Value?.ToString() ?? "";
            var safe = new FuncParserHelpers.SafeFormatMap(displayMap);
            afterFunc = safe.Format(afterFunc);
        }
        return afterFunc;
    }
    public static Dictionary<GameObject, string> ParseForContents(string? text, GameObject? actor, IEnumerable<GameObject> receivers, IDictionary<string, object?>? mapping, bool raiseErrors = false)
    {
        var dict = new Dictionary<GameObject, string>();
        foreach (var r in receivers)
            dict[r] = Parse(text, actor, r, mapping, raiseErrors);
        return dict;
    }
    public static string Parse(string? text, IDictionary<string, object?>? mapping, bool raiseErrors = false)
        => Parse(text, null, null, mapping, raiseErrors);

    // Shared internal parser (instance-like)
    private static object? ParseInternal(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs, IReadOnlyDictionary<string, ParserCallable> callables, char startChar, char escapeChar, int maxNesting, IReadOnlyDictionary<string, object?> defaultKwargs)
    {
        var callstack = new List<ParsedFunc>();
        int quoted = -1;
        string quotedChar = "";
        int doubleQuoted = -1;
        int openLParens = 0;
        int openLSquare = 0;
        int openLCurly = 0;
        bool escaped = false;
        string currentKwarg = "";
        object? execReturn = "";
        ParsedFunc? currFunc = null;
        var fullstr = new List<char>();
        var infuncstr = new List<char>();
        bool literalInFuncStr = false;
        bool localReturnStr = returnStr;
        var ctxForStatic = new ParserContext{ RaiseErrors=raiseErrors };
        if(reservedKwargs!=null){
            if(reservedKwargs.TryGetValue("caller", out var co) && co is GameObject gco) ctxForStatic.Caller=gco;
            if(reservedKwargs.TryGetValue("receiver", out var ro) && ro is GameObject gro) ctxForStatic.Receiver=gro;
            if(reservedKwargs.TryGetValue("mapping", out var mo) && mo is IDictionary<string, object?> md) ctxForStatic.Mapping=md;
        }
        // helper to execute via instance dict
        object? ExecuteWithCallables(ParsedFunc pf, bool re, ParserContext ctx){
            if(!callables.TryGetValue(pf.FuncName, out var func)){
                if(re) throw new ParsingError($"Unknown parsed function '{pf}' (available: {string.Join(", ", callables.Keys.Select(k=>"'"+k+"'"))})");
                return pf.ToString();
            }
            var argsStr = pf.Args.Select(o=> o?.ToString() ?? "").ToArray();
            var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach(var kv in defaultKwargs) merged[kv.Key]=kv.Value;
            foreach(var kv in pf.Kwargs) merged[kv.Key]=kv.Value;
            if(reservedKwargs!=null) foreach(var kv in reservedKwargs) merged[kv.Key]=kv.Value;
            merged["funcparser"] = null; // placeholder, will set to instance? For static we pass null or a dummy
            merged["raise_errors"] = re;
            // For instance we could pass actual parser, but for internal static we pass null; callables that check funcparser will see null
            // Build ParserContext from merged
            var c = new ParserContext{ RaiseErrors=re };
            if(merged.TryGetValue("caller", out var co2) && co2 is GameObject gco2) c.Caller=gco2;
            if(merged.TryGetValue("receiver", out var ro2) && ro2 is GameObject gro2) c.Receiver=gro2;
            if(merged.TryGetValue("mapping", out var mo2) && mo2 is IDictionary<string, object?> md2) c.Mapping=md2;
            // also use ctxForStatic as base
            if(c.Caller==null) c.Caller=ctxForStatic.Caller;
            if(c.Receiver==null) c.Receiver=ctxForStatic.Receiver;
            if(c.Mapping==null) c.Mapping=ctxForStatic.Mapping;
            var kwargsStr = merged.ToDictionary(kv=>kv.Key, kv=> kv.Value?.ToString() ?? "", StringComparer.Ordinal);
            try{
                var ret = func(argsStr, kwargsStr, c, pf);
                return ret;
            }catch(ParsingError){ if(re) throw; return pf.ToString(); }catch(Exception){ if(re) throw; return pf.ToString(); }
        }

        int i=0, n=str.Length;
        while(i<n){
            char ch=str[i];
            if(escaped){ if(currFunc!=null) infuncstr.Add(ch); else fullstr.Add(ch); escaped=false; i++; continue; }
            if(ch==escapeChar){ if(i+1>=n){ if(currFunc!=null) infuncstr.Add(ch); else fullstr.Add(ch); i++; continue; } escaped=true; i++; continue; }
            if(ch==startChar && i+1<n && str[i+1]==startChar){ if(currFunc!=null) infuncstr.Add(startChar); else fullstr.Add(startChar); i+=2; continue; }
            if(ch==startChar && !(currFunc!=null && quoted>=0)){
                if(currFunc!=null){
                    if(callstack.Count >= maxNesting -1){
                        if(raiseErrors) throw new ParsingError($"Only allows for parsing nesting function defs to a max depth of {maxNesting}.");
                        infuncstr.Add(ch); i++; continue;
                    }else{
                        if(execReturn is string es && es!=""){ foreach(var c in es) infuncstr.Add(c); execReturn=""; }
                        else if(execReturn!=null && execReturn.ToString()!="" ){ foreach(var c in execReturn.ToString()!) infuncstr.Add(c); execReturn=""; }
                        currFunc.CurrentKwarg=currentKwarg;
                        currFunc.InFuncStr=new List<char>(infuncstr);
                        currFunc.DoubleQuoted=quoted;
                        currFunc.QuotedChar=quotedChar;
                        currFunc.OpenLParens=openLParens;
                        currFunc.OpenLSquare=openLSquare;
                        currFunc.OpenLCurly=openLCurly;
                        currentKwarg=""; infuncstr=new List<char>(); quoted=-1; quotedChar=""; doubleQuoted=-1; openLParens=0; openLSquare=0; openLCurly=0; execReturn=""; literalInFuncStr=false;
                        callstack.Add(currFunc);
                    }
                }
                currFunc=new ParsedFunc(ch); i++; continue;
            }
            if(currFunc==null){ fullstr.Add(ch); localReturnStr=true; i++; continue; }
            if(execReturn is string ers && ers!="" && ch!=',' && ch!='=' && ch!=')'){ foreach(var c in ers) infuncstr.Add(c); execReturn=""; }
            else if(execReturn!=null && execReturn.ToString()!="" && ch!=',' && ch!='=' && ch!=')'){ foreach(var c in execReturn.ToString()!) infuncstr.Add(c); execReturn=""; }
            if(ch=='"' || ch=='\''){
                if(quoted>=0){
                    if(ch.ToString()==quotedChar){
                        if(quoted==0){ if(infuncstr.Count>0) infuncstr.RemoveAt(0); quoted=-1; quotedChar=""; doubleQuoted=-1; }
                        else if(quoted>0){ var s=new string(infuncstr.ToArray()); var prefix=s.Substring(0, quoted); s=prefix+s.Substring(quoted+1); infuncstr=new List<char>(s.ToCharArray()); quoted=-1; quotedChar=""; doubleQuoted=-1; }
                        else{ quoted=-1; quotedChar=""; doubleQuoted=-1; }
                    }else infuncstr.Add(ch);
                }else{ infuncstr.Add(ch); quoted=infuncstr.Count-1; quotedChar=ch.ToString(); doubleQuoted=quoted; literalInFuncStr=true; }
                i++; continue;
            }
            if(quoted>=0){ infuncstr.Add(ch); i++; continue; }
            if(ch=='('){
                if(string.IsNullOrEmpty(currFunc.FuncName)){ currFunc.FuncName=new string(infuncstr.ToArray()); currFunc.FullStr.AddRange(infuncstr); currFunc.FullStr.Add(ch); infuncstr=new List<char>(); } else infuncstr.Add(ch);
                openLParens++; i++; continue;
            }
            if(ch=='[' || ch==']'){ infuncstr.Add(ch); openLSquare+= ch==']'? -1:1; i++; continue; }
            if(ch=='{' || ch=='}'){ infuncstr.Add(ch); openLCurly+= ch=='}'? -1:1; i++; continue; }
            if(ch=='='){
                if(execReturn is string er2 && er2!="") infuncstr=new List<char>(er2.ToCharArray());
                else if(execReturn!=null && execReturn.ToString()!="") infuncstr=new List<char>(execReturn.ToString()!.ToCharArray());
                currentKwarg=new string(infuncstr.ToArray()).Trim(); currFunc.Kwargs[currentKwarg]=""; currFunc.FullStr.AddRange(infuncstr); currFunc.FullStr.Add(ch); infuncstr=new List<char>(); i++; continue;
            }
            if(ch==',' || ch==')'){
                if(openLParens>1){ infuncstr.Add(ch); if(ch==')') openLParens--; i++; continue; }
                if(openLCurly>0 || openLSquare>0){ infuncstr.Add(ch); i++; continue; }
                if(execReturn is string er3 && er3!=""){
                    if(!string.IsNullOrEmpty(currentKwarg)) currFunc.Kwargs[currentKwarg]=er3; else currFunc.Args.Add(er3);
                }else if(execReturn!=null && execReturn.ToString()!=""){
                    var sE=execReturn.ToString()!;
                    if(!string.IsNullOrEmpty(currentKwarg)) currFunc.Kwargs[currentKwarg]=sE; else currFunc.Args.Add(execReturn);
                }else{
                    if(!literalInFuncStr){ var s=new string(infuncstr.ToArray()).Trim(); infuncstr=new List<char>(s.ToCharArray()); }
                    if(!string.IsNullOrEmpty(currentKwarg)) currFunc.Kwargs[currentKwarg]=new string(infuncstr.ToArray());
                    else if(literalInFuncStr || new string(infuncstr.ToArray()).Trim().Length>0) currFunc.Args.Add(new string(infuncstr.ToArray()));
                }
                var execStr = execReturn?.ToString() ?? "";
                if(!string.IsNullOrEmpty(execStr)) currFunc.FullStr.AddRange(execStr.ToCharArray());
                currFunc.FullStr.AddRange(infuncstr); currFunc.FullStr.Add(ch);
                currentKwarg=""; execReturn=""; infuncstr=new List<char>(); literalInFuncStr=false;
                if(ch==')'){
                    openLParens=0;
                    if(stripMode) execReturn="";
                    else if(escapeMode) execReturn = escapeChar + new string(currFunc.FullStr.ToArray());
                    else execReturn = ExecuteWithCallables(currFunc, raiseErrors, ctxForStatic);
                    if(callstack.Count>0){
                        currFunc=callstack[callstack.Count-1]; callstack.RemoveAt(callstack.Count-1); currentKwarg=currFunc.CurrentKwarg;
                        if(currFunc.InFuncStr.Count>0){ infuncstr=new List<char>(currFunc.InFuncStr); var es2=execReturn?.ToString() ?? ""; foreach(var c in es2) infuncstr.Add(c); execReturn=""; } else infuncstr=new List<char>();
                        currFunc.InFuncStr=new List<char>(); quoted=currFunc.DoubleQuoted; quotedChar=currFunc.QuotedChar ?? ""; doubleQuoted=quoted; openLParens=currFunc.OpenLParens; openLSquare=currFunc.OpenLSquare; openLCurly=currFunc.OpenLCurly;
                    }else{
                        currFunc=null;
                        if(localReturnStr){
                            var es2=execReturn?.ToString() ?? ""; foreach(var c in es2) fullstr.Add(c); execReturn="";
                        }else{
                            // keep execReturn for returnStr false case; but we already will handle later. If returnStr true, we already added to fullstr.
                            if(returnStr){
                                var es2=execReturn?.ToString() ?? ""; foreach(var c in es2) fullstr.Add(c); execReturn="";
                            }
                        }
                        infuncstr=new List<char>(); literalInFuncStr=false;
                    }
                }
                i++; continue;
            }
            infuncstr.Add(ch); i++;
        }
        if(currFunc!=null){
            callstack.Add(currFunc);
            var combined=new List<char>();
            var stackCopy=new List<ParsedFunc>(callstack); stackCopy.Reverse();
            var trailing=new string(infuncstr.ToArray());
            bool first=true;
            foreach(var pf in stackCopy){
                var funcStr=pf.ToString();
                if(first && funcStr.EndsWith(trailing)){ combined.AddRange(funcStr.ToCharArray()); first=false; trailing=""; }
                else{ combined.AddRange(funcStr.ToCharArray()); if(!string.IsNullOrEmpty(trailing)) combined.AddRange(trailing.ToCharArray()); trailing=""; first=false; }
            }
            if(!string.IsNullOrEmpty(trailing)) combined.AddRange(trailing.ToCharArray());
            fullstr.AddRange(combined);
            if(execReturn!=null && execReturn.ToString()!="") fullstr.AddRange(execReturn.ToString()!.ToCharArray());
        }else{
            if(infuncstr.Count>0) fullstr.AddRange(infuncstr);
            if(execReturn!=null && execReturn.ToString()!="" && execReturn.ToString()!=""){
                if(localReturnStr) fullstr.AddRange(execReturn.ToString()!.ToCharArray());
            }
        }
        if(!localReturnStr && execReturn!=null && execReturn.ToString()!=""){
            // pure call: return raw execReturn if any (when returnStr false and no surrounding text)
            // execReturn may have been already added to fullstr when localReturnStr true; but here localReturnStr false => fullstr empty, execReturn holds raw
            // In our earlier branch for top-level ')', we kept execReturn when returnStr false. So check.
            if(execReturn!=null && execReturn.ToString()!="" && fullstr.Count==0) return execReturn;
            // If we already added to fullstr but returnStr false, we should return raw instead of string?
            // For purity, if fullstr empty but execReturn holds, return raw; otherwise fall through to string
        }
        // Handle case where top-level execReturn kept for returnStr false but fullstr empty
        if(!returnStr && execReturn!=null && execReturn.ToString()!="" && fullstr.Count==0){
            return execReturn;
        }
        // Also handle case where execReturn holds but fullstr has content due to early addition; need to reconstruct
        // If returnStr false but we have mixed content, fallback to string
        if(!returnStr && fullstr.Count==0 && execReturn!=null && execReturn.ToString()!="" )
            return execReturn;
        fullstr.AddRange(infuncstr.Where(c=> false)); // no-op
        return new string(fullstr.ToArray());
    }

    private static object? ParseInternalStaticLegacy(string str, bool raiseErrors, bool escapeMode, bool stripMode, bool returnStr, IDictionary<string, object?>? reservedKwargs)
    {
        // delegate to ParseInternal using static ActorStanceCallables
        return ParseInternal(str, raiseErrors, escapeMode, stripMode, returnStr, reservedKwargs, ActorStanceCallables, StartChar, EscapeChar, MaxNesting, new Dictionary<string, object?>(StringComparer.Ordinal));
    }

    // Expose for instance callables inspection
    internal static IReadOnlyDictionary<string, ParserCallable> ActorStanceCallablesMapStatic => ActorStanceCallables;
}
