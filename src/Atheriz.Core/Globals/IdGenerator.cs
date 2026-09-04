namespace Atheriz.Core.Globals;

/// <summary>
/// Global monotonic ID counter. Mirrors <c>atheriz/globals/get.py:_ID + _ID_LOCK</c>.
/// Thread-safe via lock.
/// </summary>
public static class IdGenerator
{
    internal static readonly object LockObj = new();
    private static readonly object Lock = LockObj;
    private static int _id = -1;

    public static int GetId()
    {
        lock (Lock) return _id;
    }

    public static void SetId(int id)
    {
        lock (Lock) _id = id;
    }

    public static void Reset() => SetId(-1);

    public static int GetUniqueId()
    {
        // Checked: wrap-around would silently reuse live ids (Python is unbounded).
        // 2^31 ids per boot is unreachable in practice; fail loudly instead of corrupting.
        lock (Lock) return checked(++_id);
    }
}
