using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using System.Collections.Concurrent;

namespace Atheriz.Core.Tests;

// Single canonical thread-safe connection for tests.
// Uses lock-free ConcurrentQueue / ConcurrentBag for Sent/Received isolation.
public class TestConnection : BaseConnection
{
    private readonly ConcurrentQueue<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> _sentQueue = new();
    private readonly List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> _sentSnapshotLock = new();
    private readonly object _sentLock = new();

    // Legacy Sent list (snapshot, thread-safe via lock)
    public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> Sent
    {
        get { lock (_sentLock) return _sentSnapshotLock.ToList(); }
    }

    // New spec: ConcurrentBag of (Cmd, Json) + ConcurrentQueue Received
    public ConcurrentBag<(string Cmd, string Json)> SentCommandsBag { get; } = new();
    public List<string> SentCommands
    {
        get { lock (_sentLock) return _sentSnapshotLock.Select(s => s.Cmd).ToList(); }
    }

    public ConcurrentQueue<string> Received { get; } = new();
    public bool Closed { get; private set; }

    public TestConnection(string? sessionId = "test_conn") : base(sessionId) { }

    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        var a = args ?? new List<object?>();
        var k = kwargs ?? new Dictionary<string, object?>();
        lock (_sentLock) _sentSnapshotLock.Add((cmd, a, k));
        _sentQueue.Enqueue((cmd, a, k));
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(new object[] { cmd, a, k });
            SentCommandsBag.Add((cmd, json));
        }
        catch { SentCommandsBag.Add((cmd, "")); }
    }

    public override void Close()
    {
        Closed = true;
        lock (_sentLock) _sentSnapshotLock.Add(("__closed__", new List<object?>(), new Dictionary<string, object?>()));
    }

    public void EnqueueReceived(string text) => Received.Enqueue(text);
    public string? DequeueReceived() => Received.TryDequeue(out var v) ? v : null;
    public void ClearSent() { lock (_sentLock) _sentSnapshotLock.Clear(); while (_sentQueue.TryDequeue(out _)) { } while (SentCommandsBag.TryTake(out _)) { } }
    public IReadOnlyList<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> SentSnapshot
        => Sent;
}

// Backward-compat aliases — deprecated triplication unified
public sealed class FakeConnection : TestConnection
{
    public FakeConnection(string? sessionId = "test_conn") : base(sessionId) { }
}

// Provide compat types for ConcreteConn/BareConn/TestConn triplication (used in PortedConnectionTests, NetworkTests)
public sealed class ConcreteConn : TestConnection
{
    public ConcreteConn(string? sessionId = null) : base(sessionId) { }
}
public sealed class BareConn : TestConnection
{
    public BareConn(string? sid = null) : base(sid)
    {
    }
    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null) => throw new NotImplementedException();
    public override void Close() => throw new NotImplementedException();
}
public sealed class TestConn : TestConnection
{
    public TestConn(string id, string host) : base(id) { ClientHost = host; }
}

// Port of atheriz/tests/fakes.py:64 FakeSession
public sealed class FakeSession
{
    public List<(object?[] Args, Dictionary<string, object?> Kwargs)> Msgs { get; } = new(); // Port 93
    public List<string> Prompts { get; } = new(); // Port 94
    private readonly Queue<string> _promptResponses;
    public bool ScreenReader { get; set; }
    public bool AtDisconnectCalled { get; private set; }

    public FakeSession(IEnumerable<string>? promptResponses = null)
    {
        _promptResponses = new Queue<string>(promptResponses ?? Enumerable.Empty<string>());
    }

    public void Msg(params object?[] args) => Msgs.Add((args, new Dictionary<string, object?>())); // Port 97

    public Task<string> Prompt(string text) // Port 100 async prompt
    {
        Prompts.Add(text);
        var resp = _promptResponses.Count > 0 ? _promptResponses.Dequeue() : "";
        return Task.FromResult(resp);
    }

    public void AtDisconnect() => AtDisconnectCalled = true; // Port fake at_disconnect MagicMock
}

// Port of atheriz/tests/conftest.py:515 capture_atheriz_log + helper
// Fixed to use lock + AsyncLocal routing to avoid process-global race.
public sealed class CaptureAtherizLog : IDisposable
{
    private static readonly object _captureLock = new();
    private static readonly AsyncLocal<StringWriter?> _asyncWriter = new();
    private static StringWriter? _globalWriter;
    private sealed class RoutingWriter : TextWriter
    {
        private readonly TextWriter _fallback;
        public RoutingWriter(TextWriter fallback) => _fallback = fallback;
        public override System.Text.Encoding Encoding => _fallback.Encoding;
        public override void Write(string? value)
        {
            var w = _asyncWriter.Value ?? _globalWriter;
            if (w != null) { lock (w) w.Write(value); return; }
            _fallback.Write(value);
        }
        public override void WriteLine(string? value)
        {
            var w = _asyncWriter.Value ?? _globalWriter;
            if (w != null) { lock (w) w.WriteLine(value); return; }
            _fallback.WriteLine(value);
        }
        public override void Write(char value)
        {
            var w = _asyncWriter.Value ?? _globalWriter;
            if (w != null) { lock (w) w.Write(value); return; }
            _fallback.Write(value);
        }
        public override void WriteLine() { WriteLine(string.Empty); }
        public override System.Threading.Tasks.Task WriteAsync(string? value) { Write(value); return System.Threading.Tasks.Task.CompletedTask; }
        public override System.Threading.Tasks.Task WriteLineAsync(string? value) { WriteLine(value); return System.Threading.Tasks.Task.CompletedTask; }
    }
    private static TextWriter? _routingInstalled;
    private static TextWriter _origError = Console.Error;
    private readonly StringWriter _writer;
    private bool _locked;

    public CaptureAtherizLog()
    {
        Monitor.Enter(_captureLock);
        _locked = true;
        _writer = new StringWriter();
        _asyncWriter.Value = _writer;
        _globalWriter = _writer;
        if (_routingInstalled == null)
        {
            _origError = Console.Error;
            _routingInstalled = new RoutingWriter(_origError);
            Console.SetError(_routingInstalled);
        }
        else
        {
            // Ensure routing installed still points to our writer via _globalWriter
            // No need to reinstall; RoutingWriter already checks _globalWriter
        }
    }

    public string Read()
    {
        _writer.Flush();
        lock (_writer) return _writer.ToString();
    }

    public void Dispose()
    {
        _asyncWriter.Value = null;
        if (_globalWriter == _writer) _globalWriter = null;
        if (_locked) { Monitor.Exit(_captureLock); _locked = false; }
        // Do not dispose immediately if other thread may still write; keep for read but dispose on next capture
        // Keep writer alive for a moment; dispose after lock released
        _writer.Dispose();
    }
}

// Port of fakes.py:171 make_object helper
public static class FakesHelper
{
    public static GameObject CreateObject(string name = "foo", Action<GameObject>? init = null)
    {
        var obj = GameObject.Create(name);
        init?.Invoke(obj);
        return obj;
    }

    [Obsolete("Use CreateObject(name, init) instead — reflection path is deprecated")]
    public static GameObject MakeObject(string name = "foo", params (string key, object? val)[] attrs)
    {
        var obj = GameObject.Create(name);
        foreach (var (k, v) in attrs)
        {
            var prop = typeof(GameObject).GetProperty(k);
            if (prop != null && prop.CanWrite) try { prop.SetValue(obj, v); } catch { }
            else
            {
                var field = typeof(GameObject).GetField($"_{char.ToLowerInvariant(k[0])}{k.Substring(1)}", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (field != null) try { field.SetValue(obj, v); } catch { }
            }
        }
        return obj;
    }
}
