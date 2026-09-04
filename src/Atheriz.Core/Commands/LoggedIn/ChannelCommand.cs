// Port of atheriz/commands/loggedin/channel.py:131
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>Port of atheriz/commands/loggedin/channel.py:ChannelCommand — lazy _channel_cache wontfix.</summary>
public sealed class ChannelCommand : Command
{
    public override string Key => "channel";
    public override string Desc => "Use and subscribe to channels.";
    public override string Category => "Communication";
    // wontfix: lazy cache only, cleared on is_deleted/name mismatch or via filter_by scan. No eager invalidation on delete/rename.
    private static readonly Dictionary<string, Channel> ChannelCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();
    public static void ClearCache() { lock (CacheLock) ChannelCache.Clear(); }
    public static IReadOnlyDictionary<string, Channel> GetCacheSnapshot() { lock (CacheLock) return new Dictionary<string, Channel>(ChannelCache, StringComparer.OrdinalIgnoreCase); }
    public static bool TryGetCached(string name, out Channel? ch) { lock (CacheLock) return ChannelCache.TryGetValue(name, out ch); }
    public static void SetCacheForTesting(string name, Channel ch) { lock (CacheLock) ChannelCache[name.ToLowerInvariant()] = ch; }
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("message").Help("Message to send").Nargs("*");
        p.AddArgument("-l", "--list").Help("List all channels").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-c", "--channel").Help("Channel to target");
        p.AddArgument("-u", "--unsubscribe").Help("Unsubscribe from channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-s", "--subscribe").Help("Subscribe to channel").Action(GameArgumentParser.ArgAction.StoreTrue);
        p.AddArgument("-r", "--replay").Help("View channel history").Action(GameArgumentParser.ArgAction.StoreTrue);
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { caller.Msg(PrintHelp()); return; }
        if (pa.GetBool("list"))
        {
            var channels = ObjectRegistry.FilterBy(x => x.IsChannel);
            var visible = channels.Where(c => c.Access(go, "view")).ToList();
            if (visible.Count > 0)
            {
                var msg = string.Join("\n", visible.Select(ch => $"{GameUtils.WrapXterm256(ch.Name, fg:15, bold:true)}: {ch.Desc}"));
                go.Msg($"{visible.Count} available channels:\n{msg}");
            }
            else go.Msg("No channels found.");
            return;
        }
        var chName = pa.GetString("channel");
        if (string.IsNullOrEmpty(chName)) { caller.Msg(PrintHelp()); return; }
        var nameLower = chName!.ToLowerInvariant();
        Channel? channel = null;
        lock (CacheLock)
        {
            if (ChannelCache.TryGetValue(nameLower, out var cached) && (cached.IsDeleted || !cached.Name.Equals(chName, StringComparison.OrdinalIgnoreCase)))
            {
                ChannelCache.Remove(nameLower);
                cached = null;
            }
            channel = ChannelCache.TryGetValue(nameLower, out var c) ? c : null;
        }
        if (channel == null)
        {
            var result = ObjectRegistry.FilterBy(x => x.IsChannel && x.Name.Equals(chName, StringComparison.OrdinalIgnoreCase));
            if (result.Count == 0) { go.Msg($"Channel {chName} not found."); return; }
            channel = result[0] as Channel ?? new Channel { Name = result[0].Name, Desc = result[0].Desc };
            // ensure channel object is correct instance
            if (result[0] is Channel ch2) channel = ch2;
            if (channel.IsDeleted) { go.Msg($"Channel {chName} not found."); return; }
            lock (CacheLock)
            {
                if (ChannelCache.TryGetValue(nameLower, out var existing) && !existing.IsDeleted && existing.Name.Equals(chName, StringComparison.OrdinalIgnoreCase))
                    channel = existing;
                else
                {
                    ChannelCache[nameLower] = channel;
                    foreach (var kv in ChannelCache.ToList()) if (kv.Key != nameLower && kv.Value.Id == channel.Id) ChannelCache.Remove(kv.Key);
                }
            }
        }
        if (pa.GetBool("unsubscribe"))
        {
            go.Unsubscribe(channel);
            go.Msg($"Unsubscribed from channel {channel.Name}.");
            return;
        }
        else if (pa.GetBool("subscribe"))
        {
            if (!channel.Access(go, "view")) { go.Msg("You do not have permission to view this channel."); return; }
            go.Subscribe(channel);
        }
        else if (pa.GetBool("replay"))
        {
            if (!channel.Access(go, "view")) { go.Msg("You do not have permission to view this channel."); return; }
            go.Msg(channel.GetHistory());
        }
        else
        {
            var msgs = pa.GetList("message");
            var message = string.Join(" ", msgs);
            if (string.IsNullOrWhiteSpace(message)) { go.Msg(PrintHelp()); return; }
            if (!channel.Access(go, "send")) { go.Msg("You do not have permission to send to this channel."); return; }
            channel.Send(message, go);
        }
    }
}
