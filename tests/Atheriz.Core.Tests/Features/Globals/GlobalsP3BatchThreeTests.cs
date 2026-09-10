using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Tests.Features.Regression;

namespace Atheriz.Core.Tests.Features.Globals;

// Regression pins for the Globals P3 batch 3.
[Collection("Ported")]
public class GlobalsP3BatchThreeTests
{
    private static FieldInfo? StaticField(Type t, string name) =>
        t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

    // One SetNodeHandler call publishes both singleton slots: a
    // SetNodeHandler-only caller can no longer fork GlobalServices from
    // NodeHandler.GetCurrent.
    [Fact]
    public void SetNodeHandler_PublishesBothSlots()
    {
        var gsField = StaticField(typeof(GlobalServices), "_nodeHandler");
        var nhField = StaticField(typeof(NodeHandler), "_current");
        Assert.NotNull(gsField);
        Assert.NotNull(nhField);
        var priorGs = gsField.GetValue(null);
        var priorCurrent = nhField.GetValue(null);
        try
        {
            var nh = new NodeHandler(autoLoad: false);
            GlobalServices.SetNodeHandler(nh);
            Assert.Same(nh, GlobalServices.TryGetNodeHandler());
            Assert.Same(nh, NodeHandler.GetCurrent());
        }
        finally
        {
            gsField.SetValue(null, priorGs);
            nhField.SetValue(null, priorCurrent);
        }
    }

    // ChainsSnapshot is the single snapshot body: exactly one lock+copy
    // implementation left in the file.
    [Fact]
    public void MapEdit_ChainsSnapshot_SingleCopyBody()
    {
        MapEdit.Reset();
        try
        {
            var k = MapEdit.Grant("1.1.1.1", "limbo", 0);
            Assert.True(MapEdit.ChainsSnapshot.ContainsKey(k));
            var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
            Assert.Equal(1, SourceScan.Count(src, "new Dictionary<string, MapEditChain>(_chains)"));
        }
        finally { MapEdit.Reset(); }
    }

    // Every stale-previous purge goes through the shared collector.
    [Fact]
    public void MapEdit_StalePrevious_CollectedThroughOnePath()
    {
        MapEdit.Reset();
        try
        {
            var k = MapEdit.Grant("2.2.2.2", "limbo", 0);
            Assert.True(MapEdit.RemoveChain(k));
            Assert.False(MapEdit.ChainsSnapshot.ContainsKey(k));
            Assert.False(MapEdit.ChainsSnapshot.ContainsKey(k));
            var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
            Assert.Equal(4, SourceScan.Count(src, "CollectStalePreviousLocked();"));
        }
        finally { MapEdit.Reset(); }
    }

    // The presence check is named for what it does; the old name survives
    // as an obsolete forwarder so external callers keep compiling.
    [Fact]
    public void MapEdit_ChainExists_ChecksPresenceOnly()
    {
        MapEdit.Reset();
        try
        {
            var k = MapEdit.Grant("3.3.3.3", "limbo", 0);
            Assert.True(MapEdit.ChainExists(k));
            Assert.False(MapEdit.ChainExists("no-such-key"));
#pragma warning disable CS0618
            Assert.Equal(MapEdit.ChainExists(k), MapEdit.ValidateChain(k));
#pragma warning restore CS0618
        }
        finally { MapEdit.Reset(); }
    }

    // Copies restore the source stamps instead of stamping-then-overwriting.
    [Fact]
    public void MapEdit_Copy_PreservesSourceStamps()
    {
        MapEdit.Reset();
        try
        {
            var k = MapEdit.Grant("4.4.4.4", "limbo", 0);
            var first = MapEdit.GetChain(k);
            var second = MapEdit.GetChain(k);
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Equal(first.CreatedMonotonic, second.CreatedMonotonic);
            Assert.NotSame(first, second);
            var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "MapEdit.cs");
            Assert.Contains("stamp: false", SourceScan.Region(src, "private static MapEditChain CopyOf"));
        }
        finally { MapEdit.Reset(); }
    }

    // The ClearAll lock comment states the re-entrancy fact in one line
    // instead of arguing with itself.
    [Fact]
    public void ObjectRegistry_ClearAll_CommentStatesReentrancy()
    {
        var src = SourceScan.Read("src", "Atheriz.Core", "Globals", "ObjectRegistry.cs");
        var region = SourceScan.Region(src, "public static void ClearAll()");
        Assert.DoesNotContain("reflection-safe", region);
        Assert.Contains("re-entrant", region);
    }

    [Fact]
    public void ObjectRegistry_ClearAll_StillResets()
    {
        ObjectRegistry.ClearAll();
        Assert.Equal(0, ObjectRegistry.Count);
    }
}
