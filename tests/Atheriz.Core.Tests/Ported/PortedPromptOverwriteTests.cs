// Port of atheriz/tests/test_prompt_overwrite.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPromptOverwriteTests
{
    [Fact]
    public async Task ConcurrentPromptsBothResolve()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new FakeConnection("prompt_test");
        var sess = conn.Session;
        // Simulate overlapping prompts: first prompt then second before first resolves
        var firstTask = sess.Prompt("first");
        await Task.Delay(10);
        var secondTask = sess.Prompt("second");
        await Task.Delay(10);
        // Resolve via input future (which now holds second)
        // Our Prompt implementation should have completed first with empty string when second started
        var secondResult = await Task.WhenAny(secondTask, Task.Delay(500)) == secondTask ? await secondTask : null;
        // Actually we drive via Text handler: simulate client sending answer
        // For this test, we directly set result on current future
        if (!secondTask.IsCompleted)
        {
            sess.InputFuture?.TrySetResult("answer");
            var res = await secondTask.WaitAsync(TimeSpan.FromSeconds(1));
            Assert.Equal("answer", res);
        }
        // First should have been completed with "" due to overwrite logic
        var firstRes = await firstTask.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("", firstRes);
    }
}
