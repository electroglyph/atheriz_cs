namespace Atheriz.Core.Globals;

/// <summary>
/// Global monotonic ID counter. Mirrors <c>atheriz/globals/get.py:_ID + _ID_LOCK</c>.
/// Thread-safe via lock.
/// </summary>
public static class IdGenerator
{
    internal static readonly object LockObj = new();
    private static int _id = -1;

    public static int GetId()
    {
        lock (LockObj) return _id;
    }

    public static void SetId(int id)
    {
        lock (LockObj) _id = id;
    }

    public static void Reset() => SetId(-1);

    /// <summary>
    /// Port of <c>node.py:load</c> tail (<c>_ID = max(_ID, max_node_id)</c> under
    /// <c>_ID_LOCK</c>): loading persisted rows must advance the counter past
    /// every restored id, or fresh objects reuse live ids after a load.
    /// </summary>
    public static void EnsureAtLeast(int id)
    {
        lock (LockObj) if (id > _id) _id = id;
    }

    public static int GetUniqueId()
    {
        // Checked: wrap-around would silently reuse live ids (Python is unbounded).
        // 2^31 ids per boot is unreachable in practice; fail loudly instead of corrupting.
        lock (LockObj) return checked(++_id);
    }
}
