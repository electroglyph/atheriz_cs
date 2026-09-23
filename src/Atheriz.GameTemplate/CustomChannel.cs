#nullable enable
namespace MyGame;
/// <summary>Custom Channel — mirrors test/channel.py</summary>
public class CustomChannel : Channel
{
    public CustomChannel(int historyLimit = 50) : base(historyLimit) { }
}
