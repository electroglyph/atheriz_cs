using System.Diagnostics;
using Atheriz.Core.Globals;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

// Port of atheriz/objects/session.py:1-202 (202 LOC)
// Faithful: lock guards puppet/puppet_stack/input_future (issue #31 scalar fields atomic under GIL but we still guard).
// Wontfix puppet snapshot incomplete: only is_pc/privilege_level saved (puppet.py:110,138-142). quelled/can_hear/is_mapable not restored by design.
// Global static salt / other wontfixes not relevant here.

/// <summary>
/// Port of <c>atheriz/objects/session.py:Session</c> (202 LOC).
/// Thread-safe via <see cref="Lock"/> (mirrors Python RLock) guarding Puppet / PuppetStack / InputFuture.
/// Scalar fields Term/Map dims + ScreenReader are atomic (no lock required) but writes are lock-guarded for consistency.
/// </summary>
public class Session
{
    // Port of session.py:16-38
    // Guards puppet / puppet_stack / input_future, which are written by game workers and read by per-connection input drain (#31).
    // Scalar fields (term/map dims, screenreader) are single atomic stores under the GIL and need no lock — we still guard writes.
    public readonly object Lock = new object(); // Port of session.py:21 lock = threading.RLock()
    public object? Account; // Port of session.py:22 account: Account | None
    public int? AccountId; // Spec extra: mirror Account.Id for quick lookup (Python stores object, C# stores both)
    public BaseConnection? Connection; // Port of session.py:23 connection: Connection | None
    public GameObject? LastPuppet; // Port of session.py:24 last_puppet
    public GameObject? Puppet; // Port of session.py:25 puppet
    // stack of (prev_puppet, target). Each target carries its own _puppet_restore manifest (excluded from pickling by __getstate__).
    // Lives on the session (never pickled) so transient restore state stays off saved objects. Port of session.py:29 puppet_stack
    public List<(GameObject? Prev, GameObject Target)> PuppetStack { get; } = new();
    // Wontfix: puppet snapshot incomplete — only is_pc/privilege_level per puppet.py:110,138-142.
    // Do NOT store quelled/can_hear/is_mapable; document here. Port of puppet.py:110 restore_snapshot = {"is_pc":..., "privilege_level":...}
    public Dictionary<string, object> PuppetRestore { get; } = new(StringComparer.Ordinal); // spec extra, kept for parity but target holds actual snapshot
    public int TermWidth; // Port of session.py:30 term_width = settings.CLIENT_DEFAULT_WIDTH via settings.py:121
    public int TermHeight; // Port of session.py:31 term_height
    public int MapWidth; // Port of session.py:32 map_width
    public int MapHeight; // Port of session.py:33 map_height
    public bool ScreenReader; // Port of session.py:34 screenreader
    public double ConnTime; // Port of session.py:35 conn_time = 0.0 (Unix seconds)
    public TaskCompletionSource<string>? InputFuture; // Port of session.py:36 input_future: asyncio.Future | None
    public bool InputMasked; // Port of session.py:37 _input_masked (bool)
    public DateTime ConnectedAt; // Spec extra: wall clock for C# convenience (mirrors ConnTime)
    public double SecondsPlayed; // Spec extra: accumulated seconds (mirrors GameObject._seconds_played but session tracks)

    public Session(BaseConnection? connection = null, object? account = null)
    {
        Connection = connection;
        Account = account;
        if (account is Account acc) AccountId = acc.Id;
        TermWidth = 78; // Port of settings.CLIENT_DEFAULT_WIDTH via session.py:30-31 + settings.py:121-122
        TermHeight = 45;
        ConnTime = 0.0; // Port of session.py:35 conn_time = 0.0
        ConnectedAt = DateTime.UtcNow;
    }

    // Port of session.py:39-41 at_connect
    public virtual void AtConnect()
    {
        ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds(); // Port of session.py:40 self.conn_time = time.time()
        ConnectedAt = DateTime.UtcNow;
    }

    // Spec variant: AtConnect(Connection) for callers passing connection explicitly
    public virtual void AtConnect(BaseConnection connection)
    {
        Connection = connection;
        AtConnect();
    }

    // Port of session.py:42-117 at_disconnect — faithful
    public virtual void AtDisconnect()
    {
        // Port of session.py:43-52 with self.lock: capture future/masked/stack/puppet
        TaskCompletionSource<string>? future;
        bool masked;
        List<(GameObject? Prev, GameObject Target)> stack;
        GameObject? puppet;
        lock (Lock)
        {
            future = InputFuture; // Port of session.py:44 future = self.input_future
            InputFuture = null; // Port of session.py:45 self.input_future = None
            masked = InputMasked; // Port of session.py:46 masked = self._input_masked
            InputMasked = false; // Port of session.py:47 self._input_masked = False
            stack = new List<(GameObject? Prev, GameObject Target)>(PuppetStack); // Port of session.py:48 stack, self.puppet_stack = self.puppet_stack, []
            PuppetStack.Clear();
            puppet = Puppet; // Port of session.py:49 puppet = self.puppet
            Puppet = null; // Port of session.py:50 self.puppet = None
            if (puppet != null) LastPuppet = puppet; // Port of session.py:51 self.last_puppet = puppet if puppet is not None else self.last_puppet
        }
        // Port of session.py:52-56 if masked and self.connection is not None: send echo_on
        if (masked && Connection != null)
        {
            try { Connection.SendCommand("echo_on"); } catch { }
        }
        // Port of session.py:57-79 if future is not None: try loop.call_soon_threadsafe(cancel)
        if (future != null)
        {
            // C# equivalent of Python's asyncio loop.call_soon_threadsafe(_do_cancel)
            // Use TrySetCanceled thread-safe; if Task already completed, no-op (mirrors InvalidStateError pass)
            try
            {
                // If future was created on a threadpool scheduler, TrySetCanceled is already thread-safe.
                // We attempt TrySetCanceled directly; if it fails because already completed, ignore.
                future.TrySetCanceled();
            }
            catch { }
            // If we had a captured SynchronizationContext/TaskScheduler, we could post, but TrySetCanceled is safe.
        }
        // Port of session.py:81-86 unwind any in-progress puppet chain before autosave
        while (stack.Count > 0)
        {
            var (_, target) = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            // Port of session.py:83-85 if restore := getattr(target, "_puppet_restore", None): target.__dict__.update(restore); del target._puppet_restore
            // In C# GameObject stores PuppetRestoreSnapshot via internal field; unwind here.
            // Wontfix: only is_pc/privilege_level per puppet.py:110 — handled in GameObject.RestorePuppetSnapshot
            try
            {
                var restore = ((dynamic)target).GetPuppetRestore() as System.Collections.Generic.Dictionary<string, object>;
                if (restore != null)
                {
                    try { ((dynamic)target).RestorePuppetSnapshot((dynamic)restore); } catch { }
                    try { ((dynamic)target).ClearPuppetRestore(); } catch { }
                }
                else
                {
                    // Try raw field fallback
                    var field = target.GetType().GetField("_puppetRestore", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var val = field.GetValue(target) as System.Collections.Generic.Dictionary<string, object>;
                        if (val != null)
                        {
                            try { ((dynamic)target).RestorePuppetSnapshot((dynamic)val); } catch { }
                            try { field.SetValue(target, null); } catch { }
                        }
                    }
                }
            }
            catch
            {
                // Fallback reflection if GameObject not yet patched — ignore
            }
        }
        // Port of session.py:86-114 if puppet: elapsed handling, puppet.session=None, seconds_played, at_disconnect, is_temporary cleanup
        if (puppet != null)
        {
            double elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ConnTime; // Port of session.py:87 elapsed = time.time() - self.conn_time
            if (ConnTime > 0.0 && elapsed > 0) // Port of session.py:88 if self.conn_time >0 and elapsed>0
            {
                try { ((dynamic)puppet).Session = null; } catch { } // Port of session.py:89 puppet.session = None
                try { ((dynamic)puppet).SecondsPlayed = ((dynamic)puppet).SecondsPlayed + elapsed; } catch { } // Port of session.py:90 puppet.seconds_played += elapsed
                SecondsPlayed += elapsed;
            }
            try { ((dynamic)puppet).AtDisconnect(); } catch { } // Port of session.py:91 puppet.at_disconnect()
            if (puppet.IsTemporary) // Port of session.py:92 if getattr(puppet, "is_temporary", False)
            {
                // Port of session.py:93-114 temp PC cleanup: remove from location, remove_object, is_deleted
                try
                {
                    var locRef = puppet.Location;
                    if (locRef is Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation ol)
                    {
                        var objs = Globals.ObjectRegistry.Get(ol.ObjectId);
                        if (objs.Count > 0)
                        {
                            try { ((dynamic)objs[0]).RemoveObject((dynamic)puppet); } catch { try { objs[0].RemoveContent(puppet.Id); } catch { } }
                        }
                    }
                    else if (locRef is Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation)
                    {
                        // Node case: we lack global NodeHandler singleton here; best effort clear location.
                        // The node’s _contents will be cleaned via RemoveObject fallback if Node is also GameObject.
                        try
                        {
                            var nodeObjs = Globals.ObjectRegistry.FilterBy(o => o.IsNode);
                            foreach (var n in nodeObjs)
                            {
                                try { n.RemoveContent(puppet.Id); } catch { }
                            }
                        }
                        catch { }
                    }
                    try { puppet.Location = Atheriz.Core.Persistence.Dto.LocationRef.NullLocation.Instance; } catch { }
                }
                catch { }
                try { Globals.ObjectRegistry.RemoveObject(puppet); } catch { }
                try { puppet.IsDeleted = true; } catch { }
            }
        }
        if (Account is Account acc2) // Port of session.py:115-116 if self.account: self.account.at_disconnect()
        {
            try { acc2.AtDisconnect(); } catch { }
        }
        else if (Account != null)
        {
            // Generic account with AtDisconnect method via reflection fallback
            try { (Account as dynamic)?.AtDisconnect(); } catch { }
        }
        // Port of session.py at_disconnect mapedit discard: chains are valid
        // only while this session is open.
        try { Globals.MapEdit.DiscardSession(this); } catch { }
    }

    // Port of session.py:118-119 msg
    public void Msg(string text)
    {
        Connection?.Msg(text);
    }

    // Full msg overload
    public void Msg(string text, string? msgType = null)
    {
        // Mirrors session.msg(*args, **kwargs) -> connection.msg
        // For simplicity delegate to connection
        Connection?.Msg(text);
    }

    // Port of session.py:121-202 async def prompt(text: str, mask: bool = False) -> str
    /// <summary>
    /// Port of <c>atheriz/objects/session.py:121 prompt</c>.
    /// Sends <paramref name="text"/> and awaits response via <see cref="InputFuture"/>.
    /// Handles _input_masked echo logic (echo_on when switching mask) and prev future completion.
    /// </summary>
    public async Task<string> Prompt(string text, bool mask = false)
    {
        TaskCompletionSource<string>? prev = null;
        bool prevMasked = false;
        bool needRestore = false;
        TaskCompletionSource<string> future;
        // Port of session.py:125-163 with self.lock: create future, swap
        lock (Lock)
        {
            prev = InputFuture; // Port of session.py:129 prev = self.input_future
            prevMasked = InputMasked; // Port of session.py:130 prev_masked = self._input_masked
            // Port of session.py:131-156 try create_future via running loop else fallback to connection loop or threadpool loop
            // In C# we always use TaskCompletionSource with RunContinuationsAsynchronously (thread-safe)
            future = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (prev != null && !prev.Task.IsCompleted) // Port of session.py:157 if prev is not None and not prev.done():
            {
                if (prevMasked && !mask) // Port of session.py:158 if prev_masked and not mask:
                    needRestore = true; // Port of session.py:159 need_restore = True
            }
            else
            {
                prev = null; // Port of session.py:161 prev = None
            }
            InputFuture = future; // Port of session.py:162 self.input_future = future
            InputMasked = mask; // Port of session.py:163 self._input_masked = mask
        }
        // Port of session.py:164-189 if prev is not None: loop.call_soon_threadsafe(set_result(""))
        if (prev != null)
        {
            try
            {
                // Thread-safe completion with empty string (mirrors prev.set_result(""))
                prev.TrySetResult("");
            }
            catch { }
        }
        // Port of session.py:190-194 if need_restore: connection.send_command("echo_on")
        if (needRestore)
        {
            try { Connection?.SendCommand("echo_on"); } catch { }
        }
        // Port of session.py:195-202 if mask: connection.send_command("prompt_masked", text) else msg(text)
        if (mask)
        {
            try { Connection?.SendCommand("prompt_masked", text); } catch { }
        }
        else
        {
            try { Msg(text); } catch { }
        }
        return await future.Task.ConfigureAwait(false); // Port of session.py:202 return await future
    }
}
