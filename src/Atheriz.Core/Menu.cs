namespace Atheriz.Core;

public sealed class MenuContext
{
    public object? Caller { get; }
    public Dictionary<string, object?> State { get; } = new();
    public MenuContext(object? c) { Caller = c; }
}

public sealed class Choice
{
    public string Key { get; }
    public string Desc { get; }
    public Func<MenuContext, Task<(string, List<Choice>)>>? Goto { get; }
    public Func<MenuContext, Task>? Callback { get; }
    public bool Stay { get; }
    public Choice(string key, string desc,
        Func<MenuContext, Task<(string, List<Choice>)>>? gotoNode = null,
        Func<MenuContext, Task>? callback = null,
        bool stay = false)
    {
        Key = key;
        Desc = desc;
        Goto = gotoNode;
        Callback = callback;
        Stay = stay;
    }
}

public sealed class MenuEngine
{
    public MenuContext Context { get; }
    public Func<MenuContext, Task<(string, List<Choice>)>>? CurrentNode { get; private set; }
    string _text = "";
    Dictionary<string, Choice> _choices = new(StringComparer.OrdinalIgnoreCase);

    public MenuEngine(object? caller, Func<MenuContext, Task<(string, List<Choice>)>> start)
    {
        Context = new(caller);
        CurrentNode = start;
    }

    public async Task RenderAsync()
    {
        if (CurrentNode is null) return;
        var (t, cl) = await CurrentNode(Context).ConfigureAwait(false);
        _text = t;
        _choices = BuildChoices(cl);
    }

    // Shared choice-dict build: case-insensitive map + identical
    // ToLowerInvariant().Trim() duplicate-key throw (menu.py:47-51).
    static Dictionary<string, Choice> BuildChoices(List<Choice> cl)
    {
        var d = new Dictionary<string, Choice>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in cl)
        {
            var k = NormalizeKey(c.Key);
            if (d.ContainsKey(k)) throw new InvalidOperationException($"duplicate menu key: '{c.Key}'");
            d[k] = c;
        }
        return d;
    }

    public string Display
    {
        get
        {
            if (CurrentNode is null) return "";
            var lines = new List<string> { $"\n{_text}" };
            foreach (var c in _choices.Values) lines.Add($"  [{c.Key}] {c.Desc}");
            return string.Join("\r\n", lines);
        }
    }

    // Shared input normalization and lookup (menu.py:81-82). Null means
    // "no such key" (stay).
    internal static string NormalizeKey(string? s) => s is null ? "" : s.ToLowerInvariant().Trim();
    Choice? TryGetChoice(string clean) => _choices.TryGetValue(clean, out var ch) ? ch : null;

    public async Task<bool> HandleInputAsync(string? input)
    {
        if (_choices.Count == 0) { CurrentNode = null; return false; }
        if (input is null) return true; // Null means "no such key" (stay).
        var ch = TryGetChoice(NormalizeKey(input));
        // Unknown keys are logged, not silently swallowed; staying on the
        // node keeps the menu going.
        if (ch is null)
        {
            try { AtherizLogger.LogWarning($"menu unknown key: '{input}'"); } catch { }
            return true;
        }
        if (ch.Callback is not null)
        {
            try { await ch.Callback(Context).ConfigureAwait(false); }
            catch { try { AtherizLogger.LogError("menu callback failed"); } catch { } }
        }
        if (ch.Goto is not null)
        {
            CurrentNode = ch.Goto;
            await RenderAsync().ConfigureAwait(false);
            return true;
        }
        if (ch.Stay)
        {
            await RenderAsync().ConfigureAwait(false);
            return true;
        }
        CurrentNode = null;
        return false;
    }

    public void Close()
    {
        CurrentNode = null;
        _text = "";
        _choices.Clear();
        Context.State.Clear();
    }

    public bool HasNode => CurrentNode is not null;
    public IReadOnlyDictionary<string, Choice> CurrentChoices => _choices;
    public string CurrentText => _text;

    // Single session-resolution branch: Session returns itself and GameObject
    // resolves through the same interface, while session-less and foreign
    // callers yield null without throwing.
    internal static Objects.Session? ResolveSession(object? caller)
    {
        if (caller is Commands.ISessionProvider p)
        {
            try { return p.Session; }
            catch { return null; }
        }
        return null;
    }

    // The one prompt loop (display, timeout prompt, handle, log-and-break,
    // close). A null session or a dead/cancelled prompt ends the menu.
    public async Task<bool> RunAsync(Objects.Session session, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            await RenderAsync().ConfigureAwait(false);
            while (HasNode)
            {
                ct.ThrowIfCancellationRequested();
                var wait = timeout ?? TimeSpan.FromSeconds(AtherizSettings.Global.MenuPromptTimeout);
                var inp = await MenuPrompt.PromptWithTimeoutAsync(session, Display, wait).ConfigureAwait(false);
                if (inp is null) break;
                try
                {
                    if (!await HandleInputAsync(inp).ConfigureAwait(false)) break;
                }
                catch { try { AtherizLogger.LogError("menu handle_input failed"); } catch { } break; }
            }
        }
        finally { Close(); }
        return false;
    }

    // Caller-based entry: resolves the session through ISessionProvider like
    // the old runner; unresolvable callers end immediately.
    public async Task<bool> RunAsync(object? caller, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var session = ResolveSession(caller);
        if (session is null) { Close(); return false; }
        return await RunAsync(session, timeout, ct).ConfigureAwait(false);
    }
}
