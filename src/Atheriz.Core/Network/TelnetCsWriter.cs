using telnet_cs.Server;

namespace Atheriz.Core.Network;

/// <summary>
/// Bridges a telnet_cs <see cref="ServerSession"/> to the engine's <see cref="ITelnetWriter"/>
/// contract, so <see cref="TelnetConnection"/> keeps its pending-limiter accounting and fused
/// prompt semantics while the wire is owned by telnet_cs.
/// </summary>
public sealed class TelnetCsWriter : ITelnetWriter, IDisposable
{
    private const byte TelnetWill = 251;
    private const byte TelnetWont = 252;
    private const byte TelnetEcho = 1;

    private readonly ServerSession _session;
    private readonly string _peerHost;
    private int _disposed;

    // Bound every session write: the old socket writer armed SendTimeout=2000
    // (5 s for TLS writes) while the library socket write has no deadline, so
    // a peer that never drains would park the calling thread forever. Past the
    // bound the wait surfaces IOException (the old SocketException shape) and
    // the connection's write-failure path logs + closes instead of stalling
    // the game thread. 5 s is the looser of the two old bounds (TLS-safe).
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    public TelnetCsWriter(ServerSession session, string? peerHost)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _peerHost = string.IsNullOrEmpty(peerHost) ? "?" : peerHost;
    }

    /// <summary>Exposes the owned session for read-path polling (NAWS) without transferring ownership.</summary>
    internal ServerSession Session => _session;

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Sync bridge over the session's async write: the ITelnetWriter contract is
        // synchronous (TelnetConnection.OffloopWrite runs inline on the loop thread),
        // the library awaits nothing that needs our SynchronizationContext (it uses
        // ConfigureAwait(false) throughout and the server runs context-free), and the
        // session serializes concurrent writers behind its send gate. Callers pass
        // TelnetText-normalized text; never re-normalize here (byte counts stay
        // single-sourced in TelnetConnection).
        RunWrite(static (session, payload, token) => session.WriteAsync(payload, token), text);
    }

    public void Iac(byte cmd, byte opt)
    {
        // Only WILL/WONT ECHO ever arrives here (prompt_masked/echo_on); anything
        // else is a caller error. Repeats are idempotent in the RFC 1143 machine,
        // unlike the old always-send bytes.
        var suppress = ToSuppress(cmd, opt);
        RunWrite(static (session, payload, token) => session.SetEchoAsync(payload, token), suppress);
    }

    public void IacWithText(byte cmd, byte opt, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        // Fused single-frame toggle+prompt: no broadcast can slip between the
        // negotiation bytes and the prompt they govern (parity with the old
        // locked IacWithText write).
        var suppress = ToSuppress(cmd, opt);
        RunWrite(static (session, payload, token) => session.WriteWithEchoAsync(payload.Text, payload.Suppress, token), (Text: text, Suppress: suppress));
    }

    // Sync-over-async bridge with a deadline: the token bounds both the
    // send-gate wait and the socket write, so a wedged peer faults (as
    // IOException, below) instead of parking the calling thread past WriteTimeout.
    // Abandoned in-flight bytes keep the session send gate until they finish,
    // which preserves toggle+text ordering (later writes queue behind, each
    // with their own bound) while the failing connection is closed.
    private void RunWrite<T>(Func<ServerSession, T, CancellationToken, Task> write, T payload)
    {
        using var timeout = new CancellationTokenSource(WriteTimeout);
        try
        {
            write(_session, payload, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new IOException("telnet write timed out", ex);
        }
    }

    public void Close()
    {
        try
        {
            _session.Close();
        }
        catch (Exception ex)
        {
            AtherizLogger.LogDebug("Suppressed TelnetCsWriter.Close: " + ex.Message, "TelnetCsWriter");
        }
    }

    public int? GetWriteBufferSize()
    {
        // No per-session library buffer inspector exists (queue drops are
        // server-level), so report nothing: PendingLimiter stays the sole write
        // accounting. Never report socket capacity here (old false-close bug).
        return null;
    }

    public void SetExtCallback(byte opt, Action<int, int> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        // No callback registration: NAWS reports are polled from
        // ServerSession.ClientWindowSize by the per-session handler. Kept for
        // the ITelnetWriter contract.
    }

    public string? GetPeerHost() => _peerHost;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _session.Dispose();
        }
        catch (Exception ex)
        {
            AtherizLogger.LogDebug("Suppressed TelnetCsWriter.Dispose: " + ex.Message, "TelnetCsWriter");
        }

        GC.SuppressFinalize(this);
    }

    private static bool ToSuppress(byte cmd, byte opt)
    {
        if (opt != TelnetEcho)
        {
            throw new ArgumentOutOfRangeException(nameof(opt), opt, "Only ECHO negotiation is bridged.");
        }

        return cmd switch
        {
            TelnetWill => true,
            TelnetWont => false,
            _ => throw new ArgumentOutOfRangeException(nameof(cmd), cmd, "Only WILL/WONT ECHO is bridged."),
        };
    }
}
