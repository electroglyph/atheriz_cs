// Port of atheriz/database_setup.py:do_setup Data column (JSON string)
namespace Atheriz.Core.Persistence;

/// <summary>
/// Marker for EF rows that store JSON in a <c>Data</c> column.
/// Allows generic <see cref="DbTransactionHelper.UpsertJson{T}"/> to operate on any table.
/// </summary>
public interface IJsonEntity
{
    string Data { get; set; }
}
