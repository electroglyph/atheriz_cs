// Port of polyleven:levenshtein — atheriz/commands/loggedin/none.py:5 and atheriz/commands/unloggedin/none.py:5
namespace Atheriz.Core.Utils;

/// <summary>
/// String distance helpers. Port of <c>polyleven:levenshtein</c> used in
/// <c>atheriz/commands/loggedin/none.py:5</c> and <c>atheriz/commands/unloggedin/none.py:5</c>.
/// </summary>
public static class StringDistance
{
    /// <summary>
    /// Levenshtein distance DP O(n*m) with <c>int[,] d</c> as in NoneCommand.
    /// Uses <c>StringComparer.Ordinal</c> semantics (char equality <c>a[i-1]==b[j-1]</c>).
    /// </summary>
    /// <summary>
    /// Inputs longer than this are not worth a full O(n*m) table (10k x 10k
    /// would allocate ~400MB); the capped upper bound below still orders sane.
    /// </summary>
    public const int MaxInputLength = 1024;

    public static int Levenshtein(string a, string b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        // Capped upper bound (true distance is always <= max length), no table.
        if (a.Length > MaxInputLength || b.Length > MaxInputLength)
            return Math.Max(a.Length, b.Length);
        // Two-row DP: computes identical distances to the full table (standard
        // result) at O(min(n,m)) memory. The distance is symmetric in a/b, so
        // sizing the rows by the shorter input changes nothing observable.
        if (b.Length > a.Length) (a, b) = (b, a);
        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (int j = 1; j <= b.Length; j++)
                curr[j] = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, curr) = (curr, prev);
        }
        return prev[b.Length];
    }

    /// <summary>
    /// Returns the candidate with minimal Levenshtein distance to <paramref name="query"/>,
    /// or <c>null</c> if <paramref name="candidates"/> is empty.
    /// </summary>
    public static string? BestMatch(string query, IEnumerable<string> candidates)
    {
        // MinBy returns the FIRST minimum exactly like the old stable
        // OrderBy+FirstOrDefault, without the O(C log C) sort — and yields
        // default (null) on empty input just like FirstOrDefault did.
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidates);
        return candidates.MinBy(k => Levenshtein(query, k));
    }
}
