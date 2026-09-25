using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using telnet_cs.Server;

namespace Atheriz.Core.Network;

// Telnet protocol with TLS autodetect same-port, NAWS clamping, capped line reading.

public class TelnetConnection : BaseConnection
{
    // telnet.py:121-137 TelnetConnection.__init__
    public object Reader { get; }
    public object Writer { get; }
    private readonly PendingLimiter _limiter; // sole accounting (single source of truth)
    private readonly AtherizSettings _settings;
    private int _inflight; // offloaded writes not yet finished (Dispose waits, bounded)
    // Parked-task shed bound: see ScheduleWrite.
    private const int MaxScheduledWrites = 32;
    // Dispose joins in-flight writes via this event instead of a
    // 250ms spin that loses to 2-5s write/TLS timeouts and aborts mid-write.
    private readonly ManualResetEventSlim _drained = new(true);
    // Async waiter registry for DisposeAsync: the old single TCS was
    // replaced on every ScheduleWrite, orphaning the previous waiter's
    // signal (DisposeAsync stalled the full 10 s despite a completed
    // drain). Every arm registers; every drain-completion releases all.
    private readonly Lock _drainLock = new();
    private readonly List<TaskCompletionSource<bool>> _drainWaiters = new();
    private int _disposeManaged;
    // Bound covers the longest write/TLS timeouts above (2-5s) with headroom.
    private static readonly TimeSpan DisposeJoinTimeout = TimeSpan.FromSeconds(10);
    // Close flag (with _limiter.IsClosing forms IsClosing). Pending-byte
    // accounting lives solely in PendingLimiter — no mirrors.
    // Volatile: written on the closer thread, read from arbitrary
    // senders with no other barrier — a plain bool allows unbounded
    // visibility delay on weak-memory hardware.
    private volatile bool _closing;
    // Per-connection 5s throttle for the overlong-input-drop warning.
    // The holder lives here (not on TelnetProtocol, whose session pump is
    // static) so one connection's burst never silences another's warning.
    private readonly ThrottledLog _overlongDropLog = new(5.0);

    internal bool ShouldLogOverlongDrop(string host) => _overlongDropLog.ShouldLog(host);

    public TelnetConnection(object reader, object writer, string? sessionId = null, AtherizSettings? settings = null) : base(sessionId)
    {
        Reader = reader;
        Writer = writer;
        ClientHost = "?";
        _settings = settings ?? AtherizSettings.Global;
        _limiter = new PendingLimiter(_settings.TelnetMaxPendingBytes, _settings.TelnetMaxPendingSends);
        try
        {
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
            // Re-checks _inflight past the event: a ScheduleWrite arming
            // after the event snapshot would otherwise run past teardown.
            var deadline = DateTimeOffset.UtcNow + DisposeJoinTimeout;
            while (Volatile.Read(ref _inflight) != 0 && DateTimeOffset.UtcNow < deadline)
            {
                try { _drained.Wait(deadline - DateTimeOffset.UtcNow); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); break; }
            }
            DisposeManaged();
        }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        // Async join: awaits the drain signal instead of parking the caller.
        // A timeout proceeds past the bound, mirroring the sync Wait above.
        // Registers alongside ScheduleWrite's arms, so a write armed
        // after this call still releases this waiter on completion.
        Task waitTask;
        lock (_drainLock)
        {
            if (_drainWaiters.Count == 0) waitTask = Task.CompletedTask;
            else
            {
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _drainWaiters.Add(tcs);
                waitTask = tcs.Task;
            }
        }
        try { await waitTask.WaitAsync(DisposeJoinTimeout).ConfigureAwait(false); }
        catch (TimeoutException) { }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.DisposeAsync: " + logEx.Message, "TelnetConnection"); }
        DisposeManaged();
        base.Dispose(true);
        GC.SuppressFinalize(this);
    }

    // Writer/reader/event teardown shared by both dispose paths (idempotent:
    // async and sync dispose may race, and Dispose may run twice).
    private void DisposeManaged()
    {
        if (Interlocked.Exchange(ref _disposeManaged, 1) != 0) return;
        try { if (Writer is IDisposable wd) wd.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
        try { if (Reader is System.IO.TextReader tr) tr.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
        try { _drained.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.Dispose: " + logEx.Message, "TelnetConnection"); }
    }

    private void ArmDrained()
    {
        try { _drained.Reset(); } catch (ObjectDisposedException) { /* Dispose already joined; write still runs below. */ }
        lock (_drainLock) _drainWaiters.Add(new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private void SignalDrained()
    {
        try { _drained.Set(); } catch (ObjectDisposedException) { }
        List<TaskCompletionSource<bool>> done;
        lock (_drainLock) { done = new List<TaskCompletionSource<bool>>(_drainWaiters); _drainWaiters.Clear(); }
        foreach (var tcs in done) try { tcs.TrySetResult(true); } catch { }
    }

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
                    if (b is not null) return b;
                }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetConnection.GetWriteBufferSize: " + logEx.Message, "TelnetConnection"); }
        return null;
    }

// single-pass span normalizer.
    // Net effect matches the old double-Replace (lone \n → \r\n, existing
    // \r\n untouched, bare \r stays bare) with one scan and no intermediate.
    private static string TelnetText(string text)
    {
        int extra = 0;
        for (int i = 0; i < text.Length; i++)
            if (text[i] == '\n' && (i == 0 || text[i - 1] != '\r'))
                extra++;
        if (extra == 0) return text;
        return string.Create(text.Length + extra, text, static (span, src) =>
        {
            int j = 0;
            for (int i = 0; i < src.Length; i++)
            {
                char c = src[i];
                if (c == '\n' && (i == 0 || src[i - 1] != '\r'))
                {
                    span[j++] = '\r';
                    span[j++] = '\n';
                }
                else span[j++] = c;
            }
        });
    }

    // WriterWrite expects pre-normalized text (OffloopWrite runs TelnetText first).
    private void WriterWrite(string text)
    {
        // Typed only: all writers implement ITelnetWriter (Simple/Mock/Stream).
        if (Writer is ITelnetWriter itw0) itw0.Write(text);
    }

    private void WriterIac(byte cmd, byte opt)
    {
        if (Writer is ITelnetWriter itw0) itw0.Iac(cmd, opt);
    }

    private void WriterIacText(byte cmd, byte opt, string text)
    {
        if (Writer is ITelnetWriter itw0) itw0.IacWithText(cmd, opt, text);
    }

    private void WriterClose()
    {
        if (Writer is ITelnetWriter itw0) itw0.Close();
    }

    private bool CheckWriteBufferExceeded(string suffix = "")
    {
        var buf = GetWriteBufferSize();
        if (buf is not null && buf > _settings.TelnetMaxPendingBytes)
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
        // Refuse writes past teardown: a ScheduleWrite landing after
        // DisposeManaged would run against a disposed writer.
        if (IsClosing || Volatile.Read(ref _disposeManaged) != 0) { if (nb != 0) _limiter.ReleaseSync(nb); return; }
        // Shed past the parked-task bound: each scheduled write parks
        // its task up to WriteTimeout on a wedged peer, so N wedged peers x
        // fan-out would park unbounded threads (the limiter bounds bytes,
        // not tasks). Past the bound the message is dropped for this peer —
        // a healthy peer drains in ms and never reaches it; a wedged one is
        // closed by the limiter/buffer checks anyway.
        if (Volatile.Read(ref _inflight) >= MaxScheduledWrites)
        {
            if (nb != 0) _limiter.ReleaseSync(nb);
            Atheriz.Core.AtherizLogger.LogDebug($"Suppressed TelnetProtocol.ScheduleWrite: shed write for {ClientHost} ({MaxScheduledWrites} parked)", "TelnetProtocol");
            return;
        }
        // Arm before counting: a Dispose snapshotting between the
        // increment and the arm would otherwise miss this write entirely.
        ArmDrained();
        Interlocked.Increment(ref _inflight);
        try
        {
            Task.Run(() => { try { write(); } finally { if (Interlocked.Decrement(ref _inflight) == 0) SignalDrained(); } })
                .ContinueWith(t => { AtherizLogger.LogError($"[Telnet] offloaded write task faulted for {ClientHost}", t.Exception!); },
                    TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (Exception e)
        {
            if (Interlocked.Decrement(ref _inflight) == 0) SignalDrained();
            if (nb != 0) _limiter.ReleaseSync(nb);
            AtherizLogger.LogError($"[Telnet] failed to schedule write for {ClientHost}: {e}");
            Close();
        }
    }

    // Shared before/after buffer-check + catch + single-release cycle for
    // the buffered off-loop writes. The write runs exactly once via the
    // lambda (which never touches the limiter), and ReleaseSync(nb) runs
    // exactly once per path in the finally.
    private void ExecuteBufferedWrite(int nb, Action write)
    {
        try
        {
            if (CheckWriteBufferExceeded()) return;
            write();
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

// now uses PendingLimiter with finally ReleaseSync (fix leak)
    public void OffloopWrite(string text, int nb)
    {
        text = TelnetText(text);
        ExecuteBufferedWrite(nb, () => WriterWrite(text));
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

    // Fused IAC-then-text as one writer call: a broadcast from another thread
    // cannot slip between the WILL ECHO bytes and the prompt they govern.
    // IAC bytes are control, not reserved (same accounting as OffloopIac).
    public void OffloopIacText(byte teloptCmd, byte teloptOpt, string text, int nb)
    {
        text = TelnetText(text);
        ExecuteBufferedWrite(nb, () => WriterIacText(teloptCmd, teloptOpt, text));
    }

    public int PendingBytes => _limiter.PendingBytes;
    public bool IsClosing => _limiter.IsClosing || _closing;
    // Expose limiter for testing / inspection (kept internal)
    internal PendingLimiter Limiter => _limiter;

// now via PendingLimiter (fixes sync leak)
    public override void SendCommand(string cmd, List<object?>? args = null, Dictionary<string, object?>? kwargs = null)
    {
        if (IsClosing) return;
        const byte WILL = 251; const byte WONT = 252; const byte ECHO = 1;

        if (cmd == "text" || cmd == "prompt")
        {
            var text = args?.FirstOrDefault()?.ToString() ?? "";
            if (string.IsNullOrEmpty(text)) return;
            // Reserve what OffloopWrite will actually emit: it normalizes
            // lone LF to CRLF first, so measure post-normalization bytes
            // (same serialize-then-measure accounting as the websocket path).
            // Re-normalizing inside OffloopWrite is idempotent.
            text = TelnetText(text);
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
            var text = args?.FirstOrDefault()?.ToString() ?? "";
            // Same normalize-then-measure as text/prompt above: the IAC-text
            // write normalizes before emitting.
            text = TelnetText(text);
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
            // are control, not reserved. The IAC and prompt go out as one
            // fused writer call (and one scheduled unit off-loop) so another
            // thread's bytes cannot slip between them.
            if (IsOnLoopThread())
            {
                if (string.IsNullOrEmpty(text)) OffloopIac(WILL, ECHO);
                else OffloopIacText(WILL, ECHO, text, nb);
            }
            else
            {
                if (string.IsNullOrEmpty(text)) ScheduleWrite(() => OffloopIac(WILL, ECHO), 0);
                else ScheduleWrite(() => OffloopIacText(WILL, ECHO, text, nb), nb);
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
        _closing = true;
        if (!_limiter.TryMarkClosing()) return;
        try
        {
            if (IsOnLoopThread()) WriterClose();
            else
            {
                // Track the off-loop close like a write: neither the
                // drain event nor the async waiter covers it otherwise, so
                // close bytes race teardown.
                ArmDrained();
                Interlocked.Increment(ref _inflight);
                try
                {
                    var _t = Task.Run((Action)WriterClose);
                    _ = _t.ContinueWith(t => { try { if (Interlocked.Decrement(ref _inflight) == 0) SignalDrained(); } catch { } if (t.IsFaulted && t.Exception is not null) Atheriz.Core.AtherizLogger.LogError($"[Telnet] Close fault: {t.Exception}"); }, TaskScheduler.Default);
                }
                catch
                {
                    try { if (Interlocked.Decrement(ref _inflight) == 0) SignalDrained(); } catch { }
                    throw;
                }
            }
        }
        catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] Error closing connection: {e}"); }
    }
}

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
    // Fused IAC-then-text write. Default keeps old split behavior; the stream
    // writer overrides with one locked byte write so a broadcast cannot slip
    // between the IAC negotiation bytes and the prompt they govern.
    void IacWithText(byte cmd, byte opt, string text) { Iac(cmd, opt); Write(text); }
    // transport → writer → _transport chain). Default is just this writer.
    IReadOnlyList<ITelnetBufferSource> BufferSources => [this];
}

public static class TelnetProtocol
{
    private const int TELNET_INPUT_CHUNK = 4096;

    public static (int rows, int cols) ClampNaws(int rows, int cols)
        => ClampNaws(rows, cols, AtherizSettings.Global);

    // Settings-threading overload: HandleSessionAsync honors its settings
    // parameter for TelnetMaxLine, so the NAWS clamp must too — Global can
    // diverge from the live settings (e.g. per-game configuration).
    public static (int rows, int cols) ClampNaws(int rows, int cols, AtherizSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var s = settings;
        return (Math.Max(s.TelnetNawsMinRows, Math.Min(rows, s.TelnetNawsMaxRows)), Math.Max(s.TelnetNawsMinCols, Math.Min(cols, s.TelnetNawsMaxCols)));
    }

    public static async IAsyncEnumerable<string?> ReadCappedLines(TextReader reader, int maxLine)
    {
        // Linear-time port: StringBuilder accumulation plus a checkedUpTo
        // cursor, so a huge line costs O(n) total instead of O(n^2) repeated
        // string concatenation/rescan. Consumed lines advance a head offset
        // instead of Remove(0, ...) memmove (amortized compaction below), so
        // tiny-line streams no longer pay O(chunk^2). State machine mirrors
        // the original exactly: split-CRLF holdback, overlong dropping (null
        // yield), \r\n / \r\x00 stripping, EOF tail.
        // Read errors throw IOException (deliberate): a broken transport must
        // not look like a graceful disconnect. The accept loop logs the error
        // and disconnects; only clean EOF (read 0) ends the stream quietly.
        var buf = new System.Text.StringBuilder();
        var dropping = false; var eof = false;
        int head = 0; // buf[0..head) consumed; visible content is buf[head..]
        int checkedUpTo = 0; // buf[head..checkedUpTo) holds no EOL
        char[] chunkBuf = new char[TELNET_INPUT_CHUNK];
        while (true)
        {
            int read = 0;
            try { read = await reader.ReadAsync(chunkBuf, 0, TELNET_INPUT_CHUNK).ConfigureAwait(false); }
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
        // EOF tail: an overlong tail closed by EOF (not a newline) yields the
        // same drop marker as a mid-stream overlong line — vanishing silently
        // would hide dropped input from the log-completeness accounting.
        if (dropping) { yield return null; dropping = false; }
        else if (buf.Length - head > 0)
        {
            var tail = buf.ToString(head, buf.Length - head);
            if (tail != "\r")
            {
                if (tail.EndsWith("\r", StringComparison.Ordinal)) tail = tail.Substring(0, tail.Length - 1);
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

    // F016: single TextReader overload (StreamReader binds here implicitly).
    // (An older revision claimed read errors surface as clean EOF; they
    // throw IOException and the accept loop logs + disconnects — see above.)
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

    // Peer-label helper for the accept filter: the library exposes the endpoint
    // to the filter, but ServerSession.RemoteEndPoint is internal, so the filter
    // snapshots this string for TelnetCsWriter.GetPeerHost.
    internal static string HostOf(EndPoint? endpoint)
    {
        try { return endpoint is IPEndPoint ip ? ip.Address.ToString() : "?"; }
        catch { return "?"; }
    }

    // Admission mapping (telnet.md Phase 3b): Atheriz stays the single source of
    // truth for bans/caps (ban + per-IP + total). The library evaluates this
    // before any TLS handshake or preset bytes, so a refused peer gets no bytes
    // and no handler task — the same pre-spawn refuse the raw loop did inline.
    internal static Func<EndPoint?, AcceptDecision> BuildAcceptFilter(ConnectionManager manager, ConcurrentQueue<string?> handoff)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(handoff);
        return endpoint =>
        {
            var host = HostOf(endpoint);
            // Handoff invariant: the accept loop below is sequential, so each
            // filter run enqueues exactly one entry and the loop dequeues
            // exactly one per AcceptTcpAsync (success takes it, any failure
            // drains it) — the snapshot always belongs to the current accept.
            handoff.Enqueue(host);
            if (ObjectRegistry.IsIpBanned(host) || manager.ShouldRefusePreSpawn(host))
            {
                try { Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] Refusing pre-spawn connection from {host} (banned or over connection cap)."); } catch { }
                return new AcceptDecision(false, "banned-or-over-cap");
            }
            return new AcceptDecision(true);
        };
    }

    // Settings/options mapping (telnet.md Phase 4). Library caps stay unlimited
    // so only Atheriz counts fire (no divergent enforcement); timeouts stay
    // deadline-free for parity (no enforced idle/handshake existed — the
    // 5-minute pre-login orphan sweep still reaps silent sockets).
    internal static TelnetServerOptions BuildServerOptions(AtherizSettings settings, X509Certificate2? tlsCert, Func<EndPoint?, AcceptDecision> filter)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(filter);
        if (!IPAddress.TryParse(settings.TelnetInterface, out var bindAddr))
            throw new InvalidOperationException($"Unparseable TelnetInterface '{settings.TelnetInterface}'; refusing to bind an unintended interface.");
        return new TelnetServerOptions
        {
            ListenAddress = bindAddr,
            TextEncoding = Encoding.UTF8,
            // Keep write bytes == UTF-8 reserve: charset negotiation would only
            // retune per-read decoding, never the write path, and the engine
            // reserves limiter bytes as UTF-8 — no drift while this stays off.
            RequestCharacterSet = false,
            // Re-enables NAWS (the raw-socket path sent no DO NAWS to avoid IAC
            // garbage in StreamReader text; session text is already decoded).
            RequestWindowSize = true,
            MaxConcurrentSessions = 0,
            MaxConnectionsPerIp = 0,
            // Unlimited: overlong-drop stays Atheriz-side (ReadCappedLines
            // drop+continue); the library cap would fail-closed (disconnect).
            MaxBufferedTextChars = 0,
            IdleTimeout = Timeout.InfiniteTimeSpan,
            // 10 s bounds the accept-inline TLS handshake exactly like the old
            // AuthenticateAsServerAsync cap; plaintext stays deadline-free.
            HandshakeTimeout = tlsCert is not null ? TimeSpan.FromSeconds(10) : Timeout.InfiniteTimeSpan,
            StatusInterval = null,
            ServerCertificate = tlsCert,
            // Same-port peek budget mirrors the old 250 ms poll + 250 ms budget.
            TlsAutoDetect = tlsCert is not null ? TimeSpan.FromMilliseconds(250) : Timeout.InfiniteTimeSpan,
            Log = msg => { try { AtherizLogger.LogDebug(msg, "TelnetServer"); } catch { } },
            AcceptFilter = filter,
        };
    }

    // Sequential accept loop: admission already ran inside AcceptTcpAsync, and
    // NegotiateAsync (opening preset) runs inside the per-session task so a
    // slow peer never stalls other accepts.
    internal static async Task AcceptLoopAsync(TelnetServer server, ConcurrentQueue<string?> handoff, ConnectionManager manager, AtherizSettings settings, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(handoff);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(settings);
        while (!stopping.IsCancellationRequested)
        {
            ServerSession pending;
            try
            {
                pending = await server.AcceptTcpAsync(stopping).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Shutdown only when OUR token died: a timed-out accept (e.g. the TLS
                // handshake deadline) can also surface as cancellation when the
                // library's linked CTS wins the race against its TimeoutException.
                // Breaking here would silently stop the listener on a single slow
                // peer (seen under parallel-suite load), so only a real shutdown
                // breaks; anything else drains the handoff and continues.
                handoff.TryDequeue(out _);
                if (stopping.IsCancellationRequested) break;
                try { AtherizLogger.LogDebug("[Telnet] accept cancelled without shutdown; continuing", "TelnetProtocol"); } catch { }
                continue;
            }
            catch (ObjectDisposedException) { if (stopping.IsCancellationRequested) break; handoff.TryDequeue(out _); continue; }
            catch (SocketException) { if (stopping.IsCancellationRequested) break; handoff.TryDequeue(out _); continue; }
            // Refuse exceptions derive InvalidOperationException — catch before
            // it. The filter already warned; the library logged its own
            // over-capacity line via options.Log.
            catch (ConnectionRefusedByFilterException) { handoff.TryDequeue(out _); continue; }
            // Unreachable while library caps stay 0 (Atheriz enforces bans/caps):
            // kept so a future cap change degrades to a refused peer, not a throw.
            catch (SessionCapacityException) { handoff.TryDequeue(out _); continue; }
            catch (PerIpCapacityException) { handoff.TryDequeue(out _); continue; }
            catch (TimeoutException ex) { handoff.TryDequeue(out _); try { Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] handshake timeout: {ex.Message}"); } catch { } continue; }
            catch (InvalidOperationException ex) { handoff.TryDequeue(out _); try { Atheriz.Core.AtherizLogger.LogError($"[Telnet] accept failed: {ex.Message}"); } catch { } break; }
            catch (Exception ex) { handoff.TryDequeue(out _); try { Atheriz.Core.AtherizLogger.LogError($"[Telnet] accept failed: {ex.Message}"); } catch { } continue; }
            handoff.TryDequeue(out var host);
            var session = pending;
            var peerHost = host ?? "?";
            var _ht = Task.Run(() => NegotiateThenHandleAsync(server, session, peerHost, manager, settings, stopping)); _ = _ht.ContinueWith(t => { if (t.IsFaulted && t.Exception is not null) Atheriz.Core.AtherizLogger.LogError($"[Telnet] HandleClient fault: {t.Exception}"); }, TaskScheduler.Default);
        }
    }

    // NAWS re-enable: the opening preset asks for window-size reports and the
    // library stashes them on ClientWindowSize; apply (clamped) to the game
    // session. Polled per received line — reports arrive right after connect.
    private static void ApplyNaws(TelnetCsWriter writer, TelnetConnection connection, AtherizSettings settings)
    {
        try
        {
            var size = writer.Session.ClientWindowSize;
            if (size is not { } reported) return;
            if (reported.Width == 0 || reported.Height == 0) return;
            var (rows, cols) = ClampNaws(reported.Height, reported.Width, settings);
            if (connection.Session.TermWidth != cols) connection.Session.TermWidth = cols;
            if (connection.Session.TermHeight != rows) connection.Session.TermHeight = rows;
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.ApplyNaws: " + logEx.Message, "TelnetProtocol"); }
    }

    // Opening preset (DO TTYPE et al.) goes out here, inside the per-session
    // task: a slow peer stalls only its own negotiation, never the accept loop.
    internal static async Task NegotiateThenHandleAsync(TelnetServer server, ServerSession pending, string host, ConnectionManager manager, AtherizSettings settings, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(settings);
        ServerSession session;
        try
        {
            session = await server.NegotiateAsync(pending, stopping).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { try { pending.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.NegotiateThenHandleAsync: " + logEx.Message, "TelnetProtocol"); } return; }
        catch (TimeoutException ex) { try { Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] handshake timeout: {ex.Message}"); } catch { } try { pending.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.NegotiateThenHandleAsync: " + logEx.Message, "TelnetProtocol"); } return; }
        catch (Exception ex) { try { Atheriz.Core.AtherizLogger.LogError($"[Telnet] negotiation failed for {host}: {ex.Message}"); } catch { } try { pending.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.NegotiateThenHandleAsync: " + logEx.Message, "TelnetProtocol"); } return; }
        await HandleSessionAsync(session, host, manager, settings, stopping).ConfigureAwait(false);
    }

    internal static async Task HandleSessionAsync(ServerSession session, string host, ConnectionManager manager, AtherizSettings settings, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(settings);
        if (ObjectRegistry.IsIpBanned(host)) { Atheriz.Core.AtherizLogger.LogWarning($"Host {host} in temp ban list has tried to connect."); try { session.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.HandleSessionAsync: " + logEx.Message, "TelnetProtocol"); } return; }
        var connId = manager.GenerateConnectionId();
        var writer = new TelnetCsWriter(session, host);
        var reader = new TelnetSessionReader(session, stopping);
        var connection = new TelnetConnection(reader, writer, connId, settings); connection.ClientHost = host;
        // connection.Dispose tears down writer (which owns the session) and
        // reader, so every path below exits through it exactly once.
        if (!manager.RegisterConnection(connId, connection)) { try { connection.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.HandleSessionAsync: " + logEx.Message, "TelnetProtocol"); } return; }
        try { writer.Write("\r\n\x1b[1;1H\x1b[2J"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.HandleSessionAsync: " + logEx.Message, "TelnetProtocol"); }
        ApplyNaws(writer, connection, settings);
        manager.Dispatch(connection, "client_ready", [], []);
        try { var maxLine = settings.TelnetMaxLine; await foreach (var rawLine in ReadCappedLines(reader, maxLine).ConfigureAwait(false)) { if (rawLine is null) { if (connection.ShouldLogOverlongDrop(host)) Atheriz.Core.AtherizLogger.LogWarning($"[Telnet] dropped overlong input line from {connId}"); continue; } var line = rawLine; // Session text is already negotiation-decoded, but keep the stray-IAC guard: a peer can still emit bare 0xFF, which decodes as U+FFFD.
            if (line.Length > 0 && (line[0] == '\uFFFD' || line[0] == (char)255 || line.Contains("\uFFFD"))) {
                // Strip leading IAC sequences: find first alphabetic char of actual command
                int start = 0;
                while (start < line.Length && (line[start] == '\uFFFD' || line[start] == (char)255 || line[start] == (char)253 || line[start] == (char)251 || line[start] == (char)250 || line[start] == (char)240 || line[start] == (char)31 || line[start] < 32)) start++;
                if (start >= line.Length) continue;
                line = line.Substring(start);
            }
            ApplyNaws(writer, connection, settings);
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            var logLine = line;
            if (line.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                logLine = parts.Length >= 2 ? $"connect {parts[1]} ***" : "connect ***";
            }
            Atheriz.Core.AtherizLogger.LogDebug($"[Telnet] recv '{logLine}' from {connId} host={host}");
            manager.Dispatch(connection, "text", new List<object?> { line }, []); } }
        catch (OperationCanceledException) { } catch (Exception e) { Atheriz.Core.AtherizLogger.LogError($"[Telnet] Error in shell for {connId}: {e}"); }
        finally { manager.Disconnect(connection); try { connection.Dispose(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed TelnetProtocol.HandleSessionAsync: " + logEx.Message, "TelnetProtocol"); } }
    }
}
