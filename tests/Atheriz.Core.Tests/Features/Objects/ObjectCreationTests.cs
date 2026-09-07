using Atheriz.Core.Objects;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Objects;

// GameObject.Create must leave a consistent object: the map twins stay in
// sync (setters assign both) and the map timestamp uses the monotonic clock
// like the ctor and AtMapUpdate do (GameObject.cs:640-651 vs :140,157 and
// :81 vs :217).
[Collection("Ported")]
public class ObjectCreationTests
{
    [Fact]
    public void Create_MapTwinsAgree()
    {
        // Correct: a fresh non-mapable object reports MapEnabled == IsMapable.
        var o = GameObject.Create("rock");
        Assert.Equal(o.IsMapable, o.MapEnabled);
    }

    [Fact]
    public void Create_LastMapTime_UsesMonotonicClock()
    {
        // Correct: LastMapTime is monotonic seconds, so NTP/suspend jumps
        // never skew map staleness per construction path.
        var o = GameObject.Create("rock");
        Assert.NotNull(o.LastMapTime);
        double skew = Math.Abs(Atheriz.Core.Utils.TimeProvider.MonotonicSeconds() - o.LastMapTime.Value);
        Assert.InRange(skew, 0.0, 60.0);
    }
}
