// Port of atheriz/commands/loggedin/help.py + unloggedin/help.py table formatting (fixed -12 columns)
using System.Text;

namespace Atheriz.Core.Commands;

public static class HelpFormatter
{
    public static string Format(IEnumerable<Command> cmds, bool screenreader, int termWidth)
    {
        var sb = new StringBuilder();
        // mirror help.py: PrettyTable header/border/style and max_table_width = term_width-2
        // screenreader => no border/header, simple list
        if (screenreader)
        {
            foreach (var c in cmds.OrderBy(x => x.Category).ThenBy(x => x.Key))
                sb.AppendLine($"{c.Category,-12} {c.Key,-12} {c.Desc}");
        }
        else
        {
            int width = termWidth > 0 ? termWidth : 80;
            // header row
            sb.AppendLine($"{"Category",-12} {"Command",-12} Description");
            sb.AppendLine(new string('-', Math.Min(60, Math.Max(20, width - 2))));
            foreach (var c in cmds.OrderBy(x => x.Category).ThenBy(x => x.Key))
            {
                string desc = c.Desc;
                // respect max_table_width: truncate if needed (PrettyTable would wrap)
                if (desc.Length > width - 26) desc = desc.Substring(0, Math.Max(0, width - 26 - 3)) + "...";
                sb.AppendLine($"{c.Category,-12} {c.Key,-12} {desc}");
            }
        }
        return sb.ToString();
    }
}
