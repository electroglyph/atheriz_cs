using System.Text;

namespace Atheriz.Core.Commands;

public static class HelpFormatter
{
    // Legacy column minimums for the screenreader list: tables whose
    // keys/categories fit keep the established 12-wide layout
    // byte-for-byte; wider data widens the column instead of overflowing.
    private const int MinColumnWidth = 12;

    // Box overview layout: two boxes share a row (one for narrow
    // terminals), each fixed at an equal share of the width.
    private const int PairGap = 2;
    private const int NarrowSingleColumnBelow = 50;

    public static string Format(IEnumerable<Command> cmds, bool screenreader, int termWidth)
    {
        ArgumentNullException.ThrowIfNull(cmds);
        var ordered = cmds.OrderBy(x => x.Category).ThenBy(x => x.Key).ToList();
        int catWidth = Math.Max(MinColumnWidth,
            ordered.Select(c => c.Category.Length).Append("Category".Length).Max());
        int keyWidth = Math.Max(MinColumnWidth,
            ordered.Select(c => c.Key.Length).Append("Command".Length).Max());
        var sb = new StringBuilder();
        // screenreader => no borders, simple list (box-drawing characters
        // are hostile to screen readers, so this path stays untouched).
        if (screenreader)
        {
            foreach (var c in ordered)
                sb.AppendLine($"{c.Category.PadRight(catWidth)} {c.Key.PadRight(keyWidth)} {c.Desc}");
            return sb.ToString();
        }
        int width = termWidth > 0 ? termWidth : 80;
        if (width < 20) width = 20;
        int perRow = width >= NarrowSingleColumnBelow ? 2 : 1;
        int boxWidth = (width - PairGap * (perRow - 1)) / perRow;
        var groups = ordered.GroupBy(c => c.Category).ToList();
        for (int i = 0; i < groups.Count; i += perRow)
        {
            var row = groups.Skip(i).Take(perRow).Select(g => BuildBox(g.Key, g.Select(c => c.Key), boxWidth)).ToList();
            int height = row.Max(b => b.Count);
            for (int line = 0; line < height; line++)
                sb.AppendLine(string.Join(new string(' ', PairGap),
                    row.Select(b => line < b.Count ? b[line] : BlankLine(boxWidth))));
        }
        return sb.ToString();
    }

    // One category box: title embedded in the top border, command names
    // comma-separated and wrapped at comma boundaries onto as many lines
    // as needed. A lone over-long name truncates with … instead of
    // breaking the border.
    private static List<string> BuildBox(string category, IEnumerable<string> keys, int boxWidth)
    {
        int inner = boxWidth - 4;
        var lines = new List<string> { TopBorder(category, boxWidth) };
        lines.AddRange(WrapKeys(keys, inner).Select(text => $"│ {text.PadRight(inner)} │"));
        lines.Add($"╰{new string('─', boxWidth - 2)}╯");
        return lines;
    }

    private static string TopBorder(string category, int boxWidth)
    {
        string title = category;
        int maxTitle = boxWidth - 6;
        if (title.Length > maxTitle) title = title[..Math.Max(0, maxTitle)];
        string head = $"─ {title} ";
        return $"╭{head}{new string('─', boxWidth - 2 - head.Length)}╮";
    }

    // Padding for the shorter box in a row: blank space, never a
    // border — bordered padding draws stray vertical lines next to the
    // taller box (Admin/Communication/Socials rows).
    private static string BlankLine(int boxWidth)
        => new string(' ', boxWidth);

    private static List<string> WrapKeys(IEnumerable<string> keys, int inner)
    {
        var lines = new List<string>();
        var current = new StringBuilder();
        foreach (var raw in keys)
        {
            string key = raw.Length > inner ? raw[..Math.Max(0, inner - 1)] + "…" : raw;
            string piece = current.Length == 0 ? key : ", " + key;
            if (current.Length + piece.Length > inner)
            {
                lines.Add(current.ToString());
                current.Clear();
                current.Append(key);
            }
            else
            {
                current.Append(piece);
            }
        }
        if (current.Length > 0) lines.Add(current.ToString());
        return lines;
    }
}
