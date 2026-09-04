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
    /// <summary>Load all rows as-no-tracking, swallowing DB errors (mirrors Python corrupt-row skip).</summary>
    public static List<TRow> LoadRows<TRow>(DbSet<TRow> set) where TRow : class
    {
        try { return set.AsNoTracking().ToList(); }
        catch { return []; }
    }

    /// <summary>Deserialize every <c>Data</c> row, invoking <paramref name="add"/> for each success.</summary>
    public static void LoadList<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch { return; }
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null) add(dto, row);
            }
            catch { }
        }
    }

    /// <summary>Lock-aware variant: holds <paramref name="lockObj"/> while invoking <paramref name="add"/>.</summary>
    public static void LoadInto<TRow, TDto>(DbSet<TRow> set, ReaderWriterLockSlim lockObj, Func<string, TDto?> deserialize, Action<TDto, TRow> add)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch { return; }
        var buffer = new List<(TDto dto, TRow row)>();
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null) buffer.Add((dto, row));
            }
            catch { }
        }
        lockObj.EnterWriteLock();
        try
        {
            foreach (var (dto, row) in buffer) { try { add(dto, row); } catch { } }
        }
        finally { lockObj.ExitWriteLock(); }
    }

    /// <summary>Load and deserialize without row context, returning list (for buffered copy patterns).</summary>
    public static List<TDto> LoadAll<TRow, TDto>(DbSet<TRow> set, Func<string, TDto?> deserialize)
        where TRow : class, IJsonEntity
    {
        List<TRow> rows;
        try { rows = set.AsNoTracking().ToList(); }
        catch { return []; }
        var outList = new List<TDto>();
        foreach (var row in rows)
        {
            try
            {
                var dto = deserialize(row.Data);
                if (dto != null) outList.Add(dto);
            }
            catch { }
        }
        return outList;
    }
}
