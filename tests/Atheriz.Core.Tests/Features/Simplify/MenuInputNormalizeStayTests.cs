using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared normalization + lookup for the sync/async handlers; the callback
// exception path (log + stay) is identical in both. A throwing callback logs
// "menu callback failed" and stays in BOTH paths.
[Collection("Ported")]
public class MenuInputNormalizeStayTests
{
    private static (string, List<Choice>) StayNode(MenuContext ctx)
        => ("text", [new Choice("key", "desc", cb: _ => throw new InvalidOperationException("boom"), stay: true)]);

    private static (string, List<Choice>) StayNodeAsync(MenuContext ctx)
        => ("text", [new Choice("key", "desc", cba: async _ => { await Task.Delay(1); throw new InvalidOperationException("boom"); }, stay: true)]);

    [Fact]
    public void SyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = new MenuEngine(null, StayNode);
        string log;
        using (var cap = new CaptureAtherizLog())
        {
            Assert.True(engine.HandleInput("  KEY  "));
            log = cap.Read();
        }
        Assert.Contains("menu callback failed", log);
        Assert.True(engine.HasNode);
    }

    [Fact]
    public async Task AsyncThrowingCallback_LogsAndStays()
    {
        using var env = GlobalTestEnv.Enter();
        Task<(string, List<Choice>)> Start(MenuContext ctx) => Task.FromResult(StayNodeAsync(ctx));
        var engine = new MenuEngine(null, Start);
        await engine.RenderAsync();
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
    public void UnknownKey_Stays()
    {
        using var env = GlobalTestEnv.Enter();
        var engine = new MenuEngine(null, StayNode);
        Assert.True(engine.HandleInput("nope"));
    }

    [Fact]
    public void InputPrefix_And_RunLoop_LiveInOneCoreEach()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Menu.cs");
        Assert.Equal(1, SourceScan.Count(src, "NormalizeKey(string"));
        Assert.Equal(1, SourceScan.Count(src, "TryGetChoice(string"));
        Assert.Equal(1, SourceScan.Count(src, "RunLoopAsync(MenuEngine"));
    }
}
