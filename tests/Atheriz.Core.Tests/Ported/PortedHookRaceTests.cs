// Port of atheriz/tests/test_hook_race.py — faithful (simplified snapshot test)
using System.Threading;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedHookRaceTests
{
    private sealed class HookHolder
    {
        [Before] public void FirstHook() {}
        [Before] public void SecondHook() {}
    }
    private sealed class HookTarget : GameObject
    {
        public HookTarget(HashSet<Delegate> set)
        {
            SyncRoot.EnterWriteLock();
            try
            {
                var f = typeof(GameObject).GetField("_hooks", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!;
                var dict = (Dictionary<string, HashSet<Delegate>>)f.GetValue(this)!;
                dict["run"] = set;
            }
            finally { SyncRoot.ExitWriteLock(); }
        }
        public void Run() => Hookable<object?>("run", () => null);
    }

    [Fact]
    public void HookDispatchSnapshotsBeforeConcurrentMutation()
    {
        using var env = GlobalTestEnv.Enter();
        var holder = new HookHolder();
        var first = (Delegate)Delegate.CreateDelegate(typeof(Action), holder, typeof(HookHolder).GetMethod(nameof(HookHolder.FirstHook))!);
        var second = (Delegate)Delegate.CreateDelegate(typeof(Action), holder, typeof(HookHolder).GetMethod(nameof(HookHolder.SecondHook))!);
        var hookSet = new HashSet<Delegate> { first };
        var target = new HookTarget(hookSet);

        var errors = new List<Exception>();
        var worker = new Thread(() =>
        {
            try { for(int i=0;i<100;i++) target.Run(); }
            catch (Exception ex) { lock(errors) errors.Add(ex); }
        });
        var mutator = new Thread(() =>
        {
            try { for(int i=0;i<100;i++) { target.SyncRoot.EnterWriteLock(); try { hookSet.Add(second); } finally { target.SyncRoot.ExitWriteLock(); } Thread.Sleep(1); } }
            catch (Exception ex) { lock(errors) errors.Add(ex); }
        });
        worker.Start();
        mutator.Start();
        worker.Join(2000);
        mutator.Join(2000);
        Assert.False(worker.IsAlive, "worker did not finish (possible deadlock)");
        Assert.False(mutator.IsAlive, "mutator did not finish");
        Assert.Empty(errors);
    }
}
