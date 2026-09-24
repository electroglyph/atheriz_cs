// Port of atheriz/tests/test_run_menu_timeout.py:1
using Atheriz.Core;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedRunMenuTimeoutTests
{
    private static Task<(string, List<Choice>)> Start(MenuContext c) =>
        Task.FromResult<(string, List<Choice>)>(("Choose", new List<Choice> { new("1", "One", null) }));

    [Fact] public async Task RunMenu_ReturnsWhenSessionDead_Timeout()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        // Prompt never resolves - we simulate by making Prompt hang via InputFuture not completed
        // Use Menu timeout logic: session.Prompt will not return until InputFuture completed; we leave it pending
        var engine = new MenuEngine(session, Start);
        // Run with timeout - RunAsync has internal timeout, should return quickly
        var task = engine.RunAsync(session, TimeSpan.FromSeconds(1));
        var completed = await Task.WhenAny(task, Task.Delay(3000));
        Assert.True(completed == task || task.IsCompleted, "run_menu parked forever");
    }
    [Fact] public async Task RunMenu_ReturnsWhenPromptCancelled()
    {
        using var env = GlobalTestEnv.Enter();
        var session = new Session();
        var engine = new MenuEngine(session, Start);
        var runTask = engine.RunAsync(session, TimeSpan.FromSeconds(60));
        await Task.Delay(100);
        session.AtDisconnect(); // cancels prompt via InputFuture cancellation
        var completed = await Task.WhenAny(runTask, Task.Delay(2000));
        Assert.True(runTask.IsCompleted, "run_menu did not exit after cancellation");
    }
    [Fact] public void MenuTimeout_DefaultIs60()
    {
        using var env = GlobalTestEnv.Enter();
        var def = new Atheriz.Core.Settings.AtherizSettings().MenuPromptTimeout;
        Assert.Equal(60, def);
    }
}
