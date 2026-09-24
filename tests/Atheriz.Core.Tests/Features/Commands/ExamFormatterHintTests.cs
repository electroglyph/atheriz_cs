using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands.UnloggedIn;

namespace Atheriz.Core.Tests.Features.Commands;

// The examine value/hint switch renders each hint with its dedicated
// renderer and falls anything unknown through to the generic shape.
[Collection("Ported")]
public sealed class ExamFormatterHintTests
{
    [Fact]
    public void ExamSwitch_LastTouchedBy_UsesTouchedRenderer()
    {
        // The or-pattern arm routes last_touched_by to the touched renderer.
        Assert.Equal("-1", ExamFormatter.FormatValue(-1, "last_touched_by") as string);
    }

    [Fact]
    public void ExamSwitch_ScriptsNonIds_FallThroughToGeneric()
    {
        // Non-id script members fall through to the generic list renderer.
        Assert.Equal("[x]", ExamFormatter.FormatValue(new List<string> { "x" }, "scripts") as string);
    }

    [Fact]
    public void ExamSwitch_SessionNull_RendersNone()
    {
        // The session arm renders a missing session as None.
        Assert.Equal("None", ExamFormatter.FormatValue(null, "session") as string);
    }

    [Fact]
    public void ExamFormatter_BraceBracketInterpolation_MatchesLegacyShapes()
    {
        using var env = GlobalTestEnv.Enter();
        var alice = GameObject.Create("Alice");
        ObjectRegistry.AddObject(alice);
        try
        {
            Assert.Equal($"{{#{alice.Id} (Alice)}}",
                ExamFormatter.FormatValue(new List<int> { alice.Id }, "followers") as string);
            var cs = new CmdSet();
            cs.Adds([new OpenCommand(), new CloseCommand()]);
            Assert.Equal("[open, close]", ExamFormatter.FormatValue(cs, "external_cmdset") as string);
            Assert.Equal("Session(w=78, h=45, sr=True)",
                ExamFormatter.FormatValue(new Session { ScreenReader = true }, "session") as string);
            Assert.Equal("{a: b}",
                ExamFormatter.FormatValue(new Dictionary<string, object?> { ["a"] = "b" }, null) as string);
            Assert.Equal("[x]", ExamFormatter.FormatValue(new List<string> { "x" }, "scripts") as string);
            Assert.Equal("None", ExamFormatter.FormatValue(null, "session") as string);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
