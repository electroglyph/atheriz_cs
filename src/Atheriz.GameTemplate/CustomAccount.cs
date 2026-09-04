// Port of atheriz/new.py:522 ("account","Account","atheriz.objects.base_account")
#nullable enable
namespace Atheriz.GameTemplate;
using Atheriz.Core.Objects;
/// <summary>Custom Account — mirrors test/account.py</summary>
public class CustomAccount : Account
{
    public CustomAccount() : base() { }
    public override void AtCreate()
    {
        base.AtCreate();
    }

    public override bool AtDelete(GameObject caller)
    {
        return base.AtDelete(caller);
    }

    public override void AtDisconnect()
    {
        base.AtDisconnect();
    }

    public override bool AtPrePuppet(GameObject character)
    {
        return base.AtPrePuppet(character);
    }
}
