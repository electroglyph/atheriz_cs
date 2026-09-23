namespace Atheriz.Core.Persistence;

/// <summary>
/// Marker for EF rows that store JSON in a <c>Data</c> column.
/// Allows generic <see cref="DbTransactionHelper.UpsertJson{T}"/> to operate on any table.
/// </summary>
public interface IJsonEntity
{
    string Data { get; set; }
}
