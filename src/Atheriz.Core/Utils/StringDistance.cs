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
    public static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>
    /// Returns the candidate with minimal Levenshtein distance to <paramref name="query"/>,
    /// or <c>null</c> if <paramref name="candidates"/> is empty.
    /// </summary>
    public static string? BestMatch(string query, IEnumerable<string> candidates)
    {
        // Spec: candidates.OrderBy(k=>Levenshtein(query,k)).FirstOrDefault() or null if empty.
        return candidates.OrderBy(k => Levenshtein(query, k)).FirstOrDefault();
    }
}
