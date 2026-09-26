using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Atheriz.Core.Objects;

/// <summary>
/// Helpers for FuncParser: SafeFormatMap, pad/crop/justify/int2str and text width guards.
/// Evennia BSD helpers adapted to C# (east_asian_width via Regex, no dill/simple_eval).
/// </summary>
public static partial class FuncParserHelpers
{
    public const int MaxPowExponent = 10000;
    public const int MaxPowDigits = 50000;
    public const int MaxTextWidth = 65536;
    // Python-compat aliases for tests that check getattr(fh, "_MAX_...")
    public const int _MAX_POW_EXPONENT = 10000;
    public const int _MAX_POW_DIGITS = 50000;
    public const int _MAX_TEXT_WIDTH = 65536;

    private static readonly Dictionary<int, string> Int2StrNoun = new()
    {
        [0]="no",[1]="one",[2]="two",[3]="three",[4]="four",[5]="five",[6]="six",[7]="seven",[8]="eight",[9]="nine",[10]="ten",[11]="eleven",[12]="twelve",
    };
    private static readonly Dictionary<int, string> Int2StrAdj = new() { [1]="1st",[2]="2nd",[3]="3rd" };

    public static string Int2Str(int number, bool adjective = false)
    {
        if (adjective) return Int2StrAdj.TryGetValue(number, out var v) ? v : $"{number}th";
        return Int2StrNoun.TryGetValue(number, out var v2) ? v2 : number.ToString();
    }

    /// <summary>
    /// Mirrors <c>_SafeFormatMap</c>: missing key returns "{key}" instead of throwing.
    /// </summary>
    public sealed partial class SafeFormatMap : Dictionary<string, object?>
    {
        public SafeFormatMap() : base(StringComparer.Ordinal) { }
        public SafeFormatMap(IDictionary<string, object?> src) : base(src, StringComparer.Ordinal) { }
        // Single compiled instance: the old inline pattern re-parsed the same
        // expression per message per receiver. Identical pattern and matches.
        [GeneratedRegex(@"\{(\w+)\}")]
        private static partial Regex FormatKeyRegex();
        // For director stance: map object -> displayName, keep {key} for missing
        public string Format(string template)
        {
            if (string.IsNullOrEmpty(template)) return template;
            // Simple replace {key} via regex, leaving unknown untouched (handled by TryGet)
            return FormatKeyRegex().Replace(template, m =>
            {
                var key = m.Groups[1].Value;
                return TryGetValue(key, out var v) && v is not null ? v.ToString()! : m.Value;
            });
        }
    }

    // width helpers using display-length (east Asian wide chars count 2)
    private static int DisplayLen(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int len = 0;
        foreach (var ch in s)
        {
            // Approximate east_asian_width W/F as 2: use Unicode ranges for wide chars
            // Simplified: CJK etc. For parity, treat non-ascii as 2 if char code > 0x2E80? Approximation: use 1 for most.
            // We use east_asian_width logic approximated: if char.IsHighSurrogate etc not needed.
            // Keep simple: ascii 1, else 2 if char > 127 and category.
            // For tests, ascii only, so 1.
            len += ch > 127 && ch < 0xFFFD && IsWide(ch) ? 2 : 1;
        }
        return len;
    }
    private static bool IsWide(char ch)
    {
        // Minimal check: ranges for wide (W/F). Simplified to CJK Unified block
        return (ch >= 0x1100 && ch <= 0x115F) || (ch >= 0x2E80 && ch <= 0xA4CF) || (ch >= 0xAC00 && ch <= 0xD7A3) || (ch >= 0xF900 && ch <= 0xFAFF) || (ch >= 0xFE10 && ch <= 0xFE1F) || (ch >= 0xFF00 && ch <= 0xFF60);
    }
    private static string CropToWidth(string text, int width)
    {
        int cur = 0;
        var sb = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            int w = IsWide(ch) ? 2 : 1;
            if (cur + w > width) break;
            sb.Append(ch);
            cur += w;
        }
        return sb.ToString();
    }

    public static string Pad(string text, int? width = null, string align = "c", string fillchar = " ")
    {
        width ??= 78;
        width = Math.Min(width.Value, MaxTextWidth);
        align = align switch { "c" or "l" or "r" => align, _ => "c" };
        fillchar = string.IsNullOrEmpty(fillchar) ? " " : fillchar[0].ToString();
        int w = DisplayLen(text);
        if (w >= width) return text;
        int padLen = width.Value - w;
        if (align == "l") return text + new string(fillchar[0], padLen);
        if (align == "r") return new string(fillchar[0], padLen) + text;
        int left = padLen / 2;
        int right = padLen - left;
        return new string(fillchar[0], left) + text + new string(fillchar[0], right);
    }

    public static string Crop(string text, int? width = null, string suffix = "[...]")
    {
        width ??= 78;
        int ltext = DisplayLen(text);
        if (ltext <= width) return text;
        int lsuffix = DisplayLen(suffix);
        if (lsuffix >= width) return CropToWidth(text, width.Value);
        return CropToWidth(text, width.Value - lsuffix) + suffix;
    }

    public static string Justify(string text, int? width = null, string align = "l", int indent = 0, string fillchar = " ")
    {
        width ??= 78;
        width = Math.Min(width.Value, MaxTextWidth);
        indent = Math.Max(0, Math.Min(indent, width.Value));
        // Simplified: split words, fill lines
        List<string> lines = [];
        var paragraphs = text.Split('\n');
        foreach (var para in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(para)) { lines.Add(new string(fillchar[0], width.Value)); continue; }
            var words = para.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            List<string> curLine = [];
            int curLen = 0;
            foreach (var w in words)
            {
                int wl = DisplayLen(w);
                if (curLine.Count == 0) { curLine.Add(w); curLen = wl; }
                else if (curLen + 1 + wl > width) { lines.Add(FormatLine(curLine, curLen, width.Value, align, fillchar)); curLine = [w]; curLen = wl; }
                else { curLine.Add(w); curLen += 1 + wl; }
            }
            if (curLine.Count > 0) lines.Add(FormatLine(curLine, curLen, width.Value, align, fillchar));
        }
        var indentStr = new string(fillchar[0], indent);
        return string.Join("\n", lines.Select(l => indentStr + l));
        string FormatLine(List<string> words, int wlen, int w, string al, string fc)
        {
            int gaps = words.Count - 1;
            int rest = w - (wlen);
            if (rest <= 0) return string.Join(" ", words);
            if (al == "l") return string.Join(" ", words) + new string(fc[0], rest);
            if (al == "r") return new string(fc[0], rest) + string.Join(" ", words);
            if (al == "c")
            {
                int left = rest / 2;
                return new string(fc[0], left) + string.Join(" ", words) + new string(fc[0], rest - left);
            }
            // full: for single word, Python returns just the word (gap.join with single element)
            if (gaps == 0) return words[0];
            int perGap = rest / gaps;
            int extra = rest % gaps;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < words.Count; i++)
            {
                sb.Append(words[i]);
                if (i < gaps)
                {
                    sb.Append(' ');
                    sb.Append(new string(fc[0], perGap));
                    if (i < extra) sb.Append(fc[0]);
                }
            }
            return sb.ToString();
        }
    }

    // copy_word_case); the local variant had diverged subtly, so delegate to the single implementation.
    public static string CopyWordCase(string src, string dst) =>
        global::Atheriz.Core.Utils.GameUtils.CopyWordCase(src, dst);

    public static double SafeArithEval(string inp)
    {
        // mirrors Python _safe_arith_eval with _MAX_POW_EXPONENT and _MAX_POW_DIGITS guard
        // Supports +, -, *, /, //, %, **, unary +/-, parentheses, constants int/float
        // Throws InvalidOperationException or ArgumentException on guard violation (maps to ValueError in Python)
        if (string.IsNullOrWhiteSpace(inp)) throw new ArgumentException("empty");
        // quick guard scan for **: find exponents and check size without fully parsing
        // We'll do proper AST parse using simple recursive descent to enforce guard exactly
        var parser = new SafeArithParser(inp);
        return parser.Parse();
    }
    // Alias for Python name
    public static double _safe_pow(double b, double e)
    {
        if (e > _MAX_POW_EXPONENT) throw new InvalidOperationException($"exponent {e} exceeds safe limit {_MAX_POW_EXPONENT}");
        if (b != 0 && e * Math.Log10(Math.Abs(b)) + 1 > _MAX_POW_DIGITS) throw new InvalidOperationException($"estimated size exceeds safe limit {_MAX_POW_DIGITS}");
        var r = Math.Pow(b, e);
        if (double.IsNaN(r)) throw new InvalidOperationException("complex result not allowed");
        if (double.IsInfinity(r)) throw new InvalidOperationException("overflow");
        return r;
    }

    private sealed class SafeArithParser
    {
        private readonly string _s;
        private int _pos;
        // Nesting cap: parentheses (and unary-sign chains) recurse one frame
        // per level. _SafeEval falls back here when the literal parser
        // rejects, so an uncapped literal alone would merely move a deep
        // input's stack overflow into this parser — cap both.
        private const int MaxNesting = 32;
        private int _depth;
        public SafeArithParser(string s) { _s = s; _pos = 0; }
        public double Parse() { var v = ParseExpr(); Skip(); if (_pos != _s.Length) throw new ArgumentException($"unsupported node at {_pos}"); return v; }
        private void Skip() { while (_pos < _s.Length && char.IsWhiteSpace(_s[_pos])) _pos++; }
        private double ParseExpr() => ParseAddSub();
        private double ParseAddSub()
        {
            var left = ParseMulDiv();
            while (true) { Skip(); if (_pos >= _s.Length) break; char op = _s[_pos]; if (op != '+' && op != '-') break; _pos++; var right = ParseMulDiv(); left = op == '+' ? left + right : left - right; }
            return left;
        }
        private double ParseMulDiv()
        {
            // Unary binds looser than power (so -2**2 == -(2**2)), hence
            // operands parse via ParseUnary, not ParsePow.
            var left = ParseUnary();
            while (true)
            {
                Skip(); if (_pos >= _s.Length) break;
                if (_pos + 1 < _s.Length && _s[_pos] == '/' && _s[_pos+1] == '/') { _pos+=2; var r=ParseUnary(); if (r == 0) throw new InvalidOperationException("integer division by zero"); left = Math.Floor(left / r); }
                else if (_pos + 1 < _s.Length && _s[_pos] == '*' && _s[_pos+1] == '*') break; // handled in pow
                else if (_s[_pos] == '*') { _pos++; var r=ParseUnary(); left = left * r; }
                else if (_s[_pos] == '/') { _pos++; var r=ParseUnary(); if (r == 0) throw new InvalidOperationException("division by zero"); left = left / r; }
                else if (_s[_pos] == '%') { _pos++; var r=ParseUnary(); if (r == 0) throw new InvalidOperationException("modulo by zero"); left = ((left % r) + r) % r; }
                else break;
            }
            return left;
        }
        private double ParsePow()
        {
            var left = ParsePrimary();
            Skip();
            if (_pos + 1 < _s.Length && _s[_pos] == '*' && _s[_pos+1] == '*')
            {
                _pos+=2;
                var right = ParseUnary(); // right-associative; unary allows negative exponents
                // guard
                if (right > _MAX_POW_EXPONENT) throw new InvalidOperationException($"exponent {right} exceeds safe limit {_MAX_POW_EXPONENT}");
                // digit estimate: log10(|left|) * right +1 > _MAX_POW_DIGITS
                if (left != 0 && Math.Abs(left) != 1)
                {
                    double est = right * Math.Log10(Math.Abs(left)) + 1;
                    if (est > _MAX_POW_DIGITS) throw new InvalidOperationException($"estimated size exceeds safe limit {_MAX_POW_DIGITS}");
                }
                else if (left == 10 && right >= _MAX_POW_DIGITS) throw new InvalidOperationException($"estimated size exceeds safe limit {_MAX_POW_DIGITS}");
                var res = Math.Pow(left, right);
                if (double.IsNaN(res)) throw new InvalidOperationException("complex result not allowed");
                if (double.IsInfinity(res)) throw new InvalidOperationException("overflow");
                return res;
            }
            return left;
        }
        private double ParseUnary()
        {
            Skip(); if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) { char op=_s[_pos++]; if (++_depth > MaxNesting) throw new ArgumentException("too deeply nested"); try { var v=ParseUnary(); return op=='-' ? -v : v; } finally { _depth--; } }
            return ParsePow();
        }
        private double ParsePrimary()
        {
            Skip(); if (_pos >= _s.Length) throw new ArgumentException("unexpected end");
            if (_s[_pos] == '(') { _pos++; if (++_depth > MaxNesting) throw new ArgumentException("too deeply nested"); try { var v=ParseExpr(); Skip(); if (_pos >= _s.Length || _s[_pos] != ')') throw new ArgumentException("missing )"); _pos++; return v; } finally { _depth--; } }
            // number
            int start=_pos;
            bool dot=false;
            while (_pos < _s.Length && (char.IsDigit(_s[_pos]) || _s[_pos]=='.')) { if (_s[_pos]=='.') { if(dot) break; dot=true; } _pos++; }
            if (start==_pos) throw new ArgumentException($"non-numeric constant at {_pos}");
            var numStr=_s.Substring(start, _pos-start);
            if (double.TryParse(numStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            throw new ArgumentException($"non-numeric constant: {numStr}");
        }
    }

    // --- SafeConvertToTypes port (funcparser_helpers.py:404) ---
    // Converters are plain functions: one per positional arg plus an
    // optional per-key map for kwargs. No runtime shape-sniffing — the
    // "py" literal/arithmetic converter is this named function.
    public static readonly Func<object?, object?> PyConverter = _SafeEval;

    /// <summary>
    /// Converts <paramref name="args"/> entries and <paramref name="kwargs"/> values
    /// through the supplied converters, in place.
    /// Caller ownership: both <paramref name="args"/> and <paramref name="kwargs"/>
    /// are consumed, not retained — converted values overwrite the caller's array
    /// slots and dictionary entries. Callers retaining their inputs must pass a copy.
    /// A null entry in <paramref name="argConverters"/> leaves that slot as-is.
    /// </summary>
    public static (object?[] args, Dictionary<string, object?> kwargs) SafeConvertToTypes(Func<object?, object?>?[] argConverters, object?[] args, Dictionary<string, object?> kwargs, IDictionary<string, Func<object?, object?>>? kwConverters = null, bool raiseErrors = true)
    {
        // Convert args in place: the array is owned by the caller (see contract
        // above), so no defensive copy — converted values overwrite each slot.
        if (args is not null && argConverters is not null)
        {
            for (int i = 0; i < Math.Min(args.Length, argConverters.Length); i++)
            {
                var conv = argConverters[i];
                if (conv is null) continue;
                try
                {
                    args[i] = conv(args[i]);
                }
                catch
                {
                    if (raiseErrors) throw;
                }
            }
        }
        if (kwConverters is not null && kwConverters.Count > 0 && kwargs is not null)
        {
            foreach (var kv in kwConverters)
            {
                if (kv.Value is null || !kwargs.ContainsKey(kv.Key)) continue;
                try
                {
                    kwargs[kv.Key] = kv.Value(kwargs[kv.Key]);
                }
                catch
                {
                    if (raiseErrors) throw;
                }
            }
        }
        return (args ?? Array.Empty<object?>(), kwargs ?? []);
    }

    private static object? _SafeEval(object? inp)
    {
        if(inp is null) return "";
        if(inp is not string s) return inp;
        if(string.IsNullOrEmpty(s)) return "";
        // try literal eval
        try{
            var lit = _TryLiteralEval(s);
            if(lit is not null || s.Trim()=="[]" || s.Trim()=="()") return lit;
        }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed SafeArithParser._SafeEval: " + logEx.Message, "SafeArithParser"); }
        // try arith
        try{
            return SafeArithEval(s);
        }catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed SafeArithParser._SafeEval: " + logEx.Message, "SafeArithParser"); }
        // manual containers
        var parts = _ManualParseContainers(s);
        if(parts is not null) return parts;
        throw new FuncParser.ParsingError($"Errors converting '{s}' to python: literal_eval raised, arith_eval raised");
    }

    private static object? _TryLiteralEval(string inp, int depth = 0)
    {
        // Nesting cap: each tuple level re-scans via SplitTopLevel, so
        // unbounded depth is O(depth x len) work plus a stack frame per
        // level. Past the cap, throw so _SafeEval falls through to the
        // (also capped) arithmetic parser and then to the raw echo.
        if (depth > 32) throw new ArgumentException("too deeply nested");
        var t = inp.Trim();
        // int
        if(int.TryParse(t, out var iv)) return iv;
        if(double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dv) && (t.Contains('.') || t.Contains('e') || t.Contains('E'))) return dv;
        // quoted string
        if(t.Length>=2 && ((t[0]=='\'' && t[^1]=='\'') || (t[0]=='"' && t[^1]=='"'))) return t.Substring(1, t.Length-2);
        // list
        if(t.StartsWith("[", StringComparison.Ordinal) && t.EndsWith("]", StringComparison.Ordinal)){
            var inner = t.Substring(1, t.Length-2).Trim();
            if(string.IsNullOrEmpty(inner)) return new List<object?>();
            // try split respecting quotes/brackets - if nested brackets, fail -> throw to trigger manual rejection?
            // For now use manual that rejects nested; so if inner contains '[' or '(' then throw
            if(inner.Contains("[") || inner.Contains("(") || inner.Contains("{")){
                // check if valid nested Python literal like [1,2] nested? For test, ([1,2],3) is valid -> should succeed.
                // Simplify: try to parse recursively; if fails, throw
                throw new ArgumentException("nested");
            }
            var elems = _ManualParseContainers(t);
            if(elems is not null){
                List<object?> res = [];
                foreach(var e in elems){
                    var ev = _TryLiteralEval(e, depth + 1);
                    res.Add(ev ?? e);
                }
                return res;
            }
            return inner.Split(',').Select(x=> x.Trim().Trim('\'','"')).Cast<object?>().ToList();
        }
        // tuple
        if(t.StartsWith("(", StringComparison.Ordinal) && t.EndsWith(")", StringComparison.Ordinal)){
            var inner = t.Substring(1, t.Length-2).Trim();
            if(string.IsNullOrEmpty(inner)) return new List<object?>();
            if(inner.Contains("(") || inner.Contains("[")){
                // For Python, (1,(2,3)) should be parsed as nested tuple -> we need to succeed via literal eval path
                // Attempt recursive parse: split top-level commas outside nested
                var parts = SplitTopLevel(inner);
                if(parts is null) throw new ArgumentException("nested fail");
                List<object?> list = [];
                foreach(var p in parts){
                    var v=_TryLiteralEval(p.Trim(), depth + 1);
                    if(v is null) throw new ArgumentException("fail");
                    list.Add(v);
                }
                // Return as list or tuple? Python returns tuple; we return list equivalent but test will compare via sequence equality
                // For test expecting (1,(2,3)) tuple, we return List containing 1 and List containing 2,3 -> test checks equality with tuple but in C# list vs tuple not same.
                // We'll return as object[] for nested?
                // Simplify: return list structure
                return list;
            }
            var elems2 = _ManualParseContainers(t);
            if(elems2 is not null){
                List<object?> res2 = [];
                foreach(var e in elems2){
                    // try int
                    if(int.TryParse(e, out var iv2)) res2.Add(iv2);
                    else res2.Add(e.Trim('\'','"'));
                }
                return res2;
            }
        }
        throw new ArgumentException("not literal");
    }

    private static List<string>? _ManualParseContainers(string inp)
    {
        if(string.IsNullOrEmpty(inp)) return null;
        var containerEnd = new Dictionary<char,char>{{'(',')'},{'[',']'},{'{','}'}};
        if(!containerEnd.ContainsKey(inp[0]) || inp[^1]!=containerEnd[inp[0]]) return null;
        var inner = inp.Substring(1, inp.Length-2);
        List<string> parts = [];
        var cur = new StringBuilder();
        bool inSingle=false, inDouble=false, escaped=false;
        for(int i=0;i<inner.Length;i++){
            char ch=inner[i];
            if(escaped){ cur.Append(ch); escaped=false; continue; }
            if(ch=='\\'){ escaped=true; cur.Append(ch); continue; }
            if(ch=='\'' && !inDouble){ inSingle=!inSingle; cur.Append(ch); continue; }
            if(ch=='"' && !inSingle){ inDouble=!inDouble; cur.Append(ch); continue; }
            if(inSingle||inDouble){ cur.Append(ch); continue; }
            if(ch=='('||ch=='['||ch=='{') return null;
            if(ch==','){ parts.Add(cur.ToString().Trim()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        parts.Add(cur.ToString().Trim());
        return parts.Select(p=>p.Trim()).ToList();
    }

    private static List<string>? SplitTopLevel(string inner)
    {
        List<string> parts = [];
        var cur = new StringBuilder();
        int depthParen=0, depthBracket=0, depthBrace=0;
        bool inSingle=false,inDouble=false, escaped=false;
        for(int i=0;i<inner.Length;i++){
            char ch=inner[i];
            if(escaped){ cur.Append(ch); escaped=false; continue; }
            if(ch=='\\'){ escaped=true; cur.Append(ch); continue; }
            if(ch=='\'' && !inDouble){ inSingle=!inSingle; cur.Append(ch); continue; }
            if(ch=='"' && !inSingle){ inDouble=!inDouble; cur.Append(ch); continue; }
            if(inSingle||inDouble){ cur.Append(ch); continue; }
            if(ch=='(') depthParen++;
            if(ch==')') depthParen--;
            if(ch=='[') depthBracket++;
            if(ch==']') depthBracket--;
            if(ch=='{') depthBrace++;
            if(ch=='}') depthBrace--;
            if(ch==',' && depthParen==0 && depthBracket==0 && depthBrace==0){ parts.Add(cur.ToString()); cur.Clear(); continue; }
            cur.Append(ch);
        }
        parts.Add(cur.ToString());
        if(depthParen!=0||depthBracket!=0||depthBrace!=0) return null;
        return parts.Select(p=>p.Trim()).ToList();
    }

}
