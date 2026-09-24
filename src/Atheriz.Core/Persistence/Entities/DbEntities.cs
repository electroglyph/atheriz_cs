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
