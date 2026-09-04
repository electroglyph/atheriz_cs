// Port of atheriz/new.py:522 ("node","Node","atheriz.objects.nodes")
// Dynamically generated via get_class_hooks
#nullable enable
namespace Atheriz.GameTemplate;
using Atheriz.Core;
using Atheriz.Core.Objects;
/// <summary>Custom Node — mirrors test/node.py</summary>
public class CustomNode : Node
{
    public CustomNode() : base() { }
    public CustomNode(Coord coord, string name = "room", string desc = "") : base(coord, name, desc) { }

    public override bool AtDelete(GameObject caller)
    {
        return base.AtDelete(caller);
    }

    public override void AtDesc(GameObject? looker = null)
    {
        base.AtDesc(looker);
    }

    public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtHear(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override void AtInit()
    {
        base.AtInit();
    }

    public override void AtObjectLeave(GameObject? destination, string? toExit = null)
    {
        base.AtObjectLeave(destination, toExit);
    }

    public override void AtObjectReceive(GameObject? source, string? fromExit = null)
    {
        base.AtObjectReceive(source, fromExit);
    }

    public override (bool, GameObject, string, string, double, bool) AtPreEmitSound(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtPreEmitSound(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override (bool, GameObject, string, string, double, bool) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
    {
        return base.AtPreObjectLeave(destination, toExit);
    }

    public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
    {
        return base.AtPreObjectReceive(source, fromExit);
    }

    public override void AtTick()
    {
        base.AtTick();
    }
}
