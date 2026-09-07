// Port of atheriz/globals/* load pattern (AsNoTracking + Deserialize + lock)
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Deduplicates the 3× <c>AsNoTracking().ToList() + Deserialize + Lock.EnterWriteLock</c>
/// boilerplate in <c>NodeHandler.Load</c> (3 tables), <c>MapHandler.Load</c>,
/// <c>ObjectRegistry.LoadObjects</c>, <c>GameTime.Load</c>.
/// </summary>
public static class JsonTableLoader
{
    /// <summary>Load all rows as-no-tracking. DB errors are logged (not silent): an empty world and a corrupt DB must be distinguishable.</summary>
    public static List<TRow> LoadRows<TRow>(DbSet<TRow> set) where TRow : class
    {
        try { return set.AsNoTracking().ToList(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"LoadRows<{typeof(TRow).Name}> failed; loading as empty.", ex);
            return [];
        }
    }

    /// <summary>Deserialize every <c>Data</c> row, invoking <paramref name="add"/> for each success. Skips are counted and logged.</summary>
    public static void LoadList<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"LoadList<{typeof(TRow).Name}> query failed; loading as empty.", ex);
            return;
        }
        int bad = 0, failed = 0;
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null)
                {
                    try { add(dto, row); }
                    catch (Exception ex) { failed++; AtherizLogger.LogDebug($"Suppressed JsonTableLoader.LoadList<{typeof(TRow).Name}> add: {ex.Message}", "JsonTableLoader"); }
                }
                else bad++;
            }
            catch (Exception) { bad++; }
        }
        if (bad > 0 || failed > 0)
            AtherizLogger.LogWarning($"LoadList<{typeof(TRow).Name}>: {rows.Count} rows, skipped {bad} corrupt, {failed} add-failures.");
    }

    /// <summary>Lock-aware variant: holds <paramref name="lockObj"/> while invoking <paramref name="add"/>. Skips are counted and logged.</summary>
    public static void LoadInto<TRow, TDto>(DbSet<TRow> set, ReaderWriterLockSlim lockObj, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"LoadInto<{typeof(TRow).Name}> query failed; loading as empty.", ex);
            return;
        }
        var buffer = new List<(TDto dto, TRow row)>();
        int bad = 0;
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null) buffer.Add((dto, row));
                else bad++;
            }
            catch (Exception) { bad++; }
        }
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
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch (Exception ex)
        {
            AtherizLogger.LogError($"LoadAll<{typeof(TRow).Name}> query failed; loading as empty.", ex);
            return [];
        }
        var outList = new List<TDto>();
        int bad = 0;
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null) outList.Add(dto);
                else bad++;
            }
            catch (Exception) { bad++; }
        }
        if (bad > 0)
            AtherizLogger.LogWarning($"LoadAll<{typeof(TRow).Name}>: {rows.Count} rows, skipped {bad} corrupt.");
        return outList;
    }
}
