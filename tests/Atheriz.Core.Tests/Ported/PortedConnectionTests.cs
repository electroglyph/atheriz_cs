// Port of atheriz/tests/test_connection.py + test_connection_manager.py (faithful)
using System.Diagnostics;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using System.Text.Json;
using System.Threading;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedConnectionTests
{
    private sealed class ConcreteConn : BaseConnection
    {
        public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> Sent { get; } = new();
        public bool Closed { get; private set; }
        public ConcreteConn(string? sessionId = null) : base(sessionId) { }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
        {
            Sent.Add((cmd, args ?? new List<object?>(), kwargs ?? new Dictionary<string, object?>()));
        }
        public override void Close() => Closed = true;
    }
    private sealed class BareConn : BaseConnection
    {
        public BareConn(string? sid = null) : base(sid) { }
        public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) => throw new NotImplementedException();
        public override void Close() => throw new NotImplementedException();
    }
    private static bool Wait(Func<bool> cond, int timeoutMs = 2000) => PortedHelpers.WaitFor(cond, timeoutMs);

    // ----- TestInit -----
    [Fact] public void SetsSessionId()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn(sessionId: "abc");
        Assert.Equal("abc", c.SessionId);
    }
    [Fact] public void SessionIdNoneDefault()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.Null(c.SessionId);
    }
    [Fact] public void CreatesSession()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.IsType<Session>(c.Session);
    }
    [Fact] public void SessionLinksToConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.Same(c, c.Session.Connection);
    }
    [Fact] public void InitializesLoop()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.Null(c.Loop);
    }
    [Fact] public void RecordsThreadId()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.Equal(Environment.CurrentManagedThreadId, c.ThreadId);
    }
    [Fact] public void LockIsRlock()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.NotNull(c.Lock);
        // RLock is re-entrant
        bool e1 = System.Threading.Monitor.TryEnter(c.Lock, 0);
        Assert.True(e1);
        bool e2 = System.Threading.Monitor.TryEnter(c.Lock, 0);
        Assert.True(e2);
        if (e2) System.Threading.Monitor.Exit(c.Lock);
        if (e1) System.Threading.Monitor.Exit(c.Lock);
    }
    [Fact] public void FailedLoginAttemptsStartsZero()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        Assert.Equal(0, c.FailedLoginAttempts);
    }

    // ----- TestSendCommand / TestClose -----
    [Fact] public void SendCommandNotImplementedInBase()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new BareConn();
        Assert.Throws<NotImplementedException>(() => c.SendCommand("text", new List<object?>{"hello"}));
    }
    [Fact] public void CloseNotImplementedInBase()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new BareConn();
        Assert.Throws<NotImplementedException>(() => c.Close());
    }

    // ----- TestMsg -----
    [Fact] public void MsgNoArgsNoKwargsNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg();
        Assert.Empty(c.Sent);
    }
    [Fact] public void MsgSimpleText()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg("hello");
        Assert.Single(c.Sent);
        var (cmd, args, kwargs) = c.Sent[0];
        Assert.Equal("text", cmd);
        Assert.Contains("hello", args[0]?.ToString());
        Assert.EndsWith("\r\n", args[0]?.ToString());
    }
    [Fact] public void MsgTextWithScreenreaderStripsAnsi()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Session.ScreenReader = true;
        c.Msg("\x1b[31mred\x1b[0m");
        var val = c.Sent[0].Args[0]?.ToString() ?? "";
        // Use ordinal check to avoid culture-sensitive false positives (ESC is ignorable in culture-sensitive IndexOf)
        Assert.False(val.Contains("\x1b"), $"ANSI not stripped, val={System.Text.Json.JsonSerializer.Serialize(val)}");
    }
    [Fact] public void MsgTextWithoutScreenreaderKeepsAnsi()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Session.ScreenReader = false;
        c.Msg("\x1b[31mred\x1b[0m");
        var val = c.Sent[0].Args[0]?.ToString() ?? "";
        Assert.True(val.Contains("\x1b"), $"ANSI should be preserved, val={System.Text.Json.JsonSerializer.Serialize(val)}");
    }
    [Fact] public void MsgTextKwarg()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.MsgKw(new Dictionary<string, object?>{["text"]="hi"});
        var (cmd, args, kwargs) = c.Sent[0];
        Assert.Equal("text", cmd);
        Assert.Contains("hi", args[0]?.ToString());
    }
    [Fact] public void MsgNonTextKwargBecomesCommand()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.MsgKw(new Dictionary<string, object?>{["prompt"]="> "});
        var (cmd, args, kwargs) = c.Sent[0];
        Assert.Equal("prompt", cmd);
        Assert.Equal("> ", args[0]);
    }
    [Fact] public void MsgNonTextKwargWithText()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.MsgKw(new Dictionary<string, object?>{["text"]="hello", ["prompt"]="> "});
        var (cmd, args, kwargs) = c.Sent[0];
        Assert.Equal("text", cmd);
        Assert.Contains("hello", args[0]?.ToString());
        Assert.Equal("> ", kwargs["prompt"]);
    }
    [Fact] public void MsgTextThenPositional()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg((object)"hello", (object)"world");
        var (cmd, args, _) = c.Sent[0];
        Assert.Equal("text", cmd);
        Assert.EndsWith("\r\n", args[0]?.ToString());
        Assert.Contains("hello", args[0]?.ToString());
    }

    // ----- TestFakeConnectionFromFakes -----
    [Fact] public void FakeInheritsBase()
    {
        using var env = GlobalTestEnv.Enter();
        var fc = new FakeConnection();
        Assert.IsAssignableFrom<BaseConnection>(fc);
    }
    [Fact] public void FakeRecordsMsgs()
    {
        using var env = GlobalTestEnv.Enter();
        var fc = new FakeConnection();
        fc.Msg("hello");
        Assert.Single(fc.Sent);
        Assert.Equal("text", fc.Sent[0].Cmd);
        Assert.Contains("hello", fc.Sent[0].Args[0]?.ToString());
    }
    [Fact] public void FakeClose()
    {
        using var env = GlobalTestEnv.Enter();
        var fc = new FakeConnection();
        fc.Close();
        Assert.True(fc.Closed);
    }

    // ----- TestIntegration -----
    [Fact] public void ConnectionLifecycle()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn(sessionId: "test");
        c.Msg("Welcome!");
        Assert.Single(c.Sent);
        c.Close();
        Assert.True(c.Closed);
    }
    [Fact] public void MultipleMsgs()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg("one"); c.Msg("two"); c.Msg("three");
        Assert.Equal(3, c.Sent.Count);
        foreach (var (cmd, args, _) in c.Sent) Assert.EndsWith("\r\n", args[0]?.ToString());
    }
    [Fact] public void MsgNoDoubleNewline()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg("hello\r\n");
        Assert.Equal("hello\r\n", c.Sent[0].Args[0]);
        c.Msg("hello\n");
        Assert.Equal("hello\n", c.Sent[1].Args[0]);
        c.Msg("hello");
        Assert.Equal("hello\r\n", c.Sent[2].Args[0]);
    }
    [Fact] public void MsgNonStrTextCoerced()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        c.Msg(123);
        var (cmd, args, _) = c.Sent[0];
        Assert.Equal("text", cmd);
        Assert.Equal("123\r\n", args[0]);
    }
    [Fact] public void MsgFalsyTextKwargNoCrash()
    {
        using var env = GlobalTestEnv.Enter();
        var c = new ConcreteConn();
        var ex = Record.Exception(() => c.MsgKw(new Dictionary<string, object?>{["text"]=""}));
        Assert.Null(ex);
    }

    // ----- ConnectionManager part 1 -----
    private ConnectionManager MakeManager(AtherizSettings? settings = null, AsyncThreadPool? pool = null) => PortedHelpers.MakeManager(settings, pool);

    [Fact] public void InitCreatesEmptyState()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        Assert.Empty(mgr.ConnectionsSnapshot);
        Assert.Equal(0, mgr.ConnectionCount);
        try { mgr.Atp.Stop(wait:false); } catch {}
    }
    [Fact] public void InitLockIsRlock()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var f = typeof(ConnectionManager).GetField("_lock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var l = f.GetValue(mgr)!;
        Assert.IsType<System.Threading.ReaderWriterLockSlim>(l);
        var rwl = (System.Threading.ReaderWriterLockSlim)l;
        Assert.Equal(LockRecursionPolicy.SupportsRecursion, rwl.RecursionPolicy);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void InitRegistersHandlersFromInputFuncs()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        Assert.True(mgr.GetHandlersCountForTest() > 0);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void GenerateFirstId()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var id = mgr.GenerateConnectionId();
        Assert.Equal("conn_1", id);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void GeneratesIncrement()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        Assert.Equal("conn_1", mgr.GenerateConnectionId());
        Assert.Equal("conn_2", mgr.GenerateConnectionId());
        Assert.Equal("conn_3", mgr.GenerateConnectionId());
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void GeneratesUnique()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var ids = new HashSet<string>();
        for (int i=0;i<20;i++) ids.Add(mgr.GenerateConnectionId());
        Assert.Equal(20, ids.Count);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void RegisterConnection_Registers()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c = new FakeConnection();
        Assert.True(mgr.RegisterConnection("c1", c));
        Assert.Same(c, mgr.ConnectionsSnapshot["c1"]);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void RegisterIncrementsCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        Assert.Equal(0, mgr.ConnectionCount);
        mgr.RegisterConnection("c1", new FakeConnection());
        Assert.Equal(1, mgr.ConnectionCount);
        mgr.RegisterConnection("c2", new FakeConnection());
        Assert.Equal(2, mgr.ConnectionCount);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void RegisterOverwritesExisting()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c1 = new FakeConnection(); var c2 = new FakeConnection();
        mgr.RegisterConnection("c1", c1);
        mgr.RegisterConnection("c1", c2);
        Assert.Same(c2, mgr.ConnectionsSnapshot["c1"]);
        Assert.Equal(1, mgr.ConnectionCount);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void DisconnectRemovesConnection()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c = new FakeConnection();
        mgr.RegisterConnection("c1", c);
        mgr.Disconnect(c);
        Assert.DoesNotContain("c1", mgr.ConnectionsSnapshot.Keys);
        mgr.Atp.Stop(wait:false);
    }
    private sealed class MockSession : Session
    {
        public int CallCount;
        public MockSession(BaseConnection? conn=null) : base(conn) {}
        public override void AtDisconnect() { CallCount++; base.AtDisconnect(); }
    }
    [Fact] public void DisconnectCallsSessionAtDisconnect()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c = new FakeConnection();
        // Replace session with mock that records AtDisconnect (faithful to c.session.at_disconnect = MagicMock())
        var mockSess = new MockSession(c);
        // Copy timing and puppet state if needed
        mockSess.ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1;
        // Inject via reflection (BaseConnection.Session is get-only)
        var sessField = typeof(BaseConnection).GetField("<Session>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        sessField!.SetValue(c, mockSess);
        // Ensure mockSess.Connection points to c
        mockSess.Connection = c;
        mgr.RegisterConnection("c1", c);
        mgr.Disconnect(c);
        Assert.True(Wait(() => mockSess.CallCount == 1), $"expected AtDisconnect CallCount 1, got {mockSess.CallCount}");
        Assert.Equal(1, mockSess.CallCount);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void DisconnectNoSession()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c = new FakeConnection();
        // Python sets c.session = None — set via reflection to null
        var sessField = typeof(BaseConnection).GetField("<Session>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        sessField!.SetValue(c, null);
        mgr.RegisterConnection("c1", c);
        var ex = Record.Exception(() => mgr.Disconnect(c));
        Assert.Null(ex);
        // Restore for cleanup (not needed as env will dispose)
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void DisconnectDecrementsCount()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c1 = new FakeConnection(); var c2 = new FakeConnection();
        mgr.RegisterConnection("c1", c1); mgr.RegisterConnection("c2", c2);
        Assert.Equal(2, mgr.ConnectionCount);
        mgr.Disconnect(c1);
        Assert.Equal(1, mgr.ConnectionCount);
        mgr.Atp.Stop(wait:false);
    }
    [Fact] public void DisconnectUnregisteredNoop()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = MakeManager();
        var c = new FakeConnection();
        mgr.Disconnect(c);
        Assert.Equal(0, mgr.ConnectionCount);
        mgr.Atp.Stop(wait:false);
    }
}
file sealed class TrackingGameObject : GameObject
{
    public static bool DisconnectCalled;
    public static void Reset() => DisconnectCalled = false;
    public TrackingGameObject(string name) { Name = name; }
    public override void AtDisconnect() { DisconnectCalled = true; base.AtDisconnect(); }
}
file static class ConnectionManagerTestExtensions2
{
    public static int GetHandlersCountForTest(this ConnectionManager mgr)
    {
        var f = typeof(ConnectionManager).GetField("_messageHandlers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var d = (System.Collections.IDictionary)f!.GetValue(mgr)!;
        return d.Count;
    }
    public static void AtPoolStop(this ConnectionManager mgr)
    {
        try { mgr.Atp.Stop(wait:false); } catch {}
    }
}
