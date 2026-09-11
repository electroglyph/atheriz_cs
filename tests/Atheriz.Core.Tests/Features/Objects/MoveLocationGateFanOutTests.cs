// Pins for the unified pre-gate/post-hook arms (GameObject.Move.cs): a
// receive-gate veto refuses the move with and without an old location, a
// leave-gate veto stops before the receive gate, and the successful path
// keeps the leave-before-receive order.
using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Objects;

[Collection("Ported")]
public sealed class MoveLocationGateFanOutTests
{
    private sealed class GateNode : Node
    {
        private readonly List<string> _log;
        private readonly string _tag;
        public bool VetoLeave;
        public bool VetoReceive;
        public GameObject? SeenReceiveSource;
        public GateNode(Coord coord, List<string> log, string tag) : base(coord)
        {
            _log = log;
            _tag = tag;
        }
        public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
        {
            _log.Add(_tag + ":pre-leave");
            return !VetoLeave;
        }
        public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
        {
            _log.Add(_tag + ":pre-receive");
            SeenReceiveSource = source;
            return !VetoReceive;
        }
        public override void AtObjectLeave(GameObject? destination, string? toExit = null)
        {
            _log.Add(_tag + ":leave");
            base.AtObjectLeave(destination, toExit);
        }
        public override void AtObjectReceive(GameObject? source, string? fromExit = null)
        {
            _log.Add(_tag + ":receive");
            base.AtObjectReceive(source, fromExit);
        }
    }

    private static (GateNode from, GateNode to, GameObject mover, List<string> log) Setup(bool withOldLoc)
    {
        var nh = new NodeHandler(autoLoad: false);
        NodeHandler.SetCurrent(nh);
        var log = new List<string>();
        var from = new GateNode(new Coord("gatefan", 0, 0, 0), log, "from");
        var to = new GateNode(new Coord("gatefan", 0, 1, 0), log, "to");
        nh.AddNode(from);
        nh.AddNode(to);
        var mover = GameObject.Create("mover");
        ObjectRegistry.AddObject(mover);
        if (withOldLoc)
        {
            mover.Location = new LocationRef.CoordLocation(from.Coord);
            from.AddObject(mover);
        }
        else
        {
            mover.Location = LocationRef.NullLocation.Instance;
        }
        return (from, to, mover, log);
    }

    [Fact]
    public void MoveTo_ReceiveGateVetoWithOldLocation_RefusesMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (from, to, mover, log) = Setup(withOldLoc: true);
        to.VetoReceive = true;

        Assert.False(mover.MoveTo(to, force: true, announce: false));
        Assert.Same(from, mover.ResolveLocationObject());
        Assert.Contains(mover.Id, from.ContentsSnapshot);
        Assert.DoesNotContain(mover.Id, to.ContentsSnapshot);
        Assert.Equal(["from:pre-leave", "to:pre-receive"], log);
        Assert.Same(from, to.SeenReceiveSource);
    }

    [Fact]
    public void MoveTo_ReceiveGateVetoWithoutOldLocation_RefusesMove()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, to, mover, log) = Setup(withOldLoc: false);
        to.VetoReceive = true;

        Assert.False(mover.MoveTo(to, force: true, announce: false));
        Assert.Equal(["to:pre-receive"], log);
        Assert.Null(to.SeenReceiveSource);
        Assert.IsType<LocationRef.NullLocation>(mover.Location);
    }

    [Fact]
    public void MoveTo_LeaveGateVeto_StopsBeforeReceiveGate()
    {
        using var env = GlobalTestEnv.Enter();
        var (from, to, mover, log) = Setup(withOldLoc: true);
        from.VetoLeave = true;

        Assert.False(mover.MoveTo(to, force: true, announce: false));
        Assert.Equal(["from:pre-leave"], log);
        Assert.Same(from, mover.ResolveLocationObject());
        Assert.DoesNotContain(mover.Id, to.ContentsSnapshot);
    }

    [Fact]
    public void MoveTo_Success_RunsLeaveBeforeReceive()
    {
        using var env = GlobalTestEnv.Enter();
        var (from, to, mover, log) = Setup(withOldLoc: true);

        Assert.True(mover.MoveTo(to, force: true, announce: false));
        Assert.Equal(["from:pre-leave", "to:pre-receive", "from:leave", "to:receive"], log);
        Assert.Same(to, mover.ResolveLocationObject());
        Assert.Same(from, to.SeenReceiveSource);
        Assert.DoesNotContain(mover.Id, from.ContentsSnapshot);
        Assert.Contains(mover.Id, to.ContentsSnapshot);
    }

    [Fact]
    public void MoveTo_SuccessWithoutOldLocation_ReceivesNullSource()
    {
        using var env = GlobalTestEnv.Enter();
        var (_, to, mover, log) = Setup(withOldLoc: false);

        Assert.True(mover.MoveTo(to, force: true, announce: false));
        Assert.Equal(["to:pre-receive", "to:receive"], log);
        Assert.Null(to.SeenReceiveSource);
        Assert.Same(to, mover.ResolveLocationObject());
    }
}
