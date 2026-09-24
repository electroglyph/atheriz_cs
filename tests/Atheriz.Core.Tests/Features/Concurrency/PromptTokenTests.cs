using System.Collections.Concurrent;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// PromptWithToken returns the prompt task together with the token owning the
// InputFuture slot, so a timeout cancels exactly its own prompt: superseded
// tokens refuse, and a superseding prompt is never cancelled by an older one.
[Collection("Ported")]
public class PromptTokenTests
{
    [Fact]
    public async Task PromptWithToken_TokenIsLiveSlot_CancelCancelsExactlyIt()
    {
        var conn = new TestConnection("prompt-token-live");
        var sess = conn.Session;
        var (task, token) = sess.PromptWithToken("ask");
        Assert.Same(sess.InputFuture, token);
        Assert.False(task.IsCompleted);
        Assert.True(sess.CancelPrompt(token));
        Assert.Equal("", await task);
        // Slot is clear: a repeat cancel reports nothing to cancel.
        Assert.False(sess.CancelPrompt(token));
    }

    [Fact]
    public async Task PromptWithToken_SupersededToken_RefusesCancel()
    {
        var conn = new TestConnection("prompt-token-superseded");
        var sess = conn.Session;
        var (first, token1) = sess.PromptWithToken("first");
        var (second, token2) = sess.PromptWithToken("second");
        // Supersede completes the older prompt with "".
        Assert.Equal("", await first);
        // The stale token no longer owns the slot.
        Assert.False(sess.CancelPrompt(token1));
        Assert.True(sess.CancelPrompt(token2));
        Assert.Equal("", await second);
    }

    [Fact]
    public async Task MenuPrompt_SupersededMenuPrompt_DoesNotCancelNewerPrompt()
    {
        // The menu prompt is superseded before its timeout fires: it resolves
        // with the supersede answer while the newer prompt stays pending —
        // the timeout path must not cancel a prompt it does not own.
        var conn = new TestConnection("prompt-token-menu");
        var sess = conn.Session;
        var menuTask = Atheriz.Core.MenuPrompt.PromptWithTimeout(sess, "pick", TimeSpan.FromMinutes(5));
        var (late, lateToken) = sess.PromptWithToken("late");
        Assert.Equal("", await menuTask);
        Assert.False(late.IsCompleted);
        Assert.True(sess.CancelPrompt(lateToken));
        Assert.Equal("", await late);
    }

    [Fact]
    public async Task MenuPrompt_Timeout_CancelsItsOwnPrompt()
    {
        // Unsuperseded menu prompt: the timeout cancels exactly it, leaving
        // no orphaned InputFuture behind.
        var conn = new TestConnection("prompt-token-timeout");
        var sess = conn.Session;
        var result = await Atheriz.Core.MenuPrompt.PromptWithTimeout(sess, "pick", TimeSpan.FromMilliseconds(50));
        Assert.Null(result);
        Assert.Null(sess.InputFuture);
    }

    [Fact]
    public void PromptWithToken_ConcurrentPrompts_EveryPromptCompletes()
    {
        // Parallel prompts serialize on the session lock; each task completes
        // (superseded ones with "") and the slot ends owned by exactly one.
        var conn = new TestConnection("prompt-token-race");
        var sess = conn.Session;
        var errors = new ConcurrentQueue<Exception>();
        var results = new ConcurrentBag<(Task<string> Task, object Token)>();
        Parallel.For(0, 8, _ =>
        {
            try { results.Add(sess.PromptWithToken("q")); }
            catch (Exception ex) { errors.Enqueue(ex); }
        });
        Assert.Empty(errors);
        Assert.Equal(8, results.Count);
        var winner = results.First(r => ReferenceEquals(sess.InputFuture, r.Token));
        foreach (var (task, token) in results)
        {
            if (!ReferenceEquals(token, winner.Token))
                Assert.True(task.IsCompleted, "superseded prompt never completed");
        }
        Assert.True(sess.CancelPrompt(winner.Token));
    }
}
