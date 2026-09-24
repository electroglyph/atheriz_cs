using System.Diagnostics;

namespace Atheriz.Core.Utils;

public sealed class SystemTimeProvider : ITimeProvider
{
    // Reads the stopwatch directly (never the statics): this type IS the
    // system clock even while Default points at a fake.
    public double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    public long MonotonicMilliseconds() => (long)(MonotonicSeconds() * 1000.0);
    public double Now() => MonotonicSeconds();
}
