
namespace Atheriz.Core.Commands;

// Shared triage behind ChannelCommand and BaseChannelCommand: both dispatch
// on the same flag precedence (list > unsubscribe > subscribe > replay >
// send), but each keeps its own arms — ChannelCommand's view-as-not-found
// spoofing and BaseChannelCommand's denied messages must not cross over.
// Sharing only the parser setup and the precedence decision keeps a fix to
// one from silently diverging the other.
internal enum ChannelAction
{
    List,
    Unsubscribe,
    Subscribe,
    Replay,
    Send,
}

internal static class ChannelActionParser
{
    // The message + unsubscribe + replay defs are identical in both parsers;
    // ChannelCommand adds -l/--list, -c/--channel, -s/--subscribe on top.
    internal static void AddCommonArgs(GameArgumentParser p)
    {
        ArgumentNullException.ThrowIfNull(p);
        p.AddArgument(ParsedArgKeys.Message).Help("Message to send").Nargs("*");
        p.AddArgument("-u", "--unsubscribe").Help("Unsubscribe from channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-r", "--replay").Help("View channel history").Action(GameArgumentParser.ArgAction.StoreTrue);
    }

    // Flag precedence, shared: an explicit action flag wins over a message
    // body, and list wins over everything (a flag the command's parser does
    // not define reads false, so this is uniform across both commands).
    internal static ChannelAction Parse(GameArgumentParser.ParsedArgs pa)
    {
        ArgumentNullException.ThrowIfNull(pa);
        if (pa.GetBool("list")) return ChannelAction.List;
        if (pa.GetBool("unsubscribe")) return ChannelAction.Unsubscribe;
        if (pa.GetBool("subscribe")) return ChannelAction.Subscribe;
        if (pa.GetBool("replay")) return ChannelAction.Replay;
        return ChannelAction.Send;
    }
}
