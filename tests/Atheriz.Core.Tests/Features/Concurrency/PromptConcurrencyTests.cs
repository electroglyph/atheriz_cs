using System.Collections.Concurrent;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Overlapping prompts resolve deterministically: the superseded prompt
// completes with "" and the live one with the client's answer.
[Collection("Ported")]
public class PromptConcurrencyTests
{
    [Fact]
    public async Task SequentialOverlap_SupersededPromptCompletesEmpty()
    {
        // A second Prompt (Session.cs:244-295) completes the previous future with
        // "" and installs its own; answering the live future resolves it.
        var conn = new FakeConnection("prompt-seq");
        var sess = conn.Session;
        var first = sess.Prompt("first");
        Assert.False(first.IsCompleted);
        var second = sess.Prompt("second");
        Assert.True(first.Wait(TimeSpan.FromSeconds(30)), "superseded prompt never completed");
        Assert.Equal("", await first);
        Assert.False(second.IsCompleted);
        Assert.NotNull(sess.InputFuture);
        sess.InputFuture!.TrySetResult("answer");
        Assert.True(second.Wait(TimeSpan.FromSeconds(30)), "live prompt never completed");
        Assert.Equal("answer", await second);
    }

    [Fact]
    public async Task BarrierParallelPrompts_ResolveEmptyPlusAnswer()
    {
        // Two barrier-released Prompts serialize on the session lock
        // (Session.cs:251-269): the loser still completes, exactly one prompt
        // with "" and the surviving future with the client's answer.
        var conn = new FakeConnection("prompt-par");
        var sess = conn.Session;
        using var barrier = new Barrier(3);
        var tasks = new Task<string>[2];
        var errors = new ConcurrentQueue<Exception>();
        var threads = Enumerable.Range(0, 2).Select(i =>
        {
            var thread = new Thread(() =>
            {
                try
                {
                    tasks[i] = sess.Prompt(i == 0 ? "zero" : "one");
                    Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })
            { IsBackground = true };
            return thread;
        }).ToList();
        threads.ForEach(t => t.Start());
        Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(30)));
        foreach (var thread in threads)
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "prompter did not finish; possible deadlock");
        Assert.Empty(errors);
        Assert.NotNull(tasks[0]);
        Assert.NotNull(tasks[1]);
        // The overwrite already happened inside the second Prompt's prefix, so
        // exactly one prompt completed with "" before the barrier released us.
        Assert.True(tasks[0]!.IsCompleted ^ tasks[1]!.IsCompleted);
        var done = tasks[0]!.IsCompleted ? tasks[0]! : tasks[1]!;
        var pending = tasks[0]!.IsCompleted ? tasks[1]! : tasks[0]!;
        Assert.Equal("", await done);
        Assert.NotNull(sess.InputFuture);
        sess.InputFuture!.TrySetResult("answer");
        Assert.True(pending.Wait(TimeSpan.FromSeconds(30)), "live prompt never completed");
        Assert.Equal("answer", await pending);
    }
}
