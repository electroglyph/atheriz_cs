using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Session prompt bookkeeping: a completed previous prompt is cleared (not
// re-completed), and disconnect cancels a pending prompt without throwing.
[Collection("Ported")]
public class SessionPromptCompletionTests
{
    [Fact]
    public async Task Prompt_WithCompletedPrev_InstallsFreshFuture()
    {
        var session = new Session();
        session.InputFuture = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.InputFuture.TrySetResult("old");
        try
        {
            var pending = session.Prompt("hello");
            Assert.True(session.CancelPrompt());
            Assert.Equal("", await pending);
        }
        finally { session.InputFuture = null; }
    }

    [Fact]
    public void AtDisconnect_CancelsPendingPrompt()
    {
        var session = new Session();
        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.InputFuture = tcs;

        session.AtDisconnect();

        Assert.True(tcs.Task.IsCanceled);
        Assert.Null(session.InputFuture);
    }
}
