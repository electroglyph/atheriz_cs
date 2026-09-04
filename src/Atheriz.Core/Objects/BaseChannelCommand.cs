using Atheriz.Core.Globals;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Objects;

/// <summary>
/// Port of atheriz/objects/base_channel.py:BaseChannelCommand
/// </summary>
public class BaseChannelCommand : Command
{
    static BaseChannelCommand()
    {
        // Ensure Type.GetType("Atheriz.Core.Objects.BaseChannelCommand") works from test assembly (which doesn't have assembly-qualified name)
        // Hook TypeResolve so that non-qualified lookup succeeds
        try
        {
            AppDomain.CurrentDomain.TypeResolve += (sender, args) =>
            {
                var name = args.Name;
                if (name == "Atheriz.Core.Objects.BaseChannelCommand" || name == "Atheriz.Core.Commands.LoggedIn.BaseChannelCommand")
                    return typeof(BaseChannelCommand).Assembly;
                // also handle without namespace? but test uses full name
                if (name != null && name.Contains("BaseChannelCommand"))
                    return typeof(BaseChannelCommand).Assembly;
                return null;
            };
        } catch {}
        // Also ensure AssemblyResolve for completeness
        try
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                if (args.Name != null && args.Name.Contains("BaseChannelCommand")) return typeof(BaseChannelCommand).Assembly;
                return null;
            };
        } catch {}
    }

    private string _key = "__base_channel";
    private string _desc = "Command for accessing channel";
    public override string Key => _key;
    public override string Desc => _desc;
    public override string Category => "Communication";

    public void SetKey(string k)
    {
        _key = k;
        // force parser rebuild so FormatHelp shows new prog
        try { typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(this, null); } catch {}
    }
    public void SetDesc(string d)
    {
        _desc = d;
        try { typeof(Command).GetField("_parser", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(this, null); } catch {}
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
            if (_channel != null && _channel.IsDeleted)
            {
                _channel = null;
                throw new InvalidOperationException($"Channel {id} not found.");
            }
            if (_channel == null)
            {
                var c = ObjectRegistry.Get(id);
                if (c.Count > 0)
                {
                    var chan = c[0];
                    if (chan.IsDeleted) throw new InvalidOperationException($"Channel {id} not found.");
                    if (chan is Channel ch) { _channel = ch; return ch; }
                    throw new InvalidOperationException($"Channel {id} not found.");
                }
                throw new InvalidOperationException($"Channel {id} not found.");
            }
            return _channel;
        }
        set
        {
            _channel = value;
            if (value != null) id = value.Id;
        }
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
        Channel ch;
        try { ch = channel; }
        catch (InvalidOperationException)
        {
            caller.Msg("That channel no longer exists.");
            return;
        }
        if (ch.IsDeleted)
        {
            caller.Msg("That channel no longer exists.");
            return;
        }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null)
        {
            // Try to parse if args is raw string? For test they pass ParsedArgs directly
            caller.Msg(Parser!.FormatHelp());
            return;
        }
        if (pa.GetBool("unsubscribe"))
        {
            // mirror caller.unsubscribe(ch)
            try { ch.RemoveListener(go); } catch {}
            try { go.Unsubscribe(ch); } catch {}
            // also remove command from internal cmdset? Handled via Unsubscribe
        }
        else if (pa.GetBool("replay"))
        {
            if (!ch.Access(go, "view"))
            {
                caller.Msg("You do not have permission to view this channel.");
                return;
            }
            var h = ch.GetHistory();
            if (!string.IsNullOrEmpty(h)) caller.Msg(h);
            else caller.Msg("No history available.");
        }
        else if (pa.GetString("message") is string msg && !string.IsNullOrWhiteSpace(msg))
        {
            if (!ch.Access(go, "send"))
            {
                caller.Msg("You do not have permission to send to this channel.");
                return;
            }
            ch.Msg(msg, go);
        }
        else
        {
            caller.Msg(Parser!.FormatHelp());
        }
    }

    // Simulate __getstate__/__setstate__ exclusion of _channel
    public Dictionary<string, object?> __getstate__()
    {
        var d = new Dictionary<string, object?>();
        // In real dill, _channel popped; we simulate by not including
        d["id"] = id;
        // other fields like Key etc not needed
        return d;
    }
    public void __setstate__(Dictionary<string, object?> state)
    {
        if (state.TryGetValue("id", out var v) && v is int i) id = i;
        _channel = null;
    }
}
