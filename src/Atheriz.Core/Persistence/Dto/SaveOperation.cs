namespace Atheriz.Core.Persistence.Dto;

/// <summary>
/// Typed save-checkpoint row: the object id plus its serialized DTO JSON.
/// The old <c>(sql, object[])</c> tuple erased the schema at every saver and
/// re-parsed parameter positions; the checkpoint path upserts through EF, so
/// raw SQL text has no consumer.
/// </summary>
public sealed record SaveOperation(int Id, string Json);

/// <summary>
/// Typed delete-checkpoint row: the object id whose row dies at the next
/// checkpoint. The old <c>("DELETE FROM objects WHERE id = ?", [Id])</c>
/// tuple carried SQL text no saver executed (deletes journal through
/// <c>ObjectRegistry.NoteDeleted</c> and the checkpoint drain); the id is the
/// whole contract, and <c>ObjectRegistry.DeleteObjects</c> already takes ids.
/// </summary>
public sealed record DeleteOperation(int Id);
