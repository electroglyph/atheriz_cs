using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// A cancelled prompt token resolves the pending prompt to empty promptly.
[Collection("Ported")]
public sealed class SessionPromptCancellationTests
{
    [Fact]
    public async Task Prompt_CancelledToken_CompletesPromptlyWithEmptyResult()
    {
        var session = new Session();
        using var cts = new CancellationTokenSource();
        var task = session.Prompt("batchd ask", false, cts.Token);
        Assert.False(task.IsCompleted);
        cts.Cancel();
        var result = await task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("", result);
        Assert.Null(session.InputFuture);
    }

    [Fact]
    public async Task Prompt_AlreadyCancelledToken_ReturnsEmptyWithoutHanging()
    {
        var session = new Session();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await session.Prompt("batchd pre", false, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("", result);
        Assert.Null(session.InputFuture);
    }
}
