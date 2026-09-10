// Port of atheriz/network/manager.py:10-24 + websocket.py:15-27 + connection.py:87-89 throttling

namespace Atheriz.Core.Network;

/// <summary>
/// Shared throttling helper mirroring <c>manager.py:_should_log_malformed</c> (5s per host),
/// <c>websocket.py:_should_log_oversize</c> (5s per host) and <c>connection.py:EnqueueInput</c> (1s busy).
/// Uses <c>TimeProvider.MonotonicSeconds</c> monotonic clock (centralized).
/// </summary>
public static class ThrottleWindow
{
    private static double MonotonicNow() => global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();

    // Sweep threshold for the amortized TTL eviction in ShouldLog.
    private const int MaxHostsBeforeSweep = 1024;
    // Per-call opportunistic bound: examine at most this many entries (O(1),
    // not O(n)) so small dicts still evict promptly (pinned by
    // ThrottleWindow_EvictsExpiredHosts) without taxing every call.
    private const int MaxEvictProbePerCall = 16;

    /// <summary>
    /// Per-host throttling — mirrors <c>manager.py:17-24</c> and <c>websocket.py:20-27</c>.
    /// Returns true if log should be emitted (window elapsed), false if throttled.
    /// </summary>
    public static bool ShouldLog(Dictionary<string, double> last, Lock syncLock, string host, double window, double now)
    {
        lock (syncLock)
        {
            // Amortized TTL eviction : Python (manager.py:17-24) does
            // no eviction at all; a per-call O(n) sweep here taxed every
            // throttled message under the serializing lock. Sweep fully only
            // once the dict exceeds a cap; every call additionally evicts
            // expired entries among a bounded probe prefix (O(1)), so small
            // dicts still drain promptly while the common path stays flat.
            if (last.Count > MaxHostsBeforeSweep)
            {
                List<string>? expired = null;
                foreach (var entry in last)
                    if (now - entry.Value >= window)
                        (expired ??= []).Add(entry.Key);
                if (expired is not null)
                    foreach (var k in expired) last.Remove(k);
            }
            else if (last.Count > 0)
            {
                List<string>? expiredFew = null;
                int probed = 0;
                foreach (var entry in last)
                {
                    if (probed++ >= MaxEvictProbePerCall) break;
                    if (now - entry.Value >= window)
                        (expiredFew ??= []).Add(entry.Key);
                }
                if (expiredFew is not null)
                    foreach (var k in expiredFew) last.Remove(k);
            }
            if (last.TryGetValue(host, out var prev) && now - prev < window) return false;
            last[host] = now;
            return true;
        }
    }

    /// <summary>
    /// Overload computing <c>now</c> via monotonic clock.
    /// </summary>
    public static bool ShouldLog(Dictionary<string, double> last, Lock syncLock, string host, double window)
    {
        var now = MonotonicNow();
        return ShouldLog(last, syncLock, host, window, now);
    }

    /// <summary>
    /// Per-connection single-value throttling — mirrors <c>connection.py:88-90</c> busy 1s window.
    /// Caller holds <c>BaseConnection.Lock</c>; this helper does not lock internally for ref double.
    /// Returns true if window elapsed (should notify), false if throttled.
    /// </summary>
    public static bool ShouldLog(ref double lastBusy, double window, double now)
    {
        if (now - lastBusy < window) return false;
        lastBusy = now;
        return true;
    }

    public static bool ShouldLog(ref double lastBusy, double window)
    {
        var now = MonotonicNow();
        return ShouldLog(ref lastBusy, window, now);
    }

    // Convenience for direct monotonic access (for callers that need now value for logging)
    public static double Now() => MonotonicNow();
}
