using System.Collections;
using System.Text;
using System.Text.RegularExpressions;

namespace Atheriz.Core.Utils;

/// <summary>
/// </summary>
public static partial class GameUtils
{
    // Source-generated (AOT-safe, zero startup compile): previously
    // runtime-compiled instances built per process start.
    [GeneratedRegex(@"\x1b\[[0-9;]*m")]
    private static partial Regex AnsiRegex();

    [GeneratedRegex(@"\x1b\[[0-9;]*[A-Za-z]|\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b[^[A-Za-z0-9]|\x00")]
    private static partial Regex TerminalEscapeRegex();

    private const string Punctuation = "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    // --- ansi ---

    public static string StripAnsi(string input) => AnsiRegex().Replace(input, "");

    public static string StripTerminalEscapes(string input) => TerminalEscapeRegex().Replace(input, "");

    public static string WrapXterm256(
        string input, int? fg = null, int? bg = null,
        bool bold = false, bool italic = false, bool underline = false,
        bool inverse = false, bool strikethru = false, bool clear = false)
    {
        if (clear) input = StripAnsi(input);
        if (fg is not null) input = $"\x1b[38;5;{fg}m{input}";
        if (bg is not null) input = $"\x1b[48;5;{bg}m{input}";
        return ApplyStyleFlags(input, bold, italic, underline, inverse, strikethru);
    }

    public static string WrapRgb(string input, (byte R, byte G, byte B)? fg = null, (byte R, byte G, byte B)? bg = null,
        bool bold = false, bool italic = false, bool underline = false)
    {
        input = bg is not null ? $"\x1b[48;2;{bg.Value.R};{bg.Value.G};{bg.Value.B}m{input}" : $"\x1b[48;2;0;0;0m{input}";
        input = fg is not null ? $"\x1b[38;2;{fg.Value.R};{fg.Value.G};{fg.Value.B}m{input}" : $"\x1b[38;2;204;204;204m{input}";
        return ApplyStyleFlags(input, bold, italic, underline);
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
        return ApplyStyleFlags(input, bold, italic, underline, inverse, strikethru);
    }

    // Shared style-flag applier for the Wrap* helpers above: the flag order
    // (bold, italic, underline, inverse, strikethru) plus the trailing reset is
    // identical at every site, so one helper keeps the bytes identical. It runs
    // after the color codes, leaving each caller's color order untouched.
    private static string ApplyStyleFlags(string input, bool bold, bool italic, bool underline, bool inverse = false, bool strikethru = false)
    {
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
        (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p),
            1 => (q, v, p),
            2 => (p, v, t),
            3 => (p, q, v),
            4 => (t, p, v),
            _ => (v, p, q),
        };
        return ((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }

    // --- game helpers ---

    public static int DiceRoll(int rolls, int faces)
    {
        if (rolls > 0 && faces < 1) throw new ArgumentOutOfRangeException(nameof(faces));
        var result = 0;
        for (var i = 0; i < rolls; i++) result += Random.Shared.Next(1, faces + 1);
        return result;
    }

    public static double DiceRollAverage(int rolls, int faces)
    {
        if (faces < 1) throw new ArgumentOutOfRangeException(nameof(faces));
        // Long math: faces + 1 overflows int at int.MaxValue faces.
        return rolls * ((faces + 1L) / 2.0);
    }

    public static T Clamp<T>(T min, T value, T max) where T : IComparable<T>
    {
// faithful when min>max
        var tmp = value.CompareTo(max) <= 0 ? value : max;
        return tmp.CompareTo(min) >= 0 ? tmp : min;
    }

    /// <summary>
    /// Compass direction from one coordinate to another.
    /// Returns "" when the areas differ.
    /// </summary>
    public static string GetDir(Coord origin, Coord dest)
    {
        if (origin.Area != dest.Area) return "";
        return DirFromDeltas(dest.X - origin.X, dest.Y - origin.Y);
    }

    // Shared ns/ew-to-compass composition for GetDir.
    private static string DirFromDeltas(int ew, int ns)
    {
        var dir = "";
        if (ns > 0) dir = "north"; else if (ns < 0) dir = "south";
        if (ew > 0) dir += "east"; else if (ew < 0) dir += "west";
        return dir;
    }

    // Shared Euclidean core for Dist3d. Keeps the Math.Pow
    // formulation (not dx*dx) so float results cannot drift between overloads.
    private static double DistCore(double dx, double dy, double dz)
        => Math.Sqrt(Math.Pow(dx, 2) + Math.Pow(dy, 2) + Math.Pow(dz, 2));

    public static double Dist3d(Coord origin, Coord dest)
        => DistCore(origin.X - dest.X, origin.Y - dest.Y, origin.Z - dest.Z);

    public static double Dist3d((int X, int Y, int Z) a, (int X, int Y, int Z) b)
        => DistCore(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static string WordReplace(string input, double replaceFreq, string replacement = "...")
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
            if (Random.Shared.NextDouble() < replaceFreq) words[i] = replacement;
        return string.Join(" ", words);
    }

    public const int MaxSphereRadius = 100;

    public static List<(int X, int Y, int Z)> GetPointsInSphere((int X, int Y, int Z) center, double radius, bool ignoreCenter = false, int maxResults = int.MaxValue)
    {
        if (radius < 0 || radius > MaxSphereRadius) throw new ArgumentOutOfRangeException(nameof(radius), $"radius {radius} out of bounds [0, {MaxSphereRadius}]");
        var (cx, cy, cz) = center;
        List<(int, int, int)> points = [];
        var r2 = radius * radius;
        var r = (int)radius;
        for (var x = cx - r; x <= cx + r; x++)
            for (var y = cy - r; y <= cy + r; y++)
                for (var z = cz - r; z <= cz + r; z++)
                {
                    if (ignoreCenter && x == cx && y == cy && z == cz) continue;
                    var distSq = (x - cx) * (x - cx) + (y - cy) * (y - cy) + (z - cz) * (z - cz);
                    if (distSq <= r2)
                    {
                        points.Add((x, y, z));
                        // Pagination guard: r=100 fills ~4M points in one alloc;
                        // callers that page stop the enumeration early here.
                        if (points.Count >= maxResults) return points;
                    }
                }
        return points;
    }

    // C# addition: also accepts C# game folder (any *.csproj at cwd, e.g. MyGame.csproj + GameSettings.cs from `new`) so that
    // `dotnet run --project src/Atheriz.Server -- new` + `create`/`start` work without Python settings.py.
    // No cache by design: callers are CLI-op-frequency guard paths, and markers
    // are created/deleted between calls. A directory-mtime-keyed cache proved
    // stale here — .NET's GetLastWriteTimeUtc does not observe File.Delete on
    // this platform (empirically mtime-equal across a delete).
    public static bool IsInGameFolder()
    {
        string? cwd;
        try { cwd = Directory.GetCurrentDirectory(); }
        catch { cwd = null; }
        return CheckGameFolder(cwd, OperatingSystem.IsWindows());
    }
    // Typed OS branch (not a stringly "nt"/"posix" flag): the internal
    // overload lets tests drive the Windows branch on Linux.
    internal static bool IsInGameFolder(bool windows) => CheckGameFolder(CurrentDirectory(), windows);
    private static string? CurrentDirectory()
    {
        try { return Directory.GetCurrentDirectory(); }
        catch { return null; }
    }
    private static bool CheckGameFolder(string? cwd, bool windows)
    {
        var dir = cwd ?? Directory.GetCurrentDirectory();
        bool isPython;
        if (windows)
        {
            isPython = ExistsExact(Path.Combine(dir, "settings.py"), ignoreCase: true)
                && ExistsExact(Path.Combine(dir, "__init__.py"), ignoreCase: true)
                && !ExistsExact(Path.Combine(dir, "atheriz.py"), ignoreCase: true);
        }
        else
        {
// dir / "settings.py" exists etc (case-sensitive)
            isPython = File.Exists(Path.Combine(dir, "settings.py"))
                && File.Exists(Path.Combine(dir, "__init__.py"))
                && !File.Exists(Path.Combine(dir, "atheriz.py"));
        }
        if (isPython) return true;
        // C# game folder: `atheriz new` template creates <Name>.csproj + GameSettings.cs at dir
        // Require both to avoid treating src/Atheriz.Server (csproj but no GameSettings.cs) as game folder
        try
        {
            bool hasCsproj = Directory.EnumerateFiles(dir, "*.csproj").Any();
            bool hasGameSettings = windows
                ? ExistsExact(Path.Combine(dir, "GameSettings.cs"), ignoreCase: true)
                : File.Exists(Path.Combine(dir, "GameSettings.cs"));
            if (hasCsproj && hasGameSettings) return true;
        }
        catch { }
        return false;
    }

    /// <summary>
    /// Exact name existence check — mirrors <c>utils.py:_exists_exact</c> / <c>_exists_exact_str</c>.
    /// Filesystem semantics already do the right thing per OS (case-insensitive
    /// on Windows, case-sensitive on POSIX), so production just asks the OS.
    /// </summary>
    public static bool ExistsExact(string path) => Path.Exists(path);
    // Explicit-comparison variant for tests driving the Windows
    // case-insensitive branch on a case-sensitive filesystem.
    internal static bool ExistsExact(string path, bool ignoreCase)
    {
        if (!ignoreCase) return Path.Exists(path);
        try
        {
            var parent = Path.GetDirectoryName(path) ?? ".";
            var name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name)) return Path.Exists(path);
            if (!Directory.Exists(parent)) return Path.Exists(path);
            return Directory.GetFileSystemEntries(parent)
                .Any(e => string.Equals(Path.GetFileName(e), name, StringComparison.OrdinalIgnoreCase));
        }
        catch { return Path.Exists(path); }
    }

    // --- Phase18: missing pure helpers ---

    //
    // Compiled-pattern cache keyed by (maxLinebreaks, maxSpacing): the key space
    // is tiny (small int pairs) and the method runs on broadcast paths, so sharing
    // Default maxLinebreaks preserves a single blank line: 1 erased every
    // blank gap by default, so callers passing no args lost paragraph breaks.
    public static string CompressWhitespace(string text, int maxLinebreaks = 2, int maxSpacing = 2)
    {
        if (text is null) return "";
        text = text.TrimEnd();
        // Two span passes, no regex, no cache: blank-gap normalization first
        // (`\n[ \t]*\n` -> `\n\n`, as the old ReEmpty pre-pass), then the
        // fused space-run / newline-run caps below.
        return CollapseRuns(CollapseBlankGaps(text.AsSpan()), maxLinebreaks, maxSpacing);
    }

    // Blank-gap normalization: each `\n[ \t]*\n` gap becomes exactly `\n\n`.
    // Left-to-right non-overlapping, matching the old Replace semantics
    // (`"a\n\n\nb"` keeps three breaks here; the run cap trims them after).
    private static string CollapseBlankGaps(ReadOnlySpan<char> text)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c != '\n') { sb.Append(c); i++; continue; }
            int j = i + 1;
            while (j < text.Length && (text[j] == ' ' || text[j] == '\t')) j++;
            if (j < text.Length && text[j] == '\n') { sb.Append('\n'); sb.Append('\n'); i = j + 1; }
            else { sb.Append('\n'); i++; }
        }
        return sb.ToString();
    }

    // Run caps: a space run of `maxSpacing`+ collapses to exactly that many
    // spaces, but only when preceded by a non-whitespace char (the old
    // `(?<=\S)` gate — leading spaces and spaces after a break stay as-is);
    // a newline run of `maxLinebreaks`+ collapses to exactly that many
    // breaks. A zero cap disables that collapse (the old `{0,}` pattern
    // matched empty and replaced with empty: a no-op).
    private static string CollapseRuns(ReadOnlySpan<char> text, int maxLinebreaks, int maxSpacing)
    {
        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                int j = i;
                while (j < text.Length && text[j] == '\n') j++;
                if (maxLinebreaks >= 1 && j - i >= maxLinebreaks) sb.Append('\n', maxLinebreaks);
                else sb.Append(text.Slice(i, j - i));
                i = j;
            }
            else if (c == ' ')
            {
                int j = i;
                while (j < text.Length && text[j] == ' ') j++;
                bool gated = i > 0 && !char.IsWhiteSpace(text[i - 1]);
                if (maxSpacing >= 1 && j - i >= maxSpacing && gated) sb.Append(' ', maxSpacing);
                else sb.Append(text.Slice(i, j - i));
                i = j;
            }
            else { sb.Append(c); i++; }
        }
        return sb.ToString();
    }

    // Single generic shape: one value yields one; a sequence of T passes
    // through; strings never enumerate as characters; null yields one null
    // (callers iterate unconditionally). Sequences need explicit T
    // (MakeIter<object>(items)): inferring T from the sequence itself would
    // wrap it instead of passing it through.
    public static IEnumerable<T> MakeIter<T>(T value)
    {
        if (value is IEnumerable<T> seq && value is not string) return seq;
        return new[] { value };
    }

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

    public static string IterToString(IEnumerable<object?>? iterable, string sep = ",", string endsep = ", and", bool addQuote = false)
    {
        if (iterable is null) return "";
        // Single-pass stringify: the old code materialized the enumerable plus a
        // projected snapshot before joining. Converting inline yields the same
        // list; the Take/join truncation below is untouched.
        List<string> strs = [];
        foreach (var v in iterable)
            strs.Add(addQuote ? $"\"{v}\"" : v?.ToString() ?? "");
        if (strs.Count == 0) return "";
        var normSep = sep?.Trim() ?? ",";
        var normEnd = endsep is not null ? endsep.Trim() : "";
        // handle empty endsep case like Python: if endsep falsy, keep as is (null/empty)
        if (!string.IsNullOrEmpty(normEnd))
        {
            if (normEnd.StartsWith(normSep, StringComparison.Ordinal) && normEnd != normSep)
                normEnd = strs.Count < 3 ? normEnd.Substring(1) : normEnd;
            else if (normEnd.Length > 0 && !Punctuation.Contains(normEnd[0]))
                normEnd = " " + normEnd.Trim();
        }
        if (!Punctuation.Contains(normSep)) normSep = " " + normSep.Trim();
        if (strs.Count == 1) return strs[0];
        if (strs.Count == 2) return string.Join(normEnd + " ", strs);
        return string.Join(normSep + " ", strs.Take(strs.Count - 1)) + normEnd + " " + strs[^1];
    }

// deepcopy via JSON roundtrip (mirrors dill roundtrip)
    public static T? Detach<T>(T value)
    {
        if (value is null) return default;
        // at all — never hand back the live original, and never a silent
        // blank that callers would mutate as if detached.
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<T>(json);
    }

// in C# explicit RWLock, no patch needed
    public static void EnsureThreadSafe(Type t)
    {
        // no-op stub: thread-safety in C# is explicit via ReaderWriterLockSlim on GameObject/Node etc.
        // Mirrors Python's _PATCH_LOCK __getattribute__ copy-on-read which is intentionally not ported.
        _ = t;
    }

    // Shared no-echo secret reader (InitialSetup + GameTemplateGenerator
    // credential prompts): ReadKey loop with backspace handling. Callers keep
    // their own redirected-input branches and ReadLine fallbacks.
    public static string ReadSecretLine()
    {
        var sb = new System.Text.StringBuilder();
        ConsoleKeyInfo k;
        while ((k = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) sb.Length--;
            else if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        Console.Out.WriteLine();
        return sb.ToString().Trim();
    }
}
