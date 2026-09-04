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
        catch { }
        if (fromObj != null)
        {
            try { fromObj.AtMsgSend(parsed, this, msgType); } catch { }
        }
        _lock.EnterWriteLock();
        try { _msgLog.Add(parsed); }
        finally { _lock.ExitWriteLock(); }
        // Forward to session if puppeted — mirrors base_obj.py:904 if self.session is not None: self.session.msg(*args, **kwargs)
        Session? sess = null;
        _lock.EnterReadLock();
        try { sess = _session; }
        finally { _lock.ExitReadLock(); }
        if (sess != null && sess.Connection != null)
        {
            try { sess.Msg(parsed); } catch { }
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
            if (msgSelf) Msg($"You say, \"{text}\"");
            var loc = ResolveLocationObject();
            if (loc is Node node) node.MsgContents($"{Name} says, \"{text}\"", fromObj: this, exclude: msgSelf ? new List<GameObject>{this} : null);
            else if (loc != null) loc.MsgContents($"{Name} says, \"{text}\"", fromObj: this, exclude: msgSelf ? new List<GameObject>{this} : null);
            return 0;
        }, text, msgSelf);
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
    public virtual string GetDisplayName(GameObject? looker) => Name;

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
            try { func(obj); } catch { }
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
            try { func(obj, kwargs ?? new Dictionary<string, object?>()); } catch { }
        }
    }

    /// <summary>
    /// Port of <c>atheriz/objects/base_obj.py:934</c> <c>msg_contents</c>.
    /// Emits <paramref name="text"/> to all objects inside this, handling both actor-stance
    /// <c>$You/$you/$conj/$pron</c> via <see cref="FuncParser"/> and director <c>{key}</c> via
    /// <see cref="FuncParserHelpers.SafeFormatMap"/>.
    /// </summary>
    public void MsgContents(string? text, GameObject? fromObj = null, IDictionary<string, object?>? mapping = null, IEnumerable<GameObject>? exclude = null, bool raiseErrors = false, string? msgType = null, Func<int, GameObject?>? resolver = null)
    {
        if (text == null) text = "";
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
            // Directly append to receiver without re-parsing (avoid double)
            receiver.AppendMessage(outMessage, fromObj, msgType);
        }
    }

    private void AppendMessage(string text, GameObject? fromObj, string? msgType)
    {
        try { if (!AtMsgReceive(text, fromObj, msgType)) return; } catch { }
        if (fromObj != null) try { fromObj.AtMsgSend(text, this, msgType); } catch { }
        _lock.EnterWriteLock();
        try { _msgLog.Add(text); }
        finally { _lock.ExitWriteLock(); }
    }
}
