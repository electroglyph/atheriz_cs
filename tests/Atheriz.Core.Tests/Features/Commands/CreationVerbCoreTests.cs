using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// Per-verb sync/async cores preserve each verb's messages and pairing: the
// account verb validates + creates with no puppet tail, while guest/new
// share the uniqueness check and the attach-and-home tail.
[Collection("Ported")]
public sealed class CreationVerbCoreTests
{
    private static string SentText(TestConnection conn)
        => string.Join("\n", conn.Sent.SelectMany(t => t.Args.Select(a => a?.ToString() ?? "")));

    // Hermetic gate state: Run checks the dispatch snapshot AND Global, so
    // both are forced open here and restored afterwards.
    private sealed class GateScope : IDisposable
    {
        private readonly bool _oa, _oc, _og;
        public GateScope()
        {
            var g = AtherizSettings.Global;
            _oa = g.AccountCreationEnabled; _oc = g.CharCreationEnabled; _og = g.GuestEnabled;
            g.AccountCreationEnabled = true; g.CharCreationEnabled = true; g.GuestEnabled = true;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
        public void Dispose()
        {
            var g = AtherizSettings.Global;
            g.AccountCreationEnabled = _oa; g.CharCreationEnabled = _oc; g.GuestEnabled = _og;
            CommandDispatcher.SetSettings(new AtherizSettings());
        }
    }

    [Fact]
    public void Create_Succeeds_WithNoPuppetTail()
    {
        using var env = GlobalTestEnv.Enter();
        using var gates = new GateScope();
        SaltProvider.SetSalt("testsalt");
        try
        {
            var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "coreacc1 hunter22");
            Assert.Contains("Account coreacc1 created.", SentText(conn));
            Assert.NotNull(conn.Session.Account);
            Assert.Equal("coreacc1", conn.Session.Account!.Name);
            Assert.Null(conn.Session.Puppet);
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void Guest_Succeeds_AndPuppets()
    {
        using var env = GlobalTestEnv.Enter();
        using var gates = new GateScope();
        var conn = new TestConnection();
        new GuestCommand().Run(conn, "CoreGuestA");
        Assert.Contains("Guest CoreGuestA created.", SentText(conn));
        Assert.NotNull(conn.Session.Puppet);
        Assert.Equal("CoreGuestA", conn.Session.Puppet!.Name);
    }

    [Fact]
    public void New_Succeeds_AndPuppets()
    {
        using var env = GlobalTestEnv.Enter();
        using var gates = new GateScope();
        SaltProvider.SetSalt("testsalt");
        try
        {
            var acc = Account.Create("coreacc2", "hunter22");
            ObjectRegistry.AddObject(acc);
            var conn = new TestConnection();
            conn.Session.Account = acc;
            new NewCharacterCommand().Run(conn, "CoreCharA");
            Assert.Contains("Character CoreCharA created.", SentText(conn));
            Assert.NotNull(conn.Session.Puppet);
            Assert.Equal("CoreCharA", conn.Session.Puppet!.Name);
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void Guest_DuplicateName_ReportsExists()
    {
        using var env = GlobalTestEnv.Enter();
        using var gates = new GateScope();
        new GuestCommand().Run(new TestConnection(), "CoreGuestB");
        var second = new TestConnection();
        new GuestCommand().Run(second, "CoreGuestB");
        Assert.Contains("Character with this name (CoreGuestB) already exists.", SentText(second));
    }

    [Fact]
    public void New_DuplicateName_ReportsExists()
    {
        using var env = GlobalTestEnv.Enter();
        using var gates = new GateScope();
        SaltProvider.SetSalt("testsalt");
        try
        {
            var acc = Account.Create("coreacc3", "hunter22");
            ObjectRegistry.AddObject(acc);
            var first = new TestConnection();
            first.Session.Account = acc;
            new NewCharacterCommand().Run(first, "CoreCharB");
            var second = new TestConnection();
            second.Session.Account = acc;
            new NewCharacterCommand().Run(second, "CoreCharB");
            Assert.Contains("Character with this name (CoreCharB) already exists.", SentText(second));
        }
        finally { SaltProvider.Clear(); }
    }
}
