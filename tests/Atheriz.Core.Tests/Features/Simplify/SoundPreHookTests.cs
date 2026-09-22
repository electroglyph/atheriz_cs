using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Simplify;

// Shared pre-hook core: AtPreHear and AtPreEmitSound pass the same tuple
// through the same gate, so a vetoed receiver hears nothing while others do.
[Collection("Ported")]
public class SoundPreHookTests
{
    private sealed class VetoHearer : GameObject
    {
        public override (bool ok, GameObject emitter, string desc, string msg, double loudness, bool isSay) AtPreHear(
            GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
            => (false, emitter, soundDesc, soundMsg, loudness, isSay);
    }

    [Fact]
    public void AtEmitSound_VetoedReceiver_HearsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = GameObject.Create("room", isContainer: true);
            ObjectRegistry.AddObject(room);
            var emitter = GameObject.Create("em");
            ObjectRegistry.AddObject(emitter);
            var veto = new VetoHearer();
            veto.Id = GameObject.GetNextId();
            veto.Name = "veto";
            veto.IsPc = true;
            ObjectRegistry.AddObject(veto);
            var control = GameObject.Create("ctl", isPc: true);
            ObjectRegistry.AddObject(control);
            emitter.MoveTo(room, force: true, announce: false);
            veto.MoveTo(room, force: true, announce: false);
            control.MoveTo(room, force: true, announce: false);

            emitter.AtEmitSound("boom", "bang", 50.0, false);

            Assert.Empty(veto.PeekMessages());
            Assert.NotEmpty(control.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_HearingNonPc_ReceivesSound()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5room", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5listener", isNpc: true);
            var emitter = GameObject.Create("f5emitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            Assert.False(npc.IsPc);
            Assert.True(npc.CanHear);
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Contains("You hear something", string.Join("\n", npc.PeekMessages()));
        }
        finally { ObjectRegistry.ClearAll(); }
    }

    [Fact]
    public void AtHear_DeafNonPc_HearsNothing()
    {
        ObjectRegistry.ClearAll();
        try
        {
            var room = new Node(new Coord("f5droom", 0, 0, 0));
            ObjectRegistry.AddObject(room);
            var npc = GameObject.Create("f5deaf", isNpc: true);
            var emitter = GameObject.Create("f5demitter", isNpc: true);
            ObjectRegistry.AddObject(npc);
            ObjectRegistry.AddObject(emitter);
            Assert.True(npc.MoveTo(room));
            Assert.True(emitter.MoveTo(room));
            npc.CanHear = false;
            npc.ClearMessages();

            npc.AtHear(emitter, "a clang", "", 50.0, false);

            Assert.Empty(npc.PeekMessages());
        }
        finally { ObjectRegistry.ClearAll(); }
    }
}
