using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// PascalCase script hooks install and fire: C# overrides must be spelled
// AtTick (not at_tick), so a lowercase-only marker scan silently installed
// nothing for them and the hook never fired.
public class PascalCaseHookInstallTests
{
    private sealed class TickProbeScript : Script
    {
        public int Fired;
        [After]
        public override void AtTick() { Fired++; }
    }

    [Fact]
    public void InstallHooks_PascalCaseAtTick_InstallsAndFires()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var child = GameObject.Create("child");
            var script = new TickProbeScript();
            script.InstallHooks(child);
            Assert.True(child.HasHook("at_tick"));
            child.AtTick();
            Assert.Equal(1, script.Fired);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
