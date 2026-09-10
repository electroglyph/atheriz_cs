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
}
