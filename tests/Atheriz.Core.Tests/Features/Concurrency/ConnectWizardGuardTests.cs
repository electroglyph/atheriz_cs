using System.Text.Json;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Audit 10 finding 16: two `connect` lines on one socket must not start two
// character-selection wizards. The second is refused while one is in flight;
// without the guard the two loops force-complete each other's prompt slot
// with "" and flood "Invalid choice." with no player input. Neuter (drop the
// TryStartWizard gate) removes the refusal and the send count explodes.
[Collection("Ported")]
public sealed class ConnectWizardGuardTests
{
    private static string Line(string account) =>
        JsonSerializer.Serialize(new object[] { "text", new[] { $"connect {account} pw" }, Array.Empty<object>() });

    private static bool SaysInProgress(TestConnection conn) =>
        conn.Sent.Any(t => t.Args.Count > 0 && t.Args[0] is string s &&
            s.Contains("already in progress", StringComparison.Ordinal));

    [Fact]
    public void TwoConnectLines_SecondRefused_NoFlood()
    {
        using var env = GlobalTestEnv.Enter();
        var secret = Path.Combine(env.TempPath, "secret");
        Directory.CreateDirectory(secret);
        SaltProvider.ReseedForGame(secret);
        var pool = new AsyncThreadPool(1, 1000, 2);
        try
        {
            var account = Account.Create("pin16acct", "pw");
            var pc = GameObject.Create("pin16char", isPc: true, privilege: Privilege.Admin);
            ObjectRegistry.AddObject(pc);
            account.TryAddCharacter(pc, 5);

            var mgr = new ConnectionManager(pool: pool);
            using var conn = new TestConnection();
            mgr.HandleCommand(conn, Line(account.Name));
            mgr.HandleCommand(conn, Line(account.Name));

            Assert.True(PortedHelpers.WaitFor(() => SaysInProgress(conn), 5000));
            Thread.Sleep(500);
            Assert.True(conn.Sent.Count < 100);

            // Answer the first wizard so its task ends and the token clears.
            conn.Session.InputFuture?.TrySetResult("0");
            Assert.True(PortedHelpers.WaitFor(() => conn.Session.TryStartWizard(), 5000));
            conn.Session.EndWizard();
        }
        finally
        {
            try { pool.Stop(wait: false); } catch { }
            SaltProvider.Clear();
        }
    }
}
