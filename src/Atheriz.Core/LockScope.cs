namespace Atheriz.Core;

/// <summary>
/// Shared read/write lock scope (unifies the six copy-pasted private
/// LockScope classes): releases the held lock on Dispose.
/// </summary>
internal sealed class LockScope(ReaderWriterLockSlim rw, bool isWrite) : IDisposable
{
    private bool _disposed;
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (isWrite) rw.ExitWriteLock(); else rw.ExitReadLock();
    }
}
