using Atheriz.Core.Globals;

namespace Atheriz.Core.Objects;

public partial class GameObject
{
    // IMessageTarget entry — director stance when no mapping; delegates to full overload
    public virtual void Msg(string text) => Msg(text, null, null, false, null);

    /// <summary>
    /// Full Msg port of <c>atheriz/objects/base_obj.py:880</c>.
    /// When <paramref name="text"/> contains <c>$</c> or <c>{key}</c>, it is parsed via
    /// <see cref="FuncParser"/> using director stance (mapping) and actor stance (caller vs receiver).
    /// Mirrors Python's <c>at_msg_send</c>/<c>at_msg_receive</c> hooks (advisory, no abort).
    /// </summary>
    public virtual void Msg(string text, GameObject? fromObj, IDictionary<string, object?>? mapping, bool raiseErrors = false, string? msgType = null)
    {
        // Resolve parsed text if funcparser tokens present
        string parsed = text ?? "";
        if (!string.IsNullOrEmpty(parsed) && (parsed.Contains('$') || parsed.Contains('{')))
        {
            try
            {
                // Msg is director stance: actor==receiver==this, mapping via {key} + any $func using self as actor/receiver
                var actor = fromObj ?? this;
                parsed = FuncParser.Parse(parsed, actor, this, mapping, raiseErrors);
            }
            catch (FuncParser.ParsingError)
            {
                if (raiseErrors) throw;
                // leave as original on error if !raiseErrors (already handled inside Parse)
            }
        }
        // at_msg_receive hook (advisory) — if it returns false, abort
        try
        {
            // Hookable wrapper would be used in real port; we call directly and honour false
            if (!AtMsgReceive(parsed, fromObj, msgType)) return;
        }
        catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Msg: " + logEx.Message, "GameObject"); }
        if (fromObj != null)
        {
            try { fromObj.AtMsgSend(parsed, this, msgType); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Msg: " + logEx.Message, "GameObject"); }
        }
        // single critical section — the log append and the session read
        // happen under one write hold, so a session swap between them can no
        // longer log under one session and forward under another. The actual
        // socket send stays outside the lock (never do I/O under SyncRoot).
        Session? sess;
        _lock.EnterWriteLock();
        // Bounded like Channel history (see MsgLogLimit = 200) — see AppendMessage below.
        try
        {
            _msgLog.Add(parsed); while (_msgLog.Count > MsgLogLimit) _msgLog.RemoveAt(0);
            sess = _session;
        }
        finally { _lock.ExitWriteLock(); }
        // Forward to session if puppeted — mirrors base_obj.py:904 if self.session is not None: self.session.msg(*args, **kwargs)
        if (sess != null && sess.Connection != null)
        {
            try { sess.Msg(parsed); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Msg: " + logEx.Message, "GameObject"); }
        }
    }

    /// <summary>
    /// Mirrors <c>Object.msg</c> variety with params-object style for callers using keyword-style.
    /// </summary>
    public void Msg(string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, bool raiseErrors = false) => Msg(text ?? "", fromObj, mapping, raiseErrors, null);

    public bool AtMsgReceive(string? text, GameObject? fromObj, string? msgType) => Hookable("at_msg_receive", () => true, text, fromObj, msgType);
    public void AtMsgSend(string? text, GameObject? toObj, string? msgType) => Hookable("at_msg_send", () => 0, text, toObj, msgType);

    public virtual void AtSay(string text, bool msgSelf = true)
    {
        Hookable("at_say", () =>
        {
            AtSayFull(text, msgSelf);
            return 0;
        }, text, msgSelf);
    }

    /// <summary>
    /// Full port of <c>base_obj.py:1976-2115 at_say</c>: say/whisper modes,
    /// per-receiver mapping, location exclude of self+receivers, msg_type
    /// forwarding. The `(text, msgSelf)` override above is the
    /// backwards-compatible entry point (existing overrides keep working).
    /// </summary>
    public virtual void AtSayFull(string message, object? msgSelf = null, string? msgLocation = null, IEnumerable<GameObject>? receivers = null, string? msgReceivers = null, string? msgType = null, bool whisper = false, IDictionary<string, object?>? mapping = null)
    {
        Hookable("at_say", () =>
        {
            var recvList = receivers?.ToList();
            if (recvList != null && recvList.Count == 0) recvList = null;
            string type;
            object? selfText = msgSelf;
            string? locText = msgLocation;
            string? recvText = msgReceivers;
            if (whisper)
            {
                type = "whisper";
                if (selfText is true) selfText = "{self} whisper to {all_receivers}, \"\x1b[1;37m{speech}\x1b[0m\"";
                recvText ??= "{object} whispers: \"\x1b[1;37m{speech}\x1b[0m\"";
                locText = null;
            }
            else
            {
                type = "say";
                if (selfText is true) selfText = "{self} say, \"\x1b[1;37m{speech}\x1b[0m\"";
                locText ??= "{object} says, \"\x1b[1;37m{speech}\x1b[0m\"";
                recvText ??= message;
            }
            var custom = mapping ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            var loc = ResolveLocationObject();
            string allRecvSelf = recvList != null ? string.Join(", ", recvList.Select(r => r.GetDisplayName(this))) : null!;
            if (selfText is string selfStr && !string.IsNullOrEmpty(selfStr))
            {
                var selfMapping = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["self"] = "You",
                    ["object"] = GetDisplayName(this),
                    ["location"] = loc != null ? loc.GetDisplayName(this) : null,
                    ["receiver"] = null,
                    ["all_receivers"] = allRecvSelf,
                    ["speech"] = message,
                };
                foreach (var kv in custom) selfMapping[kv.Key] = kv.Value;
                Msg(selfStr, this, selfMapping, false, type);
            }
            if (recvList != null && !string.IsNullOrEmpty(recvText))
            {
                foreach (var receiver in recvList)
                {
                    var rMapping = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["self"] = "You",
                        ["object"] = GetDisplayName(receiver),
                        ["location"] = loc != null ? loc.GetDisplayName(receiver) : null,
                        ["receiver"] = receiver.GetDisplayName(receiver),
                        ["all_receivers"] = string.Join(", ", recvList.Select(r => r.GetDisplayName(r))),
                        ["speech"] = message,
                    };
                    foreach (var kv in custom) rMapping[kv.Key] = kv.Value;
                    receiver.Msg(recvText, this, rMapping, false, type);
                }
            }
            if (loc != null && !string.IsNullOrEmpty(locText))
            {
                var locMapping = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["self"] = "You",
                    ["object"] = GetDisplayName(this),
                    ["location"] = loc.GetDisplayName(this),
                    ["all_receivers"] = recvList != null ? string.Join(", ", recvList.Select(r => r.ToString())) : null,
                    ["receiver"] = null,
                    ["speech"] = message,
                };
                foreach (var kv in custom) locMapping[kv.Key] = kv.Value;
                var exclude = new List<GameObject>();
                if (selfText is string s2 && !string.IsNullOrEmpty(s2)) exclude.Add(this);
                if (recvList != null) exclude.AddRange(recvList);
                if (loc is Node node) node.MsgContents(locText, fromObj: this, mapping: locMapping, exclude: exclude, msgType: type);
                else loc.MsgContents(locText, fromObj: this, mapping: locMapping, exclude: exclude, msgType: type);
            }
            return 0;
        }, message, msgSelf, msgLocation, receivers, msgReceivers, msgType, whisper, mapping);
    }

    public IReadOnlyList<string> PeekMessages()
    {
        _lock.EnterReadLock();
        try { return _msgLog.ToList(); }
        finally { _lock.ExitReadLock(); }
    }
    public void ClearMessages()
    {
        _lock.EnterWriteLock();
        try { _msgLog.Clear(); }
        finally { _lock.ExitWriteLock(); }
    }
    // Port of base_obj.py:1428-1437 get_display_name.
    public virtual string GetDisplayName(GameObject? looker)
    {
        if (IsPc && !IsConnected) return $"{Name} (offline)";
        if (looker == null) return Name;
        if (Access(looker, "view")) return Name;
        return IsPc || IsNpc ? "Someone" : "Something";
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:908</c> <c>for_contents</c>.
    /// Runs <paramref name="func"/> on every object contained within this one.
    /// </summary>
    public void ForContents(Action<GameObject> func, IEnumerable<GameObject>? exclude = null, Func<int, GameObject?>? resolver = null)
    {
        var excl = exclude != null ? new HashSet<GameObject>(exclude) : null;
        List<GameObject> contents;
        if (resolver != null)
        {
            var ids = ContentsSnapshot;
            contents = ids.Select(resolver).Where(o => o != null).Cast<GameObject>().ToList();
        }
        else
        {
            // Fallback to ObjectRegistry
            contents = Globals.ObjectRegistry.Get(ContentsSnapshot.ToList());
        }
        foreach (var obj in contents)
        {
            if (excl != null && excl.Contains(obj)) continue;
            try { func(obj); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ForContents: " + logEx.Message, "GameObject"); }
        }
    }
    public void ForContents(Action<GameObject, IDictionary<string, object?>> func, IDictionary<string, object?>? kwargs = null, IEnumerable<GameObject>? exclude = null, Func<int, GameObject?>? resolver = null)
    {
        var excl = exclude != null ? new HashSet<GameObject>(exclude) : null;
        List<GameObject> contents;
        if (resolver != null) contents = ContentsSnapshot.Select(resolver).Where(o=>o!=null).Cast<GameObject>().ToList();
        else contents = Globals.ObjectRegistry.Get(ContentsSnapshot.ToList());
        foreach (var obj in contents)
        {
            if (excl != null && excl.Contains(obj)) continue;
            try { func(obj, kwargs ?? new Dictionary<string, object?>()); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ForContents: " + logEx.Message, "GameObject"); }
        }
    }

    // Inspection buffer bound: PeekMessages is test/support surface, but
    // long-lived NPCs must not accumulate unbounded logs. 200 keeps whole
    // multi-screen outputs (exam dumps ~60 lines) while bounding memory.
    // Oldest entries drop first.
    private const int MsgLogLimit = 200;

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:934</c> <c>msg_contents</c>.
    /// Emits <paramref name="text"/> to all objects inside this, handling both actor-stance
    /// <c>$You/$you/$conj/$pron</c> via <see cref="FuncParser"/> and director <c>{key}</c> via
    /// <see cref="FuncParserHelpers.SafeFormatMap"/>.
    /// </summary>
    public void MsgContents(string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, IEnumerable<GameObject>? exclude = null, bool raiseErrors = false, string? msgType = null, Func<int, GameObject?>? resolver = null)
    {
        if (text == null) text = "";
        if (mapping != null) mapping = new Dictionary<string, object?>(mapping, StringComparer.Ordinal);
        mapping ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        var you = fromObj ?? this;
        if (!mapping.ContainsKey("you")) mapping["you"] = you;

        HashSet<GameObject>? exclSet = exclude != null ? new HashSet<GameObject>(exclude) : null;
        List<GameObject> receivers;
        if (resolver != null)
            receivers = ContentsSnapshot.Select(resolver).Where(o=>o!=null).Cast<GameObject>().ToList();
        else
            receivers = Globals.ObjectRegistry.Get(ContentsSnapshot.ToList());

        foreach (var receiver in receivers)
        {
            if (exclSet != null && exclSet.Contains(receiver)) continue;
            string outMessage;
            try
            {
                // Actor-stance via FuncParser (caller=you, receiver=each listener)
                outMessage = FuncParser.Parse(text, you, receiver, mapping, raiseErrors);
            }
            catch (FuncParser.ParsingError)
            {
                if (raiseErrors) throw;
                outMessage = text;
            }
            // Port of base_obj.py msg_contents tail: receiver.msg(...) — a full
            // send (session delivery), not a log-only append . Null
            // mapping avoids double-parsing the already-parsed message.
            try { receiver.Msg(outMessage, fromObj, null, false, msgType); }
            catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.MsgContents: " + logEx.Message, "GameObject"); }
        }
    }

    private void AppendMessage(string text, GameObject? fromObj, string? msgType)
    {
        try { if (!AtMsgReceive(text, fromObj, msgType)) return; } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AppendMessage: " + logEx.Message, "GameObject"); }
        if (fromObj != null) try { fromObj.AtMsgSend(text, this, msgType); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.AppendMessage: " + logEx.Message, "GameObject"); }
        _lock.EnterWriteLock();
        // Bounded like Channel history (see MsgLogLimit = 200): long-lived NPCs must not
        // accumulate unbounded message logs. Oldest entries drop first.
        try { _msgLog.Add(text); while (_msgLog.Count > MsgLogLimit) _msgLog.RemoveAt(0); }
        finally { _lock.ExitWriteLock(); }
    }
}
