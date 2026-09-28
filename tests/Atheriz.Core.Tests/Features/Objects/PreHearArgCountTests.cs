using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// AtPreHear/AtPreEmitSound dispatch exactly 5 args (emitter, desc, msg,
// loudness, isSay) at every site (GameObject.Hear RunPreHook, Node
// overrides), so the attach-time counts must say [5]: a 5-param before hook
// installs and fires, a 6-required-param before hook is refused, and a
// trailing optional param still binds (required stops at the first default).
[Collection("Ported")]
public class PreHearArgCountTests
{
    private sealed class FiveParamProbe
    {
        public bool Fired;
        [Before] public void OnPre(GameObject? emitter, string? desc, string? msg, double loudness, bool isSay) => Fired = true;
    }

    private sealed class SixParamProbe
    {
        [Before] public void OnPre(GameObject? emitter, string? desc, string? msg, double loudness, bool isSay, object? extra) { }
    }

    private sealed class OptionalTailProbe
    {
        public bool Fired;
        [Before] public void OnPre(GameObject? emitter, string? desc, string? msg, double loudness, bool isSay, object? extra = null) => Fired = true;
    }

    [Fact]
    public void FiveParamBeforeHook_InstallsAndFiresOnPreHear()
    {
        var host = new FiveParamProbe();
        var obj = new GameObject();
        obj.InstallHook(HookName.AtPreHear, (Action<GameObject?, string?, string?, double, bool>)host.OnPre);
        Assert.True(obj.HasHook(HookName.AtPreHear));
        Assert.Equal(7, obj.Hookable<int>(HookName.AtPreHear, () => 7, obj, "d", "m", 1.0, false));
        Assert.True(host.Fired);
    }

    [Fact]
    public void FiveParamBeforeHook_InstallsAndFiresOnPreEmitSound()
    {
        var host = new FiveParamProbe();
        var obj = new GameObject();
        obj.InstallHook(HookName.AtPreEmitSound, (Action<GameObject?, string?, string?, double, bool>)host.OnPre);
        Assert.True(obj.HasHook(HookName.AtPreEmitSound));
        Assert.Equal(7, obj.Hookable<int>(HookName.AtPreEmitSound, () => 7, obj, "d", "m", 1.0, false));
        Assert.True(host.Fired);
    }

    [Fact]
    public void SixParamBeforeHook_RefusedOnPreHear()
    {
        var host = new SixParamProbe();
        var obj = new GameObject();
        obj.InstallHook(HookName.AtPreHear, (Action<GameObject?, string?, string?, double, bool, object?>)host.OnPre);
        Assert.False(obj.HasHook(HookName.AtPreHear));
    }

    [Fact]
    public void OptionalTrailingParam_InstallsAndFiresOnPreHear()
    {
        var host = new OptionalTailProbe();
        var obj = new GameObject();
        obj.InstallHook(HookName.AtPreHear, (Action<GameObject?, string?, string?, double, bool, object?>)host.OnPre);
        Assert.True(obj.HasHook(HookName.AtPreHear));
        Assert.Equal(7, obj.Hookable<int>(HookName.AtPreHear, () => 7, obj, "d", "m", 1.0, false));
        Assert.True(host.Fired);
    }
}
