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

    [Obsolete("Use TimeProvider.MonotonicSeconds()")]
    public static double MonotonicNowObsolete() => global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();

    /// <summary>
    /// Per-host throttling — mirrors <c>manager.py:17-24</c> and <c>websocket.py:20-27</c>.
    /// Returns true if log should be emitted (window elapsed), false if throttled.
    /// </summary>
    public static bool ShouldLog(Dictionary<string, double> last, object syncLock, string host, double window, double now)
    {
        lock (syncLock)
        {
            if (last.TryGetValue(host, out var prev) && now - prev < window) return false;
            last[host] = now;
            return true;
        }
    }

    /// <summary>
    /// Overload computing <c>now</c> via monotonic clock.
    /// </summary>
    public static bool ShouldLog(Dictionary<string, double> last, object syncLock, string host, double window)
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
