using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Concurrency;

// Concurrency regression pins: write-gate same-flow nesting, ban-during-hash
// refusal, cooldown foreign-clear keep, ticker remove-before-fire,
// instance-scoped db close, ambient-transaction serialization, migration
// idempotence. Real engine paths; no mocks.
[Collection("Ported")]
public sealed class ThreadSafetyRegression6Tests
{
    // A second same-flow take nests instead of clobbering the claim,
    // and a nested take inside a live lease never hangs the flow.
    [Fact]
    public async Task WriteGate_ConcurrentSameFlowTakes_NestWithoutHang()
    {
        using var env = GlobalTestEnv.Enter();
        var gate = new ManualResetEventSlim(false);
        var holderDone = new ManualResetEventSlim(false);
        var entered = new ManualResetEventSlim(false);
        var holder = Task.Run(() =>
        {
            DbWriteGate.Enter();
            entered.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
            DbWriteGate.Exit();
            holderDone.Set();
        });
        Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
        var t1 = DbWriteGate.EnterAsync();
        var t2 = DbWriteGate.EnterAsync();
        gate.Set();
        DbWriteGate.WriteHold? h1 = await t1.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var h2 = await t2.WaitAsync(TimeSpan.FromSeconds(10));
            try
            {
                Task<DbWriteGate.WriteHold>? nestedTask = null;
                Exception? nestedEx = null;
                try { nestedTask = DbWriteGate.EnterAsync(); }
                catch (Exception ex) { nestedEx = ex; }
                // Post-fix the nested take settles immediately: Nested on
                // the holder thread, fail-fast fork-refuse elsewhere. Pre-fix
                // it pends on the live lease (self-deadlock inside using).
                bool settledFast = nestedEx is not null || (nestedTask is not null && nestedTask.IsCompleted);
                Assert.True(settledFast);
                if (nestedTask is not null)
                {
                    h1?.Dispose();
                    h1 = null;
                    var nh = await nestedTask.WaitAsync(TimeSpan.FromSeconds(10));
                    nh.Dispose();
                }
                else
                {
                    Assert.IsType<InvalidOperationException>(nestedEx);
                }
            }
            finally { h2.Dispose(); }
            h1?.Dispose();
            h1 = null;
        }
        catch
        {
            h1?.Dispose();
            h1 = null;
            throw;
        }
        await holder.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(holderDone.IsSet);
        Assert.Equal(1, DbWriteGate.Semaphore.CurrentCount);
    }

    // A ban landing mid-hash binds nothing.
    [Fact]
    public async Task ConnectCommand_BanDuringHash_RefusesBind()
    {
        using var env = GlobalTestEnv.Enter();
        var account = Account.Create("d2victim", "d2password123");
        var conn = new TestConnection("d2login");
        var pa = new ConnectCommand().Parser!.ParseArgs(["d2victim", "d2password123"]);
        var login = Task.Run(() => new ConnectCommand().Run(conn, pa));
        await Task.Delay(2);
        account.IsBanned = true;
        account.BanReason = "race";
        await login.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Null(conn.Session.Account);
        Assert.Contains(conn.Sent, s => s.Args.Any(a => a?.ToString()?.Contains("banned") == true));
    }

    // A foreign clear never lifts another drain's reservation.
    [Fact]
    public void Cooldown_ForeignClear_KeepsReservation()
    {
        using var env = GlobalTestEnv.Enter();
        const string host = "10.9.9.9";
        CreationCooldownStore.Clear();
        try
        {
            var t1 = CreationCooldownStore.TryReserveCreationCooldown("character", host, 1000.0, 60.0);
            Assert.NotNull(t1);
            CreationCooldownStore.ClearCreationCooldown(host, Guid.NewGuid());
            Assert.Null(CreationCooldownStore.TryReserveCreationCooldown("character", host, 1001.0, 60.0));
            CreationCooldownStore.ClearCreationCooldown(host, t1.Value);
            Assert.NotNull(CreationCooldownStore.TryReserveCreationCooldown("character", host, 1001.0, 60.0));
        }
        finally { CreationCooldownStore.Clear(); }
    }

    // A coro removed before its queued fire never runs.
    [Fact]
    public async Task Ticker_RemoveBeforeQueuedFire_NeverRuns()
    {
        using var env = GlobalTestEnv.Enter();
        using var pool = new AsyncThreadPool(maxThreads: 1, queueLimit: 1000, reliefLimit: 0);
        var park = new ManualResetEventSlim(false);
        Assert.True(pool.AddTask(() => park.Wait(TimeSpan.FromSeconds(15)), "park"));
        var ticker = new AsyncTicker(pool);
        try
        {
            var interval = TimeSpan.FromMilliseconds(20);
            int fires = 0;
            Action body = () => Interlocked.Increment(ref fires);
            ticker.AddCoro(body, interval);
            var slot = ticker.GetSlot(interval.TotalSeconds);
            Assert.NotNull(slot);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (slot!.PendingCount < 1 && sw.Elapsed < TimeSpan.FromSeconds(10))
                await Task.Delay(10);
            Assert.True(slot.PendingCount >= 1, "tick never queued behind the parked pool");
            ticker.RemoveCoro(body, interval);
            park.Set();
            await Task.Delay(500);
            Assert.Equal(0, Volatile.Read(ref fires));
        }
        finally { ticker.Clear(); park.Set(); pool.Stop(); }
    }

    // Closing one context never poisons the process for others.
    [Fact]
    public void DbContext_InstanceClose_DoesNotKillProcess()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_d8_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            using (var ctxA = new AtherizDbContext(dir))
            {
                ctxA.Close();
                Assert.True(ctxA.IsInstanceClosed);
            }
            Assert.False(AtherizDbContext.IsClosed);
            using (var ctxB = new AtherizDbContext(dir))
            {
                Assert.False(ctxB.IsInstanceClosed);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Ambient-transaction callers exclude each other under the gate.
    [Fact]
    public async Task AmbientTransaction_ConcurrentCallers_Serialized()
    {
        using var env = GlobalTestEnv.Enter();
        using var db1 = AtherizDbContextFactory.CreateForTests();
        using var db2 = AtherizDbContextFactory.CreateForTests();
        using var tx1 = db1.Database.BeginTransaction();
        using var tx2 = db2.Database.BeginTransaction();
        int inside = 0;
        bool overlapped = false;
        bool heldSeen = false;
        void Work(AtherizDbContext db)
        {
            if (DbWriteGate.IsHeld) heldSeen = true;
            if (Interlocked.Increment(ref inside) != 1) overlapped = true;
            Thread.Sleep(50);
            Interlocked.Decrement(ref inside);
        }
        using var barrier = new Barrier(2);
        var w1 = Task.Run(() => { barrier.SignalAndWait(); DbTransactionHelper.WithGateAndTransaction(db1, Work); });
        var w2 = Task.Run(() => { barrier.SignalAndWait(); DbTransactionHelper.WithGateAndTransaction(db2, Work); });
        await Task.WhenAll([w1, w2]).WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(heldSeen);
        Assert.False(overlapped);
    }

    // Planting a legacy table then running setup twice both succeed.
    [Fact]
    public void DoSetup_LegacyTransitionsTable_MigratesIdempotently()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_f18_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var dbPath = Path.Combine(dir, "database.sqlite3");
            using (var sqlite = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                sqlite.Open();
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "CREATE TABLE \"transitions\" (\"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT)";
                cmd.ExecuteNonQuery();
            }
            AtherizDbContextFactory.DoSetup(dir);
            AtherizDbContextFactory.DoSetup(dir);
            using (var sqlite = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                sqlite.Open();
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('transitions') WHERE lower(name)='fromarea'";
                Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
