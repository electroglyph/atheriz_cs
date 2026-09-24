using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// An already-cancelled token ends the guest creation wizard quietly:
// prompts resolve to empty, validation refuses, nothing throws or hangs.
[Collection("Ported")]
public sealed class GuestWizardCancellationTests
{
    [Fact]
    public async Task GuestRunAsync_CancelledToken_ReturnsWithoutThrowing()
    {
        using var env = GlobalTestEnv.Enter();
        var orig = AtherizSettings.Global.GuestEnabled;
        AtherizSettings.Global.GuestEnabled = true;
        try
        {
            var conn = new TestConnection("batchd-guest");
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ex = await Record.ExceptionAsync(() =>
                new GuestCommand().RunAsync(conn, cts.Token).WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Null(ex);
            Assert.Null(conn.Session.InputFuture);
        }
        finally { AtherizSettings.Global.GuestEnabled = orig; }
    }

    [Fact]
    public async Task GuestRunAsync_CancelledToken_ReturnsPromptly()
    {
        // An already-cancelled token ends the guest wizard quietly: the first
        // prompt resolves to "" like a cancelled prompt, name validation
        // refuses it, and the wizard returns without throwing or hanging.
        using var env = GlobalTestEnv.Enter();
        var g = AtherizSettings.Global;
        bool og = g.GuestEnabled;
        g.GuestEnabled = true;
        CommandDispatcher.SetSettings(new AtherizSettings());
        try
        {
            var conn = new TestConnection();
            var cmd = new GuestCommand();
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var ctx = new CommandContext(conn, null, null, "", cts.Token);
            await cmd.RunAsync(ctx, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));
            var sent = string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
            Assert.Contains("Name cannot be empty.", sent);
            Assert.Null(conn.Session.Puppet);
        }
        finally
        {
            g.GuestEnabled = og;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }
}
