using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// The public virtual At* surface stays virtual (no flip to a non-virtual
// template-method), and the must-call-base contract holds — an override
// that skips base (and Hookable) drops installed script hooks, while one
// that calls base keeps them firing.
[Collection("Ported")]
public class HookOverrideContractTests
{
    // AtMsgReceive/AtMsgSend dispatch through Hookable directly and are
    // intentionally non-virtual (game code hooks them via scripts, not
    // overrides); everything else public At* on GameObject/Node is virtual.
    private static readonly HashSet<string> NonVirtualAtMethods = new(StringComparer.Ordinal)
    {
        "AtMsgReceive",
        "AtMsgSend",
    };

    [Fact]
    public void PublicAtMethods_StayVirtual()
    {
        var checkedAny = false;
        foreach (var type in new[] { typeof(GameObject), typeof(Node) })
        {
            foreach (var m in type.GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (!m.Name.StartsWith("At", StringComparison.Ordinal)) continue;
                if (m.DeclaringType == typeof(object)) continue;
                if (NonVirtualAtMethods.Contains(m.Name)) continue;
                checkedAny = true;
                Assert.True(m.IsVirtual && !m.IsFinal,
                    $"{type.Name}.{m.Name} must stay public virtual so game subclasses can override it");
            }
        }
        Assert.True(checkedAny);
    }

    private sealed class DescProbe
    {
        public bool Fired;
        [Before] public void OnDesc(GameObject? looker) => Fired = true;
    }

    private sealed class NoBaseDesc : GameObject
    {
        public override void AtDesc(GameObject? looker = null) { }
    }

    private sealed class BaseDesc : GameObject
    {
        public override void AtDesc(GameObject? looker = null) => base.AtDesc(looker);
    }

    [Fact]
    public void OverrideSkippingBase_DropsInstalledHooks()
    {
        var host = new DescProbe();
        var obj = new NoBaseDesc();
        obj.InstallHook(HookName.AtDesc, (Action<GameObject?>)host.OnDesc);
        Assert.True(obj.HasHook(HookName.AtDesc));
        obj.AtDesc(null);
        Assert.False(host.Fired);
    }

    [Fact]
    public void OverrideCallingBase_KeepsInstalledHooks()
    {
        var host = new DescProbe();
        var obj = new BaseDesc();
        obj.InstallHook(HookName.AtDesc, (Action<GameObject?>)host.OnDesc);
        Assert.True(obj.HasHook(HookName.AtDesc));
        obj.AtDesc(null);
        Assert.True(host.Fired);
    }
}
