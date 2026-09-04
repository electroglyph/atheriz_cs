// Port of atheriz/new.py:522 TEMPLATE_CONFIGS ("object","Object","atheriz.objects.base_obj")
// Dynamically generated via get_class_hooks (atheriz/utils.py:701) — mirrors test/object.py full hook list
#nullable enable
namespace Atheriz.GameTemplate;
using System.Text.Json;
using Atheriz.Core.Objects;
using Atheriz.Core;
using Atheriz.Core.Globals;
/// <summary>Custom Object — mirrors test/object.py. Override methods below to customize behavior.</summary>
public class CustomObject : GameObject
{
    public CustomObject() : base() { }
    public CustomObject(string name, bool isPc = false) : base() { Name = name; IsPc = isPc; }

    public override bool Access(GameObject? accessingObj, string lockName)
    {
        return base.Access(accessingObj, lockName);
    }

    public override void AtAlarm(GameTime.GameTimeInfo time, Dictionary<string, JsonElement>? data)
    {
        base.AtAlarm(time, data);
    }

    public override void AtCreate()
    {
        base.AtCreate();
    }

    public override bool AtDelete(GameObject caller)
    {
        return base.AtDelete(caller);
    }

    public override void AtDesc(GameObject? looker = null)
    {
        base.AtDesc(looker);
    }

    public override void AtDisconnect()
    {
        base.AtDisconnect();
    }

    public override void AtDrop(GameObject dropper)
    {
        base.AtDrop(dropper);
    }

    public override void AtEmitSound(string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        base.AtEmitSound(soundDesc, soundMsg, loudness, isSay);
    }

    public override void AtGet(GameObject getter)
    {
        base.AtGet(getter);
    }

    public override void AtGive(GameObject giver, GameObject receiver)
    {
        base.AtGive(giver, receiver);
    }

    public override double AtHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtHear(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override void AtInit()
    {
        base.AtInit();
    }

    public override void AtLegendUpdate(List<(string, string, (int, int))> entries, bool show, string area)
    {
        base.AtLegendUpdate(entries, show, area);
    }

    public override string AtLook(GameObject? target)
    {
        return base.AtLook(target);
    }

    public override void AtLunarEvent(string message)
    {
        base.AtLunarEvent(message);
    }

    public override void AtMapUpdate(string mapStr, List<(string, string, (int, int))> entries, int minX, int maxY, bool showLegend, string name)
    {
        base.AtMapUpdate(mapStr, entries, minX, maxY, showLegend, name);
    }

    public override void AtObjectLeave(GameObject? destination, string? toExit = null)
    {
        base.AtObjectLeave(destination, toExit);
    }

    public override void AtObjectReceive(GameObject? source, string? fromExit = null)
    {
        base.AtObjectReceive(source, fromExit);
    }

    public override void AtPostMove(GameObject? destination, string? toExit = null)
    {
        base.AtPostMove(destination, toExit);
    }

    public override void AtPostPuppet()
    {
        base.AtPostPuppet();
    }

    public override bool AtPreDrop(GameObject dropper)
    {
        return base.AtPreDrop(dropper);
    }

    public override (bool, GameObject, string, string, double, bool) AtPreEmitSound(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtPreEmitSound(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override bool AtPreGet(GameObject getter)
    {
        return base.AtPreGet(getter);
    }

    public override bool AtPreGive(GameObject giver, GameObject receiver)
    {
        return base.AtPreGive(giver, receiver);
    }

    public override (bool, GameObject, string, string, double, bool) AtPreHear(GameObject emitter, string soundDesc, string soundMsg, double loudness, bool isSay)
    {
        return base.AtPreHear(emitter, soundDesc, soundMsg, loudness, isSay);
    }

    public override Dictionary<(int, int), string> AtPreMapRender(Dictionary<(int, int), string> grid)
    {
        return base.AtPreMapRender(grid);
    }

    public override bool AtPreMove(GameObject? destination, string? toExit = null)
    {
        return base.AtPreMove(destination, toExit);
    }

    public override bool AtPreObjectLeave(GameObject? destination, string? toExit = null)
    {
        return base.AtPreObjectLeave(destination, toExit);
    }

    public override bool AtPreObjectReceive(GameObject? source, string? fromExit = null)
    {
        return base.AtPreObjectReceive(source, fromExit);
    }

    public override bool AtPrePut(GameObject putter, GameObject destination)
    {
        return base.AtPrePut(putter, destination);
    }

    public override string AtPreSay(string message)
    {
        return base.AtPreSay(message);
    }

    public override void AtPuppet(GameObject caller)
    {
        base.AtPuppet(caller);
    }

    public override void AtPut(GameObject putter, GameObject destination)
    {
        base.AtPut(putter, destination);
    }

    public override void AtSay(string text, bool msgSelf = true)
    {
        base.AtSay(text, msgSelf);
    }

    public override void AtSolarEvent(string message)
    {
        base.AtSolarEvent(message);
    }

    public override void AtTick()
    {
        base.AtTick();
    }

    public override void AtUnpuppet(GameObject caller)
    {
        base.AtUnpuppet(caller);
    }
}
