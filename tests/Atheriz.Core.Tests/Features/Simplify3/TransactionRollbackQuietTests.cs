using System.Reflection;
using Atheriz.Core.Persistence;
using Atheriz.Core.Persistence.Entities;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.Data.Sqlite;

namespace Atheriz.Core.Tests.Features.Simplify3;

// Both rollback swallows route through one helper; the tracker clear stays on
// the busy-retry path only, and rollback/onRollback failures never mask the
// save error under handling.
[Collection("Ported")]
public class TransactionRollbackQuietTests
{
    [Fact]
    public void WithGateAndTransaction_BusyRetry_ReaddsSameKeyAfterTrackerClear()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        var busy = MakeBusyException();
        int runs = 0;
        DbTransactionHelper.WithGateAndTransaction(db, d =>
        {
            runs++;
            d.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" });
            if (runs == 1) throw new Exception("locked", busy);
        });
        Assert.Equal(2, runs);
        Assert.NotNull(db.GameTime.Find(0));
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public void WithGateAndTransaction_ThrowingOnRollback_PropagatesOriginalError()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = AtherizDbContextFactory.CreateForTests();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            DbTransactionHelper.WithGateAndTransaction(db,
                d =>
                {
                    d.GameTime.Add(new GameTimeRow { Id = 0, Data = "{}" });
                    throw new InvalidOperationException("boom");
                },
                onRollback: () => throw new InvalidOperationException("rollback-hook blew up")));
        Assert.Equal("boom", ex.Message);
        Assert.False(DbWriteGate.IsHeld);
    }

    [Fact]
    public void RollbackQuiet_SingleSwallowSite_RetryOnlyClear()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "DbTransactionHelper.cs");
        Assert.Contains("private static void RollbackQuiet(", src);
        Assert.Equal(1, SourceScan.Count(src, "tx.Rollback()"));
        Assert.Equal(2, SourceScan.Count(src, "RollbackQuiet(tx)"));
        Assert.Equal(1, SourceScan.Count(src, "ChangeTracker.Clear()"));
    }

    // SqliteException ctor visibility differs by version; prefer the public
    // (string, int) ctor, else fall back to non-public reflection (tests may reflect; prod may not).
    private static Exception MakeBusyException()
    {
        var t = typeof(SqliteException);
        var ctor = t.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c =>
            {
                var p = c.GetParameters();
                return p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(int);
            });
        Assert.True(ctor != null, "No (string,int) SqliteException ctor found");
        return (Exception)ctor!.Invoke(["database is locked", 5]);
    }
}
