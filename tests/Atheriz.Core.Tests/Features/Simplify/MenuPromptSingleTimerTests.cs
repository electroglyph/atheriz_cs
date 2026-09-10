using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// One armed timer (the delay) instead of two; the timeout path still releases
// the prompt future, and the success path releases the delay. The sync alias
// stays: the parity suite and external game code call that spelling.
[Collection("Ported")]
public class MenuPromptSingleTimerTests
{
    [Fact]
    public async Task Timeout_ReturnsNull_ReleasesFuture()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var r = await MenuPrompt.PromptWithTimeoutAsync(s, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(r);
        Assert.True(s.InputFuture == null || s.InputFuture.Task.IsCompleted);
    }

    [Fact]
    public async Task SyncAlias_ForwardsToAsync()
    {
        using var env = GlobalTestEnv.Enter();
        var s = new Session();
        var r = await MenuPrompt.PromptWithTimeout(s, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(r);
        Assert.True(s.InputFuture == null || s.InputFuture.Task.IsCompleted);
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeout"));
        Assert.NotNull(typeof(MenuPrompt).GetMethod("PromptWithTimeoutAsync"));
    }

    [Fact]
    public void SingleTimer_KeepsCancelPrompt_ReleasesDelay()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "MenuPrompt.cs");
        var region = SourceScan.Region(src, "public static async Task<string?> PromptWithTimeoutAsync(");
        Assert.DoesNotContain("new CancellationTokenSource(timeout)", region);
        Assert.Contains("session.CancelPrompt(pending)", region);
        Assert.Contains("CancelAsync()", region);
    }
}
