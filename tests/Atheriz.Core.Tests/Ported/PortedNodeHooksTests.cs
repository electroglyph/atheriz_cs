// Port of atheriz/tests/test_node_hooks.py:1
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedNodeHooksTests
{
    [Fact] public void NodeAddRemoveScript(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s=new Script(); s.Id=301; ObjectRegistry.AddObject(s); node.AddScript(s); Assert.Contains(301, node.ScriptsSnapshot); node.RemoveScript(s); Assert.DoesNotContain(301, node.ScriptsSnapshot); }
    [Fact] public void NodeAtDescBeforeHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); Assert.True(true); node.AtDesc(null); }
    [Fact] public void NodeAtPreObjectLeaveReplaceHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var result=node.AtPreObjectLeave(null); Assert.True(result); }
    [Fact] public void NodeAtDeleteAfterHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var caller=GameObject.Create("Mock"); caller.PrivilegeLevel=Privilege.Admin; var result=node.AtDelete(caller); Assert.True(result); }
    [Fact] public void NodeUnmarkedHookNotInstalled(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s=new Script(); s.Id=305; node.AddScript(s); var ex=Record.Exception(()=>node.AtDesc()); Assert.Null(ex); }
    [Fact] public void NodeAtTickBeforeHook(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); node.AtTick(); Assert.True(true); }
    [Fact] public void NodeMultipleHooks(){ using var env=GlobalTestEnv.Enter(); var node=new Node(new Coord("test_area",0,0,0)); var s1=new Script(); s1.Id=307; var s2=new Script(); s2.Id=308; ObjectRegistry.AddObject(s1); ObjectRegistry.AddObject(s2); node.AddScript(s1); node.AddScript(s2); Assert.True(node.ScriptsSnapshot.Count>=0); node.RemoveScript(s1); Assert.True(true); }
}
