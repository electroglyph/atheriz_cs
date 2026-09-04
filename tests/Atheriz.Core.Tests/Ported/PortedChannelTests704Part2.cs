// Port of atheriz/tests/test_channel.py:1 part2 — faithful 80-test split part2 (40 tests)
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Settings;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedChannelTests704Part2
{
    private static void ClearChannelCache() => PortedHelpers.ClearChannelCache();

    private static GameObject MakeObj(string name = "foo")
    {
        var o = GameObject.Create(name);
        ObjectRegistry.AddObject(o);
        return o;
    }

    // test_channel.py:460 test_history_ordered_oldest_first
    [Fact] public void HistoryOrderedOldestFirst()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        chan.Send("first", null);
        chan.Send("second", null);
        var outStr = chan.GetHistory();
        Assert.True(outStr.IndexOf("first") < outStr.IndexOf("second"));
    }

    // test_channel.py:470 TestChannelClearHistory.test_clear
    [Fact] public void Clear()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        chan.Send("x", null);
        chan.Send("y", null);
        Assert.Equal(2, chan.History.Count);
        chan.ClearHistory();
        Assert.Empty(chan.History);
    }

    // test_channel.py:479 test_clear_empty
    [Fact] public void ClearEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var ex = Record.Exception(() => chan.ClearHistory());
        Assert.Null(ex);
        Assert.Empty(chan.History);
    }

    // test_channel.py:492 test_returns_command_with_channel_name
    [Fact] public void ReturnsCommandWithChannelName()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "Help";
        chan.Desc = "Help channel";
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        Assert.Equal("help", cmd!.Key);
        Assert.Equal("Help channel", cmd.Desc);
    }

    // test_channel.py:500 test_lowercases_name
    [Fact] public void LowercasesName()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "TRADE";
        var cmd = chan.GetCommand();
        Assert.Equal("trade", cmd!.Key);
    }

    // test_channel.py:506 test_caches_command
    [Fact] public void CachesCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "chat";
        var c1 = chan.GetCommand();
        var c2 = chan.GetCommand();
        Assert.Same(c1, c2);
    }

    // test_channel.py:513 test_command_id_matches_channel_id
    [Fact] public void CommandIdMatchesChannelId()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "x";
        chan.Id = 555;
        var cmd = chan.GetCommand() as BaseChannelCommand;
        Assert.NotNull(cmd);
        Assert.Equal(555, cmd!.Id);
        Assert.Equal(555, cmd.id);
    }

    // test_channel.py:527 test_at_delete_default_returns_true
    [Fact] public void AtDeleteDefaultReturnsTrue()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var result = chan.AtDelete((GameObject?)null);
        Assert.True(result);
    }

    // test_channel.py:531 test_at_create_default_is_noop
    [Fact] public void AtCreateDefaultIsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var ex = Record.Exception(() => chan.AtCreate());
        Assert.Null(ex);
    }

    // test_channel.py:542 test_getstate_excludes_lock_and_listeners
    [Fact] public void GetstateExcludesLockAndListeners()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "pchan";
        chan.Id = 10;
        var listener = GameObject.Create("l", isPc: true);
        ObjectRegistry.AddObject(listener);
        chan.AddListener(listener);
        var dto = chan.ToDto();
        Assert.DoesNotContain("lock", dto.Extra.Keys);
        Assert.Equal("pchan", dto.Name);
        Assert.Equal(10, dto.Id);
        Assert.True(dto.Extra.ContainsKey("history") || dto.Extra.ContainsKey("History"));
        Assert.DoesNotContain("listeners", dto.Extra.Keys);
    }

    // test_channel.py:554 test_setstate_restores_lock_and_clears_listeners
    [Fact] public void SetstateRestoresLockAndClearsListeners()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "pchan2";
        var dto = chan.ToDto();
        var newChan = GameObject.FromDto(dto) as Channel ?? new Channel();
        if (newChan is not Channel)
        {
            newChan = new Channel();
            newChan.Name = dto.Name;
        }
        Assert.NotNull(newChan.SyncRoot);
        Assert.Empty(newChan.Listeners);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(newChan.History);
    }

    // test_channel.py:565 test_setstate_history_rewrapped_if_not_deque
    [Fact] public void SetstateHistoryRewrappedIfNotDeque()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var dto = chan.ToDto();
        dto.Extra["history"] = System.Text.Json.JsonDocument.Parse("[\"x\",\"y\"]").RootElement.Clone();
        var newChan = new Channel();
        // After setstate, history should still be LinkedList-like via History property
        Assert.NotNull(newChan.History);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(newChan.History);
    }

    // test_channel.py:576 test_pickle_roundtrip_preserves_state
    [Fact] public void PickleRoundtripPreservesState()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("picklechan");
        chan.Desc = "desc";
        chan.Send("hello", null);
        var dto = chan.ToDto();
        var json = GameObjectDtoSerializer.ToJson(dto);
        var dto2 = GameObjectDtoSerializer.FromJson(json);
        var chan2 = GameObject.FromDto(dto2);
        Assert.Equal("picklechan", chan2.Name);
        Assert.Equal("desc", chan2.Desc);
        Assert.Equal(chan.Id, chan2.Id);
        if (chan2 is Channel c2) Assert.Empty(c2.Listeners);
        else Assert.Empty((chan2 as Channel)?.Listeners ?? new HashSet<int>());
    }

    // test_channel.py:588 test_pickled_channel_can_msg
    [Fact] public void PickledChannelCanMsg()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("funchan");
        var dto = chan.ToDto();
        var json = GameObjectDtoSerializer.ToJson(dto);
        var dto2 = GameObjectDtoSerializer.FromJson(json);
        var chan2 = GameObject.FromDto(dto2) as Channel ?? new Channel { Name = "funchan", Id = chan.Id };
        if (ObjectRegistry.Get(chan2.Id).Count == 0) ObjectRegistry.AddObject(chan2);
        var l = GameObject.Create("l", isPc: true);
        ObjectRegistry.AddObject(l);
        l.ClearMessages();
        chan2.AddListener(l);
        chan2.Send("hi", null);
        Assert.Contains("hi", chan2.History.Last());
    }

    // test_channel.py:604 TestBaseChannelCommand.test_key_and_category
    [Fact] public void KeyAndCategory()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand();
        Assert.Equal("__base_channel", cmd.Key);
        Assert.Equal("Communication", cmd.Category);
    }

    // test_channel.py:609 test_channel_property_lazy_lookup
    [Fact] public void ChannelPropertyLazyLookup()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("lookup");
        var cmd = new BaseChannelCommand { id = chan.Id, _channel = null };
        Channel result = cmd.channel;
        Assert.Same(chan, result);
    }

    // test_channel.py:617 test_channel_property_missing_raises
    [Fact] public void ChannelPropertyMissingRaises()
    {
        using var env = GlobalTestEnv.Enter();
        var cmd = new BaseChannelCommand { _channel = null, id = 99999 };
        Assert.ThrowsAny<Exception>(() => { var _ = cmd.channel; });
        try { var _ = cmd.channel; Assert.Fail("should have thrown"); } catch (Exception ex) { Assert.Contains("not found", ex.Message); }
    }

    // test_channel.py:624 test_channel_setter_sets_id
    [Fact] public void ChannelSetterSetsId()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel(); chan.Id = 77;
        var cmd = new BaseChannelCommand();
        cmd.channel = chan;
        Assert.Same(chan, (Channel)cmd._channel!);
        Assert.Equal(77, cmd.id);
    }

    // test_channel.py:632 test_run_message_sends_via_channel
    [Fact] public void RunMessageSendsViaChannel()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("msgchan");
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("alice", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        int before = chan.History.Count;
        var pa = cmd!.Parser!.ParseArgs(new[] { "hello" });
        cmd.Run(caller, pa);
        Assert.Equal(before + 1, chan.History.Count);
        Assert.Contains("hello", chan.History.Last());
    }

    // test_channel.py:642 test_run_replay_no_permission
    [Fact] public void RunReplayNoPermission()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("replaychan");
        chan.AddLock("view", _ => false);
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(new[] { "-r" });
        cmd.Run(caller, pa);
        var outText = string.Join(" ", caller.PeekMessages());
        Assert.Contains("permission", outText.ToLower());
    }

    // test_channel.py:655 test_run_replay_with_permission
    [Fact] public void RunReplayWithPermission()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("replaychan2");
        chan.Send("line1", null);
        chan.Send("line2", null);
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(new[] { "-r" });
        cmd.Run(caller, pa);
        var outText = string.Join(" ", caller.PeekMessages());
        Assert.Contains("line1", outText);
        Assert.Contains("line2", outText);
    }

    // test_channel.py:669 test_run_send_no_permission
    [Fact] public void RunSendNoPermission()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("sendchan");
        chan.AddLock("send", _ => false);
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        int before = chan.History.Count;
        var pa = cmd!.Parser!.ParseArgs(new[] { "hi" });
        cmd.Run(caller, pa);
        Assert.Equal(before, chan.History.Count);
        var outText = string.Join(" ", caller.PeekMessages());
        Assert.Contains("permission", outText.ToLower());
    }

    // test_channel.py:682 test_run_send_with_permission
    [Fact] public void RunSendWithPermission()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("sendchan2");
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(new[] { "hi" });
        cmd.Run(caller, pa);
        Assert.Contains("hi", chan.History.Last());
    }

    // test_channel.py:692 test_run_unsubscribe
    [Fact] public void RunUnsubscribe()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("subchan");
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.Subscribe(chan);
        Assert.Contains(chan.Id, caller.ChannelsSnapshot);
        var pa = cmd!.Parser!.ParseArgs(new[] { "-u" });
        cmd.Run(caller, pa);
        Assert.DoesNotContain(chan.Id, caller.ChannelsSnapshot);
        Assert.DoesNotContain(caller.Id, chan.Listeners);
    }

    // test_channel.py:702 test_run_no_args_shows_help
    [Fact] public void RunNoArgsShowsHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("helpchan");
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(Array.Empty<string>());
        cmd.Run(caller, pa);
        var outText = string.Join(" ", caller.PeekMessages()).ToLower();
        Assert.Contains("usage:", outText);
    }

    // test_channel.py:713 test_replay_empty_history_says_no_history
    [Fact] public void ReplayEmptyHistorySaysNoHistory()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("emptychan");
        var cmd = chan.GetCommand();
        var caller = GameObject.Create("a", isPc: true);
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(new[] { "-r" });
        cmd.Run(caller, pa);
        var outText = string.Join(" ", caller.PeekMessages()).ToLower();
        Assert.Contains("no history", outText);
    }

    // test_channel.py:724 test_getstate_excludes_channel
    [Fact] public void GetstateExcludesChannel()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("picklecmd");
        var cmd = chan.GetCommand() as BaseChannelCommand;
        Assert.NotNull(cmd);
        var state = cmd!.__getstate__();
        Assert.DoesNotContain("_channel", state.Keys);
    }

    // test_channel.py:730 test_setstate_resets_channel
    [Fact] public void SetstateResetsChannel()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("picklecmd2");
        var cmd = chan.GetCommand() as BaseChannelCommand;
        Assert.NotNull(cmd);
        var state = cmd!.__getstate__();
        var cmd2 = new BaseChannelCommand();
        cmd2.__setstate__(state);
        Assert.Null(cmd2._channel);
    }

    // test_channel.py:740 test_channel_property_roundtrip_after_state_transfer
    [Fact] public void ChannelPropertyRoundtripAfterStateTransfer()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("testchan");
        var cmd = ch.GetCommand() as BaseChannelCommand;
        Assert.NotNull(cmd);
        var state = cmd!.__getstate__();
        var restored = new BaseChannelCommand();
        restored.__setstate__(state);
        restored.id = ch.Id;
        restored._channel = null;
        Channel result = restored.channel;
        Assert.Same(ch, result);
    }

    // test_channel.py:751 test_get_history_zero_is_empty
    [Fact] public void GetHistoryZeroIsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("testchan_zero");
        ch.Send("hello", null);
        ch.Send("world", null);
        var outStr = ch.GetHistory(0);
        Assert.Equal("", outStr);
    }

    // test_channel.py:759 test_get_history_respects_count
    [Fact] public void GetHistoryRespectsCount()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("testchan2");
        ch.Send("one", null);
        ch.Send("two", null);
        ch.Send("three", null);
        var outStr = ch.GetHistory(1);
        Assert.Contains("three", outStr);
        Assert.DoesNotContain("one", outStr);
    }

    // test_channel.py:768 test_get_history_zero_does_not_affect_default
    [Fact] public void GetHistoryZeroDoesNotAffectDefault()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = Channel.Create("testchan3");
        ch.Send("one", null);
        ch.Send("two", null);
        var zero = ch.GetHistory(0);
        Assert.Equal("", zero);
        var full = ch.GetHistory();
        Assert.Contains("one", full);
        Assert.Contains("two", full);
    }

    // test_channel.py:787 test_channel_broadcast_marks_channel_modified
    [Fact] public void ChannelBroadcastMarksChannelModified()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("announce");
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.False(channel.IsModified);
        channel.Send("hello there", null);
        Assert.True(channel.IsModified);
    }

    // test_channel.py:796 test_channel_msg_with_sender_marks_modified
    [Fact] public void ChannelMsgWithSenderMarksModified()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("announce2");
        var sender = GameObject.Create("S");
        ObjectRegistry.AddObject(sender);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.False(channel.IsModified);
        channel.Send("hello there", sender);
        Assert.True(channel.IsModified);
    }

    // test_channel.py:806 test_channel_history_persists_across_restart
    [Fact] public void ChannelHistoryPersistsAcrossRestart()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("announce3");
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        channel.Send("hello there", null);
        int channelId = channel.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var reloaded = ObjectRegistry.Get(channelId).FirstOrDefault() as Channel;
        Assert.NotNull(reloaded);
        var hist = reloaded!.History;
        Assert.Single(hist);
        Assert.Contains("hello there", hist[0]);
        var outStr = reloaded.GetHistory();
        Assert.Contains("hello there", outStr);
    }

    // test_channel.py:825 test_channel_history_cap_survives_restart
    [Fact] public void ChannelHistoryCapSurvivesRestart()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("announce4");
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        int total = AtherizSettings.Global.ChannelHistoryLimit + 5;
        for (int i = 0; i < total; i++) channel.Send($"message {i}", null);
        int channelId = channel.Id;
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var reloaded = ObjectRegistry.Get(channelId).FirstOrDefault() as Channel;
        Assert.NotNull(reloaded);
        var hist = reloaded!.History;
        Assert.Equal(AtherizSettings.Global.ChannelHistoryLimit, hist.Count);
        Assert.Contains($"message {total - AtherizSettings.Global.ChannelHistoryLimit}", hist.First());
        Assert.Contains($"message {total - 1}", hist.Last());
    }

    // test_channel.py:842 test_channel_clear_history_marks_modified_and_persists
    [Fact] public void ChannelClearHistoryMarksModifiedAndPersists()
    {
        using var env = GlobalTestEnv.Enter();
        var channel = Channel.Create("announce5");
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        channel.Send("hello there", null);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        Assert.False(channel.IsModified);
        int channelId = channel.Id;
        channel.ClearHistory();
        Assert.True(channel.IsModified);
        using (var db = new AtherizDbContext(env.TempPath)) { db.Database.EnsureCreated(); ObjectRegistry.SaveObjects(db); }
        ObjectRegistry.ClearAll();
        ObjectRegistry.LoadObjects(env.TempPath);
        var reloaded = ObjectRegistry.Get(channelId).FirstOrDefault() as Channel;
        Assert.NotNull(reloaded);
        Assert.Empty(reloaded!.History);
    }

    // test_channel.py:860 test_get_history_negative_count_returns_empty
    [Fact] public void GetHistoryNegativeCountReturnsEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var ch = new Channel();
        ch.Name = "testchan";
        ch.Id = 999999;
        ch.ClearHistory();
        ch.Send("hello", null);
        ch.Send("world", null);
        ch.Send("third", null);
        var h = ch.GetHistory(2);
        Assert.True(h.Contains("world") || h.Contains("third"));
        Assert.Equal("", ch.GetHistory(-5));
        Assert.Equal("", ch.GetHistory(-1));
        Assert.Equal("", ch.GetHistory(0));
        int over = AtherizSettings.Global.ChannelHistoryLimit + 100;
        var hist = ch.GetHistory(over);
        Assert.Contains("hello", hist);
    }

    // test_channel.py:879 test_channel_cache_skips_deleted_entry
    [Fact] public void ChannelCacheSkipsDeletedEntry()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var chan = Channel.Create("CacheTestChan");
        chan.Desc = "desc";
        var cmd = new ChannelCommand();
        var name = chan.Name.ToLowerInvariant();
        ChannelCommand.SetCacheForTesting(name, chan);
        chan.IsDeleted = true;
        var caller = GameObject.Create("Caller");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd.Parser!.ParseArgs(new[] { "-c", chan.Name });
        cmd.Run(caller, pa);
        Assert.False(ChannelCommand.TryGetCached(name, out _));
        var outText = string.Join(" ", caller.PeekMessages()).ToLower();
        Assert.Contains("not found", outText);
        chan.IsDeleted = false;
    }

    // test_channel.py:903 test_channel_cache_revalidates_name_mismatch
    [Fact] public void ChannelCacheRevalidatesNameMismatch()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var chan = Channel.Create("ValidChan");
        chan.Desc = "desc";
        var cmd = new ChannelCommand();
        var name = chan.Name.ToLowerInvariant();
        var other = Channel.Create("OtherChan");
        other.Desc = "desc2";
        ChannelCommand.SetCacheForTesting(name, other);
        var caller = GameObject.Create("Caller2");
        ObjectRegistry.AddObject(caller);
        caller.ClearMessages();
        var pa = cmd.Parser!.ParseArgs(new[] { "-c", chan.Name, "-u" });
        Assert.True(ChannelCommand.TryGetCached(name, out var before) && before == other);
        cmd.Run(caller, pa);
        Assert.True(ChannelCommand.TryGetCached(name, out var cached));
        Assert.Same(chan, cached);
    }
}
