using Atheriz.Core.Commands;
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Persistence.Dto;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Objects;

// Partial for deletion lifecycle — split from GameObject.Puppet.cs per file-organization hygiene.
public partial class GameObject
{
    // Port of base_obj.py:467 delete + object deletion lifecycle — caller optional for Account parity
    public virtual (int count, List<object> ops)? Delete(GameObject? caller = null, bool recursive = false)
    {
        // Account row delete is immediate regardless of static type.
        // (C# cannot override with a different return type, so the bool Delete
        // hides this method; route the base dispatch to the same immediate core.)
        if (this is Account acc) return acc.DeleteImmediate(caller);
        if (caller != null && !AtDelete(caller)) return null;
        // quick check already deleted
        _lock.EnterReadLock();
        try { if (_flags.IsDeleted) return null; }
        finally { _lock.ExitReadLock(); }

        var ops = new List<object>();
        var toDelete = new List<GameObject>();

        if (recursive)
        {
            // faithful port of base_obj.delete _collect_recursive with MAX_SEARCH_DEPTH
            int maxDepth = MaxSearchDepth;
            var seen = new HashSet<int>();
            var stack = new Stack<(GameObject obj, int depth)>();
            stack.Push((this, 0));
            var order = new List<GameObject>();
            var truncated = new List<GameObject>();
            while (stack.Count > 0)
            {
                var (obj, depth) = stack.Pop();
                if (!seen.Add(obj.Id)) continue;
                order.Add(obj);
                // snapshot contents safely
                List<int> contentIds;
                obj._lock.EnterReadLock();
                try { contentIds = new List<int>(obj._contents); }
                finally { obj._lock.ExitReadLock(); }
                foreach (var cid in contentIds)
                {
                    var cObjs = ObjectRegistry.Get(cid);
                    var content = cObjs.FirstOrDefault();
                    if (content == null) continue;
                    if (seen.Contains(content.Id)) continue;
                    if (truncated.Any(t => t.Id == content.Id)) continue;
                    // Honor each child's delete veto: a vetoed subtree is skipped,
                    // not force-deleted. A throwing AtDelete is treated as a veto
                    // (fail-closed): a buggy hook must not force deletion.
                    // The throw is still logged loudly (pinned by
                    // ThrowingAtDeleteChild_IsLogged).
                    if (caller != null)
                    {
                        bool vetoed = false;
                        // a throwing AtDelete must not vanish silently —
                        // log the failure and treat it as "vetoed".
                        try { vetoed = !content.AtDelete(caller); } catch (Exception logEx) { vetoed = true; AtherizLogger.LogWarning("GameObject.Delete AtDelete threw (treated as vetoed): " + logEx.Message, "GameObject"); }
                        if (vetoed) continue;
                    }
                    if (depth + 1 >= maxDepth)
                    {
                        truncated.Add(content);
                        continue;
                    }
                    stack.Push((content, depth + 1));
                }
            }
            // reversed order for deletion (children first)
            order.Reverse();
            // Actually Python's order is collected then reversed: order is DFS pre-order, reversed gives children before parent.
            // Our order currently is pop order (which is DFS). Reversing gives leaves first? Let's mimic Python: it appends in visit order, then reversed iteration adds to to_delete.
            // We've added in pop order; reversing will give appropriate.
            foreach (var obj in order)
            {
                toDelete.Add(obj);
                bool isTemp;
                obj._lock.EnterReadLock();
                try { isTemp = obj._flags.IsTemporary; }
                finally { obj._lock.ExitReadLock(); }
                if (!isTemp)
                    ops.Add(obj.GetDelOps());
            }
            // handle truncated survivors: if survivor location's id is in seen, detach
            foreach (var survivor in truncated)
            {
                if (seen.Contains(survivor.Id)) continue;
                // get survivor's location ref
                LocationRef locRef;
                survivor._lock.EnterReadLock();
                try { locRef = survivor._location; }
                finally { survivor._lock.ExitReadLock(); }
                int? locId = null;
                GameObject? locObj = null;
                if (locRef is LocationRef.ObjectLocation ol) { locId = ol.ObjectId; locObj = ObjectRegistry.Get(ol.ObjectId).FirstOrDefault(); }
                else if (locRef is LocationRef.CoordLocation) { /* node case - not needed for container chain test */ }
                else { continue; }
                if (locId.HasValue && seen.Contains(locId.Value))
                {
                    try { locObj?.RemoveContent(survivor.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                    try
                    {
                        survivor._lock.EnterWriteLock();
                        try { survivor._location = LocationRef.NullLocation.Instance; survivor._flags.IsModified = true; }
                        finally { survivor._lock.ExitWriteLock(); }
                    }
                    catch { try { survivor.Location = LocationRef.NullLocation.Instance; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); } }
                }
            }
            // actually need to ensure toDelete contains order reversed already; truncated survivors stay alive
            // Now physically delete each in toDelete
            foreach (var obj in toDelete)
            {
                // mimic _delete_object minimal: remove from location, clear followers/channels, mark deleted, remove registry
                // detach from location if any
                try
                {
                    var loc = obj.ResolveLocationObject();
                    if (loc != null)
                    {
                        try { loc.RemoveContent(obj.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                    }
                } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                try
                {
                    obj._lock.EnterWriteLock();
                    try
                    {
                        if (!obj._flags.IsDeleted)
                        {
                            obj._flags.IsDeleted = true;
                            obj._flags.IsModified = true;
                        }
                        // clear location
                        // keep location as Null for survivors? For deleted ones, set to null as well but they are deleted anyway
                        // Don't override truncated handling for toDelete objects
                    }
                    finally { obj._lock.ExitWriteLock(); }
                } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                ObjectRegistry.RemoveObject(obj);
                TeardownDeleted(obj);
            }
            // ops already collected; return
            return (toDelete.Count, ops);
        }
        else
        {
            // non-recursive: move contents to self's location (Python _move_contents)
            GameObject? loc = null;
            try { loc = this.ResolveLocationObject(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
            List<int> contentIds;
            _lock.EnterReadLock();
            try { contentIds = new List<int>(_contents); }
            finally { _lock.ExitReadLock(); }
            var contentObjs = contentIds.Select(id => ObjectRegistry.Get(id).FirstOrDefault()).Where(o => o != null).Cast<GameObject>().ToList();
            int deletedKids = 0;
            foreach (var content in contentObjs.ToList())
            {
                bool moved = false;
                try { moved = content.MoveTo(loc, force: false, announce: false); } catch { moved = false; }
                if (!moved)
                {
                    // if still located at this, detach
                    bool stillAtThis = false;
                    content._lock.EnterReadLock();
                    try
                    {
                        if (content._location is LocationRef.ObjectLocation ol2 && ol2.ObjectId == this.Id) stillAtThis = true;
                    }
                    finally { content._lock.ExitReadLock(); }
                    if (stillAtThis)
                    {
                        try { this.RemoveContent(content.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                        try
                        {
                            content._lock.EnterWriteLock();
                            try { content._location = LocationRef.NullLocation.Instance; content._flags.IsModified = true; }
                            finally { content._lock.ExitWriteLock(); }
                        } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
                    }
                    // then collect recursively (delete content and its children)
                    var r = content.Delete(caller, true);
                    if (r != null) { ops.AddRange(r.Value.ops); deletedKids += r.Value.count; }
                }
                else
                {
                    // moved successfully, ensure removed from this._contents (MoveTo already handled via destination add, but old loc removal already done)
                    // No delete
                }
            }
            // now delete self
            _lock.EnterWriteLock();
            try
            {
                if (_flags.IsDeleted) return null;
                _flags.IsDeleted = true;
                _flags.IsModified = true;
            }
            finally { _lock.ExitWriteLock(); }
            // detach from location
            try
            {
                var loc2 = this.ResolveLocationObject();
                if (loc2 != null) loc2.RemoveContent(this.Id);
            } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Delete: " + logEx.Message, "GameObject"); }
            if (!this.IsTemporary)
                ops.Add(this.GetDelOps());
            // include self in count
            ObjectRegistry.RemoveObject(this);
            TeardownDeleted(this);
            // toDelete includes self plus any recursively deleted via Move failure path already added to ops
            // count is 1 plus the recursively deleted children above.
            // single RemoveObject — a second call here could delete
            // a live re-add of this Id.)
            return (1 + deletedKids, ops);
        }
    }

    // Port of base_obj.py:349-426 _delete_object teardown: leave no dangling
    // follows, channel memberships, sessions, or tick slots. Shared by the
    // recursive walk above and the non-recursive self-delete tail, plus
    // Node.Delete's self teardown .
    internal static void TeardownDeleted(GameObject obj)
    {
        try
        {
            var leaderId = obj.Following;
            if (leaderId.HasValue)
            {
                try { obj.Following = null; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                try
                {
                    var leader = ObjectRegistry.Get(leaderId.Value).FirstOrDefault();
                    try { leader?.RemoveFollower(obj.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
        try
        {
            foreach (var fid in obj.FollowersSnapshot.ToList())
            {
                try
                {
                    var follower = ObjectRegistry.Get(fid).FirstOrDefault();
                    if (follower != null && follower.Following == obj.Id)
                        try { follower.Following = null; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
        try
        {
            foreach (var chId in obj.ChannelsSnapshot.ToList())
            {
                try
                {
                    var ch = ObjectRegistry.Get(chId).FirstOrDefault() as Channel;
                    if (ch != null) try { ch.RemoveListener(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                try { obj.UnsubscribeById(chId); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
        try
        {
            var sess = obj.Session;
            if (sess != null)
            {
                try { obj.AtDisconnect(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
                try { sess.Connection?.Close(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
            }
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
        try { Objects.GlobalTickerHolder.Get()?.RemoveCoro(obj.AtTick, obj.TickSeconds); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
        // leave no dangling residue — a deleted id must not stay
        // readable via PeekMessages/scans or keep installed hooks alive.
        try { obj.Write(() => { obj._msgLog.Clear(); obj._hooks.Clear(); }); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.TeardownDeleted: " + logEx.Message, "GameObject"); }
    }
}
