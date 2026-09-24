using telnet_cs.Server;

namespace Atheriz.Core.Network;

/// <summary>
/// Blocking <see cref="TextReader"/> over a telnet_cs <see cref="ServerSession"/>'s decoded text
/// slices, so the engine's <see cref="TelnetProtocol.ReadCappedLines"/> framing (overlong-drop,
/// split-CRLF holdback, \r\x00 strip, EOF tail) runs unchanged on session text instead of raw
/// socket bytes. Timeout slices ("") on a live session are waited through; only a dead session
/// reads as EOF. Wire errors propagate for the accept loop to log and disconnect on.
/// </summary>
public sealed class TelnetSessionReader : TextReader
{
    private static readonly TimeSpan ReadSlice = TimeSpan.FromMilliseconds(100);

    private readonly ServerSession _session;
    private readonly CancellationToken _stopping;
    private string _carry = string.Empty;
    private int _pos;
    // Leading-BOM guard: the first command word can arrive with a U+FEFF prefix
    // (pinned live by PortedServerIntegrationTests telnet login — neutered,
    // login fails with a FEFF-prefixed command word; the bytes come from the
    // client's StreamWriter(Encoding.UTF8), which emits EF-BB-BF), which would
    // otherwise poison the lookup. The library preserves FEFF on every path
    // (hermetic probe: EF BB BF + "cmd" survives raw ReadAsync), so this strip
    // does the work everywhere. Exactly once per connection; later FEFF is data.
    private bool _preamble = true;

    public TelnetSessionReader(ServerSession session, CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _stopping = stopping;
    }

    public override Task<int> ReadAsync(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (buffer.Length - index < count)
        {
            throw new ArgumentException("Offset and count exceed buffer bounds.", nameof(count));
        }

        return ReadCoreAsync(buffer.AsMemory(index, count), CancellationToken.None).AsTask();
    }

    public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(buffer, cancellationToken);

    private async ValueTask<int> ReadCoreAsync(Memory<char> destination, CancellationToken callerToken)
    {
        if (destination.IsEmpty)
        {
            return 0;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_stopping, callerToken);
        while (true)
        {
            if (_pos < _carry.Length)
            {
                int n = Math.Min(_carry.Length - _pos, destination.Length);
                _carry.AsSpan(_pos, n).CopyTo(destination.Span);
                _pos += n;
                return n;
            }

            _carry = string.Empty;
            _pos = 0;
            string slice = await _session.ReadAsync(ReadSlice, linked.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(slice))
            {
                if (_preamble)
                {
                    _preamble = false;
                    if (slice[0] == '\uFEFF')
                    {
                        slice = slice[1..];
                    }
                }
                _carry = slice;
                continue;
            }

            if (!_session.IsConnected || linked.Token.IsCancellationRequested)
            {
                return 0;
            }

            // Live but quiet: keep blocking like a socket read instead of reporting EOF.
        }
    }
}
