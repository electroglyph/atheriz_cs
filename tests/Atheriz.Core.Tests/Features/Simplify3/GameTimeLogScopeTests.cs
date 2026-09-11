using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify3;

// All nine GameTime log sites share one category spelling: no site may keep
// the copy-pasted DTO name as its category.
[Collection("Ported")]
public class GameTimeLogScopeTests
{
    [Fact]
    public void GameTime_LogCategory_UsesSharedScope()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "GameTime.cs");
        Assert.Contains("private const string LogScope = \"GameTime\";", src);
        Assert.Equal(0, SourceScan.Count(src, "\"GameTimePersistDto\""));
        Assert.Equal(9, SourceScan.Count(src, ", LogScope)"));
    }
}
