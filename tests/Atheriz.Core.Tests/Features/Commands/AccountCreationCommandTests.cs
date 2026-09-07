using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Commands;

// The sync socket path must enforce creation cooldowns just like the prompt flow
// (CreateAccountCommand.cs:14-47 vs :50-83, GuestCommand.cs:16-69 vs :70-133,
// NewCharacterCommand.cs:15-79 vs :80-145).
[Collection("Ported")]
public class AccountCreationCommandTests
{
    private static string SentText(TestConnection conn)
    {
        return string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));
    }

    [Fact]
    public void Create_SyncPath_RespectsCreationCooldown()
    {
        // A reserved cooldown must make the sync create refuse as rate-limited.
        using var env = GlobalTestEnv.Enter();
        try
        {
            var settings = AtherizSettings.Global;
            settings.CreationCooldown = 60;
            var conn = new TestConnection("c1") { ClientHost = "1.2.3.4" };
            double now = Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            Assert.True(ObjectRegistry.TryReserveCreationCooldown("account", "1.2.3.4", now, settings.CreationCooldown));
            var job = CommandDispatcher.ResolveUnloggedIn(conn, "create coolacc1 password123");
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var text = SentText(conn);
            Assert.Contains("rate-limited", text, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals("coolacc1", StringComparison.OrdinalIgnoreCase)));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void Guest_SyncPath_RespectsCreationCooldown()
    {
        // Same cooldown rule covers the sync guest path.
        using var env = GlobalTestEnv.Enter();
        try
        {
            var settings = AtherizSettings.Global;
            settings.CreationCooldown = 60;
            var conn = new TestConnection("c2") { ClientHost = "5.6.7.8" };
            double now = Atheriz.Core.Utils.TimeProvider.MonotonicSeconds();
            Assert.True(ObjectRegistry.TryReserveCreationCooldown("guest", "5.6.7.8", now, settings.CreationCooldown));
            var job = CommandDispatcher.ResolveUnloggedIn(conn, "guest tmpguest1");
            Assert.NotNull(job);
            job!.Func(job.Caller, job.Args);
            var text = SentText(conn);
            Assert.Contains("rate-limited", text, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(ObjectRegistry.FilterBy(o => o.IsPc && o.Name.Equals("tmpguest1", StringComparison.OrdinalIgnoreCase)));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
