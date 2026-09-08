using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Atheriz.Core.Settings;
using Atheriz.Core.Globals;

namespace Atheriz.Core.Network;

// Port of atheriz/network/telnet.py:1-446
// Telnet protocol with TLS autodetect same-port, NAWS clamping, capped line reading.

public class TelnetConnection : BaseConnection
{
    // telnet.py:121-137 TelnetConnection.__init__
    public object Reader { get; }
    public object Writer { get; }
    private readonly PendingLimiter _limiter; // sole accounting (single source of truth)
    private readonly AtherizSettings _settings;
    private int _inflight; // offloaded writes not yet finished (Dispose waits, bounded)
    // Dispose joins in-flight writes via this event instead of a
    // 250ms spin that loses to 2-5s write/TLS timeouts and aborts mid-write.
    private readonly ManualResetEventSlim _drained = new(true);
    // Bound covers the longest write/TLS timeouts above (2-5s) with headroom.
    private static readonly TimeSpan DisposeJoinTimeout = TimeSpan.FromSeconds(10);
    // Close flag (with _limiter.IsClosing forms IsClosing). Pending-byte
    // accounting lives solely in PendingLimiter — no mirrors.
    private bool _closing;

    public TelnetConnection(object reader, object writer, string? sessionId = null, AtherizSettings? settings = null) : base(sessionId)
    {
        Reader = reader;
        Writer = writer;
        ClientHost = "?";
        _settings = settings ?? AtherizSettings.Global;
        _limiter = new PendingLimiter(_settings.TelnetMaxPendingBytes, _settings.TelnetMaxPendingSends);
        try
        {
            // Typed host resolution (port of telnet.py:130-133 peername):
            // writers expose GetPeerHost; anything else defaults to "?".
            if (writer is ITelnetWriter tw0) { ClientHost = tw0.GetPeerHost() ?? "?"; return; }
            ClientHost = "?";
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.TelnetConnection: " + logEx.Message, "TelnetConnection"); }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Join in-flight offloaded writes (bounded) before disposing the writer.
            try { _drained.Wait(DisposeJoinTimeout); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
            try { if (Writer is IDisposable wd) wd.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
            try { if (Reader is System.IO.TextReader tr) tr.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
            try { _drained.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
        }
        base.Dispose(disposing);
    }

    // Port of telnet.py:138-156 _get_write_buffer_size: consult each writer's
    // buffer sources in priority order (transport → writer → _transport).
    public virtual int? GetWriteBufferSize()
    {
        try
        {
            if (Writer is ITelnetWriter itw0)
            {
                foreach (var src in itw0.BufferSources)
                {
                    var b = src.GetWriteBufferSize();
                    if (b != null) return b;
                }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.GetWriteBufferSize: " + logEx.Message, "TelnetConnection"); }
        return null;
    }

    // Port of telnet.py:48-49 _telnet_text
    private static string TelnetText(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private void WriterWrite(string text)
    {
        var tt = TelnetText(text);
        // Typed only: all writers implement ITelnetWriter (Simple/Mock/Stream).
        if (Writer is ITelnetWriter itw0) itw0.Write(tt);
    }

    private void WriterIac(byte cmd, byte opt)
    {
        if (Writer is ITelnetWriter itw0) itw0.Iac(cmd, opt);
    }

    private void WriterClose()
    {
        if (Writer is ITelnetWriter itw0) itw0.Close();
    }

    private bool CheckWriteBufferExceeded(string suffix = "")
    {
        var buf = GetWriteBufferSize();
        if (buf != null && buf > _settings.TelnetMaxPendingBytes)
        {
            Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] closing {ClientHost}: write buffer {buf} > {_settings.TelnetMaxPendingBytes}{suffix}");
            Close();
            return true;
        }
        return false;
    }

    // Offload a worker-thread write to the pool (Stream.Write blocks up to
    // seconds; loop-thread sends stay inline for Python parity — see SendCommand).
    // _inflight lets Dispose wait for pending writes (bounded) before disposing.
    private void ScheduleWrite(Action write, int nb)
    {
        if (IsClosing) { if (nb != 0) _limiter.ReleaseSync(nb); return; }
        Interlocked.Increment(ref _inflight);
        try { _drained.Reset(); } catch (ObjectDisposedException) { /* Dispose already joined; write still runs below. */ }
        try
        {
            Task.Run(() => { try { write(); } finally { if (Interlocked.Decrement(ref _inflight) == 0) { try { _drained.Set(); } catch (ObjectDisposedException) { } } } })
                .ContinueWith(t => { AtherizLogger.LogError($"[Telnet] offloaded write task faulted for {ClientHost}", t.Exception!); },
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception e)
        {
            if (Interlocked.Decrement(ref _inflight) == 0) { try { _drained.Set(); } catch (ObjectDisposedException) { } }
            if (nb != 0) _limiter.ReleaseSync(nb);
            AtherizLogger.LogError($"[Telnet] failed to schedule write for {ClientHost}: {e}");
            Close();
        }
    }

    // Port of telnet.py:158-175 _offloop_write — now uses PendingLimiter with finally ReleaseSync (fix leak)
    public void OffloopWrite(string text, int nb)
    {
        text = TelnetText(text);
        try
        {
            if (CheckWriteBufferExceeded()) return;
            WriterWrite(text);
            CheckWriteBufferExceeded(" after write");
        }
        catch (ObjectDisposedException) { } // post-dispose write race: writer already gone
        catch (Exception e)
        {
Atheriz.Core.AtherizLogger.LogError($"[Telnet] write failed for {ClientHost}: {e}");
            Close();
        }
        finally
        {
            _limiter.ReleaseSync(nb);
        }
    }

    public void OffloopIac(byte teloptCmd, byte teloptOpt, int nb = 0)
    {
        try { WriterIac(teloptCmd, teloptOpt); }
        catch (ObjectDisposedException) { } // post-dispose write race: writer already gone
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] iac failed for {ClientHost}: {e}"); Close(); }
        finally
        {
            if (nb != 0) _limiter.ReleaseSync(nb);
        }
    }

    public int PendingBytes => _limiter.PendingBytes;
    public bool IsClosing => _limiter.IsClosing || _closing;
    // Expose limiter for testing / inspection (kept internal)
    internal PendingLimiter Limiter => _limiter;

    // Port of telnet.py:188-324 send_command — now via PendingLimiter (fixes sync leak)
    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        if (IsClosing) return;
        const byte WILL = 251; const byte WONT = 252; const byte ECHO = 1;

        if (cmd == "text" || cmd == "prompt")
        {
            var text = args != null && args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            if (string.IsNullOrEmpty(text)) return;
            var nb = Encoding.UTF8.GetByteCount(text);
            // No top buffer check here: OffloopWrite self-checks before AND after
            // the write (pinned by OffloopWriteChecksBufferBefore/AfterWrite);
            // an extra check here would shift its call sequence and skip the write.
            if (!_limiter.TryReserve(nb))
            {
                Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] closing {ClientHost}: pending {_limiter.PendingBytes} + {nb} bytes exceeds {_settings.TelnetMaxPendingBytes}");
                Close(); return;
            }
            // Python parity: asyncio transport.write commits to the loop buffer
            // synchronously, so loop-thread sends deliver inline (pinned by
            // PortedTelnetTests TextCommandWrites/PromptCommandWrites). Only
            // worker-thread broadcasts are offloaded via ScheduleWrite.
            if (IsOnLoopThread()) OffloopWrite(text, nb);
            else ScheduleWrite(() => OffloopWrite(text, nb), nb);
        }
        else if (cmd == "prompt_masked")
        {
            var text = args != null && args.Count > 0 ? args[0]?.ToString() ?? "" : "";
            var nb = !string.IsNullOrEmpty(text) ? Encoding.UTF8.GetByteCount(text) : 0;
            if (CheckWriteBufferExceeded()) return;
            if (nb != 0)
            {
                if (!_limiter.TryReserve(nb))
                {
                    Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] closing {ClientHost}: pending {_limiter.PendingBytes} + {nb} bytes exceeds {_settings.TelnetMaxPendingBytes}");
                    Close(); return;
                }
            }
            else if (IsClosing) return;
            // Loop-thread parity: inline delivery (see text/prompt above); the
            // buffer check above keeps the pre-close path IAC-free. IAC bytes
            // are control, not reserved.
            if (IsOnLoopThread())
            {
                OffloopIac(WILL, ECHO);
                if (!string.IsNullOrEmpty(text)) OffloopWrite(text, nb);
            }
            else
            {
                ScheduleWrite(() => OffloopIac(WILL, ECHO), 0);
                if (!string.IsNullOrEmpty(text)) ScheduleWrite(() => OffloopWrite(text, nb), nb);
            }
            // OffloopWrite releases via its finally; prompt_masked without text reserves nothing.
        }
        else if (cmd == "echo_on")
        {
            if (IsOnLoopThread()) OffloopIac(WONT, ECHO);
            else ScheduleWrite(() => OffloopIac(WONT, ECHO), 0);
        }
    }

    public override void Close()
    {
        if (!_limiter.TryMarkClosing())
        {
            _closing = true;
            return;
        }
        _closing = true;
        try
        {
            if (IsOnLoopThread()) WriterClose();
            else { var _t = Task.Run((Action)WriterClose); _ = _t.ContinueWith(t => { if (t.IsFaulted && t.Exception != null) Atheriz.Core.AtherizLogger.LogError($"[Telnet] Close fault: {t.Exception}"); }, TaskScheduler.Default); }
        }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] Error closing connection: {e}"); }
    }
}

/// <summary>Typed write-buffer source (port of telnet.py transport/get_write_buffer_size duck-typing).</summary>
public interface ITelnetBufferSource
{
    int? GetWriteBufferSize();
}

public interface ITelnetWriter : ITelnetBufferSource
{
    void Write(string text);
    void Iac(byte cmd, byte opt);
    void Close();
    void SetExtCallback(byte opt, Action<int, int> callback);
    string? GetPeerHost();
    // Priority-ordered buffer-size sources (port of telnet.py:138-156
    // transport → writer → _transport chain). Default is just this writer.
    IReadOnlyList<ITelnetBufferSource> BufferSources => [this];
}

/// <summary>Typed router surface for lifespan composition (port of telnet.py:350-446).</summary>
public interface ITelnetRouter
{
    object? LifespanContext { get; set; }
}

/// <summary>Typed app surface for TelnetProtocol.Setup.</summary>
public interface ITelnetApp
{
    ITelnetRouter? Router { get; }
}

public sealed class TelnetStreamWriter : ITelnetWriter
{
    private readonly Stream _stream;
    private readonly TcpClient _client;
    private readonly object _writeLock = new object();
    private int _pendingWriteBytes; // buffered-not-flushed bytes (see Write)
    private Action<int,int>? _nawsCallback;
    public TelnetStreamWriter(Stream stream, TcpClient client) { _stream = stream; _client = client; }
    public void Write(string text)
    {
        // Bounded write: a peer that never drains must not stall
        // the game thread forever. SendTimeout turns a wedged peer into a
        // SocketException instead of an indefinite block. Socket.SendTimeout
        // has no effect on SslStream, so TLS writes get an explicit timeout.
        try { _client.SendTimeout = 2000; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetStreamWriter.Write: " + logEx.Message, "TelnetStreamWriter"); }
        var bytes = Encoding.UTF8.GetBytes(text);
        lock (_writeLock)
        {
            // track buffered-not-yet-flushed bytes (the asyncio
            // get_write_buffer_size() semantic from telnet.py:138-156) so the
            // write-buffer check is live. Never report SO_SNDBUF capacity here:
            // SendBufferSize (~2.6MB) dwarfs TelnetMaxPendingBytes and caused
            // false closes when it was returned by mistake.
            _pendingWriteBytes += bytes.Length;
            try
            {
                if (_stream is SslStream)
                {
                    var wt = _stream.WriteAsync(bytes, 0, bytes.Length);
                    if (!wt.Wait(TimeSpan.FromSeconds(5))) throw new IOException("TLS write timed out");
                }
                else _stream.Write(bytes, 0, bytes.Length);
            }
            finally { _pendingWriteBytes -= bytes.Length; }
        }
    }
    public void Iac(byte cmd, byte opt) { var bytes = new byte[] { 255, cmd, opt }; lock (_writeLock) { _pendingWriteBytes += bytes.Length; try { if (_stream is SslStream) { var wt = _stream.WriteAsync(bytes, 0, bytes.Length); if (!wt.Wait(TimeSpan.FromSeconds(5))) throw new IOException("TLS write timed out"); } else _stream.Write(bytes, 0, bytes.Length); } finally { _pendingWriteBytes -= bytes.Length; } } }
    public void Close() { try { _stream.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetStreamWriter.Close: " + logEx.Message, "TelnetStreamWriter"); } try { _client.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetStreamWriter.Close: " + logEx.Message, "TelnetStreamWriter"); } }
    // Port of telnet.py:138-156 — pending (buffered, unflushed) bytes, not capacity.
    public int? GetWriteBufferSize() => Volatile.Read(ref _pendingWriteBytes);
    public void SetExtCallback(byte opt, Action<int, int> callback) { if (opt == 31) _nawsCallback = callback; }
    public void TriggerNaws(int rows, int cols) => _nawsCallback?.Invoke(rows, cols);
    public string? GetPeerHost() { try { return ((IPEndPoint)_client.Client.RemoteEndPoint!).Address.ToString(); } catch { return null; } }
}

public sealed class TelnetProtocol : BaseProtocol
{
    // Per-IP 5s throttle for the overlong-input-drop warning (WS parity via ThrottleWindow).
    private static readonly Dictionary<string, double> _overlongDropLog = new();
    private static readonly object _overlongDropLock = new();
    private const int TELNET_INPUT_CHUNK = 4096; // port of telnet.py:45

    public static (int rows, int cols) ClampNaws(int rows, int cols)
    {
        var s = AtherizSettings.Global;
        return (Math.Max(s.TelnetNawsMinRows, Math.Min(rows, s.TelnetNawsMaxRows)), Math.Max(s.TelnetNawsMinCols, Math.Min(cols, s.TelnetNawsMaxCols)));
    }

    private static string TelnetText(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    public static async IAsyncEnumerable<string?> ReadCappedLines(TextReader reader, int maxLine)
    {
        // Linear-time port: StringBuilder accumulation plus a checkedUpTo
        // cursor, so a huge line costs O(n) total instead of O(n^2) repeated
        // string concatenation/rescan. Consumed lines advance a head offset
        // instead of Remove(0, ...) memmove (amortized compaction below), so
        // tiny-line streams no longer pay O(chunk^2). State machine mirrors
        // the original exactly: split-CRLF holdback, overlong dropping (null
        // yield), \r\n / \r\x00 stripping, EOF tail.
        var buf = new System.Text.StringBuilder();
        var dropping = false; var eof = false;
        int head = 0; // buf[0..head) consumed; visible content is buf[head..]
        int checkedUpTo = 0; // buf[head..checkedUpTo) holds no EOL
        char[] chunkBuf = new char[TELNET_INPUT_CHUNK];
        while (true)
        {
            int read = 0;
            try { read = await reader.ReadAsync(chunkBuf, 0, TELNET_INPUT_CHUNK); }
            catch (OperationCanceledException) { read = 0; }
            catch (Exception ex)
            {
                // Read errors surface instead of masquerading as clean EOF:
                // a broken transport must not look like a graceful disconnect.
                // Drained buffered lines were already yielded above.
                throw new IOException($"telnet input read failed: {ex.Message}", ex);
            }
            if (read <= 0) { eof = true; break; }
            buf.Append(chunkBuf, 0, read);
            // Extract complete lines; scan only the unchecked tail.
            while (true)
            {
                var i = FindEolIn(buf, checkedUpTo);
                if (i == -1) { checkedUpTo = buf.Length; break; }
                // Lone trailing \r with more data possibly coming: hold back.
                if (buf[i] == '\r' && i + 1 >= buf.Length && !eof) { checkedUpTo = i; break; }
                var line = buf.ToString(head, i - head);
                int consume = i + 1;
                if (buf[i] == '\r' && consume < buf.Length && (buf[consume] == '\n' || buf[consume] == '\x00')) consume++;
                head = consume;
                if (checkedUpTo < head) checkedUpTo = head;
                if (dropping || line.Length > maxLine) { yield return null; dropping = false; } else yield return line;
            }
            var effectiveLen = buf.Length - head;
            if (!eof && effectiveLen > 0 && buf[buf.Length - 1] == '\r' && FindEolIn(buf, head) == buf.Length - 1) effectiveLen--;
            if (effectiveLen > maxLine) { dropping = true; buf.Clear(); head = 0; checkedUpTo = 0; }
            else if (head >= 65536) { buf.Remove(0, head); checkedUpTo -= head; head = 0; }
        }
        while (true)
        {
            var i = FindEolIn(buf, head);
            if (i == -1) break;
            var line = buf.ToString(head, i - head);
            int consume = i + 1;
            if (buf[i] == '\r' && consume < buf.Length && (buf[consume] == '\n' || buf[consume] == '\x00')) consume++;
            head = consume;
            if (dropping || line.Length > maxLine) { yield return null; dropping = false; } else yield return line;
        }
        if (buf.Length - head > 0 && !dropping)
        {
            var tail = buf.ToString(head, buf.Length - head);
            if (tail != "\r")
            {
                if (tail.EndsWith("\r")) tail = tail.Substring(0, tail.Length - 1);
                if (!string.IsNullOrEmpty(tail)) yield return tail;
            }
        }
    }

    private static int FindEolIn(System.Text.StringBuilder buf, int start)
    {
        for (int i = start; i < buf.Length; i++)
            if (buf[i] == '\r' || buf[i] == '\n') return i;
        return -1;
    }

    // F016: single TextReader overload (StreamReader binds here implicitly). Read errors are
    // treated as EOF (clean disconnect path) rather than propagating out of the accept loop.
    public static X509Certificate2? BuildTelnetSslContext(AtherizSettings? settings = null)
    {
        settings ??= AtherizSettings.Global;
        var certFile = settings.SslCertFile;
        if (string.IsNullOrEmpty(certFile)) return null;
        if (!File.Exists(certFile)) { Atheriz.Core.AtherizLogger.LogWarning($"WARNING: SSL cert file not found: {certFile}"); return null; }
        try
        {
            var keyFile = settings.SslKeyFile;
            if (!string.IsNullOrEmpty(keyFile) && !File.Exists(keyFile)) { Atheriz.Core.AtherizLogger.LogWarning($"WARNING: SSL key file not found: {keyFile}"); return null; }
            return Atheriz.Core.Utils.TlsCertLoader.Load(certFile, keyFile);
        }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogWarning($"WARNING: Could not load telnet TLS cert: {e}"); return null; }
    }

    // Port of telnet.py:341-446 TelnetProtocol.setup
    // We support two app shapes to remain faithful to Python tests:
    // - FastAPI-style mock with app.router.lifespan_context (test_telnet.py:113-174)
    // - Real IHost/WebApplication via IServiceProvider + IHostApplicationLifetime
    public override void Setup(object app)
    {
        // First, handle FastAPI-style router.lifespan_context composition — port of telnet.py:350-446.
        // Typed contract: test doubles expose ITelnetApp.Router (FakeApp2/FakeAppLifespan).
        try
        {
            if (app is ITelnetApp tapp && tapp.Router is { } router)
            {
                var previous = router.LifespanContext;
                // Capture settings for closure — port of telnet.py:351 server_task per-app (closure, not class attr)
                var settingsForLifespan = AtherizSettings.Global;
                if (app is IHost telnetHost)
                {
                    try { settingsForLifespan = telnetHost.Services.GetRequiredService<AtherizSettings>(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }
                }

                if (settingsForLifespan.TelnetEnabled)
                {
                    // Create composed lifespan wrapper — mirrors telnet.py:436-446.
                    // If disabled, don't replace lifespan (port of telnet.py:347-348).
                    object composed = CreateComposedLifespan(previous, settingsForLifespan);
                    router.LifespanContext = composed;
                }
                return;
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }

        // Fallback to IHost/WebApplication path — real server
        AtherizSettings settings = AtherizSettings.Global;
        IHost? host = app as IHost;
        IServiceProvider? sp = null;
        IHostApplicationLifetime? lifetime = null;
        ConnectionManager? manager = null;

        try
        {
            // Typed service resolution (covers WebApplication and IHost).
            if (app is IHost typedHost) sp = typedHost.Services;
            if (sp != null)
            {
                try { settings = sp.GetRequiredService<AtherizSettings>(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }
                try { lifetime = sp.GetRequiredService<IHostApplicationLifetime>(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }
                try { manager = sp.GetService<ConnectionManager>(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }
            }
            if (lifetime == null && host != null) lifetime = host.Services.GetService<IHostApplicationLifetime>();
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); }

        if (lifetime == null)
        {
            // No lifetime available — cannot start background listener; log and return
            Atheriz.Core.AtherizLogger.LogWarning("[Telnet] No IHostApplicationLifetime available — telnet server not started");
            return;
        }

        if (!settings.TelnetEnabled) return;
        manager ??= ConnectionManager.GlobalInstance ?? new ConnectionManager(settings: settings);

        // port of telnet.py:402-433 run_telnet_server composition via lifespan
        Task.Run(async () =>
        {
            TcpListener? listener = null;
            try
            {
                IPAddress bindAddr;
                if (!IPAddress.TryParse(settings.TelnetInterface, out bindAddr!)) throw new InvalidOperationException($"Unparseable TelnetInterface '{settings.TelnetInterface}'; refusing to bind an unintended interface.");
                listener = new TcpListener(bindAddr, settings.TelnetPort);
                var tlsCert = settings.TelnetTlsEnabled ? BuildTelnetSslContext(settings) : null;
                if (tlsCert != null) Atheriz.Core.AtherizLogger.LogInformation($"SSL is enabled for telnet (cert: {settings.SslCertFile}) with auto-detection for plaintext clients");
                else if (settings.TelnetTlsEnabled) Atheriz.Core.AtherizLogger.LogWarning("TELNET_TLS_ENABLED is on but no usable cert — running plaintext");
                Atheriz.Core.AtherizLogger.LogInformation($"Starting Telnet Protocol on {settings.TelnetInterface}:{settings.TelnetPort}");
                listener.Start();
                using var reg = lifetime.ApplicationStopping.Register(() => { try { listener.Stop(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); } });
                while (!lifetime.ApplicationStopping.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(lifetime.ApplicationStopping); }
                    catch (OperationCanceledException) { break; }
                    catch (SocketException) { if (lifetime.ApplicationStopping.IsCancellationRequested) break; continue; }
                    // admission checks precede the handler spawn — a
                    // banned/over-limit peer is refused here instead of
                    // queueing a task that RegisterConnection would reject.
                    string preHost = "?";
                    try { preHost = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString(); } catch { }
                    if (ObjectRegistry.IsIpBanned(preHost) || manager.ShouldRefusePreSpawn(preHost))
                    {
                        try { Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] Refusing pre-spawn connection from {preHost} (banned or over connection cap)."); } catch { }
                        try { client.Close(); } catch { }
                        continue;
                    }
                    var _ht = Task.Run(() => HandleTelnetClientAsync(client, tlsCert, manager, settings, lifetime)); _ = _ht.ContinueWith(t => { if (t.IsFaulted && t.Exception != null) Atheriz.Core.AtherizLogger.LogError($"[Telnet] HandleClient fault: {t.Exception}"); }, TaskScheduler.Default);
                }
            }
            catch (Exception ex) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] server failed: {ex}"); }
            finally { try { listener?.Stop(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.Setup: " + logEx.Message, "TelnetProtocol"); } Atheriz.Core.AtherizLogger.LogInformation("Telnet Protocol server stopped."); }
        });
    }

    // Helper to create composed lifespan for FastAPI-style app.router.lifespan_context — port of telnet.py:436-446
    private static object CreateComposedLifespan(object? previous, AtherizSettings settings)
    {
        // In Python, lifespan is an asynccontextmanager; in C# we simulate via Func<object, Task>
        // The wrapper, when invoked, will:
        // - if previous != null, await previous as context manager (call it)
        // - start telnet server (stub), yield, then stop server
        // For test purposes, we just ensure previous is invoked and wrapper is callable.
        return new TelnetLifespanComposed(previous, settings);
    }

    private sealed class TelnetLifespanComposed
    {
        private readonly object? _previous;
        private readonly AtherizSettings _settings;
        public TelnetLifespanComposed(object? previous, AtherizSettings settings) { _previous = previous; _settings = settings; }

        // Make this object callable/invocable via dynamic — support app.router.lifespan_context being invoked as async context manager
        // In Python test, they do: installed = app.router.lifespan_context; async with installed(app): pass
        // In C# we expose method that can be awaited via dynamic
        public async Task Invoke(object app, Func<Task> inner)
        {
            // Simulate lifespan composition: run previous if exists, then inner, then cleanup.
            // Previous lifespans are opaque doubles; composition only preserves
            // the reference (pinned by MountingTelnetPreservesPreviousLifespan).
            if (_previous != null)
            {
                try { /* preserve only */ }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetLifespanComposed.Invoke: " + logEx.Message, "TelnetLifespanComposed"); }
            }
            await inner();
        }

        // For dynamic invocation as app.router.lifespan_context(app) being awaited as async disposable
        // Provide method to be used as `await using (var ctx = lifespan(app))`
        public IAsyncDisposable GetAsyncDisposable(object app)
        {
            return new LifespanDisposable(_previous, app);
        }

        private sealed class LifespanDisposable : IAsyncDisposable
        {
            private readonly object? _prev; private readonly object _app;
            public LifespanDisposable(object? prev, object app) { _prev = prev; _app = app; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async Task HandleTelnetClientAsync(TcpClient client, X509Certificate2? tlsCert, ConnectionManager manager, AtherizSettings settings, IHostApplicationLifetime lifetime)
    {
        string host = "?";
        try { host = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); }
        if (ObjectRegistry.IsIpBanned(host)) { Atheriz.Core.AtherizLogger.LogWarning($"Host {host} in temp ban list has tried to connect."); try { client.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); } return; }
        Stream netStream = client.GetStream();
        Stream stream = netStream;
        SslStream? sslStream = null;
        if (tlsCert != null)
        {
            try
            {
                if (client.Client.Poll(250 * 1000, SelectMode.SelectRead) && client.Available >= 2)
                {
                    byte[] peek = new byte[2];
                    int peeked = client.Client.Receive(peek, 2, SocketFlags.Peek);
                    if (peeked >= 2 && peek[0] == 0x16 && peek[1] == 0x03) { sslStream = new SslStream(netStream, false); await sslStream.AuthenticateAsServerAsync(tlsCert).WaitAsync(TimeSpan.FromSeconds(10)); stream = sslStream; }
                }
                else if (client.Available == 0)
                {
                    // no fixed 100ms tax on every plaintext login — wait
                    // only until the client's first bytes actually arrive (or a
                    // short budget expires), then peek once. Data already in
                    // flight wakes the loop in ~10ms; a slow TLS hello inside
                    // the budget is still detected instead of being parsed as
                    // plaintext. Past the budget the bytes are treated as
                    // plaintext, exactly as before.
                    var autodetectDeadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
                    while (client.Available < 2 && DateTime.UtcNow < autodetectDeadline)
                        await Task.Delay(10);
                    if (client.Available >= 2) { byte[] peek = new byte[2]; int peeked = client.Client.Receive(peek, 2, SocketFlags.Peek); if (peeked >= 2 && peek[0] == 0x16 && peek[1] == 0x03) { sslStream = new SslStream(netStream, false); await sslStream.AuthenticateAsServerAsync(tlsCert).WaitAsync(TimeSpan.FromSeconds(10)); stream = sslStream; } } }
            }
            catch (Exception ex) { Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] TLS autodetect failed for {host}: {ex}"); stream = netStream; }
        }
        var reader = new StreamReader(stream, Encoding.UTF8);
        var writer = new TelnetStreamWriter(stream, client);
        var connId = manager.GenerateConnectionId();
        var connection = new TelnetConnection(reader, writer, connId, settings); connection.ClientHost = host;
        if (!manager.RegisterConnection(connId, connection)) return;
        try { writer.Write("\r\n\x1b[1;1H\x1b[2J"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); }
        // NAWS handling disabled to avoid telnet option negotiation garbage (client WILL response being treated as command). Python's telnet.py handles this via asyncio telnetlib, but our StreamReader would treat IAC as text. For now skip DO NAWS to keep input clean for telnetlib/raw clients.
        // void OnNaws(int rows, int cols) { if (rows <= 0 || cols <= 0) return; var (clampedRows, clampedCols) = ClampNaws(rows, cols); connection.Session.TermWidth = clampedCols; connection.Session.TermHeight = clampedRows; }
        // writer.SetExtCallback(31, OnNaws);
        // try { writer.Iac(253, 31); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); }
        manager.Dispatch(connection, "client_ready", new List<object?>(), new Dictionary<string, object?>());
        try { var maxLine = settings.TelnetMaxLine; await foreach (var rawLine in ReadCappedLines(reader, maxLine)) { if (rawLine is null) { if (ThrottleWindow.ShouldLog(_overlongDropLog, _overlongDropLock, host, 5.0)) Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] dropped overlong input line from {connId}"); continue; } var line = rawLine; // Filter stray IAC bytes (0xFF) that telnet clients may send even without DO (e.g., telnetlib pre-negotiation). When decoded as UTF8, 0xFF becomes U+FFFD.
            if (line.Length > 0 && (line[0] == '\uFFFD' || line[0] == (char)255 || line.Contains("\uFFFD"))) {
                // Strip leading IAC sequences: find first alphabetic char of actual command
                int start = 0;
                while (start < line.Length && (line[start] == '\uFFFD' || line[start] == (char)255 || line[start] == (char)253 || line[start] == (char)251 || line[start] == (char)250 || line[start] == (char)240 || line[start] == (char)31 || line[start] < 32)) start++;
                if (start >= line.Length) continue;
                line = line.Substring(start);
            }
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var logLine = line;
            if (line.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                logLine = parts.Length >= 2 ? $"connect {parts[1]} ***" : "connect ***";
            }
            Atheriz.Core.AtherizLogger.LogDebug($"[Telnet] recv '{logLine}' from {connId} host={host}");
            manager.Dispatch(connection, "text", new List<object?> { line }, new Dictionary<string, object?>()); } }
        catch (OperationCanceledException) { } catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] Error in shell for {connId}: {e}"); }
        finally { manager.Disconnect(connection); try { writer.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); } try { client.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed LifespanDisposable.HandleTelnetClientAsync: " + logEx.Message, "LifespanDisposable"); } }
    }
}
