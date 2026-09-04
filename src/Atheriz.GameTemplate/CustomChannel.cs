// Port of atheriz/new.py:522 ("channel","Channel","atheriz.objects.base_channel")
#nullable enable
namespace Atheriz.GameTemplate;
using Atheriz.Core.Objects;
/// <summary>Custom Channel — mirrors test/channel.py</summary>
public class CustomChannel : Channel
{
    public CustomChannel(int historyLimit = 50) : base(historyLimit) { }
    public override void AtCreate()
    {
        base.AtCreate();
    }

    public override bool AtDelete(GameObject? caller)
    {
        return base.AtDelete(caller);
    }
}
