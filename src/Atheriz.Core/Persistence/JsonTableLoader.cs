// Port of atheriz/globals/* load pattern (AsNoTracking + Deserialize + lock)
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Deduplicates the 3× <c>AsNoTracking().ToList() + Deserialize + Lock.EnterWriteLock</c>
/// boilerplate in <c>NodeHandler.Load</c> (3 tables), <c>MapHandler.Load</c>,
/// <c>ObjectRegistry.LoadObjects</c>, <c>GameTime.Load</c>.
/// </summary>
public static class JsonTableLoader
{
    private static List<TRow> TryQuerySet<TRow>(DbSet<TRow> set, string op) where TRow : class
    {
        try { return set.AsNoTracking().ToList(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"{op}<{typeof(TRow).Name}> query failed; loading as empty.", ex);
            return [];
        }
    }
    /// <summary>Load all rows as-no-tracking. DB errors are logged (not silent): an empty world and a corrupt DB must be distinguishable.</summary>
    public static List<TRow> LoadRows<TRow>(DbSet<TRow> set) where TRow : class
    {
        return TryQuerySet(set, nameof(LoadRows));
    }

    /// <summary>Deserialize every <c>Data</c> row, invoking <paramref name="add"/> for each success. Skips are counted and logged.</summary>
    public static void LoadList<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        var (buffer, bad, rows) = DeserializeBuffer(set, deserialize, nameof(LoadList));
        int failed = 0;
        foreach (var (dto, row) in buffer)
        {
            try { add(dto, row); }
            catch (Exception ex) { failed++; AtherizLogger.LogDebug($"Suppressed JsonTableLoader.LoadList<{typeof(TRow).Name}> add: {ex.Message}", "JsonTableLoader"); }
        }
        if (bad > 0 || failed > 0)
            AtherizLogger.LogWarning($"LoadList<{typeof(TRow).Name}>: {rows.Count} rows, skipped {bad} corrupt, {failed} add-failures.");
    }

    /// <summary>Lock-aware variant: holds <paramref name="lockObj"/> while invoking <paramref name="add"/>. Skips are counted and logged.</summary>
    public static void LoadInto<TRow, TDto>(DbSet<TRow> set, ReaderWriterLockSlim lockObj, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        // Buffer outside the lock, add under it: deserializing while holding the
        // write lock would stall every reader for the whole parse.
        var (buffer, bad, rows) = DeserializeBuffer(set, deserialize, nameof(LoadInto));
        int failed = 0;
        lockObj.EnterWriteLock();
        try
        {
            foreach (var (dto, row) in buffer) { try { add(dto, row); } catch (Exception ex) { failed++; AtherizLogger.LogDebug($"Suppressed JsonTableLoader.LoadInto<{typeof(TRow).Name}> add: {ex.Message}", "JsonTableLoader"); } }
        }
        finally { lockObj.ExitWriteLock(); }
        if (bad > 0 || failed > 0)
            AtherizLogger.LogWarning($"LoadInto<{typeof(TRow).Name}>: {rows.Count} rows, skipped {bad} corrupt, {failed} add-failures.");
    }

    /// <summary>Load and deserialize without row context, returning list (for buffered copy patterns). Skips are counted and logged.</summary>
    public static List<TDto> LoadAll<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize)
        where TRow : class, IJsonEntity
    {
        var (buffer, bad, rows) = DeserializeBuffer(set, deserialize, nameof(LoadAll));
        List<TDto> outList = new(buffer.Count);
        foreach (var (dto, _) in buffer) outList.Add(dto);
        if (bad > 0)
            AtherizLogger.LogWarning($"LoadAll<{typeof(TRow).Name}>: {rows.Count} rows, skipped {bad} corrupt.");
        return outList;
    }

    // Shared query + deserialize + bad-count core for the three loaders above.
    // Only the add phase differs per caller, so each caller adds from the
    // returned buffer in its own way (LoadInto adds under its write lock).
    // Each method's log name rides along so warnings keep their origin.
    private static (List<(TDto dto, TRow row)> buffer, int bad, List<TRow> rows) DeserializeBuffer<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize, string op)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows = TryQuerySet(set, op);
        List<(TDto dto, TRow row)> buffer = [];
        int bad = 0;
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto is not null) buffer.Add((dto, row));
                else bad++;
            }
            catch (Exception) { bad++; }
        }
        return (buffer, bad, rows);
    }
}
