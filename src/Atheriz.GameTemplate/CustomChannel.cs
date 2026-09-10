// Port of atheriz/new.py:522 ("channel","Channel","atheriz.objects.base_channel")
#nullable enable
namespace MyGame;
/// <summary>Custom Channel — mirrors test/channel.py</summary>
public class CustomChannel : Channel
{
    public CustomChannel(int historyLimit = 50) : base(historyLimit) { }
}
