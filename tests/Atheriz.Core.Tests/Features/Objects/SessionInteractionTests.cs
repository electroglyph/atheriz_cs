using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Objects;

// Behavior pins for session interaction and presence: prompt-with-timeout
// yields null on negative or elapsed timeouts, and the online/known counts
// come from a stable non-negative short-TTL cache.
[Collection("Ported")]
public class SessionInteractionTests
{
    [Fact]
    public async Task MenuPrompt_NegativeTimeout_ReturnsNull()
    {
        // Negative timeout: CancellationTokenSource throws, the bare catch
        // converts it to null (documented; change deliberately if ever fixed).
        var session = new Session();
        var result = await MenuPrompt.PromptWithTimeoutAsync(session, "display", TimeSpan.FromSeconds(-1));
        Assert.Null(result);
    }

    [Fact]
    public async Task MenuPrompt_ShortTimeout_ReturnsNull()
    {
        // No input arrives: the timeout elapses and the prompt yields null.
        var session = new Session();
        var result = await MenuPrompt.PromptWithTimeoutAsync(session, "display", TimeSpan.FromMilliseconds(50));
        Assert.Null(result);
    }

    [Fact]
    public void ConnectionScreen_Online_IsStableAndNonNegative()
    {
        // 5 s TTL cache: back-to-back reads agree and never go negative.
        var (o1, k1) = ConnectionScreen.GetOnline();
        var (o2, k2) = ConnectionScreen.GetOnline();
        Assert.True(o1 >= 0 && k1 >= 0);
        Assert.Equal((o1, k1), (o2, k2));
        Assert.False(string.IsNullOrWhiteSpace(ConnectionScreen.Render()));
    }
}
