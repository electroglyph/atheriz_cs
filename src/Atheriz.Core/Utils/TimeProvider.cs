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

    // F015: injectable seam for tests/game code that needs a fake clock.
    // The static methods above stay as the default fast path (port of time.monotonic()).
    public static ITimeProvider Default { get; set; } = new SystemTimeProvider();
}

/// <summary>
/// F015: mockable clock abstraction. Production default is <see cref="SystemTimeProvider"/>
/// (monotonic stopwatch); tests can substitute a fake via <c>TimeProvider.Default = ...</c>.
/// </summary>
public interface ITimeProvider
{
    double MonotonicSeconds();
    long MonotonicMilliseconds();
    double Now();
}

public sealed class SystemTimeProvider : ITimeProvider
{
    public double MonotonicSeconds() => TimeProvider.MonotonicSeconds();
    public long MonotonicMilliseconds() => TimeProvider.MonotonicMilliseconds();
    public double Now() => TimeProvider.Now();
}
