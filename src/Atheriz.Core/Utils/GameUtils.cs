using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Atheriz.Core.Utils;

/// <summary>
/// Port of <c>atheriz/utils.py</c> pure helpers (no global singleton access).
/// </summary>
public static class GameUtils
{
    private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

    private static readonly Regex TerminalEscapeRegex = new(
        @"\x1b\[[0-9;]*[A-Za-z]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[^[A-Za-z0-9]|\x00",
        RegexOptions.Compiled);

    // Port of utils.py:431 re_empty = re.compile("\n\\s*\n")
    private static readonly Regex ReEmpty = new(@"\n\s*\n", RegexOptions.Compiled);
    private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    // --- ansi ---

    public static string StripAnsi(string input) => AnsiRegex.Replace(input, ""); // Port of utils.py:312

    public static string StripTerminalEscapes(string input) => TerminalEscapeRegex.Replace(input, ""); // Port of utils.py:324

    public static string WrapXterm256(
        string input, int? fg = null, int? bg = null,
        bool bold = false, bool italic = false, bool underline = false,
        bool inverse = false, bool strikethru = false, bool clear = false)
    {
        if (clear) input = StripAnsi(input);
        if (fg is not null) input = $"\x1b[38;5;{fg}m{input}";
        if (bg is not null) input = $"\x1b[48;5;{bg}m{input}";
        if (bold) input = $"\x1b[1m{input}";
        if (italic) input = $"\x1b[3m{input}";
        if (underline) input = $"\x1b[4m{input}";
        if (inverse) input = $"\x1b[7m{input}";
        if (strikethru) input = $"\x1b[9m{input}";
        return $"{input}\x1b[0m";
    }

    public static string WrapRgb(string input, (byte R, byte G, byte B)? fg = null, (byte R, byte G, byte B)? bg = null,
        bool bold = false, bool italic = false, bool underline = false)
    {
        input = bg is not null ? $"\x1b[48;2;{bg.Value.R};{bg.Value.G};{bg.Value.B}m{input}" : $"\x1b[48;2;0;0;0m{input}";
        input = fg is not null ? $"\x1b[38;2;{fg.Value.R};{fg.Value.G};{fg.Value.B}m{input}" : $"\x1b[38;2;204;204;204m{input}";
        if (bold) input = $"\x1b[1m{input}";
        if (italic) input = $"\x1b[3m{input}";
        if (underline) input = $"\x1b[4m{input}";
        return $"{input}\x1b[0m";
    }

    public static string WrapTruecolor(string input, double? fg = null, double? bg = 0.0,
        double fgBright = 100.0, double fgSat = 100.0, double bgBright = 100.0, double bgSat = 100.0,
        bool bold = false, bool italic = false, bool underline = false,
        bool inverse = false, bool strikethru = false, bool clear = false)
    {
        if (clear) input = StripAnsi(input);
        if (bg is not null && bg != 0.0)
        {
            var (r, g, b) = HsvToRgb(bg.Value / 360.0, bgSat / 100.0, bgBright / 100.0);
            input = $"\x1b[48;2;{r};{g};{b}m{input}";
        }
        else input = $"\x1b[48;2;0;0;0m{input}";

        if (fg is not null && fg != 0.0)
        {
            var (r, g, b) = HsvToRgb(fg.Value / 360.0, fgSat / 100.0, fgBright / 100.0);
            input = $"\x1b[38;2;{r};{g};{b}m{input}";
        }
        else
        {
            var (r, g, b) = HsvToRgb(1.0, 0.0, 1.0);
            input = $"\x1b[38;2;{r};{g};{b}m{input}";
        }
        if (bold) input = $"\x1b[1m{input}";
        if (italic) input = $"\x1b[3m{input}";
        if (underline) input = $"\x1b[4m{input}";
        if (inverse) input = $"\x1b[7m{input}";
        if (strikethru) input = $"\x1b[9m{input}";
        return $"{input}\x1b[0m";
    }

    private static (int R, int G, int B) HsvToRgb(double h, double s, double v)
    {
        if (s == 0) { var c = (int)Math.Round(v * 255); return (c, c, c); }
        h = h * 6.0;
        var i = (int)Math.Floor(h);
        var f = h - i;
        var p = v * (1 - s);
        var q = v * (1 - s * f);
        var t = v * (1 - s * (1 - f));
        double r, g, b;
        switch (i % 6)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    // --- game helpers ---

    public static int DiceRoll(int rolls, int faces)
    {
        var result = 0;
        for (var i = 0; i < rolls; i++) result += Random.Shared.Next(1, faces + 1);
        return result;
    }

    public static double DiceRollAverage(int rolls, int faces) => rolls * ((faces + 1) / 2.0);

    public static T Clamp<T>(T min, T value, T max) where T : IComparable<T>
    {
        // Port of atheriz/utils.py:340 return max(min(maximum, value), minimum) — faithful when min>max
        var tmp = value.CompareTo(max) <= 0 ? value : max;
        return tmp.CompareTo(min) >= 0 ? tmp : min;
    }

    /// <summary>
    /// Mirrors <c>atheriz/utils.py:get_dir</c>. Takes generic tuples:
    /// (area,x,y,z) or (x,y) etc. Returns "" if areas differ or coords malformed.
    /// Wontfix: mixed Coord/tuple with different arities is caller error — returns "".
    /// </summary>
    public static string GetDir(IReadOnlyList<object?> origin, IReadOnlyList<object?> dest)
    {
        try
        {
            if (origin.Count != dest.Count) return "";
            if (origin.Count >= 4 && origin[0] is string oa && dest[0] is string da && oa != da)
                return "";
            int oX, oY, dX, dY;
            if (origin[0] is string)
            {
                oX = Convert.ToInt32(origin[1]); oY = Convert.ToInt32(origin[2]);
                dX = Convert.ToInt32(dest[1]); dY = Convert.ToInt32(dest[2]);
            }
            else
            {
                oX = Convert.ToInt32(origin[0]); oY = Convert.ToInt32(origin[1]);
                dX = Convert.ToInt32(dest[0]); dY = Convert.ToInt32(dest[1]);
            }
            var ew = dX - oX; var ns = dY - oY;
            var dir = "";
            if (ns > 0) dir = "north"; else if (ns < 0) dir = "south";
            if (ew > 0) dir += "east"; else if (ew < 0) dir += "west";
            return dir;
        }
        catch { return ""; }
    }

    public static string GetDir(Coord origin, Coord dest)
    {
        if (origin.Area != dest.Area) return "";
        var ns = dest.Y - origin.Y; var ew = dest.X - origin.X;
        var dir = "";
        if (ns > 0) dir = "north"; else if (ns < 0) dir = "south";
        if (ew > 0) dir += "east"; else if (ew < 0) dir += "west";
        return dir;
    }

    public static double Dist3d(Coord origin, Coord dest)
        => Math.Sqrt(Math.Pow(origin.X - dest.X, 2) + Math.Pow(origin.Y - dest.Y, 2) + Math.Pow(origin.Z - dest.Z, 2));

    public static double Dist3d((int X, int Y, int Z) a, (int X, int Y, int Z) b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));

    public static double Dist3d(IReadOnlyList<object?> origin, IReadOnlyList<object?> dest)
    {
        try
        {
            if (origin.Count == 3 && dest.Count == 3)
                return Math.Sqrt(Math.Pow(Convert.ToDouble(origin[0]) - Convert.ToDouble(dest[0]),2) + Math.Pow(Convert.ToDouble(origin[1]) - Convert.ToDouble(dest[1]),2) + Math.Pow(Convert.ToDouble(origin[2]) - Convert.ToDouble(dest[2]),2));
            // area,x,y,z,... use indices 1,2,3
            return Math.Sqrt(Math.Pow(Convert.ToDouble(origin[1]) - Convert.ToDouble(dest[1]),2) + Math.Pow(Convert.ToDouble(origin[2]) - Convert.ToDouble(dest[2]),2) + Math.Pow(Convert.ToDouble(origin[3]) - Convert.ToDouble(dest[3]),2));
        } catch { return 0; }
    }
    public static double Dist3d(Coord origin, IReadOnlyList<object?> dest)
    {
        try{
            var o = new object[]{origin.Area, origin.X, origin.Y, origin.Z};
            return Dist3d(o, dest);
        }catch{ return 0; }
    }
    public static double Dist3d(IReadOnlyList<object?> origin, Coord dest) => Dist3d(dest, origin);

    public static string WordReplace(string input, double replaceFreq, string replacement = "...")
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            if (Random.Shared.NextDouble() < replaceFreq) words[i] = replacement;
        return string.Join(" ", words);
    }

    public const int MaxSphereRadius = 100;

    public static List<(int X, int Y, int Z)> GetPointsInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = false)
    {
        if (radius < 0 || radius > MaxSphereRadius) throw new ArgumentOutOfRangeException(nameof(radius), $"radius {radius} out of bounds [0, {MaxSphereRadius}]");
        var (cx, cy, cz) = center;
        var points = new List<(int, int, int)>();
        var r2 = radius * radius;
        var r = (int)radius;
        for (var x = cx - r; x <= cx + r; x++)
            for (var y = cy - r; y <= cy + r; y++)
                for (var z = cz - r; z <= cz + r; z++)
                {
                    if (ignoreCenter && x == cx && y == cy && z == cz) continue;
                    var distSq = (x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz);
                    if (distSq <= r2) points.Add((x, y, z));
                }
        return points;
    }

    // Port of atheriz/utils.py:47-61 is_in_game_folder (with Windows case-insensitive branch)
    // C# addition: also accepts C# game folder (any *.csproj at cwd, e.g. MyGame.csproj + GameSettings.cs from `new`) so that
    // `dotnet run --project src/Atheriz.Server -- new` + `create`/`start` work without Python settings.py.
    public static bool IsInGameFolder() => IsInGameFolder(OperatingSystem.IsWindows() ? "nt" : "posix");
    public static bool IsInGameFolder(string osName)
    {
        var cwd = Directory.GetCurrentDirectory();
        bool isNt = string.Equals(osName, "nt", StringComparison.OrdinalIgnoreCase);
        bool isPython;
        // Port of utils.py:49-55 nt branch uses _exists_exact_str case-insensitive
        if (isNt)
        {
            isPython = ExistsExact(Path.Combine(cwd, "settings.py"), "nt")
                && ExistsExact(Path.Combine(cwd, "__init__.py"), "nt")
                && !ExistsExact(Path.Combine(cwd, "atheriz.py"), "nt");
        }
        else
        {
            // Port of utils.py:56-61 posix branch — cwd / "settings.py" exists etc (case-sensitive)
            isPython = File.Exists(Path.Combine(cwd, "settings.py"))
                && File.Exists(Path.Combine(cwd, "__init__.py"))
                && !File.Exists(Path.Combine(cwd, "atheriz.py"));
        }
        if (isPython) return true;
        // C# game folder: `atheriz new` template creates <Name>.csproj + GameSettings.cs at cwd
        // Require both to avoid treating src/Atheriz.Server (csproj but no GameSettings.cs) as game folder
        try
        {
            bool hasCsproj = Directory.EnumerateFiles(cwd, "*.csproj").Any();
            bool hasGameSettings;
            if (isNt)
                hasGameSettings = ExistsExact(Path.Combine(cwd, "GameSettings.cs"), "nt");
            else
                hasGameSettings = File.Exists(Path.Combine(cwd, "GameSettings.cs"));
            if (hasCsproj && hasGameSettings) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Exact name existence check — mirrors <c>utils.py:_exists_exact</c> / <c>_exists_exact_str</c>.
    /// On Windows (<c>os.name=="nt"</c>) does case-insensitive compare via <c>lower()</c>;
    /// on POSIX does case-sensitive <c>name in os.listdir</c>.
    /// </summary>
    public static bool ExistsExact(string path) => ExistsExact(path, OperatingSystem.IsWindows() ? "nt" : "posix");
    public static bool ExistsExact(string path, string osName)
    {
        try
        {
            var parent = Path.GetDirectoryName(path) ?? ".";
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return File.Exists(path) || Directory.Exists(path);
            if (!Directory.Exists(parent)) return File.Exists(path) || Directory.Exists(path);
            var entries = Directory.GetFileSystemEntries(parent);
            bool isNt = string.Equals(osName, "nt", StringComparison.OrdinalIgnoreCase);
            if (isNt)
            {
                // Port of utils.py:30 name.lower() in (n.lower() for n in os.listdir(parent))
                return entries.Any(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
            }
            // Port of utils.py:31 return path.name in os.listdir(path.parent) — case-sensitive
            return entries.Any(e => Path.GetFileName(e) == name);
        }
        catch { return File.Exists(path) || Directory.Exists(path); }
    }

    // --- Phase18: missing pure helpers ---

    // Port of atheriz/utils.py:434 compress_whitespace
    public static string CompressWhitespace(string text, int maxLinebreaks = 1, int maxSpacing = 2)
    {
        if (text == null) return "";
        text = text.TrimEnd();
        text = ReEmpty.Replace(text, "\n\n");
        text = Regex.Replace(text, $@"(?<=\S) {{{maxSpacing},}}", new string(' ', maxSpacing));
        text = Regex.Replace(text, $@"\n{{{maxLinebreaks},}}", new string('\n', maxLinebreaks));
        return text;
    }

    // Port of atheriz/utils.py:458 is_iter
    public static bool IsIter(object? obj)
    {
        if (obj is null) return false;
        if (obj is string) return false;
        if (obj is byte[]) return false;
        return obj is IEnumerable;
    }

    // Port of atheriz/utils.py:483 make_iter
    public static IEnumerable<object?> MakeIter(object? obj)
    {
        if (!IsIter(obj)) return new object?[] { obj };
        if (obj is IEnumerable<object?> e) return e;
        if (obj is IEnumerable en) return en.Cast<object?>();
        return new object?[] { obj };
    }

    // Port of atheriz/utils.py:483 generic helper
    public static IEnumerable<T> MakeIter<T>(object? obj)
    {
        if (obj is IEnumerable<T> seq && obj is not string) return seq;
        if (obj is T t) return new[] { t };
        if (obj == null) return new T[] { default! };
        // fallback: try cast
        try { return new[] { (T)obj }; } catch { return Array.Empty<T>(); }
    }

    // Port of atheriz/utils.py:498 copy_word_case
    public static string CopyWordCase(string baseWord, string newWord)
    {
        if (string.IsNullOrEmpty(baseWord) || string.IsNullOrEmpty(newWord)) return newWord ?? "";
        if (IsTitle(baseWord)) return ToTitle(newWord);
        if (IsLower(baseWord)) return newWord.ToLowerInvariant();
        if (IsUpper(baseWord)) return newWord.ToUpperInvariant();
        var maxlen = baseWord.Length;
        var shared = newWord.Length <= maxlen ? newWord : newWord.Substring(0, maxlen);
        var excess = newWord.Length <= maxlen ? "" : newWord.Substring(maxlen);
        var chars = shared.Select((ch, i) => char.IsUpper(baseWord[i]) ? char.ToUpperInvariant(ch) : char.ToLowerInvariant(ch)).ToArray();
        return new string(chars) + excess;
    }

    private static bool IsTitle(string s) => !string.IsNullOrEmpty(s) && char.IsUpper(s[0]) && s.Skip(1).All(c => !char.IsLetter(c) || char.IsLower(c)) && s.Any(char.IsLetter);
    private static bool IsLower(string s) => s.Any(char.IsLetter) && s.All(c => !char.IsUpper(c));
    private static bool IsUpper(string s) => s.Any(char.IsLetter) && s.All(c => !char.IsLower(c));
    private static string ToTitle(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

    // Port of atheriz/utils.py:536 iter_to_str
    public static string IterToString(IEnumerable<object?>? iterable, string sep = ",", string endsep = ", and", bool addQuote = false)
    {
        if (iterable == null) return "";
        // mimic make_iter then list
        var list = iterable.ToList();
        if (list.Count == 0) return "";
        List<string> strs = addQuote ? list.Select(v => $"\"{v}\"").ToList() : list.Select(v => v?.ToString() ?? "").ToList();
        var normSep = sep?.Trim() ?? ",";
        var normEnd = endsep != null ? endsep.Trim() : "";
        // handle empty endsep case like Python: if endsep falsy, keep as is (null/empty)
        if (!string.IsNullOrEmpty(normEnd))
        {
            if (normEnd.StartsWith(normSep) && normEnd != normSep)
                normEnd = strs.Count < 3 ? normEnd.Substring(1) : normEnd;
            else if (normEnd.Length > 0 && !Punctuation.Contains(normEnd[0]))
                normEnd = " " + normEnd.Trim();
        }
        if (!Punctuation.Contains(normSep)) normSep = " " + normSep.Trim();
        if (strs.Count == 1) return strs[0];
        if (strs.Count == 2) return string.Join(normEnd + " ", strs);
        return string.Join(normSep + " ", strs.Take(strs.Count - 1)) + normEnd + " " + strs[^1];
    }

    // Port of atheriz/utils.py:605 is_empty_method — approximated via IL (handles debug locals/br)
    public static bool IsEmptyMethod(MethodInfo? method)
    {
        if (method == null) return false;
        try
        {
            var body = method.GetMethodBody();
            if (body == null) return false;
            var il = body.GetILAsByteArray();
            if (il == null || il.Length == 0) return true;
            var ops = new List<byte>();
            for (int i = 0; i < il.Length;)
            {
                byte op = il[i++];
                if (op == 0xFE) { if (i < il.Length) { ops.Add(il[i++]); } continue; }
                ops.Add(op);
                int sz = op switch { 0x13 or 0x11 or 0x2B or 0x1F => 1, 0x38 => 4, 0x20 or 0x28 or 0x72 or 0x73 => 4, 0x21 or 0x22 or 0x23 => 8, _ => 0 };
                i += sz;
            }
            var trivial = new HashSet<byte>{0x00,0x14,0x2A,0x0A,0x0B,0x0C,0x0D,0x06,0x07,0x08,0x09,0x11,0x13,0x2B,0x38};
            if (ops.Any(o => !trivial.Contains(o))) return false;
            bool hasLdNull = ops.Contains((byte)0x14), hasRet = ops.Contains((byte)0x2A);
            if (!hasRet) return false; return !hasLdNull || ops.Count(o => o == 0x14) == 1;
        }
        catch { return false; }
    }

    // Port of atheriz/utils.py:642 _build_signature_from_code shim — in C# use MethodInfo.GetParameters
    public static ParameterInfo[] BuildSignature(Delegate del) => del.Method.GetParameters(); // Port of utils.py:642 shim

    public static ParameterInfo[] BuildSignature(MethodInfo method) => method.GetParameters();

    // Alias for Python name
    public static ParameterInfo[] BuildSignatureFromCode(MethodInfo method) => BuildSignature(method);

    // Port of atheriz/utils.py:701 get_class_hooks
    public static List<(string Name, ParameterInfo[] Signature, string? Doc, bool IsEmpty)> GetClassHooks(Type cls)
    {
        var result = new List<(string, ParameterInfo[], string?, bool)>();
        var overridePrefixes = new[] { "at_", "access_", "format_", "pre_", "post_" };
        var alwaysInclude = new HashSet<string>(StringComparer.Ordinal) { "setup_parser", "run", "SetupParser", "Run" };
        var methods = cls.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy);
        foreach (var m in methods)
        {
            var name = m.Name;
            if (name.StartsWith("_") && !alwaysInclude.Contains(name)) continue;
            bool faithful = overridePrefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal)) || alwaysInclude.Contains(name);
            bool pascalHook = name.StartsWith("At", StringComparison.Ordinal) || name.StartsWith("Access", StringComparison.Ordinal)
                || name.StartsWith("Format", StringComparison.Ordinal) || name.StartsWith("Pre", StringComparison.Ordinal) || name.StartsWith("Post", StringComparison.Ordinal);
            if (!faithful && !pascalHook) continue;
            // Skip object base methods
            if (m.DeclaringType == typeof(object)) continue;
            ParameterInfo[] sig;
            try { sig = m.GetParameters(); } catch { sig = BuildSignature(m); }
            string? doc = null;
            try
            {
                var desc = m.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>();
                doc = desc?.Description;
            }
            catch { }
            bool isEmpty = IsEmptyMethod(m);
            result.Add((name, sig, doc, isEmpty));
        }
        return result;
    }

    // Port of atheriz/utils.py:141 detach — deepcopy via JSON roundtrip (mirrors dill roundtrip)
    public static T? Detach<T>(T value)
    {
        if (value == null) return default;
        try
        {
            var json = JsonSerializer.Serialize(value);
            return JsonSerializer.Deserialize<T>(json);
        }
        catch
        {
            // fallback: try copy via JSON element clone for primitives
            try { return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value)); } catch { return value; }
        }
    }

    // Port of atheriz/utils.py:74 ensure_thread_safe — in C# explicit RWLock, no patch needed
    // plan2.md rationale: C# uses explicit ReaderWriterLockSlim + Immutable snapshots instead of runtime __getattribute__ patch.
    public static void EnsureThreadSafe(Type t)
    {
        // no-op stub: thread-safety in C# is explicit via ReaderWriterLockSlim on GameObject/Node etc.
        // Mirrors Python's _PATCH_LOCK __getattribute__ copy-on-read which is intentionally not ported.
        _ = t;
    }
}
