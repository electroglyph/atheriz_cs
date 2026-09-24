namespace Atheriz.Core.Commands;

public interface ILagInfo
{
    bool IsLagged { get; }
    int Level { get; }
}
