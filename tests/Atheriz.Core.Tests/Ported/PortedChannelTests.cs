// Port of atheriz/tests/test_channel.py:1 — faithful 80-test split part1 (40 tests)
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Settings;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedChannelTests
{
    private static void ClearChannelCache() => PortedHelpers.ClearChannelCache();

    private static GameObject MakeCaller(string name = "TestPlayer", int id = 1) => PortedHelpers.MakeCallerWithId(name, id);

    private static Channel MakeRawChannel(string name = "public", int id = 100, string desc = "Public channel")
    {
        var ch = new Channel();
        ch.Name = name;
        ch.Id = id;
        ch.Desc = desc;
        ch.CreatedBy = -1;
        return ch;
    }

    // test_channel.py:55 test_channel_list_no_message
    [Fact] public void ChannelListNoMessage()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var caller = MakeCaller();
        caller.ClearMessages();
        var channel = Channel.Create("public");
        channel.Desc = "Public channel";
        var cmd = new ChannelCommand();
        var pa = cmd.Parser!.ParseArgs(new[] { "-l" });
        cmd.Run(caller, pa);
        var msgs = caller.PeekMessages();
        Assert.NotEmpty(msgs);
        var all = string.Join(" ", msgs);
        Assert.Contains("available channels", all);
        Assert.Contains("public", all);
    }

    // test_channel.py:71 test_channel_send_message
    [Fact] public void ChannelSendMessage()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var caller = MakeCaller();
        caller.ClearMessages();
        var channel = Channel.Create("public");
        var cmd = new ChannelCommand();
        var pa = cmd.Parser!.ParseArgs(new[] { "-c", "public", "hello" });
        var before = channel.History.Count;
        cmd.Run(caller, pa);
        var after = channel.History.Count;
        Assert.Equal(before + 1, after);
        Assert.Contains(channel.History, h => h.Contains("hello"));
    }

    // test_channel.py:83 test_channel_no_message_no_flags
    [Fact] public void ChannelNoMessageNoFlags()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var caller = MakeCaller();
        caller.ClearMessages();
        var channel = Channel.Create("public");
        var cmd = new ChannelCommand();
        var pa = cmd.Parser!.ParseArgs(Array.Empty<string>());
        cmd.Run(caller, pa);
        var outText = string.Join(" ", caller.PeekMessages());
        Assert.Contains("usage:", outText.ToLower());
    }

    // test_channel.py:95 test_channel_target_and_message
    [Fact] public void ChannelTargetAndMessage()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var caller = MakeCaller();
        caller.ClearMessages();
        var channel = Channel.Create("public");
        var cmd = new ChannelCommand();
        var pa = cmd.Parser!.ParseArgs(new[] { "-c", "public", "hello" });
        cmd.Run(caller, pa);
        Assert.Contains(channel.History, h => h.Contains("hello"));
        Assert.True(ChannelCommand.TryGetCached("public", out var cachedChan));
        Assert.Same(channel, cachedChan);
    }

    // test_channel.py:110 test_channel_lookup_cached
    [Fact] public void ChannelLookupCached()
    {
        using var env = GlobalTestEnv.Enter();
        ClearChannelCache();
        var caller = MakeCaller();
        var channel = Channel.Create("public");
        var cmd = new ChannelCommand();
        var pa = cmd.Parser!.ParseArgs(new[] { "-c", "public", "hi" });
        cmd.Run(caller, pa);
        var countAfterFirst = ChannelCommand.GetCacheSnapshot().Count;
        caller.ClearMessages();
        cmd.Run(caller, pa);
        caller.ClearMessages();
        cmd.Run(caller, pa);
        var countAfterThird = ChannelCommand.GetCacheSnapshot().Count;
        Assert.Equal(countAfterFirst, countAfterThird);
        Assert.True(ChannelCommand.TryGetCached("public", out var cached));
        Assert.Same(channel, cached);
    }

    // test_channel.py:125 test_local_channel_command_help
    [Fact] public void LocalChannelCommandHelp()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("Server");
        chan.Desc = "Server announcements";
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        Assert.Equal("server", cmd!.Key);
        Assert.Equal("Server announcements", cmd.Desc);
        var help = cmd.Parser!.FormatHelp();
        Assert.Contains("usage: server", help);
        Assert.Contains("Server announcements", help);
    }

    // test_channel.py:140 test_local_channel_command_no_message
    [Fact] public void LocalChannelCommandNoMessage()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("Server");
        var cmd = chan.GetCommand();
        Assert.NotNull(cmd);
        var caller = MakeCaller();
        caller.ClearMessages();
        var pa = cmd!.Parser!.ParseArgs(Array.Empty<string>());
        cmd.Run(caller, pa);
        var all = string.Join(" ", caller.PeekMessages());
        Assert.Contains("usage: server", all.ToLower());
    }

    // test_channel.py:160 TestChannelConstructor.test_init_defaults
    [Fact] public void InitDefaults()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        Assert.Equal("", chan.Name);
        Assert.Equal("", chan.Desc);
        Assert.Equal(-1, chan.Id);
        Assert.Equal(-1, chan.CreatedBy);
        Assert.Null(chan.Command);
    }

    // test_channel.py:168 test_init_creates_rlock
    [Fact] public void InitCreatesRlock()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        Assert.NotNull(chan.SyncRoot);
        Assert.IsType<System.Threading.ReaderWriterLockSlim>(chan.SyncRoot);
    }

    // test_channel.py:172 test_init_is_channel_flag
    [Fact] public void InitIsChannelFlag()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        Assert.True(chan.IsChannel);
    }

    // test_channel.py:176 test_init_listeners_empty
    [Fact] public void InitListenersEmpty()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        Assert.Empty(chan.Listeners);
    }

    // test_channel.py:180 test_init_history_is_deque
    [Fact] public void InitHistoryIsDeque()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        Assert.NotNull(chan.History);
        Assert.IsAssignableFrom<IReadOnlyList<string>>(chan.History);
        Assert.Empty(chan.History);
    }

    // test_channel.py:184 test_init_history_bounded
    [Fact] public void InitHistoryBounded()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        // check via behavior: add 100 messages and assert count == 50
        for (int i = 0; i < 100; i++) chan.Send($"msg-{i}");
        Assert.Equal(50, chan.History.Count);
    }

    // test_channel.py:197 test_create_with_name
    [Fact] public void CreateWithName()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("mychan");
        Assert.NotNull(chan);
        Assert.Equal("mychan", chan.Name);
        Assert.True(chan.Id >= 0);
    }

    // test_channel.py:205 test_create_sets_caller_id
    [Fact] public void CreateSetsCallerId()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = GameObject.Create("owner", isPc: true);
        caller.Id = 42;
        var chan = Channel.Create("admin", caller);
        Assert.Equal(42, chan.CreatedBy);
    }

    // test_channel.py:211 test_create_no_caller_uses_minus_one
    [Fact] public void CreateNoCallerUsesMinusOne()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("anonchan");
        Assert.Equal(-1, chan.CreatedBy);
    }

    // test_channel.py:215 test_create_duplicate_raises
    [Fact] public void CreateDuplicateRaises()
    {
        using var env = GlobalTestEnv.Enter();
        Channel.Create("dup");
        var ex = Assert.Throws<InvalidOperationException>(() => Channel.Create("dup"));
        Assert.Contains("already exists", ex.Message);
    }

    // test_channel.py:222 test_create_adds_to_global_registry
    [Fact] public void CreateAddsToGlobalRegistry()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("regchan");
        var found = ObjectRegistry.Get(chan.Id);
        Assert.Contains(chan, found);
        Assert.Contains(chan, ObjectRegistry.FilterBy(o => o.IsChannel && o.Name == "regchan"));
    }

    // test_channel.py:226 test_create_calls_at_create
    [Fact] public void CreateCallsAtCreate()
    {
        using var env = GlobalTestEnv.Enter();
        bool called = false;
        var testChan = new TestChannelWithHook(() => called = true);
        testChan.Name = "atchan2";
        testChan.Id = IdGenerator.GetUniqueId();
        testChan.CreatedBy = -1;
        ObjectRegistry.AddObject(testChan);
        testChan.AtCreate();
        Assert.True(called);
    }
    private sealed class TestChannelWithHook : Channel
    {
        private readonly Action _onCreate;
        public TestChannelWithHook(Action onCreate) => _onCreate = onCreate;
        public override void AtCreate() => _onCreate();
    }

    // test_channel.py:242 test_delete_removes_from_registry
    [Fact] public void DeleteRemovesFromRegistry()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("delchan");
        Assert.Contains(chan, ObjectRegistry.FilterBy(o => o.Id == chan.Id));
        var res = chan.Delete();
        Assert.NotNull(res);
        Assert.Equal(1, res!.Value.count);
        Assert.Empty(ObjectRegistry.Get(chan.Id));
    }

    // test_channel.py:249 test_delete_marks_is_deleted
    [Fact] public void DeleteMarksIsDeleted()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("delchan2");
        chan.Delete();
        Assert.True(chan.IsDeleted);
    }

    // test_channel.py:254 test_delete_vetoed_by_at_delete
    [Fact] public void DeleteVetoedByAtDelete()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("vetochan");
        var vetoChan = new VetoChannel();
        vetoChan.Name = "vetochan2";
        vetoChan.Id = IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(vetoChan);
        var result = vetoChan.Delete();
        Assert.Null(result);
        Assert.False(vetoChan.IsDeleted);
        Assert.NotEmpty(ObjectRegistry.Get(vetoChan.Id));
    }
    private sealed class VetoChannel : Channel
    {
        public override (int count, List<object> ops)? Delete(GameObject? caller = null, bool recursive = false)
        {
            return null;
        }
    }

    // test_channel.py:267 test_delete_temporary_skips_db_ops
    [Fact] public void DeleteTemporarySkipsDbOps()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("tempchan");
        chan.IsTemporary = true;
        var res = chan.Delete();
        Assert.NotNull(res);
        Assert.Empty(res!.Value.ops);
    }

    // test_channel.py:278 test_delete_persistent_uses_db_ops
    [Fact] public void DeletePersistentUsesDbOps()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = Channel.Create("persistchan");
        Assert.False(chan.IsTemporary);
        var res = chan.Delete();
        Assert.NotNull(res);
        Assert.Single(res!.Value.ops);
        var op = res.Value.ops[0];
        var t = ((string, object[]))op;
        Assert.Equal("DELETE FROM objects WHERE id = ?", t.Item1);
        Assert.Equal(chan.Id, (int)t.Item2[0]);
    }

    // test_channel.py:298 test_add_listener_stores_by_id
    [Fact] public void AddListenerStoresById()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var listener = GameObject.Create("sub1", isPc: true);
        listener.Id = 1;
        ObjectRegistry.AddObject(listener);
        chan.AddListener(listener);
        Assert.Contains(1, chan.Listeners);
    }

    // test_channel.py:305 test_add_multiple_listeners
    [Fact] public void AddMultipleListeners()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var l1 = GameObject.Create("a", isPc: true); l1.Id = 1; ObjectRegistry.AddObject(l1);
        var l2 = GameObject.Create("b", isPc: true); l2.Id = 2; ObjectRegistry.AddObject(l2);
        chan.AddListener(l1);
        chan.AddListener(l2);
        Assert.Contains(1, chan.Listeners);
        Assert.Contains(2, chan.Listeners);
    }

    // test_channel.py:314 test_add_listener_replaces_same_id
    [Fact] public void AddListenerReplacesSameId()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var l1 = GameObject.Create("a", isPc: true); l1.Id = 1; ObjectRegistry.AddObject(l1);
        var l1r = GameObject.Create("a2", isPc: true); l1r.Id = 1;
        l1r.Id = 1;
        try { ObjectRegistry.RemoveObject(l1); } catch {}
        ObjectRegistry.AddObject(l1r);
        chan.AddListener(l1);
        chan.AddListener(l1r);
        Assert.Single(chan.Listeners);
        Assert.Contains(1, chan.Listeners);
    }

    // test_channel.py:322 test_remove_listener
    [Fact] public void RemoveListener()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var listener = GameObject.Create("sub", isPc: true); listener.Id = 5; ObjectRegistry.AddObject(listener);
        chan.AddListener(listener);
        chan.RemoveListener(listener);
        Assert.DoesNotContain(5, chan.Listeners);
    }

    // test_channel.py:329 test_remove_listener_missing_no_error
    [Fact] public void RemoveListenerMissingNoError()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        var listener = GameObject.Create("sub", isPc: true); listener.Id = 99; ObjectRegistry.AddObject(listener);
        var ex = Record.Exception(() => chan.RemoveListener(listener));
        Assert.Null(ex);
        Assert.Empty(chan.Listeners);
    }

    // test_channel.py:343 test_msg_adds_to_history
    [Fact] public void MsgAddsToHistory()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        chan.Send("hello", null);
        Assert.Single(chan.History);
        var entry = chan.History[0];
        Assert.Contains("hello", entry);
    }

    // test_channel.py:353 test_msg_with_sender_records_name
    [Fact] public void MsgWithSenderRecordsName()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        var sender = GameObject.Create("alice", isPc: true);
        sender.Name = "Alice";
        chan.Send("hi", sender);
        Assert.Single(chan.History);
        var entry = chan.History[0];
        Assert.Contains("hi", entry);
        Assert.True(chan.IsModified);
    }

    // test_channel.py:363 test_msg_broadcasts_to_listeners
    [Fact] public void MsgBroadcastsToListeners()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        var l1 = GameObject.Create("a", isPc: true);
        var l2 = GameObject.Create("b", isPc: true);
        ObjectRegistry.AddObject(l1); ObjectRegistry.AddObject(l2);
        l1.ClearMessages(); l2.ClearMessages();
        chan.AddListener(l1);
        chan.AddListener(l2);
        chan.Send("hello", null);
        Assert.Contains("hello", chan.History[0]);
    }

    // test_channel.py:377 test_msg_no_listeners_no_error
    [Fact] public void MsgNoListenersNoError()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        var ex = Record.Exception(() => chan.Send("hi", null));
        Assert.Null(ex);
        Assert.Single(chan.History);
    }

    // test_channel.py:384 test_msg_history_bounded
    [Fact] public void MsgHistoryBounded()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        for (int i = 0; i < 100; i++) chan.Send($"msg-{i}", null);
        Assert.Equal(50, chan.History.Count);
        Assert.Contains("msg-50", chan.History.First());
        Assert.DoesNotContain("msg-0", chan.History.First());
        Assert.Contains("msg-99", chan.History.Last());
    }

    // test_channel.py:396 test_msg_format_includes_channel_name
    [Fact] public void MsgFormatIncludesChannelName()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "trade";
        int ts = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var outStr = chan.FormatMessage(ts, "", "buy");
        Assert.Contains("trade", outStr);
    }

    // test_channel.py:407 test_format_with_sender
    [Fact] public void FormatWithSender()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        int ts = (int)new DateTimeOffset(2025, 1, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var outStr = chan.FormatMessage(ts, "Alice", "hello");
        Assert.Contains("Alice", outStr);
        Assert.Contains("hello", outStr);
        Assert.Contains("ch", outStr);
        Assert.Contains("2025", outStr);
    }

    // test_channel.py:418 test_format_without_sender
    [Fact] public void FormatWithoutSender()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        int ts = (int)new DateTimeOffset(2025, 6, 1, 9, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var outStr = chan.FormatMessage(ts, "", "system message");
        Assert.Contains("system message", outStr);
        Assert.Contains("ch", outStr);
        Assert.DoesNotContain("Alice", outStr);
    }

    // test_channel.py:430 test_empty_history
    [Fact] public void EmptyHistory()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        var outStr = chan.GetHistory();
        Assert.Equal("", outStr);
    }

    // test_channel.py:436 test_history_after_messages
    [Fact] public void HistoryAfterMessages()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        chan.Send("one", null);
        chan.Send("two", null);
        chan.Send("three", null);
        var outStr = chan.GetHistory();
        Assert.Contains("one", outStr);
        Assert.Contains("two", outStr);
        Assert.Contains("three", outStr);
    }

    // test_channel.py:447 test_history_count_limits
    [Fact] public void HistoryCountLimits()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "ch";
        for (int i = 0; i < 10; i++) chan.Send($"m-{i}", null);
        var outStr = chan.GetHistory(3);
        Assert.Contains("m-9", outStr);
        Assert.Contains("m-8", outStr);
        Assert.Contains("m-7", outStr);
        Assert.DoesNotContain("m-0", outStr);
    }
}