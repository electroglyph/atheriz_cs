using System.Text;

namespace Atheriz.Core.Commands;

public static class HelpFormatter
{
    // Legacy column minimums: tables whose keys/categories fit keep the
    // established 12-wide layout byte-for-byte; wider data widens the column
    // instead of overflowing into the description.
    private const int MinColumnWidth = 12;

    public static string Format(IEnumerable<Command> cmds, bool screenreader, int termWidth)
    {
        ArgumentNullException.ThrowIfNull(cmds);
        var ordered = cmds.OrderBy(x => x.Category).ThenBy(x => x.Key).ToList();
        int catWidth = Math.Max(MinColumnWidth,
            ordered.Select(c => c.Category.Length).Append("Category".Length).Max());
        int keyWidth = Math.Max(MinColumnWidth,
            ordered.Select(c => c.Key.Length).Append("Command".Length).Max());
        var sb = new StringBuilder();
        // mirror help.py: PrettyTable header/border/style and max_table_width = term_width-2
        // screenreader => no border/header, simple list
        if (screenreader)
        {
            foreach (var c in ordered)
                sb.AppendLine($"{c.Category.PadRight(catWidth)} {c.Key.PadRight(keyWidth)} {c.Desc}");
        }
        else
        {
            int width = termWidth > 0 ? termWidth : 80;
            // header row
            sb.AppendLine($"{"Category".PadRight(catWidth)} {"Command".PadRight(keyWidth)} Description");
            sb.AppendLine(new string('-', Math.Min(60, Math.Max(20, width - 2))));
            int budget = width - (catWidth + keyWidth + 2);
            foreach (var c in ordered)
            {
                string desc = c.Desc;
                // respect max_table_width: truncate if needed (PrettyTable would wrap)
                if (desc.Length > budget) desc = desc[..Math.Max(0, budget - 3)] + "...";
                sb.AppendLine($"{c.Category.PadRight(catWidth)} {c.Key.PadRight(keyWidth)} {desc}");
            }
        }
        return sb.ToString();
    }
}
