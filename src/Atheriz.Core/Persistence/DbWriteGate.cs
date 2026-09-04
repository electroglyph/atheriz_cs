// Port of atheriz/database_setup.py:Database.lock RLock (re-entrant)
using System.Threading;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Re-entrant gate mirroring Python `RLock`. Same logical flow may re-enter
/// without deadlock (StartStop.DoShutdown → SaveWorld → nested SaveObjects).
/// Uses AsyncLocal recursion count + underlying SemaphoreSlim(1,1).
/// </summary>
public static class DbWriteGate
{
    private static readonly SemaphoreSlim _sem = new(1, 1);
    private static readonly AsyncLocal<int> _recursion = new();

    public static SemaphoreSlim SemaphoreForTesting => _sem;

    public static bool IsHeld => _recursion.Value > 0;

    public static void Enter()
    {
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return;
        }
        _sem.Wait();
        _recursion.Value = 1;
    }

    public static async Task EnterAsync(CancellationToken ct = default)
    {
        if (_recursion.Value > 0)
        {
            _recursion.Value++;
            return;
        }
        await _sem.WaitAsync(ct);
        _recursion.Value = 1;
    }

    public static void Exit()
    {
        var c = _recursion.Value;
        if (c <= 1)
        {
            _recursion.Value = 0;
            _sem.Release();
        }
        else
        {
            _recursion.Value = c - 1;
        }
    }
}
