using System.Diagnostics;

namespace Atheriz.Core.Utils;

/// <summary>
/// Central monotonic clock — deduplicates <c>Stopwatch.GetTimestamp</c>/<c>Frequency</c> usage
/// previously duplicated in <c>ThrottleWindow.MonotonicNow</c>, <c>BaseConnection.MonotonicSeconds</c>,
/// <c>GameTime.Stopwatch</c>, <c>MapEdit.GetMonotonic</c>, <c>ConnectionScreen.GetOnline</c> etc.
/// Port of <c>time.monotonic()</c>.
/// </summary>
public static class TimeProvider
{
    public static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    public static long MonotonicMilliseconds() => (long)(MonotonicSeconds() * 1000.0);

    // Compat alias matching ThrottleWindow.Now naming
    public static double Now() => MonotonicSeconds();
}
