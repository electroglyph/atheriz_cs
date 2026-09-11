
namespace Atheriz.Core.Commands;

/// <summary>
/// Port of atheriz/objects/base_channel.py:BaseChannelCommand
/// </summary>
public class BaseChannelCommand : Command
{
    private string _key = "__base_channel";
    private string _desc = "Command for accessing channel";
    public override string Key => _key;
    public override string Desc => _desc;
    public override string Category => "Communication";

    /// <summary>
    /// Renames this command. Must be called before the command is added to a
    /// <see cref="CmdSet"/>: the set snapshots keys at registration, so a
    /// post-add rename orphans the command from Get/AutoAlias/Help.
    /// </summary>
    public void SetKey(string k)
    {
        _key = k;
        // force parser rebuild so FormatHelp shows new prog (Parser lazily rebuilds when null)
        Parser = null;
    }
    public void SetDesc(string d)
    {
        _desc = d;
        Parser = null;
    }

    // Use fields to match Python's __dict__ keys for test reflection (id, _channel)
    public int id = -1;
    public Channel? _channel;

    public int Id
    {
        get => id;
        set => id = value;
    }

    public Channel channel
    {
        get
        {
            if (TryGetChannel(out var ch) && ch is not null) return ch;
            ThrowChannelNotFound();
            return null!;
        }
        set
        {
            _channel = value;
            if (value is not null) id = value.Id;
        }
    }

    // Single throw site for every channel-not-found outcome below (exceptions
    // are compared by type+message, so the observable behavior is identical).
    private void ThrowChannelNotFound() => throw new InvalidOperationException($"Channel {id} not found.");

    /// <summary>
    /// Non-throwing channel resolution: false when the cached channel was
    /// deleted, the id resolves to a non-channel/missing object, or nothing
    /// resolves at all. A deleted cached reference is dropped, matching the
    /// throwing getter it backs.
    /// </summary>
    public bool TryGetChannel(out Channel? ch)
    {
        ch = null;
        if (_channel is not null)
        {
            if (_channel.IsDeleted) { _channel = null; return false; }
            ch = _channel;
            return true;
        }
        var obj = ObjectRegistry.GetSingle(id);
        if (obj is null) return false;
        if (obj.IsDeleted) return false;
        if (obj is not Channel found) return false;
        _channel = found;
        ch = found;
        return true;
    }

    // Alias for C# property Channel (capital) used by some code, but test uses dynamic channel (lower)
    // Provide both
    public Channel Channel
    {
        get => channel;
        set => channel = value;
    }

    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("message").Help("Message to send").Nargs("?");
        p.AddArgument("-u", "--unsubscribe").Help("Unsubscribe from channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-r", "--replay").Help("View channel history").Action(GameArgumentParser.ArgAction.StoreTrue);
    }

    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        // The single getter below only hands out live channels, so the
        // deleted re-check it replaces is subsumed (same user message).
        if (!TryGetChannel(out var ch) || ch is null)
        {
            caller.Msg("That channel no longer exists.");
            return;
        }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null)
        {
            // Try to parse if args is raw string? For test they pass ParsedArgs directly
            caller.Msg(Parser!.FormatHelp());
            return;
        }
        if (pa.GetBool("unsubscribe"))
        {
            // Port of channel.py:110-111 caller.unsubscribe(channel):
            // failures propagate, never swallowed.
            ch.RemoveListener(go);
            go.Unsubscribe(ch);
            // also remove command from internal cmdset? Handled via Unsubscribe
        }
        else if (pa.GetBool("replay"))
        {
            if (!ch.Access(go, "view"))
            {
                CommandHelpers.MsgChannelViewDenied(caller);
                return;
            }
            var h = ch.GetHistory();
            if (!string.IsNullOrEmpty(h)) caller.Msg(h);
            else CommandHelpers.MsgNoChannelHistory(caller);
        }
        else if (pa.GetString("message") is string msg && !string.IsNullOrWhiteSpace(msg))
        {
            if (!ch.Access(go, "send"))
            {
                CommandHelpers.MsgChannelSendDenied(caller);
                return;
            }
            ch.Msg(msg, go);
        }
        else
        {
            caller.Msg(Parser!.FormatHelp());
        }
    }

    // State save/restore excluding the live _channel reference.
    public Dictionary<string, object?> GetState()
    {
        Dictionary<string, object?> d = [];
        // In real dill, _channel popped; we simulate by not including
        d["id"] = id;
        // other fields like Key etc not needed
        return d;
    }
    public void SetState(Dictionary<string, object?> state)
    {
        if (state.TryGetValue("id", out var v) && v is int i) id = i;
        _channel = null;
    }
}
