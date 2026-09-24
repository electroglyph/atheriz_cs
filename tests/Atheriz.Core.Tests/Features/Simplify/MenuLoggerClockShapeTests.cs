using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One async menu system, one real log sink, one clock name: no sync/async
// duality, no null logger pair, no BCL type collision.
public class MenuLoggerClockShapeTests
{
    [Fact]
    public void Menu_IsAsyncOnly()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.DoesNotContain("GotoSync", src);
        Assert.DoesNotContain("CallbackSync", src);
        Assert.DoesNotContain("CurrentNodeSync", src);
        Assert.DoesNotContain("class MenuRunner", src);
        Assert.DoesNotContain("static Task RunMenu(", src);
        Assert.DoesNotContain("Task.Run(", src);
        Assert.Contains("public async Task<bool> HandleInputAsync(", src);
        Assert.Contains("public async Task<bool> RunAsync(", src);
    }

    [Fact]
    public void Menu_SingleChoiceShape()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "public sealed class Choice"));
        Assert.Contains("Func<MenuContext, Task<(string, List<Choice>)>>? Goto", src);
        Assert.Contains("Func<MenuContext, Task>? Callback", src);
    }

    [Fact]
    public void Logger_HasRealSink_NoNullPair()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Logger.cs");
        Assert.Contains("AtherizSinkProvider", src);
        Assert.DoesNotContain("NullLoggerProvider", src);
        Assert.DoesNotContain("NullLogger", src);
    }

    [Fact]
    public void Clock_IsGameClock_NotTimeProvider()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Utils", "GameClock.cs");
        Assert.Contains("public static class GameClock", src);
        Assert.False(File.Exists("/home/anon/atheriz-cs/src/Atheriz.Core/Utils/TimeProvider.cs"));
    }
}
