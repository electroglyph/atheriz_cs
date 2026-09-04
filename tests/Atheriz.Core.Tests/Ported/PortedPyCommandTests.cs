// Port of atheriz/tests/test_py_command.py:1
using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedPyCommandTests
{
    [Fact] public void PyCommand_NotRegistered_ExcludedPerPlan()
    {
        using var env = GlobalTestEnv.Enter();
        var loggedIn = CommandRegistry.LoggedIn;
        var allKeys = loggedIn.GetAll().Select(c => c.Key.ToLowerInvariant()).ToHashSet();
        var allAliases = loggedIn.GetAll().SelectMany(c => c.Aliases.Select(a => a.ToLowerInvariant())).ToHashSet();
        Assert.DoesNotContain("py", allKeys);
        Assert.DoesNotContain("py", allAliases);
        // Also ensure no is_open / drop-lock etc. needed — per AGENTS wontfix
        var dummy = GameObject.Create("PyVictim");
        Assert.False(dummy.IsContainer && dummy.GetType().GetProperty("IsOpen") != null, "is_open not a feature on generic Object");
    }
    [Fact] public void PyCommand_PrivilegeWouldRequireBuilderOrSuperuser()
    {
        using var env = GlobalTestEnv.Enter();
        // Intent: py would have required Builder+ (plan.md PY_REQUIRE_SUPERUSER). Since excluded, we verify privilege gates exist via other builder commands
        var player = GameObject.Create("Player");
        player.PrivilegeLevel = Privilege.Player;
        var builder = GameObject.Create("Builder");
        builder.PrivilegeLevel = Privilege.Builder;
        var admin = GameObject.Create("Admin");
        admin.PrivilegeLevel = Privilege.Admin;
        // Use ExamCommand as proxy for builder-gated command
        var exam = new Atheriz.Core.Commands.LoggedIn.ExamCommand();
        Assert.False(exam.Access(player));
        Assert.True(exam.Access(builder));
        Assert.True(exam.Access(admin));
        // Quelled builder denied
        builder.Quelled = true;
        Assert.False(exam.Access(builder));
    }
    [Fact] public void PyCommand_Sandbox_NotPresent_MustNotExecutePython()
    {
        using var env = GlobalTestEnv.Enter();
        var loggedIn = CommandRegistry.LoggedIn;
        var hasPy = loggedIn.GetAll().Any(c => c.Key.Equals("py", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasPy, "py command must remain excluded; no python execution in C# port");
    }
    [Fact] public void PyCommand_Excluded_DoesNotExposeBoard()
    {
        using var env = GlobalTestEnv.Enter();
        // Ensure no Roslyn scripting backdoor present
        var asm = typeof(CommandRegistry).Assembly;
        var hasPyType = asm.GetTypes().Any(t => t.Name.Equals("PyCommand", StringComparison.OrdinalIgnoreCase));
        Assert.False(hasPyType, "PyCommand type must not exist in Atheriz.Core");
    }
}
