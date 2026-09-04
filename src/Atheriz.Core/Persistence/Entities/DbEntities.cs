using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

/// <summary>
/// EF Core entities mirroring <c>atheriz/database_setup.py:do_setup</c> tables.
/// Data columns store JSON (System.Text.Json) instead of dill BLOBs.
/// </summary>
public sealed class ObjectRow : IJsonEntity
{
    [Key]
    public int Id { get; set; }
    // JSON of GameObjectDto
    public string Data { get; set; } = "";
    // Optional type discriminator for queryable filtering (replaces scan)
    public string Type { get; set; } = "object";
    public int Version { get; set; } = 1;
}

public sealed class MapDataRow : IJsonEntity
{
    public string Area { get; set; } = "";
    public int Z { get; set; }
    public string Data { get; set; } = "";
}

public sealed class AreaRow : IJsonEntity
{
    [Key]
    public string Name { get; set; } = "";
    public string Data { get; set; } = "";
}

public sealed class TransitionRow : IJsonEntity
{
    public string ToArea { get; set; } = "";
    public int ToX { get; set; }
    public int ToY { get; set; }
    public int ToZ { get; set; }
    public string Data { get; set; } = "";
}

public sealed class DoorRow : IJsonEntity
{
    public string Area { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public string Data { get; set; } = "";
}

public sealed class GameTimeRow : IJsonEntity
{
    [Key]
    public int Id { get; set; }
    public string Data { get; set; } = "";
}
