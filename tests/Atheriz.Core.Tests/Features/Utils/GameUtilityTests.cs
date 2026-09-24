using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Utils;

// Behavior pins for pure collection helpers: iteration checks,
// iteration wrapping, detach, and thread-safety marking.
[Collection("Ported")]
public class GameUtilityTests
{
    [Fact]
    public void GameUtils_IterHelpers()
    {
        Assert.Equal(new[] { 5 }, GameUtils.MakeIter(5));
        Assert.Equal(new[] { "s" }, GameUtils.MakeIter("s"));
        Assert.Equal(new object[] { 1, 2 }, GameUtils.MakeIter<object>(new object[] { 1, 2 }));
        Assert.Equal("x", GameUtils.Detach("x"));
        GameUtils.EnsureThreadSafe(typeof(GameObject));
    }
}
