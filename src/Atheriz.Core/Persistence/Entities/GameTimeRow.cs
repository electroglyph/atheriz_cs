using System.ComponentModel.DataAnnotations;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

public sealed class GameTimeRow : IJsonEntity
{
    [Key]
    public int Id { get; set; }
    public string Data { get; set; } = "";
}
