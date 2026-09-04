// Port of atheriz/tests/test_last_touch.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedLastTouchTests
{
    [Fact]
    public void NodeIdsAreReal()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test_last", 1,1,0));
        Assert.NotEqual(-1, node.Id);
    }

    [Fact]
    public void MoveIntoRoomSetsMeaningfulLastTouched()
    {
        using var env = GlobalTestEnv.Enter();
        var node = new Node(new Coord("test_last2", 2,2,0));
        ObjectRegistry.AddObject(node);
        var walker = GameObject.Create("walker");
        ObjectRegistry.AddObject(walker);
        bool ok = walker.MoveTo(node);
        Assert.True(ok);
        Assert.True(walker.Location is Persistence.Dto.LocationRef.CoordLocation cl && cl.Coord.Equals(node.Coord));
        // LastTouchedBy is stored as id of last room; after fix it equals node.Id
        var fld = typeof(GameObject).GetField("_lastTouchedBy", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (fld != null)
        {
            var val = (int)(fld.GetValue(walker) ?? -1);
            Assert.NotEqual(-1, val);
            Assert.Equal(node.Id, val);
        }
        else
        {
            // fallback: check via property if exists
            var prop = typeof(GameObject).GetProperty("LastTouchedBy");
            if (prop != null) Assert.NotEqual(-1, (int)prop.GetValue(walker)!);
        }
    }
}
