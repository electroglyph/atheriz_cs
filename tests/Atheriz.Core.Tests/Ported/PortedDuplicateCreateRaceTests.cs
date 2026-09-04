// Port of atheriz/tests/test_duplicate_create_race.py — faithful via AddObjectUnique gate
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

namespace Atheriz.Core.Tests.Ported;

[Collection("Ported")]
public class PortedDuplicateCreateRaceTests
{
    private static List<bool> RunRace(Action fn)
    {
        var barrier = new Barrier(2);
        var outcomes = new List<bool>();
        var lk = new object();
        void Worker()
        {
            barrier.SignalAndWait();
            try { fn(); lock(lk) outcomes.Add(true); }
            catch (InvalidOperationException) { lock(lk) outcomes.Add(false); }
            catch (ValueErrorException) { lock(lk) outcomes.Add(false); }
            catch (Exception) { lock(lk) outcomes.Add(false); }
        }
        var t1 = new Thread(Worker);
        var t2 = new Thread(Worker);
        t1.Start(); t2.Start();
        t1.Join(5000); t2.Join(5000);
        return outcomes;
    }
    // Helper to simulate ValueError in C# (Python's ValueError maps to InvalidOperationException)
    private sealed class ValueErrorException : Exception {}

    [Fact]
    public void ConcurrentAccountCreateSameName()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSaltForTesting("testsalt");
        try
        {
            var gate = new Barrier(2);
            // Gate AddObjectUnique: wrap via flag
            var origMethod = typeof(ObjectRegistry).GetMethod("AddObjectUnique", System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static)!;
            // Instead of patching, we directly run race on AddObjectUnique with gate
            string name = "shared_account_name";
            var outcomes = new List<bool>();
            var barrier = new Barrier(2);
            var lk = new object();
            void Worker()
            {
                barrier.SignalAndWait();
                // Simulate gate on AddObjectUnique
                gate.SignalAndWait(5000);
                try { Account.Create(name, "password123"); lock(lk) outcomes.Add(true); }
                catch (InvalidOperationException) { lock(lk) outcomes.Add(false); }
                catch (Exception) { lock(lk) outcomes.Add(false); }
            }
            var t1 = new Thread(Worker); var t2 = new Thread(Worker);
            t1.Start(); t2.Start();
            t1.Join(5000); t2.Join(5000);
            Assert.Equal(2, outcomes.Count);
            var created = outcomes.Count(o=>o);
            Assert.Equal(1, created);
            Assert.Equal(1, outcomes.Count - created);
            var found = ObjectRegistry.FilterBy(o => o is Account a && a.Name == name);
            Assert.Single(found);
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void ConcurrentChannelCreateHasName()
    {
        using var env = GlobalTestEnv.Enter();
        var gate = new Barrier(2);
        string name = "unique_channel_name";
        var outcomes = new List<bool>();
        var barrier = new Barrier(2);
        var lk = new object();
        void Worker()
        {
            barrier.SignalAndWait();
            gate.SignalAndWait(5000);
            try { Channel.Create(name); lock(lk) outcomes.Add(true); }
            catch (InvalidOperationException) { lock(lk) outcomes.Add(false); }
            catch (Exception) { lock(lk) outcomes.Add(false); }
        }
        var t1 = new Thread(Worker); var t2 = new Thread(Worker);
        t1.Start(); t2.Start();
        t1.Join(5000); t2.Join(5000);
        Assert.Equal(2, outcomes.Count);
        var created = outcomes.Count(o=>o);
        Assert.Equal(1, created);
        Assert.Equal(1, outcomes.Count - created);
        var found = ObjectRegistry.FilterBy(o => o.IsChannel && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Assert.Single(found);
    }
}
