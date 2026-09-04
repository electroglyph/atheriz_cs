// Port of atheriz/tests/test_deadlocks.py — 5 defs faithful
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Commands;
using System.Threading;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDeadlocksTests
{
    [Fact] public void MsgReleasesChannelLockBeforeDelivery()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel();
        chan.Name = "locktest";
        chan.Id = 1000;
        ObjectRegistry.AddObject(chan);
        var deliveryStarted = new ManualResetEventSlim(false);
        var releaseDelivery = new ManualResetEventSlim(false);
        var listener = new BlockingListener(deliveryStarted, releaseDelivery);
        listener.Id = 1001;
        ObjectRegistry.AddObject(listener);
        chan.AddListener(listener);
        var thread = new Thread(() => chan.Msg("hello", listener)) { IsBackground = true };
        thread.Start();
        Assert.True(deliveryStarted.Wait(5000), "message delivery never reached listener");
        // Channel lock should be free during delivery (snapshot outside lock) — check SyncRoot (chan.lock) not _histLock
        bool acquired = chan.SyncRoot.TryEnterWriteLock(0);
        Assert.True(acquired, "channel lock still held during listener delivery");
        if (acquired) chan.SyncRoot.ExitWriteLock();
        // Also check _histLock is free (for completeness)
        var histField = typeof(Channel).GetField("_histLock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var histLock = histField.GetValue(chan)!;
        bool histAcquired = Monitor.TryEnter(histLock, 0);
        Assert.True(histAcquired, "_histLock still held during delivery");
        if (histAcquired) Monitor.Exit(histLock);
        releaseDelivery.Set();
        thread.Join(5000);
        Assert.False(thread.IsAlive, "delivery thread did not finish");
    }
    private sealed class BlockingListener : GameObject
    {
        private readonly ManualResetEventSlim _started, _release;
        public BlockingListener(ManualResetEventSlim s, ManualResetEventSlim r) { _started = s; _release = r; Name = "Listener"; }
        public override void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null)
        {
            _started.Set();
            _release.Wait(10000);
            base.Msg(text, fromObj, mapping, raiseErrors, msgType);
        }
    }
    [Fact] public void ConcurrentSubscribeUnsubscribeAndMsg()
    {
        using var env = GlobalTestEnv.Enter();
        var chan = new Channel(); chan.Name = "churn"; chan.Id = 2000; ObjectRegistry.AddObject(chan);
        var objs = new List<GameObject>();
        for (int i=0;i<4;i++) { var o = GameObject.Create($"Churner-{i}"); o.Id = 3000+i; ObjectRegistry.AddObject(o); o.InternalCmdSet = new CmdSet(); chan.AddListener(o); objs.Add(o); }
        var stop = new ManualResetEventSlim(false);
        var failures = new List<Exception>();
        void SubscribeChurn() { for(int i=0;i<40;i++){ if(stop.IsSet) return; var target=objs[i%objs.Count]; try{ target.Subscribe(chan); target.Unsubscribe(chan);} catch(Exception ex){ lock(failures)failures.Add(ex); stop.Set();}}}
        void Messenger(){ for(int i=0;i<40;i++){ if(stop.IsSet) return; try{ chan.Msg("tick", objs[0]); } catch(Exception ex){ lock(failures)failures.Add(ex); stop.Set();}}}
        var threads = Enumerable.Range(0,4).Select(_=> new Thread(SubscribeChurn){IsBackground=true}).ToList();
        threads.Add(new Thread(Messenger){IsBackground=true});
        threads.ForEach(t=>t.Start());
        foreach(var t in threads) t.Join(10000);
        Assert.False(threads.Any(t=>t.IsAlive), "subscribe/msg threads deadlocked");
        Assert.Empty(failures);
    }
    [Fact] public void MoveToChurnNoDeadlock()
    {
        using var env = GlobalTestEnv.Enter();
        var coord1 = new Coord("TestArea", 1,0,0);
        var coord2 = new Coord("TestArea", 2,0,0);
        var room1 = new Node(coord1, desc:"Room 1"); room1.Id = 1; ObjectRegistry.AddObject(room1);
        var room2 = new Node(coord2, desc:"Room 2"); room2.Id = 2; ObjectRegistry.AddObject(room2);
        room1.AddLink(new NodeLink("East", coord2));
        room2.AddLink(new NodeLink("West", coord1));
        var nh = GlobalServices.GetNodeHandler();
        var area = nh.GetArea("TestArea") ?? new NodeArea("TestArea");
        if (nh.GetArea("TestArea")==null) nh.AddArea(area);
        var grid = area.GetOrCreateGrid(0);
        grid.AddNode(room1); grid.AddNode(room2);
        var movers = new List<GameObject>();
        for(int i=0;i<5;i++){ var npc=GameObject.Create($"Mover-{i}", isNpc:true); npc.Id=10+i; ObjectRegistry.AddObject(npc); npc.InternalCmdSet=new CmdSet(); npc.MoveTo(room1); movers.Add(npc); }
        var looker = GameObject.Create("Looker", isNpc:true); looker.Id=100; ObjectRegistry.AddObject(looker); looker.InternalCmdSet=new CmdSet(); looker.MoveTo(room1);
        var stop=new ManualResetEventSlim(false);
        var failures=new List<Exception>();
        void MoverLogic(GameObject npc){ for(int k=0;k<15;k++){ if(stop.IsSet) return; var cur=npc.ResolveLocationObject() as Node; if(cur==null) continue; var link=cur.Links.FirstOrDefault(); if(link==null) continue; var target=nh.GetNode(link.Coord); try{ if(target!=null) npc.MoveTo(target, toExit: link.Name);}catch(Exception ex){ lock(failures)failures.Add(ex); stop.Set(); return;} Thread.Sleep(Random.Shared.Next(10,30));}}
        void LookerLogic(GameObject npc){ for(int k=0;k<30;k++){ if(stop.IsSet) return; try{ npc.AtLook(npc.ResolveLocationObject());}catch(Exception ex){ lock(failures)failures.Add(ex); stop.Set(); return;} Thread.Sleep(Random.Shared.Next(5,10));}}
        var threads = movers.Select(n=> new Thread(()=>MoverLogic(n)){IsBackground=true}).ToList();
        threads.Add(new Thread(()=>LookerLogic(looker)){IsBackground=true});
        threads.ForEach(t=>t.Start());
        foreach(var t in threads) t.Join(15000);
        Assert.False(threads.Any(t=>t.IsAlive), "move/look threads deadlocked");
        Assert.Empty(failures);
        nh.RemoveArea("TestArea");
    }
    [Fact] public void MoveIntoDeletingNodeNotOrphan()
    {
        using var env = GlobalTestEnv.Enter();
        var nh = GlobalServices.GetNodeHandler();
        var area = new NodeArea("TestDeadlock");
        var grid = new NodeGrid("TestDeadlock", 0);
        area.AddGrid(grid);
        nh.AddArea(area);
        var coordA = new Coord("TestDeadlock", 0,0,0);
        var coordB = new Coord("TestDeadlock", 1,0,0);
        var coordHome = new Coord("TestDeadlock", 2,0,0);
        var nodeA = new Node(coordA, desc:"A"); var nodeB = new Node(coordB, desc:"B"); var home = new Node(coordHome, desc:"home");
        nodeA.Id=IdGenerator.GetUniqueId(); nodeB.Id=IdGenerator.GetUniqueId(); home.Id=IdGenerator.GetUniqueId();
        ObjectRegistry.AddObject(nodeA); ObjectRegistry.AddObject(nodeB); ObjectRegistry.AddObject(home);
        grid.AddNode(nodeA); grid.AddNode(nodeB); grid.AddNode(home);
        var mover = GameObject.Create("Mover"); ObjectRegistry.AddObject(mover); mover.MoveTo(nodeA);
        Assert.Same(nodeA, mover.ResolveLocationObject());
        var caller = GameObject.Create("Caller"); ObjectRegistry.AddObject(caller); caller.MoveTo(nodeA);
        mover.Home = new LocationRef.ObjectLocation(home.Id);
        var barrier = new Barrier(2);
        var failures = new List<Exception>();
        Thread t1 = new Thread(()=>{
            try{ barrier.SignalAndWait(5000); mover.MoveTo(nodeB, toExit: null);}catch(Exception ex){ lock(failures)failures.Add(ex);}
        }){IsBackground=true};
        Thread t2 = new Thread(()=>{
            try{ barrier.SignalAndWait(5000); nodeB.Delete(caller, false);}catch(Exception ex){ lock(failures)failures.Add(ex);}
        }){IsBackground=true};
        t1.Start(); t2.Start();
        t1.Join(5000); t2.Join(5000);
        Assert.False(t1.IsAlive && t2.IsAlive, "move/delete threads deadlocked");
        Assert.Empty(failures);
        var bInGrid = grid.GetNode((coordB.X, coordB.Y)) == nodeB;
        var loc = mover.ResolveLocationObject();
        if (loc == nodeB)
        {
            Assert.True(bInGrid, "orphan: mover.location is deleted node B but B not in grid");
            Assert.False(nodeB.IsDeleted, "mover in node marked deleted");
        }
        var after = GameObject.Create("After"); ObjectRegistry.AddObject(after); after.MoveTo(nodeA);
        var moved = after.MoveTo((object)nodeB);
        Assert.False(moved);
        Assert.Same(nodeA, after.ResolveLocationObject());
        nh.RemoveArea("TestDeadlock");
    }
    [Fact] public void AddObjectUniqueDoesNotDeadlockWithContents()
    {
        using var env = GlobalTestEnv.Enter();
        var victim = GameObject.Create("Victim"); ObjectRegistry.AddObject(victim);
        var failures = new List<Exception>();
        var threads = new List<Thread>();
        // Adder threads: each tries to add a unique object with predicate that touches victim lock
        for(int i=0;i<5;i++)
        {
            int idx=i;
            threads.Add(new Thread(()=>{
                try{
                    for(int k=0;k<20;k++)
                    {
                        var cand = GameObject.Create($"Cand_{idx}_{k}");
                        ObjectRegistry.RemoveObject(cand);
                        ObjectRegistry.AddObjectUnique(cand, r => {
                            if(r.Id==victim.Id){
                                victim.SyncRoot.EnterReadLock();
                                try{ Thread.Sleep(2); } finally{ victim.SyncRoot.ExitReadLock(); }
                                return false;
                            }
                            return false;
                        }, "dup");
                    }
                }catch(Exception ex){ lock(failures) failures.Add(ex); }
            }){IsBackground=true});
        }
        // Reader threads: repeatedly read victim contents while holding lock
        for(int i=0;i<3;i++)
        {
            threads.Add(new Thread(()=>{
                try{
                    for(int k=0;k<30;k++)
                    {
                        victim.SyncRoot.EnterReadLock();
                        try{ var _ = victim.ContentsSnapshot; Thread.Sleep(1); } finally{ victim.SyncRoot.ExitReadLock(); }
                    }
                }catch(Exception ex){ lock(failures) failures.Add(ex); }
            }){IsBackground=true});
        }
        foreach(var t in threads) t.Start();
        foreach(var t in threads) t.Join(5000);
        Assert.False(threads.Any(t=>t.IsAlive), "deadlock detected: Global->Object vs Object->Global lock inversion");
        Assert.Empty(failures);
    }
}
