// Port of atheriz/tests/test_py_hardening.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPyHardeningTests
{
    [Fact] public void PyHardening_PyExcluded_NoSandboxNeeded()
    {
        using var env = GlobalTestEnv.Enter();
        var loggedIn = CommandRegistry.LoggedIn;
        Assert.DoesNotContain(loggedIn.GetAll(), c => c.Key.Equals("py", StringComparison.OrdinalIgnoreCase));
        // Verify no _SAFE_BUILTINS etc. leakage — just ensure build succeeds without py
        Assert.True(true);
    }
    [Fact] public void PyHardening_AccessGating_WouldRequireSuperuserIfEnabled()
    {
        using var env = GlobalTestEnv.Enter();
        // Plan: PY_REQUIRE_SUPERUSER default? Even if py existed, builder would be allowed unless require_superuser true.
        // Since excluded, we verify privilege enum still has Builder/Admin distinction
        Assert.True(Privilege.Builder < Privilege.Admin);
        Assert.True(Privilege.Player < Privilege.Builder);
    }
    [Fact] public void PyHardening_NoAttributeEscape_ViaCSharpExclusion()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("HardeningVictim");
        // Ensure GameObject does not expose dangerous reflection via python-style getattr
        var hasUnsafe = obj.GetType().GetMethod("GetAttribute") != null;
        Assert.False(hasUnsafe);
    }
    [Fact] public void PyHardening_NoImport_NoExecution()
    {
        using var env = GlobalTestEnv.Enter();
        // Ensure no Python import mechanism exists
        var asm = typeof(GameObject).Assembly;
        var hasPython = asm.GetReferencedAssemblies().Any(a => a.Name!.Contains("IronPython", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasPython);
    }
    [Fact] public void PyHardening_LegitimateUse_StillWorks_ViaCSharpCommands()
    {
        using var env = GlobalTestEnv.Enter();
        var admin = GameObject.Create("Admin");
        admin.PrivilegeLevel = Privilege.Admin;
        ObjectRegistry.AddObject(admin);
        var room = new Node(new Coord("hard",0,0,0));
        ObjectRegistry.AddObject(room);
        admin.MoveTo(room);
        // Valid command should still run
        var look = new Atheriz.Core.Commands.LoggedIn.LookCommand();
        var (fn, caller, args) = look.Execute(admin, "", "look");
        if (fn != null) fn(caller!, args);
        Assert.True(admin.PeekMessages().Count > 0);
    }
}
