using Atheriz.Core.Commands;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

// `#id` parsing has one truth: malformed references ("#", "#x") report the
// invalid-format message while well-formed-but-missing ids ("#-1",
// "#99999") report not-found. Both id consumers share the parse.
[Collection("Ported")]
public sealed class IdRefResolutionTests
{
    [Fact]
    public void TryParseIdRef_EdgeInputs()
    {
        Assert.True(CommandHelpers.TryParseIdRef("#5", out var id));
        Assert.Equal(5, id);
        Assert.True(CommandHelpers.TryParseIdRef("#-1", out var neg));
        Assert.Equal(-1, neg);
        Assert.False(CommandHelpers.TryParseIdRef("#", out _));
        Assert.False(CommandHelpers.TryParseIdRef("#x", out _));
        Assert.False(CommandHelpers.TryParseIdRef("5", out _));
    }

    [Fact]
    public void ResolveObject_MalformedId_ReportsInvalidFormat()
    {
        using var env = GlobalTestEnv.Enter();
        foreach (var q in new[] { "#", "#x" })
        {
            var caller = new GameObject { Name = "Hero" };
            caller.ClearMessages();
            Assert.Null(CommandHelpers.ResolveObject(caller, q));
            Assert.Contains("Invalid ID format. Use #<number>.", caller.PeekMessages());
        }
    }

    [Fact]
    public void ResolveObject_MissingId_ReportsNotFound()
    {
        using var env = GlobalTestEnv.Enter();
        var caller = new GameObject { Name = "Hero" };
        caller.ClearMessages();
        Assert.Null(CommandHelpers.ResolveObject(caller, "#27481"));
        Assert.Contains("No object found with ID 27481.", caller.PeekMessages());
        caller.ClearMessages();
        Assert.Null(CommandHelpers.ResolveObject(caller, "#-1"));
        Assert.Contains("No object found with ID -1.", caller.PeekMessages());
    }

    [Fact]
    public void ResolveObject_KnownId_Resolves()
    {
        using var env = GlobalTestEnv.Enter();
        var obj = GameObject.Create("pinobj");
        ObjectRegistry.AddObject(obj);
        var caller = new GameObject { Name = "Hero" };
        Assert.Same(obj, CommandHelpers.ResolveObject(caller, $"#{obj.Id}"));
    }
}
