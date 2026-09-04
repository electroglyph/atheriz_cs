// Port of atheriz/objects/base_obj.py:1632 at_pre_get/at_get/at_pre_drop/at_drop/at_pre_put/at_put

namespace Atheriz.Core.Objects;

public partial class GameObject
{
    public virtual bool AtPreGet(GameObject getter)
    {
        return Hookable("at_pre_get", () => Access(getter, "get"), getter);
    }
    public virtual void AtGet(GameObject getter)
    {
        Hookable("at_get", () => 0, getter);
    }
    public virtual bool AtPreDrop(GameObject dropper)
    {
        return Hookable("at_pre_drop", () => Access(dropper, "drop"), dropper);
    }
    public virtual void AtDrop(GameObject dropper)
    {
        Hookable("at_drop", () => 0, dropper);
    }
    public virtual bool AtPrePut(GameObject putter, GameObject destination)
    {
        return Hookable("at_pre_put", () => true, putter, destination);
    }
    public virtual void AtPut(GameObject putter, GameObject destination)
    {
        Hookable("at_put", () => 0, putter, destination);
    }
    public virtual bool AtPreGive(GameObject giver, GameObject receiver)
    {
        return Hookable("at_pre_give", () => Access(receiver, "give"), giver, receiver);
    }
    public virtual void AtGive(GameObject giver, GameObject receiver)
    {
        Hookable("at_give", () => 0, giver, receiver);
    }
}
