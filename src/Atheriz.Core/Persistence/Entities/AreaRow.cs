using System.ComponentModel.DataAnnotations;
using Atheriz.Core.Persistence;

namespace Atheriz.Core.Persistence.Entities;

public sealed class AreaRow : IJsonEntity
{
    [Key]
    public string Name { get; set; } = "";
    public string Data { get; set; } = "";
}
