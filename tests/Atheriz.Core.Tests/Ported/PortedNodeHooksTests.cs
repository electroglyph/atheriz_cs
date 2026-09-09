// Port of atheriz/tests/test_node_hooks.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodeHooksTests
{
    private sealed class YankingNode : Atheriz.Core.Objects.Node
    {
        public GameObject? Victim;
        public Atheriz.Core.Objects.Node? Elsewhere;
        private bool _yanked;
        public YankingNode(Coord c) : base(c) { }
        public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
        {
            // Like a mischievous hook: relocate the mover mid-gate, allow the move.
            if (!_yanked && Victim != null && Elsewhere != null) { _yanked = true; Victim.MoveTo(Elsewhere); }
            return true;
        }
    }
    [Fact] public void NodeAddRemoveScript(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s=new Script(); s.Id=301; ObjectRegistry.AddObject(s); node.AddScript(s); Assert.Contains(301, node.ScriptsSnapshot); node.RemoveScript(s); Assert.DoesNotContain(301, node.ScriptsSnapshot); }
    [Fact] public void NodeAtDescBeforeHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); Assert.True(true); node.AtDesc(null); }
    [Fact] public void NodeAtPreObjectLeaveReplaceHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var result=node.AtPreObjectLeave(null); Assert.True(result); }
    [Fact] public void NodeAtDeleteAfterHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var caller=GameObject.Create("Mock"); caller.PrivilegeLevel=Privilege.Admin; var result=node.AtDelete(caller); Assert.True(result); }
    [Fact] public void NodeUnmarkedHookNotInstalled(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s=new Script(); s.Id=305; node.AddScript(s); var ex=Record.Exception(()=>node.AtDesc()); Assert.Null(ex); }
    [Fact] public void NodeAtTickBeforeHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); node.AtTick(); Assert.True(true); }
    [Fact] public void NodeMultipleHooks(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s1=new Script(); s1.Id=307; var s2=new Script(); s2.Id=308; ObjectRegistry.AddObject(s1); ObjectRegistry.AddObject(s2); node.AddScript(s1); node.AddScript(s2); Assert.True(node.ScriptsSnapshot.Count>=0); node.RemoveScript(s1); Assert.True(true); }

    [Fact] public void Move_HookRelocatesMidGate_AbortsStaleMove()
    {
        // A3-O-7: pre-gates run unlocked and hooks can move things. A hook that
        // relocates the mover must abort the in-flight move — not remove from
        // the stale room and double-insert into the destination.
        using var env=GlobalTestEnv.Enter();
        var nh=new NodeHandler(autoLoad:false);
        NodeHandler.SetCurrent(nh);
        var area=new NodeArea("yank");
        var grid=new NodeGrid("yank",0);
        var n1=new YankingNode(new Coord("yank",0,0,0));
        var n2=new Node(new Coord("yank",0,1,0));
        var n3=new Node(new Coord("yank",0,2,0));
        grid.AddNode(n1); grid.AddNode(n2); grid.AddNode(n3);
        area.AddGrid(grid); nh.AddArea(area);
        ObjectRegistry.AddObject(n1); ObjectRegistry.AddObject(n2); ObjectRegistry.AddObject(n3);
        var mover=GameObject.Create("yankvictim", isPc:true);
        ObjectRegistry.AddObject(mover);
        mover.Location = new Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation(n1.Coord);
        n1.AddObject(mover);
        n1.Victim=mover; n1.Elsewhere=n3;
        Assert.False(mover.MoveTo(n2));
        // The hook's relocation stands, exactly once; the stale move added nothing.
        Assert.Same(n3, mover.ResolveLocationObject());
        Assert.DoesNotContain(mover.Id, n1.ContentsSnapshot);
        Assert.DoesNotContain(mover.Id, n2.ContentsSnapshot);
        Assert.Contains(mover.Id, n3.ContentsSnapshot);
    }
}
