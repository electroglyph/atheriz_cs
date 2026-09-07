namespace Atheriz.Core;

/// <summary>
/// Shared read/write lock scope (unifies the six copy-pasted private
/// LockScope classes): releases the held lock on Dispose.
/// </summary>
internal sealed class LockScope : IDisposable
{
    private readonly ReaderWriterLockSlim _rw;
    private readonly bool _isWrite;
    private bool _disposed;
    public LockScope(ReaderWriterLockSlim rw, bool isWrite) { _rw = rw; _isWrite = isWrite; }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_isWrite) _rw.ExitWriteLock(); else _rw.ExitReadLock();
    }
}
