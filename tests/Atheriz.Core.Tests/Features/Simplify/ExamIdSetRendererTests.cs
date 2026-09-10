// Pins for the shared id-set renderers (ExamFormatter.TryCollectIds /
// FormatIdSet) and the static member-accessor table: identical output for
// followers/scripts/_contents inputs, per-object evaluation (no cached
// values), and strict fall-through for non-int members.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Simplify;

[Collection("Ported")]
public sealed class ExamIdSetRendererTests
{
    [Fact]
    public void FormatValue_Followers_RendersNamedIdSet()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        var b = GameObject.Create("Bob");
        ObjectRegistry.AddObject(a);
        ObjectRegistry.AddObject(b);
        var rendered = ExamFormatter.FormatValue(new List<int> { a.Id, b.Id }, "followers") as string;
        Assert.Equal($"{{{ExamId(a, "Alice")}, {ExamId(b.Id, "Bob")}}}", rendered);
    }

    [Fact]
    public void FormatValue_Followers_CoercesNumericStrings_SkipsJunk()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        ObjectRegistry.AddObject(a);
        var rendered = ExamFormatter.FormatValue(new List<object?> { a.Id, a.Id.ToString(), "junk" }, "followers") as string;
        Assert.Equal($"{{{ExamId(a, "Alice")}, {ExamId(a.Id, "Alice")}}}", rendered);
    }

    [Fact]
    public void FormatValue_ScriptsAndContents_ShareStrictRenderer()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        ObjectRegistry.AddObject(a);
        var ids = new List<int> { a.Id };
        Assert.Equal(
            ExamFormatter.FormatValue(ids, "scripts") as string,
            ExamFormatter.FormatValue(ids, "_contents") as string);
        Assert.Equal($"{{{ExamId(a, "Alice")}}}", ExamFormatter.FormatValue(ids, "scripts") as string);
        // Empty renders set() in both...
        Assert.Equal("set()", ExamFormatter.FormatValue(new List<int>(), "scripts") as string);
        Assert.Equal("set()", ExamFormatter.FormatValue(new List<int>(), "_contents") as string);
        Assert.Equal("set()", ExamFormatter.FormatValue(null, "followers") as string);
        // ...while non-int members fall through to the generic renderer, not a partial set.
        var mixed = ExamFormatter.FormatValue(new List<object?> { "junk" }, "scripts") as string;
        Assert.Equal(ExamFormatter.FormatValue(new List<object?> { "junk" }, "_contents") as string, mixed);
        Assert.DoesNotContain("#", mixed);
    }

    [Fact]
    public void BaseMembers_TableShape_EvaluatedPerObject()
    {
        using var env = GlobalTestEnv.Enter();
        var a = GameObject.Create("Alice");
        var b = GameObject.Create("Bob");
        var membersA = ExamFormatter.BaseMembers(a).ToList();
        var membersB = ExamFormatter.BaseMembers(b).ToList();
        Assert.Equal(membersA.Count, membersB.Count);
        Assert.Equal(44, membersA.Count);
        Assert.Equal(a.Id, membersA.First(m => m.name == "Id").value);
        Assert.Equal("Alice", membersA.First(m => m.name == "Name").value);
        Assert.Equal("Bob", membersB.First(m => m.name == "Name").value);
        // Order matches the old per-call yield sequence (Id first, extra last).
        Assert.Equal("Id", membersA[0].name);
        Assert.Equal("extra", membersA[^1].name);
    }

    private static string ExamId(GameObject o, string name) => $"#{o.Id} ({name})";
    private static string ExamId(int id, string name) => $"#{id} ({name})";
}
