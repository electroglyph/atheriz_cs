using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Commands;

// CreationCooldownHelper + SessionPuppetHelper extraction + sync-stub parity.
public sealed class CreationHelperRegressionTests
{
    [Fact]
    public void Create_PasswordWithSpaces_JoinedNotTruncated()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        var conn = new FakeConnection();
        new CreateAccountCommand().Run(conn, "bob my secret words");
        var acc = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name == "bob").FirstOrDefault() as Account;
        Assert.NotNull(acc);
        Assert.True(acc!.CheckPassword("my secret words"));
        Assert.False(acc.CheckPassword("my"));
        SaltProvider.Clear();
    }

    [Fact]
    public void Create_ValidationFailure_ClearsCooldown()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        var conn = new TestConn("c1", "198.51.100.11");
        var cmd = new CreateAccountCommand();
        cmd.Run(conn, "bad name! x"); // invalid account name -> reservation must be released
        cmd.Run(conn, "goodname hunter22"); // same host: must NOT be rate-limited
        Assert.Contains(conn.Sent, s => s.Args.Any(a => a?.ToString()?.Contains("Account goodname created.") == true));
        SaltProvider.Clear();
    }

    [Fact]
    public void Guest_ValidationFailure_ClearsCooldown()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new TestConn("c2", "198.51.100.12");
        var cmd = new GuestCommand();
        cmd.Run(conn, ""); // empty name -> usage, reservation released
        cmd.Run(conn, "GuestOk M");
        Assert.NotNull(ObjectRegistry.FilterBy(o => o.Name == "GuestOk").FirstOrDefault());
    }

    [Fact]
    public void New_DescRemainder_Used()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        var acc = Account.Create("alice", "secret");
        ObjectRegistry.AddObject(acc);
        var conn = new FakeConnection();
        conn.Session.Account = acc;
        new NewCharacterCommand().Run(conn, "Hobbis M A tall figure");
        var ch = ObjectRegistry.FilterBy(o => o.Name == "Hobbis").FirstOrDefault() as GameObject;
        Assert.NotNull(ch);
        Assert.Equal("A tall figure", ch!.Desc);
        SaltProvider.Clear();
    }

    [Fact]
    public void Guest_SyncStub_AttachesPuppet()
    {
        using var env = GlobalTestEnv.Enter();
        var conn = new FakeConnection();
        new GuestCommand().Run(conn, "PupGuest M");
        Assert.NotNull(conn.Session.Puppet);
        Assert.Equal("PupGuest", conn.Session.Puppet!.Name);
    }
}
