using Atheriz.Core.Commands;
using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The Session.Account setter owns the AccountId mirror, so a successful login
// leaves Account and AccountId in agreement with no follow-up rewrite.
[Collection("Ported")]
public class ConnectAccountMirrorTests
{
    [Fact]
    public void ConnectCommand_SuccessfulLogin_SessionAccountAndIdAgree()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        try
        {
            var acc = Account.Create("mirror_user", "correct");
            ObjectRegistry.AddObject(acc);
            var conn = new FakeConnection();
            conn.ClientHost = "9.9.9.9";
            conn.Session.Puppet = GameObject.Create("hero");
            var cmd = new ConnectCommand();
            var parsed = new GameArgumentParser.ParsedArgs();
            parsed["account_name"] = "mirror_user";
            parsed["password"] = "correct";
            cmd.Run(conn, parsed);

            Assert.Same(acc, conn.Session.Account);
            Assert.Equal(acc.Id, conn.Session.AccountId);
            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void SessionAccountSetter_SwapAndClear_KeepsAccountIdInSync()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var session = new Session();
            var acc = new Account { Name = "setter_user" };
            acc.Id = 51001;

            session.Account = acc;
            Assert.Same(acc, session.Account);
            Assert.Equal(acc.Id, session.AccountId);

            var other = new Account { Name = "other_user" };
            other.Id = acc.Id + 1;
            session.Account = other;
            Assert.Equal(other.Id, session.AccountId);

            session.Account = null;
            Assert.Null(session.Account);
            Assert.Null(session.AccountId);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
