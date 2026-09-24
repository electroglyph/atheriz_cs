using Atheriz.Core.Objects;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Simplify;

// Small core types on framework shapes: prompt timeouts via WaitAsync,
// one-time version lookup, record path nodes with integer priorities,
// predicate-pushed registry lookups, frozen verb tables.
public class SmallCoreTypesTests
{
    [Fact]
    public void MenuPrompt_UsesWaitAsync()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "MenuPrompt.cs");
        Assert.Contains(".WaitAsync(timeout)", src);
        Assert.DoesNotContain("Task.WhenAny", src);
        Assert.DoesNotContain("Task.Delay", src);
    }

    [Fact]
    public void ConnectionScreen_LooksUpVersionOnce()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "ConnectionScreen.cs");
        Assert.Equal(1, SourceScan.Count(src, "GetName().Version"));
        Assert.Contains("VersionString", src);
    }

    [Fact]
    public void ConnectionScreen_VersionMatchesAssembly()
    {
        var rendered = Atheriz.Core.ConnectionScreen.Render(null);
        // Pin the Core assembly version (the assembly that serves the screen),
        // not the test assembly's.
        var coreVersion = typeof(Atheriz.Core.ConnectionScreen).Assembly.GetName().Version?.ToString();
        Assert.NotNull(coreVersion);
        Assert.Contains(coreVersion, rendered);
        Assert.Equal(rendered, Atheriz.Core.ConnectionScreen.Render(null));
    }

    [Fact]
    public void PathNode_IsRecord_WithIntPriorityQueue()
    {
        var node = SourceScan.Read("src", "Atheriz.Core", "Utils", "PathNode.cs");
        Assert.Contains("sealed record PathNode(", node);
        Assert.Contains("public int F => G + H;", node);
        Assert.DoesNotContain("{ get; set; }", node);
        var pathfind = SourceScan.Read("src", "Atheriz.Core", "Utils", "Pathfind.cs");
        Assert.Contains("PriorityQueue<PathNode, int>", pathfind);
        Assert.DoesNotContain("PriorityQueue<PathNode, PathNode>", pathfind);
        Assert.DoesNotContain(".F =", pathfind);
    }

    [Fact]
    public void ServerEvents_KeepsSnapshotThenScanEarlyExit()
    {
        // Snapshot-then-scan with early exit stays: pushing the predicate
        // into FilterBy would materialize every match instead of stopping
        // at the first (CharCreateExistenceTests pins this shape).
        var src = SourceScan.Read("src", "Atheriz.Core", "ServerEvents.cs");
        Assert.Contains("ObjectRegistry.FilterBy(_ => true)", src);
        Assert.Contains("if (predicate(o)) return true;", src);
        Assert.Contains("if (predicate(o)) return o;", src);
    }

    [Fact]
    public void Conjugate_TablesAreFrozen()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Objects", "VerbConjugation", "Conjugate.cs");
        Assert.Contains("FrozenDictionary<string, int> VerbTensesKeys", src);
        Assert.Contains("FrozenDictionary<string, string[]> VerbTenses", src);
        Assert.Contains("FrozenDictionary<string, string> VerbLemmas", src);
        Assert.Contains("ToFrozenDictionary(", src);
    }
}
