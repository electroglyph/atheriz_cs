namespace Atheriz.Core.Utils;

/// <summary>
/// F015: mockable clock abstraction. Production default is <see cref="SystemTimeProvider"/>
/// (monotonic stopwatch); tests can substitute a fake via <c>GameClock.Default = ...</c>.
/// </summary>
public interface ITimeProvider
{
    double MonotonicSeconds();
    long MonotonicMilliseconds();
    double Now();
}
