#pragma warning disable xUnit1031 // CaptureAtherizLog constructor takes the capture semaphore synchronously
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using System.Collections.Concurrent;

namespace Atheriz.Core.Tests;

// Single canonical thread-safe connection for tests: one tuple store plus
// the Received queue. (Former TestConnection/ConcreteConn/TestConn/BareConn
// aliases and the parallel bag/string/queue stores deleted 2026-09-25 —
// every consumer reads Sent/Received/Closed.)
public class TestConnection : BaseConnection
{
    private readonly object _sentLock = new();
    private readonly List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> _sent = new();

    public List<(string Cmd, List<object?> Args, Dictionary<string, object?> Kwargs)> Sent
    {
        get { lock (_sentLock) return _sent.ToList(); }
    }

    public ConcurrentQueue<string> Received { get; } = new();
    public bool Closed { get; private set; }

    // Failure injection for delivery-failure paths (the one use the old
    // throwing BareConn served: FailedMapSend_LeavesLastMapTime).
    public bool ThrowOnSend { get; set; }

    public TestConnection(string? sessionId = "test_conn") : base(sessionId) { }

    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        if (ThrowOnSend) throw new InvalidOperationException("ThrowOnSend: simulated delivery failure.");
        var a = args ?? new List<object?>();
        var k = kwargs ?? new Dictionary<string, object?>();
        lock (_sentLock) _sent.Add((cmd, a, k));
    }

    public override void Close()
    {
        Closed = true;
        lock (_sentLock) _sent.Add(("__closed__", new List<object?>(), new Dictionary<string, object?>()));
    }

    public void ClearSent() { lock (_sentLock) _sent.Clear(); }
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
    // Semaphore (not Monitor): capture is used across awaits, and Monitor is
    // thread-affine — disposing on a different thread than construction threw
    // SynchronizationLockException. A semaphore is thread-agnostic.
    private static readonly SemaphoreSlim _captureLock = new(1, 1);
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
        _captureLock.Wait();
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
        if (_locked) { _locked = false; try { _captureLock.Release(); } catch { } }
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
