namespace Atheriz.Core.Network;

/// <summary>
/// Per-site throttle-log holder: owns its host-timestamp map and lock,
/// delegating accept/reject decisions to <see cref="ThrottleWindow"/>.
/// Each throttle site keeps its own instance (with its own window) so host
/// state is never shared across sites and windows can diverge later.
/// </summary>
public sealed class ThrottledLog(double window)
{
    private readonly Dictionary<string, double> _last = new();
    private readonly Lock _lock = new();

    /// <summary>
    /// Returns true when a log for <paramref name="host"/> should be emitted
    /// (window elapsed), false when throttled. The clock is read at call time.
    /// </summary>
    public bool ShouldLog(string host)
        => ThrottleWindow.ShouldLog(_last, _lock, host, window);

    /// <summary>
    /// Explicit-clock overload for deterministic callers.
    /// </summary>
    public bool ShouldLog(string host, double now)
        => ThrottleWindow.ShouldLog(_last, _lock, host, window, now);
}
