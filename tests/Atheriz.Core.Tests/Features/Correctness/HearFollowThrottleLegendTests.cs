// Pins: a node-gate emitter swap reaches fan-out, opposite simultaneous
// follows complete, throttle tail entries drain, legend RGB triples
// survive storage.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Objects;
using Atheriz.Core;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public sealed class HearFollowThrottleLegendTests
{
    private sealed class SwapNode : Node
    {
        public GameObject? Proxy;
        public SwapNode(Coord coord) : base(coord) { }
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreEmitSound(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
            => (true, Proxy ?? emitter, soundDesc + "[swapped]", soundMsg, loudness, isSay);
    }

    private sealed class Ear : GameObject
    {
        public GameObject? HeardEmitter;
        public string? HeardDesc;
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
        {
            HeardEmitter = emitter;
            HeardDesc = soundDesc;
            return base.AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
        }
    }

    [Fact]
    public void AtEmitSound_NodeGateEmitterSwap_ReachesListeners()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var room = new SwapNode(new Coord("swaproom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var proxy = GameObject.Create("proxy");
            ObjectRegistry.AddObject(proxy);
            room.Proxy = proxy;
            var emitter = GameObject.Create("em", isNpc: true);
            ObjectRegistry.AddObject(emitter);
            var ear = new Ear { Name = "ear", IsPc = true, CanHear = true };
            ear.Id = GameObject.GetNextId();
            ObjectRegistry.AddObject(ear);
            Assert.True(emitter.MoveTo(room, force: true, announce: false));
            Assert.True(ear.MoveTo(room, force: true, announce: false));
            ear.ClearMessages();

            emitter.AtEmitSound("boom", "BANG", 100.0, false);

            Assert.Same(proxy, ear.HeardEmitter);
            Assert.Contains("[swapped]", ear.HeardDesc ?? "");
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void FollowLockOrder_OppositeDirections_Agree()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var a = GameObject.Create("Aaa");
            ObjectRegistry.AddObject(a);
            var b = GameObject.Create("Bbb");
            ObjectRegistry.AddObject(b);
            var (f1, s1) = FollowCommand.FollowLockOrder(a, b);
            var (f2, s2) = FollowCommand.FollowLockOrder(b, a);
            Assert.Same(f1, f2);
            Assert.Same(s1, s2);
            Assert.True(f1.Id <= s1.Id);
            var (self1, self2) = FollowCommand.FollowLockOrder(a, a);
            Assert.Same(self1, self2);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public async Task OppositeSimultaneousFollows_CompleteWithoutDeadlock()
    {
        using var env = GlobalTestEnv.Enter();
        try
        {
            var room = new Node(new Coord("followroom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            GameObject MakePc(string name)
            {
                var o = GameObject.Create(name, isPc: true);
                ObjectRegistry.AddObject(o);
                o.IsConnected = true;
                o.Location = LocationRef.FromCoord(room.Coord);
                room.AddObject(o);
                o.ClearMessages();
                return o;
            }
            var a = MakePc("Aaa");
            var b = MakePc("Bbb");
            // Concurrent smoke run over the real command path (the order pin
            // above is the deterministic guard: lock order agreement makes
            // hold-and-wait in opposite order impossible).
            for (int round = 0; round < 5; round++)
            {
                new UnfollowCommand().Run(a, null);
                new UnfollowCommand().Run(b, null);
                a.ClearMessages();
                b.ClearMessages();
                using var start = new Barrier(2);
                var t1 = Task.Run(() =>
                {
                    start.SignalAndWait();
                    new FollowCommand().Run(a, new FollowCommand().Parser!.ParseArgs(["Bbb"]));
                });
                var t2 = Task.Run(() =>
                {
                    start.SignalAndWait();
                    new FollowCommand().Run(b, new FollowCommand().Parser!.ParseArgs(["Aaa"]));
                });
                var both = Task.WhenAll(t1, t2);
                var winner = await Task.WhenAny(both, Task.Delay(TimeSpan.FromSeconds(60)));
                Assert.Same(both, winner);
            }
            Assert.Equal(b.Id, a.Following);
            Assert.Equal(a.Id, b.Following);
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void ThrottleWindow_TailExpiredEntry_DrainsPromptly()
    {
        var last = new Dictionary<string, double>();
        var lck = new System.Threading.Lock();
        for (int i = 0; i < 16; i++)
            Assert.True(ThrottleWindow.ShouldLog(last, lck, $"h{i}", 5.0, now: 1000.0));
        last["old"] = 990.0;
        Assert.True(ThrottleWindow.ShouldLog(last, lck, "new", 5.0, now: 1000.0));
        Assert.False(last.ContainsKey("old"));
    }

    [Fact]
    public void LegendEntry_RgbTriple_RoundTrips()
    {
        var e = new LegendEntry("s", "d", (1, 2)) { FgRgb = [200, 100, 50], BgRgb = [1, 2, 3] };
        var back = MapInfo.LegendEntryDto.FromDomain(e).ToDomain();
        Assert.Equal(new List<int> { 200, 100, 50 }, back.FgRgb);
        Assert.Equal(new List<int> { 1, 2, 3 }, back.BgRgb);
        var jsonBack = LegendEntry.FromJson(e.ToJson());
        Assert.Equal(new List<int> { 200, 100, 50 }, jsonBack.FgRgb);
        Assert.Null(new LegendEntry("s").FgRgb);
    }
}
