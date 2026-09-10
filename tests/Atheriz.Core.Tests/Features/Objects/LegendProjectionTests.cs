using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Features.Objects;

// Legend projection shared by AtMapUpdate/AtLegendUpdate: one helper emits
// the identical [sym, desc, [x, y]] rows for both payloads, and the shared
// session fetch keeps the null-session fallback (map fallback message).
[Collection("Ported")]
public class LegendProjectionTests
{
    [Fact]
    public void ProjectLegendEntries_EmitsSymDescCoordRows()
    {
        var entries = new List<(string sym, string desc, (int x, int y) coord)>
        {
            ("@", "town", (3, 4)),
            ("#", "wall", (-1, 0)),
        };
        var rows = GameObject.ProjectLegendEntries(entries);
        Assert.Equal(2, rows.Count);
        Assert.Equal(["@", "town"], rows[0][..2]);
        Assert.Equal(new List<int> { 3, 4 }, Assert.IsType<List<int>>(rows[0][2]));
        Assert.Equal("#", rows[1][0]);
        Assert.Equal(new List<int> { -1, 0 }, Assert.IsType<List<int>>(rows[1][2]));
    }

    [Fact]
    public void ProjectLegendEntries_Empty_StaysEmpty()
    {
        Assert.Empty(GameObject.ProjectLegendEntries([]));
    }

    [Fact]
    public void AtMapUpdate_WithoutSession_FallsBackToLog()
    {
        // No session/connection: the payload still builds (projection runs)
        // and delivery falls back to the [map:name] message.
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("mapper", isPc: true);
            ObjectRegistry.AddObject(o);
            o.ClearMessages();
            o.AtMapUpdate("mapdata", [("@", "town", (0, 0))], 0, 0, true, "area51");
            Assert.Contains(o.PeekMessages(), m => m.Contains("[map:area51]"));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtLegendUpdate_WithoutSession_DoesNotThrow()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var o = GameObject.Create("mapper", isPc: true);
            ObjectRegistry.AddObject(o);
            var ex = Record.Exception(() => o.AtLegendUpdate([("@", "town", (0, 0))], true, "area51"));
            Assert.Null(ex);
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
