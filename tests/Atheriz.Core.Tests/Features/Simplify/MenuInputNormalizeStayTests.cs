using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared normalization + lookup for input handling; a throwing callback logs
// "menu callback failed" and stays.
[Collection("Ported")]
public class MenuInputNormalizeStayTests
{
    private static Task<(string, List<Choice>)> StayNode(MenuContext ctx)
        => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("key", "desc", callback: _ => Task.FromException(new InvalidOperationException("boom")), stay: true)]));

    private static Task<(string, List<Choice>)> StayNodeAsync(MenuContext ctx)
        => Task.FromResult<(string, List<Choice>)>(("text", [new Choice("key", "desc", callback: async _ => { await Task.Delay(1); throw new InvalidOperationException("boom"); }, stay: true)]));

    private static async Task<MenuEngine> Rendered(Func<MenuContext, Task<(string, List<Choice>)>> start)
    {
        var engine = new MenuEngine(null, start);
        await engine.RenderAsync();
        return engine;
    }

    [Fact]
    public async Task SyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNode);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            Assert.True(await engine.HandleInputAsync("  KEY  "));
            log = cap.Read();
        }
        Assert.Contains("menu callback failed", log);
        Assert.True(engine.HasNode);
    }

    [Fact]
    public async Task AsyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNodeAsync);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            Assert.True(await engine.HandleInputAsync("  KEY  "));
            log = cap.Read();
        }
        Assert.Contains("menu callback failed", log);
        Assert.True(engine.HasNode);
    }

    [Fact]
    public async Task UnknownKey_Stays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = await Rendered(StayNode);
        Assert.True(await engine.HandleInputAsync("nope"));
    }

    [Fact]
    public void InputPrefix_And_RunLoop_LiveInOneCoreEach()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "NormalizeKey(string"));
        Assert.Equal(1, SourceScan.Count(src, "TryGetChoice(string"));
        Assert.DoesNotContain("RunLoopAsync(", src);
        Assert.DoesNotContain("Task.Run(", src);
        Assert.DoesNotContain("class MenuRunner", src);
        Assert.DoesNotContain("public bool HandleInput(", src);
    }
}
