using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Persistence;

/// <summary>
/// Object-row export: streams live read-only snapshots (never the clearing
/// path) to a JSON array file, one row at a time. Scope is the object table
/// only — map, node, and time rows live in their own tables and are not
/// included; a full world restore needs the database file. Import is
/// offline-only: it validates and returns DTOs without touching the live
/// world. Replacing the world must happen in a maintenance window (server
/// stopped), never beside live players — there is deliberately no live
/// import command.
/// </summary>
public static class WorldExport
{
    /// <summary>Streams every non-deleted registry object to <paramref name="path"/>. Returns the row count.</summary>
    public static int ExportToFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        List<GameObject> snapshot = Globals.ObjectRegistry.FilterBy(_ => true);
        int count = 0;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write('[');
        foreach (var obj in snapshot)
        {
            bool keep;
            try
            {
                obj.SyncRoot.EnterReadLock();
                try { keep = !obj.IsDeleted; }
                finally { obj.SyncRoot.ExitReadLock(); }
            }
            catch { continue; }
            if (!keep) continue;
            string json;
            try { json = GameObjectDtoSerializer.ToJson(obj.ToDto()); }
            catch (Exception ex) { throw new InvalidDataException($"Export failed serializing object {obj.Id}.", ex); }
            if (count > 0) writer.Write(',');
            writer.Write(json);
            count++;
        }
        writer.Write(']');
        return count;
    }

    /// <summary>
    /// Reads and validates an export file. Offline-only: mutating the live
    /// world from these DTOs (FromDto + ResolveRelations + registry swap)
    /// must happen while the server is stopped.
    /// </summary>
    public static List<GameObjectDto> ImportFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json = File.ReadAllText(path);
        List<GameObjectDto>? dtos;
        try { dtos = System.Text.Json.JsonSerializer.Deserialize<List<GameObjectDto>>(json, JsonOptions.Default); }
        catch (Exception ex) { throw new InvalidDataException($"Import file '{path}' is not a GameObjectDto list.", ex); }
        if (dtos is null) throw new InvalidDataException($"Import file '{path}' is not a GameObjectDto list.");
        HashSet<int> ids = [];
        foreach (var dto in dtos)
        {
            GameObjectDtoSerializer.Migrate(dto);
            if (dto.Id < 0) throw new InvalidDataException($"Import file '{path}' has a negative object id.");
            if (!ids.Add(dto.Id)) throw new InvalidDataException($"Import file '{path}' has a duplicate object id {dto.Id}.");
            if (dto.Type is not ("object" or "account" or "channel" or "script" or "node"))
                throw new InvalidDataException($"Import file '{path}' has unknown object type '{dto.Type}' (id {dto.Id}).");
        }
        return dtos;
    }
}
