using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Atheriz.Core.Tests")]

namespace Atheriz.Core.Utils;

/// <summary>
/// Central monotonic clock — deduplicates <c>Stopwatch.GetTimestamp</c>/<c>Frequency</c> usage
/// previously duplicated in <c>ThrottleWindow.MonotonicNow</c>, <c>BaseConnection.MonotonicSeconds</c>,
/// <c>GameTime.Stopwatch</c>, <c>MapEdit.GetMonotonic</c>, <c>ConnectionScreen.GetOnline</c> etc.
/// </summary>
public static class TimeProvider
{
    // The initial default instance. The statics take the Stopwatch fast path
    // while it is current (one ReferenceEquals, no dispatch); swapping
    // Default to a fake routes the statics through the seam, so test and
    // game clocks actually drive every reader.
    private static readonly SystemTimeProvider SystemDefault = new();

    public static double MonotonicSeconds() =>
        ReferenceEquals(Default, SystemDefault)
            ? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency
            : Default.MonotonicSeconds();

    public static long MonotonicMilliseconds() => (long)(MonotonicSeconds() * 1000.0);

    // Compat alias matching ThrottleWindow.Now naming
    public static double Now() => MonotonicSeconds();

    // F015: injectable seam for tests/game code that needs a fake clock.
    // Setter is internal (visible to tests via InternalsVisibleTo) so production
    // code cannot swap the global clock.
    public static ITimeProvider Default { get; internal set; } = SystemDefault;
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
    // Reads the stopwatch directly (never the statics): this type IS the
    // system clock even while Default points at a fake.
    public double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public long MonotonicMilliseconds() => (long)(MonotonicSeconds() * 1000.0);
    public double Now() => MonotonicSeconds();
}
