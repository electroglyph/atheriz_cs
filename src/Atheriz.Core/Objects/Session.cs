using System.Diagnostics;
using Atheriz.Core.Network;
using Atheriz.Core.Persistence.Dto;

namespace Atheriz.Core.Objects;

// Faithful: lock guards puppet/puppet_stack/input_future (issue #31 scalar fields atomic under GIL but we still guard).
// Wontfix puppet snapshot incomplete: only is_pc/privilege_level saved (puppet.py:110,138-142). quelled/can_hear/is_mapable not restored by design.
// Global static salt / other wontfixes not relevant here.

/// <summary>
/// Thread-safe via <see cref="Lock"/> (mirrors Python RLock) guarding Puppet / PuppetStack / InputFuture.
/// Scalar fields Term/Map dims + ScreenReader are atomic (no lock required) but writes are lock-guarded for consistency.
/// </summary>
public class Session : Atheriz.Core.Commands.ISessionProvider
{
    // Guards puppet / puppet_stack / input_future, which are written by game workers and read by per-connection input drain (#31).
    // Scalar fields (term/map dims, screenreader) are single atomic stores under the GIL and need no lock — we still guard writes.
    public readonly object Lock = new();
    public Account? Account
    {
        get => _account;
        // Spec extra mirror: keep AccountId in sync (it is otherwise set only
        // in the ctor and goes stale on later swaps).
        set { _account = value; AccountId = value?.Id; }
    }
    private Account? _account;
    public int? AccountId; // Spec extra: mirror Account.Id for quick lookup (Python stores object, C# stores both)
    public BaseConnection? Connection;
    public GameObject? LastPuppet;
    public GameObject? Puppet;
    // stack of (prev_puppet, target). Each target carries its own _puppet_restore manifest (excluded from pickling by __getstate__).
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
    internal void RemovePuppetEntriesFor(GameObject obj)
    {
        if (obj is null) return;
        int id = obj.Id;
        _puppetStack.RemoveAll(e => (e.Prev is not null && e.Prev.Id == id) || e.Target.Id == id);
    }
    /// <summary>
    /// Hot-reload rewire: point Puppet/LastPuppet/PuppetStack entries at the
    /// replacement instance (matched by id). Python's __class__ swap preserves
    /// identity; C# must rewire direct refs after AddObject replaces the id.
    /// </summary>
    private static bool ShouldRewire(GameObject? cur, GameObject rep) => cur is not null && cur.Id == rep.Id && !ReferenceEquals(cur, rep);
    public void ReplacePuppetRefs(GameObject replacement)
    {
        lock (Lock)
        {
            if (ShouldRewire(Puppet, replacement))
                Puppet = replacement;
            if (ShouldRewire(LastPuppet, replacement))
                LastPuppet = replacement;
            for (int i = 0; i < _puppetStack.Count; i++)
            {
                var (prev, target) = _puppetStack[i];
                var nprev = ShouldRewire(prev, replacement) ? replacement : prev;
                var ntarget = ShouldRewire(target, replacement) ? replacement : target;
                if (!ReferenceEquals(nprev, prev) || !ReferenceEquals(ntarget, target))
                    _puppetStack[i] = (nprev, ntarget);
            }
        }
    }
    // Wontfix: puppet snapshot incomplete — only is_pc/privilege_level per puppet.py:110,138-142.
    public int TermWidth;
    public int TermHeight;
    public int MapWidth;
    public int MapHeight;
    public bool ScreenReader;
    public double ConnTime;
    public TaskCompletionSource<string>? InputFuture;
    public bool InputMasked;
    // True once AtDisconnect runs, until the next AtConnect. Input arriving
    // after teardown must not attach puppets or dispatch commands on a dead
    // session — checked under Lock in the puppet-attach and text paths.
    // Written only under Lock (set in AtDisconnect, cleared in AtConnect).
    public bool Closed { get; private set; }
    public DateTime ConnectedAt; // Spec extra: wall clock for C# convenience (mirrors ConnTime)
    public double SecondsPlayed; // Spec extra: accumulated seconds (mirrors GameObject._seconds_played but session tracks)

    // F001 typed seam: a session provides itself (satisfies ISessionProvider without reflection).
    Session? Atheriz.Core.Commands.ISessionProvider.Session => this;

    public Session(BaseConnection? connection = null, Account? account = null)
    {
        Connection = connection;
        Account = account;
        if (account is not null) AccountId = account.Id;
        TermWidth = 78;
        TermHeight = 45;
        ConnTime = 0.0;
        ConnectedAt = DateTime.UtcNow;
    }

    public virtual void AtConnect()
    {
        ConnTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        ConnectedAt = DateTime.UtcNow;
        lock (Lock) { Closed = false; }
    }

    // Spec variant: AtConnect(Connection) for callers passing connection explicitly
    public virtual void AtConnect(BaseConnection connection)
    {
        Connection = connection;
        AtConnect();
    }

// faithful
    public virtual void AtDisconnect()
    {
        TaskCompletionSource<string>? future;
        bool masked;
        List<(GameObject? Prev, GameObject Target)> stack;
        GameObject? puppet;
        lock (Lock)
        {
            future = InputFuture;
            InputFuture = null;
            masked = InputMasked;
            InputMasked = false;
            Closed = true;
            stack = new List<(GameObject? Prev, GameObject Target)>(_puppetStack);
            ClearPuppetEntries();
            puppet = Puppet;
            Puppet = null;
            if (puppet is not null) LastPuppet = puppet;
            // Runs INSIDE the lock : a Puppet landing between the
            // snapshot and the unwind would otherwise leak IsPc + privilege
            // on the NPC. Restore helpers take only the target's lock
            // (session -> object order, same as Puppet/Unpuppet).
            while (stack.Count > 0)
            {
                var (_, target) = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
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
        if (masked && Connection is not null)
        {
            try { Connection.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
        }
        if (future is not null)
        {
            // C# equivalent of Python's asyncio loop.call_soon_threadsafe(_do_cancel)
            // TrySetCanceled is thread-safe and never throws by .NET contract
            // (a completed future simply returns false), so no try/catch armor.
            future.TrySetCanceled();
            // If we had a captured SynchronizationContext/TaskScheduler, we could post, but TrySetCanceled is safe.
        }
        // (unwind itself runs inside the lock above).
        if (puppet is not null)
        {
            double elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 - ConnTime;
            if (ConnTime > 0.0 && elapsed > 0)
            {
                puppet.Session = null;
                // NOTE: Session is nulled first so the SecondsPlayed getter returns
                // the stored base (no live elapsed), matching Python's += elapsed.
                puppet.SecondsPlayed = puppet.SecondsPlayed + elapsed;
                SecondsPlayed += elapsed;
            }
            puppet.AtDisconnect();
            if (puppet.IsTemporary)
            {
                try
                {
                    var locRef = puppet.Location;
                    if (locRef is Atheriz.Core.Persistence.Dto.LocationRef.ObjectLocation ol)
                    {
                        var single = Globals.ObjectRegistry.GetSingle(ol.ObjectId);
                        if (single is not null)
                        {
                            single.RemoveObject(puppet);
                        }
                    }
                    else if (locRef is Atheriz.Core.Persistence.Dto.LocationRef.CoordLocation cl)
                    {
                        // Node case: fast path resolves the primary node by coord
                        // index; the full scan runs only when the lookup misses
                        // (ungridded node or stray contents scattered), preserving
                        // the old multi-node cleanup semantics for that case.
                        GameObject? primary = null;
                        try { primary = Globals.ObjectRegistry.FindNodeByCoord(cl.Coord); }
                        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                        if (primary is not null)
                        {
                            try { primary.RemoveContent(puppet.Id); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                        }
                        else
                        {
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
                    }
                    try { puppet.Location = Atheriz.Core.Persistence.Dto.LocationRef.NullLocation.Instance; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                }
                catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                try { Globals.ObjectRegistry.RemoveObject(puppet); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
                try { puppet.IsDeleted = true; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
            }
        }
        if (Account is not null)
        {
            try { Account.AtDisconnect(); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
        }
        // only while this session is open.
        try { Globals.MapEdit.DiscardSession(this); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.AtDisconnect: " + logEx.Message, "Session"); }
    }

    public void Msg(string text) => Msg(text, null);

    // a msgType becomes the command (mirrors connection.py popping the kwarg key).
    public void Msg(string text, string? msgType = null)
    {
        if (Connection is null) return;
        if (msgType is null) Connection.Msg(text);
        else Connection.MsgKw(new Dictionary<string, object?> { [msgType] = text });
    }

    /// <summary>
    /// Sends <paramref name="text"/> and awaits response via <see cref="InputFuture"/>.
    /// Handles _input_masked echo logic (echo_on when switching mask) and prev future completion.
    /// </summary>
    public async Task<string> Prompt(string text, bool mask = false)
    {
        var (task, _) = PromptWithToken(text, mask);
        return await task.ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a prompt and atomically returns its task plus the token owning the
    /// <see cref="InputFuture"/> slot, so a racing timeout cancels exactly this
    /// prompt via <see cref="CancelPrompt(object?)"/> instead of re-reading the
    /// slot and cancelling whatever prompt happens to own it then.
    /// </summary>
    public (Task<string> Task, object Token) PromptWithToken(string text, bool mask = false)
    {
        TaskCompletionSource<string>? prev = null;
        bool prevMasked = false;
        bool needRestore = false;
        TaskCompletionSource<string> future;
        lock (Lock)
        {
            prev = InputFuture;
            prevMasked = InputMasked;
            // In C# we always use TaskCompletionSource with RunContinuationsAsynchronously (thread-safe)
            future = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (prev?.Task.IsCompleted != false)
                prev = null;
            else if (prevMasked && !mask)
                needRestore = true;
            InputFuture = future;
            InputMasked = mask;
        }
        if (prev is not null)
        {
            // Thread-safe completion with empty string (mirrors prev.set_result("")) — TrySetResult never throws.
            prev.TrySetResult("");
        }
        if (needRestore)
        {
            try { Connection?.SendCommand("echo_on"); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        if (mask)
        {
            try { Connection?.SendCommand("prompt_masked", text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        else
        {
            try { Msg(text); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed Session.Prompt: " + logEx.Message, "Session"); }
        }
        return (future.Task, future);
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
        f.TrySetResult(""); // never throws.
        return true;
    }
}
