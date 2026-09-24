using System.ComponentModel.DataAnnotations;

namespace Atheriz.Core.Persistence.Entities;

/// <summary>
/// Crash-consistency journal for multi-table checkpoints.
/// A full checkpoint (AutosaveTick/SaveWorld) marks the row dirty BEFORE
/// writing tables and clean AFTER all commit. A dirty row at startup means
/// the previous checkpoint died mid-way: tables may be torn.
/// Single-table saves never touch this row.
/// </summary>
public sealed class CheckpointRow
{
    [Key]
    public int Id { get; set; }
    public string State { get; set; } = "clean";
    public string Token { get; set; } = "";
    public long SavedAtUnix { get; set; }
}
