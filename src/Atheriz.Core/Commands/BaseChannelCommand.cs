
namespace Atheriz.Core.Commands;

/// <summary>
/// </summary>
public class BaseChannelCommand : LoggedInCommand
{
    private string _key = "__base_channel";
    private string _desc = "Command for accessing channel";
    public override string Key => _key;
    public override string Desc => _desc;
    public override string Category => "Communication";

    /// <summary>
    /// Keeps the lazy path working: commands are built parameterless and then
    /// renamed through <see cref="SetKey"/>/<see cref="SetDesc"/>.
    /// </summary>
    public BaseChannelCommand() { }

    /// <summary>
    /// Captures a live channel with the same wiring the
    /// <see cref="Objects.Channel.GetCommand"/> lazy path installs: lowercased
    /// channel name as key, channel desc as desc, channel id plus the live
    /// reference backing <see cref="TryGetChannel"/>.
    /// </summary>
    public BaseChannelCommand(Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        string key;
        string desc;
        using (channel.ReadScope()) { key = channel.Name.ToLowerInvariant(); desc = channel.Desc; }
        SetKey(key);
        SetDesc(desc);
        Channel = channel;
        Id = channel.Id;
    }

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

    protected override void SetupParser(GameArgumentParser p) => ChannelActionParser.AddCommonArgs(p);

    protected override bool AllowMissingArgs => true;

    protected override void RunPuppet(GameObject go, GameArgumentParser.ParsedArgs pa, CancellationToken ct)
    {
        // The single getter below only hands out live channels, so the
        // deleted re-check it replaces is subsumed (same user message).
        if (!TryGetChannel(out var ch) || ch is null)
        {
            go.Msg("That channel no longer exists.");
            return;
        }
        // One triage shared with ChannelCommand (same flag precedence); the
        // arms stay local — this command's denied messages must not cross
        // over to ChannelCommand's view-as-not-found spoofing.
        switch (ChannelActionParser.Parse(pa))
        {
            case ChannelAction.Unsubscribe:
                // failures propagate, never swallowed.
                ch.RemoveListener(go);
                go.Unsubscribe(ch);
                // also remove command from internal cmdset? Handled via Unsubscribe
                break;
            case ChannelAction.Replay:
                if (!ch.Access(go, "view"))
                {
                    CommandHelpers.MsgChannelViewDenied(go);
                    return;
                }
                var h = ch.GetHistory();
                if (!string.IsNullOrEmpty(h)) go.Msg(h);
                else CommandHelpers.MsgNoChannelHistory(go);
                break;
            default:
                // Send (List/Subscribe carry no flags here — the parser
                // defines neither — so they read as an empty message).
                if (string.Join(" ", pa.GetList(ParsedArgKeys.Message)) is string msg && !string.IsNullOrWhiteSpace(msg))
                {
                    if (!ch.Access(go, "send"))
                    {
                        CommandHelpers.MsgChannelSendDenied(go);
                        return;
                    }
                    ch.Msg(msg, go);
                }
                else
                {
                    go.Msg(Parser!.FormatHelp());
                }
                break;
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
