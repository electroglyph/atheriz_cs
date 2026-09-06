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
        Assert.False(GameUtils.IsIter("s"));
        Assert.False(GameUtils.IsIter(null));
        Assert.True(GameUtils.IsIter(new[] { 1, 2 }));
        Assert.Equal(new object?[] { 5 }, GameUtils.MakeIter(5));
        Assert.Equal(new object?[] { "s" }, GameUtils.MakeIter("s"));
        Assert.Equal("x", GameUtils.Detach("x"));
        GameUtils.EnsureThreadSafe(typeof(GameObject));
    }
}
