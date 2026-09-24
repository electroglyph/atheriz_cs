namespace Atheriz.Core.Persistence.Dto;

public sealed record LockDefDto
{
    public string Name { get; set; } = ""; // e.g. "view", "get", "delete", "puppet"
    public List<Atheriz.Core.Objects.LockPolicies.LockPolicy> Policies { get; set; } = []; // declarative, e.g. Builder, NotSelf
}
