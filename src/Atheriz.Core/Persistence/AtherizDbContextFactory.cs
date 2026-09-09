using Atheriz.Core.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Factory porting <c>atheriz/database_setup.py</c> reopen pattern.
/// Mirrors <c>get_database / reopen_database / do_setup</c> with static <c>_CLOSED</c> guard.
/// </summary>
public static class AtherizDbContextFactory
{
    public static bool IsClosed => AtherizDbContext.IsClosed;

    // Port of database_setup.py:45 reopen_database() — clears _CLOSED for reset command (atheriz.py:1474)
    public static void ReopenDatabase() => AtherizDbContext.ReopenDatabase();

    // Alias for Python Database.close() marking closed
    public static void CloseDatabase() => AtherizDbContext.CloseDatabase();

    // Port of database_setup.py:56 get_database() — creates context with guard and directory ensure.
    // Guard violations (bad save path) PROPAGATE: silently substituting an
    // ephemeral :memory: database makes writes succeed and go nowhere.
    // Tests needing memory use CreateForTests() explicitly.
    public static AtherizDbContext Create(string savePath)
    {
        // Guard mirrors get_database raising if _CLOSED
        if (IsClosed) throw new InvalidOperationException("database is closed; refusing to reopen");
        return new AtherizDbContext(savePath);
    }

    // Parameterless: same default-path resolution as ObjectRegistry.SaveObjects
    // (ATHERIZ_SAVE_PATH else configured SavePath) — never a divergent file.
    public static AtherizDbContext Create()
    {
        var savePath = Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? AtherizSettings.Global.SavePath;
        return Create(savePath);
    }

    // Settings-aware creation : explicit settings win over
    // the ambient Global, but the test-device env override still comes first
    // (same precedence as ObjectRegistry.SaveObjects).
    public static string ResolveSavePath(AtherizSettings settings) =>
        Environment.GetEnvironmentVariable("ATHERIZ_SAVE_PATH") ?? settings.SavePath;
    public static AtherizDbContext CreateForSettings(AtherizSettings settings) =>
        Create(ResolveSavePath(settings));

    // Port of test helper: in-memory or temp file context
    public static AtherizDbContext CreateForTests(string? savePath = null)
    {
        if (savePath is null)
        {
            var opts = new DbContextOptionsBuilder<AtherizDbContext>().UseSqlite("Data Source=:memory:").Options;
            var ctx = new AtherizDbContext(opts);
            ctx.Database.OpenConnection();
            ctx.Database.EnsureCreated();
            return ctx;
        }
        return Create(savePath);
    }

    // Port of database_setup.py:92 do_setup() — EnsureCreated + seed gametime id 0 if missing
    public static void DoSetup(string savePath)
    {
        using var ctx = Create(savePath);
        DoSetup(ctx);
    }

    public static void DoSetup(AtherizDbContext ctx)
    {
        ctx.EnsureCreated();
        // no unguarded WAL re-send here. EnsureCreated applies the
        // pragmas under the write gate; re-sending journal_mode outside the
        // gate raced concurrent saves.
        // No gametime row seed (database_setup.py:92-111 do_setup creates
        // tables only): GameTime.Save upserts, and a seeded "{}" row would
        // shadow legacy save/time migration (time.py:72-74 migrates only
        // when the row is missing).
        MigrateTransitionsTable(ctx);
    }

    // Destination-only transitions tables (Python database_setup.py:105 PK,
    // mirrored by older C# builds) lack the source columns the fan-in PK
    // needs: EF queries referencing them fail. Rebuild the table, decoding
    // each row's source from its Data JSON (which carries the full
    // Transition incl. FromCoord). Undecodable rows (e.g. Python dill
    // blobs — a separate format boundary, JSON vs dill) are dropped with a
    // loud warning; the row loader skips them anyway.
    public static void MigrateTransitionsTable(AtherizDbContext ctx)
    {
        DbWriteGate.Enter();
        try
        {
            var conn = ctx.Database.GetDbConnection();
            bool wasClosed = conn.State == System.Data.ConnectionState.Closed;
            if (wasClosed) conn.Open();
            try
            {
                bool hasTable = false, hasSource = false;
                using var pragma = conn.CreateCommand();
                pragma.CommandText = "PRAGMA table_info(\"transitions\")";
                using var r = pragma.ExecuteReader();
                while (r.Read())
                {
                    hasTable = true;
                    if (NormCol(r.GetString(1)) == "fromarea")
                        hasSource = true;
                }
                if (!hasTable || hasSource) return;
                List<(string ToArea, int ToX, int ToY, int ToZ, string? Data)> rows = [];
                using var sel = conn.CreateCommand();
                // SELECT * with normalized ordinals: old tables come in
                // Python snake_case (to_area) or C# PascalCase (ToArea).
                sel.CommandText = "SELECT * FROM \"transitions\"";
                using var r2 = sel.ExecuteReader();
                var ord = new System.Collections.Generic.Dictionary<string, int>(StringComparer.Ordinal);
                while (r2.Read())
                {
                    if (ord.Count == 0)
                        for (int i = 0; i < r2.FieldCount; i++) ord[NormCol(r2.GetName(i))] = i;
                    rows.Add((GetStr(r2, ord, "toarea"), GetInt(r2, ord, "tox"), GetInt(r2, ord, "toy"), GetInt(r2, ord, "toz"), GetMaybeStr(r2, ord, "data")));
                }
                int dropped = 0;
                using var txn = conn.BeginTransaction();
                using var mk = conn.CreateCommand();
                mk.Transaction = txn;
                mk.CommandText = "CREATE TABLE \"transitions_new\" (\"FromArea\" TEXT NOT NULL, \"FromX\" INTEGER NOT NULL, \"FromY\" INTEGER NOT NULL, \"FromZ\" INTEGER NOT NULL, \"ToArea\" TEXT NOT NULL, \"ToX\" INTEGER NOT NULL, \"ToY\" INTEGER NOT NULL, \"ToZ\" INTEGER NOT NULL, \"Data\" TEXT, PRIMARY KEY (\"FromArea\",\"FromX\",\"FromY\",\"FromZ\",\"ToArea\",\"ToX\",\"ToY\",\"ToZ\"))";
                mk.ExecuteNonQuery();
                foreach (var row in rows)
                {
                    Objects.Transition? t = null;
                    try
                    {
                        if (!string.IsNullOrEmpty(row.Data))
                            t = System.Text.Json.JsonSerializer.Deserialize<Objects.Transition>(row.Data, JsonOptions.Default);
                    }
                    catch { t = null; }
                    if (t is null) { dropped++; continue; }
                    using var ins = conn.CreateCommand();
                    ins.Transaction = txn;
                    ins.CommandText = "INSERT OR IGNORE INTO \"transitions_new\" VALUES (@fa,@fx,@fy,@fz,@ta,@tx,@ty,@tz,@d)";
                    AddParam(ins, "@fa", t.FromCoord.Area);
                    AddParam(ins, "@fx", t.FromCoord.X);
                    AddParam(ins, "@fy", t.FromCoord.Y);
                    AddParam(ins, "@fz", t.FromCoord.Z);
                    AddParam(ins, "@ta", row.ToArea);
                    AddParam(ins, "@tx", row.ToX);
                    AddParam(ins, "@ty", row.ToY);
                    AddParam(ins, "@tz", row.ToZ);
                    AddParam(ins, "@d", (object?)row.Data ?? DBNull.Value);
                    ins.ExecuteNonQuery();
                }
                using var drop = conn.CreateCommand();
                drop.Transaction = txn;
                drop.CommandText = "DROP TABLE \"transitions\"";
                drop.ExecuteNonQuery();
                using var ren = conn.CreateCommand();
                ren.Transaction = txn;
                ren.CommandText = "ALTER TABLE \"transitions_new\" RENAME TO \"transitions\"";
                ren.ExecuteNonQuery();
                txn.Commit();
                AtherizLogger.LogWarning($"MigrateTransitionsTable: rebuilt destination-only table, migrated {rows.Count - dropped}/{rows.Count} rows, dropped {dropped} undecodable.");
            }
            finally { if (wasClosed) conn.Close(); }
        }
        finally { DbWriteGate.Exit(); }
    }

    // Column-name normalization for migrations: Python snake_case
    // (to_area) and C# PascalCase (ToArea) spell the same column.
    private static string NormCol(string name) =>
        name.Replace("_", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string GetStr(System.Data.Common.DbDataReader r, System.Collections.Generic.Dictionary<string, int> ord, string col)
        => r.IsDBNull(ord[col]) ? "" : r.GetString(ord[col]);
    private static string? GetMaybeStr(System.Data.Common.DbDataReader r, System.Collections.Generic.Dictionary<string, int> ord, string col)
        => r.IsDBNull(ord[col]) ? null : r.GetString(ord[col]);
    private static int GetInt(System.Data.Common.DbDataReader r, System.Collections.Generic.Dictionary<string, int> ord, string col)
        => r.IsDBNull(ord[col]) ? 0 : Convert.ToInt32(r.GetValue(ord[col]));

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    public static async Task DoSetupAsync(string savePath, CancellationToken ct = default)
    {
        await using var ctx = Create(savePath);
        await ctx.EnsureCreatedAsync(ct);
        // no unguarded WAL re-send (see sync DoSetup above).
        // No gametime row seed — parity with sync DoSetup above (and Python
        // do_setup, tables-only): GameTime.Save upserts, and a seeded "{}" row
        // would shadow legacy save/time migration (time.py:72-74 migrates only
        // when the row is missing). The old check-then-add also raced
        // concurrent setups into PK conflicts.
        // Destination-only transitions tables migrate here too, same as sync
        // DoSetup: without it an async boot leaves the old shape behind and
        // EF queries referencing the source columns fail.
        MigrateTransitionsTable(ctx);
    }

    // Parameterless overload using default settings SavePath
    public static void DoSetup()
    {
        var savePath = AtherizSettings.Global.SavePath;
        DoSetup(savePath);
    }
}
