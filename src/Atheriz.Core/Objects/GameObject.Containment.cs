
namespace Atheriz.Core.Objects;

public partial class GameObject
{
    public virtual bool AtPreGet(GameObject getter)
    {
        return Hookable(HookNames.AtPreGet, () => Access(getter, "get"), getter);
    }
    public virtual void AtGet(GameObject getter)
    {
        Hookable(HookNames.AtGet, () => 0, getter);
    }
    public virtual bool AtPreDrop(GameObject dropper)
    {
        return Hookable(HookNames.AtPreDrop, () => Access(dropper, "drop"), dropper);
    }
    public virtual void AtDrop(GameObject dropper)
    {
        Hookable(HookNames.AtDrop, () => 0, dropper);
    }
    public virtual bool AtPrePut(GameObject putter, GameObject destination)
    {
        return Hookable(HookNames.AtPrePut, () => true, putter, destination);
    }
    public virtual void AtPut(GameObject putter, GameObject destination)
    {
        Hookable(HookNames.AtPut, () => 0, putter, destination);
    }
    public virtual bool AtPreGive(GameObject giver, GameObject receiver)
    {
        // The actor is the giver (mirrors AtPreGet/AtPreDrop checking the
        // getter/dropper) — the receiver's "give" lock must not gate the giver.
        return Hookable(HookNames.AtPreGive, () => Access(giver, "give"), giver, receiver);
    }
    public virtual void AtGive(GameObject giver, GameObject receiver)
    {
        Hookable(HookNames.AtGive, () => 0, giver, receiver);
    }
}
