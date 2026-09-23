
namespace Atheriz.Core.Objects;

public partial class GameObject
{
    public virtual bool AtPreGet(GameObject getter)
    {
        return Hookable(HookName.AtPreGet, () => Access(getter, "get"), getter);
    }
    public virtual void AtGet(GameObject getter)
    {
        Hookable(HookName.AtGet, () => 0, getter);
    }
    public virtual bool AtPreDrop(GameObject dropper)
    {
        return Hookable(HookName.AtPreDrop, () => Access(dropper, "drop"), dropper);
    }
    public virtual void AtDrop(GameObject dropper)
    {
        Hookable(HookName.AtDrop, () => 0, dropper);
    }
    public virtual bool AtPrePut(GameObject putter, GameObject destination)
    {
        return Hookable(HookName.AtPrePut, () => true, putter, destination);
    }
    public virtual void AtPut(GameObject putter, GameObject destination)
    {
        Hookable(HookName.AtPut, () => 0, putter, destination);
    }
    public virtual bool AtPreGive(GameObject giver, GameObject receiver)
    {
        // The actor is the giver (mirrors AtPreGet/AtPreDrop checking the
        // getter/dropper) — the receiver's "give" lock must not gate the giver.
        return Hookable(HookName.AtPreGive, () => Access(giver, "give"), giver, receiver);
    }
    public virtual void AtGive(GameObject giver, GameObject receiver)
    {
        Hookable(HookName.AtGive, () => 0, giver, receiver);
    }
}
