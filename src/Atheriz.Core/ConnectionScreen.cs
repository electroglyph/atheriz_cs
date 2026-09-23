using System.Reflection;

namespace Atheriz.Core;

public static class ConnectionScreen
{
    // Shared gate for the unlogged-in hint lines: a hint must agree with the
    // dispatch gate (IsUnloggedInEnabled), not just the display setting.
    private static string HintText(bool enabled, Commands.Command cmd, string text)
    {
        if (!enabled) return "";
        try { if (!Commands.CommandDispatcher.IsUnloggedInEnabled(cmd)) return ""; } catch { }
        return text;
    }
    // Static gate probes: the commands are stateless (Key/Desc get-only,
    // IsUnloggedInEnabled does type tests only, Run is never invoked), so one
    // shared instance per Render is exact — no per-Render allocation to test
    // the gate.
    private static readonly Commands.UnloggedIn.GuestCommand GuestProbe = new();
    private static readonly Commands.UnloggedIn.CreateAccountCommand CreateProbe = new();
    // hints must agree with the dispatch gate, not just the display
    // settings — the gate also requires the dispatcher snapshot.
    private static string GuestText(AtherizSettings? s = null)
        => HintText((s ?? AtherizSettings.Global).GuestEnabled, GuestProbe, "enter 'guest' to create a temporary character");
    private static string CreateText(AtherizSettings? s = null)
        => HintText((s ?? AtherizSettings.Global).AccountCreationEnabled, CreateProbe, "enter 'create' to make a new account");

    private const string Screen = """
       _____   __  .__                 .____________
      /  _  \_/  |_|  |__   ___________|__\____    /
     /  /_\  \   __\  |  \_/ __ \_  __ \  | /     / 
    /    |    \  | |   Y  \  ___/|  | \/  |/     /_ 
    \____|__  /__| |___|  /\___  >__|  |__/_______ \
            \/          \/     \/                 \/                                  
                                                                             
                                                                       
             ATHERIZ VERSION = {0}
           KNOWN ADVENTURERS = {1}
          ONLINE ADVENTURERS = {2}

    enter 'sr' for screenreader mode
    enter 'connect <account> <password>' to login
    {3}
    {4}
    """;

    private const string Screen2 = """
                     
             ATHERIZ VERSION = {0}
           KNOWN ADVENTURERS = {1}
          ONLINE ADVENTURERS = {2}

    enter 'sr' for screenreader mode
    enter 'connect <account> <password>' to login
    {3}
    {4}
    """;

    private static readonly Lock _lock = new();
    private static double _cacheTs;
    private static int _cacheOnline;
    private static int _cacheKnown;

    // Static predicate (no per-call closure alloc); the 5 s render cache above
    // is untouched.
    private static readonly Func<Objects.GameObject, bool> IsPcFilter = static o => o.IsPc;
    public static (int online, int known) GetOnline()
    {
        var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds(); // now via TimeProvider
        lock (_lock)
        {
            if (now - _cacheTs < 5) return (_cacheOnline, _cacheKnown);
        }
        var results = ObjectRegistry.FilterBy(IsPcFilter);
        // Single pass over the one snapshot: both counts derive from the same
        // already-materialized list, so one loop is arithmetically identical
        // to the old Count(predicate) + Count (no double-enumeration drift).
        int online = 0;
        int known = 0;
        foreach (var o in results)
        {
            known++;
            if (o.IsConnected) online++;
        }
        lock (_lock) { _cacheTs = now; _cacheOnline = online; _cacheKnown = known; }
        return (online, known);
    }

    private static string GetVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var ver = asm.GetName().Version?.ToString();
            if (!string.IsNullOrEmpty(ver)) return ver!;
            // Try package metadata fallback
            return "?";
        }
        catch { return "?"; }
    }

    public static string Render(Session? session = null) => Render(AtherizSettings.Global, session);
    public static string Render(AtherizSettings? settings, Session? session = null)
    {
        settings ??= AtherizSettings.Global;
        var (online, known) = GetOnline();
        var version = GetVersion();
        var createText = CreateText(settings);
        var guestText = GuestText(settings);

        bool isScreenReader = session is not null && session.ScreenReader;
        string full;
        if (isScreenReader)
        {
            full = string.Format(Screen2, version, known, online, createText, guestText);
        }
        else
        {
            full = string.Format(Screen, version, known, online, createText, guestText);
        }

        // Byte-faithful to connection_screen.py:79-94 render — SCREEN/SCREEN2 only, no extra
        // header/footer/banner lines (removed 2026-09-04).

// via GameUtils.WrapTruecolor
        if (!isScreenReader)
        {
            try { full = GameUtils.WrapTruecolor(full); } catch { }
        }
        else
        {
            try { full = GameUtils.StripAnsi(full); } catch { }
        }
        return full;
    }
}
