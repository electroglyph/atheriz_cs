using System.Text.Json;
using Atheriz.Core;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence;
using Atheriz.Core.Tests;
using Microsoft.Data.Sqlite;

namespace Atheriz.Core.Tests.Features.Simplify;

// Single prepared INSERT rebound per row: every migrated row keeps its own
// source, destination, and payload, and undecodable rows are still dropped.
[Collection("Ported")]
public class TransitionsSingleCommandTests
{
    [Fact]
    public void MigrateTransitionsTable_RebindsEveryRow_DropsUndecodable()
    {
        using var env = GlobalTestEnv.Enter();
        var dir = Path.Combine(Path.GetTempPath(), "atheriz-simplify-" + Guid.NewGuid().ToString("N"));
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
                cmd.CommandText = "DROP TABLE \"transitions\"; CREATE TABLE \"transitions\" (\"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT, PRIMARY KEY (\"ToArea\",\"ToX\",\"ToY\",\"ToZ\"))";
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
            using (var ctx = AtherizDbContextFactory.Create(dir))
            {
                AtherizDbContextFactory.MigrateTransitionsTable(ctx);
            }
            using (var conn = new SqliteConnection(cs))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT \"FromArea\",\"FromX\",\"FromY\",\"FromZ\",\"ToArea\",\"ToX\",\"ToY\",\"ToZ\",\"Data\" FROM \"transitions\" ORDER BY \"FromX\"";
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                Assert.Equal("A", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1));
                Assert.Equal("B", reader.GetString(4));
                Assert.Equal(2, reader.GetInt32(6));
                Assert.Equal(first, reader.GetString(8));
                Assert.True(reader.Read());
                Assert.Equal("C", reader.GetString(0));
                Assert.Equal(3, reader.GetInt32(1));
                Assert.Equal(4, reader.GetInt32(2));
                Assert.Equal(5, reader.GetInt32(3));
                Assert.Equal("D", reader.GetString(4));
                Assert.Equal(6, reader.GetInt32(5));
                Assert.Equal(7, reader.GetInt32(6));
                Assert.Equal(8, reader.GetInt32(7));
                Assert.Equal(second, reader.GetString(8));
                Assert.False(reader.Read());
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
