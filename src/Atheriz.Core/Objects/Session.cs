using System.Diagnostics;
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
public class Session : Atheriz.Core.Commands.ISessionProvider
{
    // Port of session.py:16-38
    // Guards puppet / puppet_stack / input_future, which are written by game workers and read by per-connection input drain (#31).
    // Scalar fields (term/map dims, screenreader) are single atomic stores under the GIL and need no lock — we still guard writes.
    public readonly object Lock = new(); // Port of session.py:21 lock = threading.RLock()
    public Account? Account
    {
        get => _account;
        // Spec extra mirror: keep AccountId in sync (it is otherwise set only
        // in the ctor and goes stale on later swaps).
        set { _account = value; AccountId = value?.Id; }
    }
    private Account? _account;
    public int? AccountId; // Spec extra: mirror Account.Id for quick lookup (Python stores object, C# stores both)
    public BaseConnection? Connection; // Port of session.py:23 connection: Connection | None
    public GameObject? LastPuppet; // Port of session.py:24 last_puppet
    public GameObject? Puppet; // Port of session.py:25 puppet
    // stack of (prev_puppet, target). Each target carries its own _puppet_restore manifest (excluded from pickling by __getstate__).
    // Lives on the session (never pickled) so transient restore state stays off saved objects. Port of session.py:29 puppet_stack
    private readonly List<(GameObject? Prev, GameObject Target)> _puppetStack = new();
    // snapshot, not the live list — an escaped List lets any holder
    // mutate the unwind stack without session.Lock. Writers use the entry
    // methods below; every caller already holds session.Lock (single critical
    // sections), so the mutators take no lock themselves.
    public IReadOnlyList<(GameObject? Prev, GameObject Target)> PuppetStack
    {
        get { lock (Lock) { return _puppetStack.ToList(); } }
    }
    internal void PushPuppetEntry(GameObject? prev, GameObject target) => _puppetStack.Add((prev, target));
    internal bool TryPopPuppetEntry(out GameObject? prev, out GameObject target)
    {
        if (_puppetStack.Count == 0) { prev = null; target = null!; return false; }
        var last = _puppetStack[^1];
        _puppetStack.RemoveAt(_puppetStack.Count - 1);
        prev = last.Prev;
        target = last.Target;
        return true;
    }
    internal void ClearPuppetEntries() => _puppetStack.Clear();
    /// <summary>
    /// Hot-reload rewire: point Puppet/LastPuppet/PuppetStack entries at the
    /// replacement instance (matched by id). Python's __class__ swap preserves
    /// identity; C# must rewire direct refs after AddObject replaces the id.
    /// </summary>
    public void ReplacePuppetRefs(GameObject replacement)
    {
        lock (Lock)
        {
            if (Puppet is not null && Puppet.Id == replacement.Id && !ReferenceEquals(Puppet, replacement))
                Puppet = replacement;
            if (LastPuppet is not null && LastPuppet.Id == replacement.Id && !ReferenceEquals(LastPuppet, replacement))
                LastPuppet = replacement;
            for (int i = 0; i < _puppetStack.Count; i++)
            {
                var (prev, target) = _puppetStack[i];
                var nprev = (prev is not null && prev.Id == replacement.Id && !ReferenceEquals(prev, replacement)) ? replacement : prev;
                var ntarget = (target.Id == replacement.Id && !ReferenceEquals(target, replacement)) ? replacement : target;
                if (!ReferenceEquals(nprev, prev) || !ReferenceEquals(ntarget, target))
                    _puppetStack[i] = (nprev, ntarget);
            }
        }
    }
    // Wontfix: puppet snapshot incomplete — only is_pc/privilege_level per puppet.py:110,138-142.
    // Do NOT store quelled/can_hear/is_mapable; document here. Port of puppet.py:110 restore_snapshot = {"is_pc":..., "privilege_level":...}
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

    // F001 typed seam: a session provides itself (satisfies ISessionProvider without reflection).
    Session? Atheriz.Core.Commands.ISessionProvider.Session => this;

    public Session(BaseConnection? connection = null, Account? account = null)
    {
        Connection = connection;
        Account = account;
        if (account is not null) AccountId = account.Id;
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
            stack = new List<(GameObject? Prev, GameObject Target)>(_puppetStack); // Port of session.py:48 stack, self.puppet_stack = self.puppet_stack, []
            ClearPuppetEntries();
            puppet = Puppet; // Port of session.py:49 puppet = self.puppet
            Puppet = null; // Port of session.py:50 self.puppet = None
            if (puppet is not null) LastPuppet = puppet; // Port of session.py:51 self.last_puppet = puppet if puppet is not None else self.last_puppet
            // Port of session.py:81-86 unwind any in-progress puppet chain.
            // Runs INSIDE the lock : a Puppet landing between the
            // snapshot and the unwind would otherwise leak IsPc + privilege
            // on the NPC. Restore helpers take only the target's lock
            // (session -> object order, same as Puppet/Unpuppet).
            while (stack.Count > 0)
            {
                var (_, target) = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                // Port of session.py:83-85 if restore := getattr(target, "_puppet_restore", None): target.__dict__.update(restore); del target._puppet_restore
                // GameObject carries the snapshot as a typed internal member (same
                // assembly) — no dynamic/reflection needed.
                // Wontfix: only is_pc/privilege_level per puppet.py:110 — handled in GameObject.RestorePuppetSnapshot
                try
                {
                    var restore = target.GetPuppetRestore();
                    if (restore is not null)
                    {
                        target.RestorePuppetSnapshot(restore);
                        target.ClearPuppetRestore();
                    }
                }
                catch
                {
                    // One bad target must not break the unwind loop.
                }
            }
        }
        // Port of session.py:52-56 if masked and self.connection is not None: send echo_on
        if (masked && Connection is not null)
        {
            try { Connection.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
        }
        // Port of session.py:57-79 if future is not None: try loop.call_soon_threadsafe(cancel)
        if (future is not null)
        {
            // C# equivalent of Python's asyncio loop.call_soon_threadsafe(_do_cancel)
            // Use TrySetCanceled thread-safe; if Task already completed, no-op (mirrors InvalidStateError pass)
            try
            {
                // If future was created on a threadpool scheduler, TrySetCanceled is already thread-safe.
                // We attempt TrySetCanceled directly; if it fails because already completed, ignore.
                future.TrySetCanceled();
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
            // If we had a captured SynchronizationContext/TaskScheduler, we could post, but TrySetCanceled is safe.
        }
        // Port of session.py:81-86 unwind any in-progress puppet chain before autosave
        // (unwind itself runs inside the lock above).
        // Port of session.py:86-114 if puppet: elapsed handling, puppet.session=None, seconds_played, at_disconnect, is_temporary cleanup
        if (puppet is not null)
        {
            double elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - ConnTime; // Port of session.py:87 elapsed = time.time() - self.conn_time
            if (ConnTime > 0.0 && elapsed > 0) // Port of session.py:88 if self.conn_time >0 and elapsed>0
            {
                puppet.Session = null; // Port of session.py:89 puppet.session = None
                // NOTE: Session is nulled first so the SecondsPlayed getter returns
                // the stored base (no live elapsed), matching Python's += elapsed.
                puppet.SecondsPlayed = puppet.SecondsPlayed + elapsed; // Port of session.py:90 puppet.seconds_played += elapsed
                SecondsPlayed += elapsed;
            }
            puppet.AtDisconnect(); // Port of session.py:91 puppet.at_disconnect()
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
                            objs[0].RemoveObject(puppet);
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
                                try { n.RemoveContent(puppet.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                            }
                        }
                        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                    }
                    try { puppet.Location = Atheriz.Core.Persistence.Dto.LocationRef.NullLocation.Instance; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                try { Globals.ObjectRegistry.RemoveObject(puppet); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                try { puppet.IsDeleted = true; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
            }
        }
        if (Account is not null) // Port of session.py:115-116 if self.account: self.account.at_disconnect()
        {
            try { Account.AtDisconnect(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
        }
        // Port of session.py at_disconnect mapedit discard: chains are valid
        // only while this session is open.
        try { Globals.MapEdit.DiscardSession(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
    }

    // Port of session.py:118-119 msg
    public void Msg(string text)
    {
        Connection?.Msg(text);
    }

    // Full msg overload. Port of session.msg(*args, **kwargs) -> connection.msg:
    // a msgType becomes the command (mirrors connection.py popping the kwarg key).
    public void Msg(string text, string? msgType = null)
    {
        if (Connection is null) return;
        if (msgType is null) Connection.Msg(text);
        else Connection.MsgKw(new Dictionary<string, object?> { [msgType] = text });
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
            if (prev is not null && !prev.Task.IsCompleted) // Port of session.py:157 if prev is not None and not prev.done():
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
        if (prev is not null)
        {
            try
            {
                // Thread-safe completion with empty string (mirrors prev.set_result(""))
                prev.TrySetResult("");
            }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        // Port of session.py:190-194 if need_restore: connection.send_command("echo_on")
        if (needRestore)
        {
            try { Connection?.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        // Port of session.py:195-202 if mask: connection.send_command("prompt_masked", text) else msg(text)
        if (mask)
        {
            try { Connection?.SendCommand("prompt_masked", text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        else
        {
            try { Msg(text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        return await future.Task.ConfigureAwait(false); // Port of session.py:202 return await future
    }

    /// <summary>
    /// Completes the currently pending prompt (if it is still <paramref name="token"/>) with an
    /// empty result and clears it, so a timed-out <see cref="Prompt"/> leaves no orphaned
    /// <see cref="InputFuture"/>. Mirrors Python <c>asyncio.wait_for</c> cancelling the prompt
    /// coroutine on timeout in <c>menu.py run_menu</c>. Returns false when there is nothing to
    /// cancel (already answered, or a newer prompt has taken over).
    /// </summary>
    public bool CancelPrompt(object? token = null)
    {
        TaskCompletionSource<string>? f;
        lock (Lock)
        {
            f = InputFuture;
            if (f is null) return false;
            if (token is not null && !ReferenceEquals(f, token)) return false; // newer prompt owns the slot
            InputFuture = null;
            InputMasked = false;
        }
        try { f.TrySetResult(""); }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.CancelPrompt: " + logEx.Message, "Session"); }
        return true;
    }
}
