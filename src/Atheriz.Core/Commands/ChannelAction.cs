
namespace Atheriz.Core.Commands;

// Shared triage behind ChannelCommand and BaseChannelCommand: both dispatch
// on the same flag precedence (list > mine > unsubscribe > subscribe >
// replay > send), but each keeps its own arms — ChannelCommand's
// view-as-not-found spoofing and BaseChannelCommand's denied messages must
// not cross over.
// Sharing only the parser setup and the precedence decision keeps a fix to
// one from silently diverging the other.
internal enum ChannelAction
{
    List,
    Mine,
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
    // Mine sits right below list: only ChannelCommand defines -m, so the
    // per-channel commands keep parsing exactly as before.
    internal static ChannelAction Parse(GameArgumentParser.ParsedArgs pa)
    {
        ArgumentNullException.ThrowIfNull(pa);
        if (pa.GetBool(ParsedArgKeys.List)) return ChannelAction.List;
        if (pa.GetBool(ParsedArgKeys.Mine)) return ChannelAction.Mine;
        if (pa.GetBool(ParsedArgKeys.Unsubscribe)) return ChannelAction.Unsubscribe;
        if (pa.GetBool(ParsedArgKeys.Subscribe)) return ChannelAction.Subscribe;
        if (pa.GetBool(ParsedArgKeys.Replay)) return ChannelAction.Replay;
        return ChannelAction.Send;
    }
}
