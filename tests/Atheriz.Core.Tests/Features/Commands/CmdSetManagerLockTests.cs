using Atheriz.Core.Commands;
using Atheriz.Core.Network;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;

namespace Atheriz.Core.Tests.Features.Commands;

// CmdSet and ConnectionManager guards moved from recursive reader/writer
// locks to plain mutual-exclusion locks: concurrent mutation and reads must
// stay consistent (no lost registrations, no torn snapshots).
[Collection("Ported")]
public sealed class CmdSetManagerLockTests
{
    private sealed class LockCmd : Command
    {
        private readonly string _key;
        public LockCmd(string key) => _key = key;
        public override string Key => _key;
        public override bool UseParser => false;
        public override void Run(IMessageTarget caller, object? args) { }
    }

    [Fact]
    public void CmdSet_ParallelAddsAndReads_StaysConsistent()
    {
        var cs = new CmdSet();
        Parallel.For(0, 64, i => cs.Add(new LockCmd($"lockcmd{i:00}")));
        Assert.Equal(64, cs.Count);
        // Concurrent readers during further mutation must never observe a
        // torn snapshot; the sorted cache must match the key order.
        Parallel.For(0, 64, i =>
        {
            _ = cs.Get($"lockcmd{i:00}");
            _ = cs.GetAll().Count;
            _ = cs.GetSortedKeys().Count;
            if (i % 2 == 0) cs.Add(new LockCmd($"lockextra{i:00}"));
        });
        Assert.Equal(96, cs.Count);
        Assert.Equal(
            cs.GetKeys().OrderBy(k => k, StringComparer.Ordinal).ToList(),
            cs.GetSortedKeys().ToList());
    }

    [Fact]
    public void ConnectionManager_ParallelRegisterDisconnect_CountConsistent()
    {
        using var env = GlobalTestEnv.Enter();
        var mgr = PortedHelpers.MakeManager();
        try
        {
            Parallel.For(0, 32, i =>
            {
                var c = new TestConnection();
                Assert.True(mgr.RegisterConnection($"lockconn{i}", c));
            });
            Assert.Equal(32, mgr.ConnectionCount);
            var all = mgr.GetAllConnections();
            Assert.Equal(32, all.Count);
            Parallel.ForEach(all, c => mgr.Disconnect(c));
            Assert.Equal(0, mgr.ConnectionCount);
        }
        finally
        {
            ConnectionManager.GlobalInstance = null;
            mgr.Atp.Stop(wait: false);
        }
    }
}
