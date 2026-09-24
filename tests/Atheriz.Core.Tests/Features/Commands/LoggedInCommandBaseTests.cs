using System.Reflection;
using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Commands;

// Pins the template-method migration: every concrete logged-in command runs
// through the LoggedInCommand.Run(CommandContext) preamble (single puppet
// resolve, single parsed/raw split) instead of pasting the RequirePuppet
// guard into its own Run. Builder-gated verbs run through BuilderCommand's
// single IsBuilder gate. Reflection is permitted in tests/.
[Collection("Ported")]
public sealed class LoggedInCommandBaseTests
{
    // Verbs that intentionally stay on the raw Command base: they either
    // serve unpuppeted callers (help/none/quit) or resolve the puppet
    // through a non-standard path (room exits, the map-editor connection
    // fallback). Everything else must sit on a template base.
    private static readonly HashSet<string> RawBaseKeeps =
    [
        "Atheriz.Core.Commands.LoggedIn.HelpCommand",
        "Atheriz.Core.Commands.LoggedIn.NoneCommand",
        "Atheriz.Core.Commands.LoggedIn.QuitCommand",
        "Atheriz.Core.Commands.LoggedIn.LoggedInExitCommand",
        "Atheriz.Core.Commands.LoggedIn.DrawCommand",
    ];

    private static IEnumerable<Type> LoggedInCommandTypes()
    {
        var asm = typeof(Command).Assembly;
        foreach (var t in asm.GetTypes())
        {
            if (t.IsAbstract || t.IsInterface) continue;
            if (!t.IsSubclassOf(typeof(Command))) continue;
            if (t == typeof(BaseChannelCommand)) continue;
            if (t.Namespace != "Atheriz.Core.Commands.LoggedIn") continue;
            yield return t;
        }
    }

    [Fact]
    public void LoggedInVerbs_InheritTemplateBases()
    {
        // Every concrete logged-in verb except the documented keeps resolves
        // its puppet through LoggedInCommand.Run (or BuilderCommand's gate).
        // A verb slipping back to `: Command` with a hand-rolled preamble
        // fails here instead of silently forking the guard.
        var offenders = LoggedInCommandTypes()
            .Where(t => !RawBaseKeeps.Contains(t.FullName!))
            .Where(t => !t.IsSubclassOf(typeof(LoggedInCommand)))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void BuilderCommands_CoverExactlyBuilderGate()
    {
        // The BuilderCommand set is exactly the verbs whose Access was the
        // single IsBuilder check: one gate spelling, greppable in one place.
        // Superuser (reload/save/shutdown/spam) and raw-privilege
        // (quell/unquell) verbs stay on LoggedInCommand with their own gate.
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            "BanCommand", "BuildCommand", "CreateCommand", "DeleteCommand",
            "DescCommand", "DoorCommand", "ExamCommand", "MazeCommand",
            "MoveCommand", "NounCommand", "PuppetCommand", "SetCommand",
            "UnbanCommand", "UnpuppetCommand", "UnsetCommand", "WanderCommand",
        };
        var actual = LoggedInCommandTypes()
            .Where(t => t.IsSubclassOf(typeof(BuilderCommand)))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    // Commands live one family per file now (registry-builder grouping);
    // this maps a command type to the region of its own class declaration.
    private static readonly string[] FamilyFiles =
    [
        "AdminCommands.cs", "BuildingCommands.cs", "CommunicationCommands.cs",
        "SocialCommands.cs", "ItemCommands.cs", "MovementCommands.cs", "InfoCommands.cs",
    ];

    private static string SourceFor(Type t)
    {
        foreach (var f in FamilyFiles)
        {
            var file = SourceScan.Read("src", "Atheriz.Core", "Commands", "LoggedIn", f);
            var m = System.Text.RegularExpressions.Regex.Match(
                file, @"(?m)^public\s+(sealed\s+|abstract\s+)?class\s+" + t.Name + @"\b");
            if (!m.Success) continue;
            int end = file.IndexOf("\npublic ", m.Index + m.Length, StringComparison.Ordinal);
            return end < 0 ? file.Substring(m.Index) : file.Substring(m.Index, end - m.Index);
        }
        Assert.Fail($"no family file declares {t.FullName}");
        throw new InvalidOperationException();
    }

    [Fact]
    public void CommandSources_ContainNoPuppetPreamble()
    {
        // The two-line preamble (puppet guard, parsed-args guard) lives in
        // the base now. Any copy left behind in a migrated command means it
        // bypasses the template and its null-arg/help behavior. Commands
        // share family files, so each command's own class region is checked
        // (raw-base keeps in the same files are skipped per type as before).
        foreach (var t in LoggedInCommandTypes())
        {
            if (RawBaseKeeps.Contains(t.FullName!)) continue;
            var src = SourceFor(t);
            Assert.DoesNotContain("RequirePuppet", src);
            Assert.DoesNotContain("RequireParsedArgs", src);
            Assert.DoesNotContain("override void Run(", src);
        }
        var channelSrc = SourceScan.Read("src", "Atheriz.Core", "Commands", "BaseChannelCommand.cs");
        Assert.DoesNotContain("RequirePuppet", channelSrc);
    }

    [Fact]
    public void RawCommand_NonPuppetCaller_GetsRefusalMessage()
    {
        // The Run(CommandContext) preamble denies non-puppet callers on the
        // raw path too (parser verbs pin this in LoggedInCommandTests for say/emote).
        var conn = new TestConnection();
        new TimeCommand().Run(conn, null);
        Assert.Contains(conn.Sent, t => t.Args.Any(a => a != null && a.ToString()!.Contains("You can't do that.")));
    }

    [Fact]
    public void BuilderGate_StaysSingleCheck()
    {
        // BuilderCommand seals the single IsBuilder gate: a per-command
        // Access override on a BuilderCommand subclass does not compile,
        // so reaching this assertion means the seal held. Pin the behavior
        // end to end for one migrated verb.
        var builder = GameObject.Create("template_builder", isPc: true, privilege: Privilege.Builder);
        var player = GameObject.Create("template_player", isPc: true, privilege: Privilege.Player);
        Assert.True(new NounCommand().Access(builder));
        Assert.False(new NounCommand().Access(player));
    }
}
