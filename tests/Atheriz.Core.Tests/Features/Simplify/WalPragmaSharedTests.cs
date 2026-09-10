using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared PRAGMA core: sync + async creation paths apply identical pragmas,
// with WAL journal mode active on the resulting database.
[Collection("Ported")]
public class WalPragmaSharedTests
{
    private static string ReadJournalMode(AtherizDbContext db)
    {
        var conn = db.Database.GetDbConnection();
        if (conn.State == System.Data.ConnectionState.Closed) conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode";
        return cmd.ExecuteScalar()?.ToString() ?? "";
    }

    [Fact]
    public void EnsureCreated_AppliesWalPragmas()
    {
        using var env = GlobalTestEnv.Enter();
        using var db = new AtherizDbContext(env.TempPath);
        db.EnsureCreated();
        Assert.True(File.Exists(Path.Combine(env.TempPath, "database.sqlite3")));
        Assert.Equal("wal", ReadJournalMode(db), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureCreatedAsync_AppliesWalPragmas()
    {
        using var env = GlobalTestEnv.Enter();
        await using var db = new AtherizDbContext(env.TempPath);
        await db.EnsureCreatedAsync();
        Assert.True(File.Exists(Path.Combine(env.TempPath, "database.sqlite3")));
        Assert.Equal("wal", ReadJournalMode(db), StringComparer.OrdinalIgnoreCase);
    }
}
