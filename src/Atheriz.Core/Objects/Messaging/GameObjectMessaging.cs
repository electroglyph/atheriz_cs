
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
        string parsed = text;
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
        if (fromObj is not null)
        {
            try { fromObj.AtMsgSend(parsed, this, msgType); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.Msg: " + logEx.Message, "GameObject"); }
        }
        // single critical section — the log append and the session read
        // happen under one write hold, so a session swap between them can no
        // longer log under one session and forward under another. The actual
        // socket send stays outside the lock (never do I/O under SyncRoot).
        Session? sess;
        _lock.EnterWriteLock();
        // Bounded like Channel history (see MsgLogLimit = 200).
        try
        {
            _msgLog.Add(parsed); while (_msgLog.Count > MsgLogLimit) _msgLog.RemoveAt(0);
            sess = _session;
        }
        finally { _lock.ExitWriteLock(); }
        // Forward to session if puppeted — mirrors base_obj.py:904 if self.session is not None: self.session.msg(*args, **kwargs)
        if (sess is not null && sess.Connection is not null)
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
            if (recvList is not null && recvList.Count == 0) recvList = null;
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
                // A caller-supplied type (emote and friends route through this
                // same entry) is preserved; plain say passes none and keeps "say".
                type = msgType ?? "say";
                if (selfText is true) selfText = "{self} say, \"\x1b[1;37m{speech}\x1b[0m\"";
                locText ??= "{object} says, \"\x1b[1;37m{speech}\x1b[0m\"";
                recvText ??= message;
            }
            var custom = mapping ?? new Dictionary<string, object?>(StringComparer.Ordinal);
            var loc = ResolveLocationObject();
            string allRecvSelf = recvList is not null ? string.Join(", ", recvList.Select(r => r.GetDisplayName(this))) : null!;
            if (selfText is string selfStr && !string.IsNullOrEmpty(selfStr))
            {
                var selfMapping = BuildSayMapping(GetDisplayName(this), loc is not null ? loc.GetDisplayName(this) : null, null, allRecvSelf, message, custom);
                Msg(selfStr, this, selfMapping, false, type);
            }
            if (recvList is not null && !string.IsNullOrEmpty(recvText))
            {
                string allRecv = string.Join(", ", recvList.Select(r => r.GetDisplayName(r)));
                foreach (var receiver in recvList)
                {
                    var rMapping = BuildSayMapping(GetDisplayName(receiver), loc is not null ? loc.GetDisplayName(receiver) : null, receiver.GetDisplayName(receiver), allRecv, message, custom);
                    receiver.Msg(recvText, this, rMapping, false, type);
                }
            }
            if (loc is not null && !string.IsNullOrEmpty(locText))
            {
                var locMapping = BuildSayMapping(GetDisplayName(this), loc.GetDisplayName(this), null, recvList is not null ? string.Join(", ", recvList.Select(r => r.ToString())) : null, message, custom);
                List<GameObject> exclude = [];
                if (selfText is string s2 && !string.IsNullOrEmpty(s2)) exclude.Add(this);
                if (recvList is not null) exclude.AddRange(recvList);
                ContentUtils.EmitToLocation(loc, locText, fromObj: this, mapping: locMapping, exclude: exclude, msgType: type);
            }
            return 0;
        }, message, msgSelf, msgLocation, receivers, msgReceivers, msgType, whisper, mapping);
    }

    // Shared builder for the three AtSayFull per-audience mappings. The key
    // sets are identical (self/object/location/receiver/all_receivers/speech)
    // — verified element-wise — only the values vary per audience, plus the
    // caller's custom entries merged over the top in each.
    private Dictionary<string, object?> BuildSayMapping(string objectName, string? locationName, string? receiverName, string? allReceivers, string speech, IDictionary<string, object?> custom)
    {
        var built = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["self"] = "You",
            ["object"] = objectName,
            ["location"] = locationName,
            ["receiver"] = receiverName,
            ["all_receivers"] = allReceivers,
            ["speech"] = speech,
        };
        foreach (var kv in custom) built[kv.Key] = kv.Value;
        return built;
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
        if (looker is null) return Name;
        if (Access(looker, "view")) return Name;
        return IsPc || IsNpc ? "Someone" : "Something";
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:908</c> <c>for_contents</c>.
    /// Runs <paramref name="func"/> on every object contained within this one.
    /// </summary>
    public void ForContents(Action<GameObject> func, IEnumerable<GameObject>? exclude = null, Func<int, GameObject?>? resolver = null)
        => ForContents((o, _) => func(o), null, exclude, resolver);
    public void ForContents(Action<GameObject, IDictionary<string, object?>> func, IDictionary<string, object?>? kwargs = null, IEnumerable<GameObject>? exclude = null, Func<int, GameObject?>? resolver = null)
    {
        var excl = exclude is not null ? new HashSet<GameObject>(exclude) : null;
        List<GameObject> contents = ResolveContents(resolver);
        // Hoisted out of the loop: the fallback is only ever read downstream
        // (callers passing an explicit dict already share one instance across
        // iterations), so one shared empty instance behaves the same.
        IDictionary<string, object?> kw = kwargs ?? new Dictionary<string, object?>();
        foreach (var obj in contents)
        {
            if (excl is not null && excl.Contains(obj)) continue;
            try { func(obj, kw); } catch (Exception logEx) { AtherizLogger.LogDebug("Suppressed GameObject.ForContents: " + logEx.Message, "GameObject"); }
        }
    }

    // Inspection buffer bound: PeekMessages is test/support surface, but
    // long-lived NPCs must not accumulate unbounded logs. 200 keeps whole
    // multi-screen outputs (exam dumps ~60 lines) while bounding memory.
    // Oldest entries drop first.
    private const int MsgLogLimit = 200;

    /// <summary>
    /// Shared resolver-vs-registry contents resolution for
    /// <see cref="ForContents"/> and <see cref="MsgContents"/>. A caller
    /// resolver filters nulls (OfType); otherwise the registry resolves the
    /// snapshot (its Get already snapshots internally, so no caller ToList).
    /// </summary>
    private List<GameObject> ResolveContents(Func<int, GameObject?>? resolver)
    {
        if (resolver is not null) return ContentsSnapshot.Select(resolver).OfType<GameObject>().ToList();
        return Globals.ObjectRegistry.Get(ContentsSnapshot);
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:934</c> <c>msg_contents</c>.
    /// Emits <paramref name="text"/> to all objects inside this, handling both actor-stance
    /// <c>$You/$you/$conj/$pron</c> via <see cref="FuncParser"/> and director <c>{key}</c> via
    /// <see cref="FuncParserHelpers.SafeFormatMap"/>.
    /// </summary>
    public void MsgContents(string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, IEnumerable<GameObject>? exclude = null, bool raiseErrors = false, string? msgType = null, Func<int, GameObject?>? resolver = null)
    {
        // Broadcast loop lives in ContentUtils.EmitToContents (shared with the
        // Node overload); only the receiver source stays here. Object delivery
        // keeps ParsingError-only fallback semantics (nodeSemantics: false).
        ContentUtils.EmitToContents(ResolveContents(resolver), this, text, fromObj, mapping, exclude, msgType, raiseErrors, nodeSemantics: false);
    }
}
