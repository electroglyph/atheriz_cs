using System.Reflection;
using System.Text.Json;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;
using Microsoft.Data.Sqlite;

namespace Atheriz.Core.Tests.Features.Simplify3;

// The three DDL commands share one helper (the rebound single-INSERT loop is
// untouched) and the ordinal getters hoist one lookup while still throwing on
// unknown columns.
[Collection("Ported")]
public class TransitionsMigrationDdlTests
{
    [Fact]
    public void MigrateTransitionsTable_SnakeCaseColumns_MigratesCountsAndDropsUndecodable()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            AtherizDbContextFactory.DoSetup(dir);
            var first = JsonSerializer.Serialize(
                new Transition(new Coord("A", 0, 1, 0), new Coord("B", 0, 2, 0), "door"),
                JsonOptions.Default);
            var second = JsonSerializer.Serialize(
                new Transition(new Coord("C", 3, 4, 5), new Coord("D", 6, 7, 8), "gate"),
                JsonOptions.Default);
            var cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "database.sqlite3") }.ToString();
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DROP TABLE \"transitions\"; CREATE TABLE \"transitions\" (\"to_area\" TEXT NOT NULL, \"to_x\" INTEGER NOT NULL, \"to_y\" INTEGER NOT NULL, \"to_z\" INTEGER NOT NULL, \"data\" TEXT, PRIMARY KEY (\"to_area\",\"to_x\",\"to_y\",\"to_z\"))";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('B',0,2,0,@d)";
                cmd.Parameters.AddWithValue("@d", first);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('D',6,7,8,@d)";
                cmd.Parameters.AddWithValue("@d", second);
                cmd.ExecuteNonQuery();
                cmd.Parameters.Clear();
                cmd.CommandText = "INSERT INTO \"transitions\" VALUES ('Z',9,9,9,@d)";
                cmd.Parameters.AddWithValue("@d", "not json at all");
                cmd.ExecuteNonQuery();
            }
            string log;
            using (var cap = new CaptureAtherizLog())
            {
                using (var ctx = AtherizDbContextFactory.Create(dir))
                {
                    AtherizDbContextFactory.MigrateTransitionsTable(ctx);
                }
                log = cap.Read();
            }
            Assert.Contains("migrated 2/3 rows, dropped 1 undecodable", log);
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT \"FromArea\",\"FromX\",\"FromY\",\"FromZ\",\"ToArea\",\"ToX\",\"ToY\",\"ToZ\",\"Data\" FROM \"transitions\" ORDER BY \"FromX\"";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("A", reader.GetString(0));
                Assert.Equal("B", reader.GetString(4));
                Assert.Equal(first, reader.GetString(8));
                Assert.True(reader.Read());
                Assert.Equal("C", reader.GetString(0));
                Assert.Equal(5, reader.GetInt32(3));
                Assert.Equal("D", reader.GetString(4));
                Assert.Equal(second, reader.GetString(8));
                Assert.False(reader.Read());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void MigrationOrdinalLookup_KnownColumn_ReturnsValue_UnknownColumn_ThrowsKeyNotFound()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 AS one";
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        var ord = new Dictionary<string, int>(StringComparer.Ordinal) { ["one"] = 0 };
        var getStr = typeof(AtherizDbContextFactory).GetMethod("GetStr", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(getStr);
        Assert.Equal("1", getStr!.Invoke(null, [reader, ord, "one"]));
        var ex = Assert.Throws<TargetInvocationException>(() => getStr.Invoke(null, [reader, ord, "missing"]));
        Assert.IsType<KeyNotFoundException>(ex.InnerException);
    }

    [Fact]
    public void MigrationDdl_ThreeSitesShareHelper_InsLoopUntouched()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Persistence", "AtherizDbContextFactory.cs");
        Assert.Equal(3, SourceScan.Count(src, "ExecuteNonQuery(conn, txn,"));
        Assert.Equal(3, SourceScan.Count(src, "ord[col]"));
        Assert.Contains("ins.ExecuteNonQuery();", src);
        Assert.Contains("Every parameter is rebound", src);
    }
}
