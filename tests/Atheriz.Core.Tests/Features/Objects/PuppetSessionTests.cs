using System.Reflection;
using Atheriz.Core;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Session and puppet identity: AccountId tracking and self-puppet checks.
[Collection("Ported")]
public class PuppetSessionTests
{
    private static void SetId(GameObject o, int id)
    {
        var f = typeof(GameObject).GetField("_id", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(f);
        f!.SetValue(o, id);
    }

    private static void RegisterAll(params GameObject[] objs)
    {
        foreach (var o in objs)
            if (ObjectRegistry.Get(o.Id).Count == 0)
                ObjectRegistry.AddObject(o);
    }

    // --- Session/puppet identity ---

    [Fact]
    public void Session_AccountSwap_UpdatesAccountId()
    {
        // Session AccountId tracks later Account swaps instead of staying at
        // the constructor value.
        var a1 = new Account();
        SetId(a1, 501);
        var a2 = new Account();
        SetId(a2, 502);
        var s = new Session(connection: null, account: a1);
        Assert.Equal(501, s.AccountId);
        s.Account = a2;
        Assert.Equal(502, s.AccountId);
    }

    [Fact]
    public void Puppet_SameIdDistinctInstance_RefusedAsSelf()
    {
        // Self-puppeting is refused even for a distinct instance with the same
        // Id (e.g. after a reload rewire).
        ObjectRegistry.ClearAll();
        try
        {
            var caller = GameObject.Create("hero", isPc: true, privilege: Privilege.Admin);
            var twin = GameObject.Create("hero-twin");
            SetId(twin, caller.Id);
            RegisterAll(caller, twin);
            var session = new Session();
            Assert.False(caller.Puppet(session, twin));
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
