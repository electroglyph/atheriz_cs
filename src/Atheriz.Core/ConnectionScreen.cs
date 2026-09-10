// Port of atheriz/connection_screen.py:1-95
using System.Reflection;

namespace Atheriz.Core;

// Port of atheriz/connection_screen.py:11-95 faithful welcome screen
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
    // Port of connection_screen.py:11 _guest_text
    // hints must agree with the dispatch gate, not just the display
    // settings — the gate also requires the dispatcher snapshot.
    private static string GuestText(AtherizSettings? s = null)
        => HintText((s ?? AtherizSettings.Global).GuestEnabled, GuestProbe, "enter 'guest' to create a temporary character");
    // Port of connection_screen.py:15 _create_text
    private static string CreateText(AtherizSettings? s = null)
        => HintText((s ?? AtherizSettings.Global).AccountCreationEnabled, CreateProbe, "enter 'create' to make a new account");

    // Port of connection_screen.py:22 SCREEN
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

    // Port of connection_screen.py:41 SCREEN2 (screenreader)
    private const string Screen2 = """
                     
             ATHERIZ VERSION = {0}
           KNOWN ADVENTURERS = {1}
          ONLINE ADVENTURERS = {2}

    enter 'sr' for screenreader mode
    enter 'connect <account> <password>' to login
    {3}
    {4}
    """;

    // Port of connection_screen.py:54-56 _CACHE + _LOCK, 5 sec TTL
    private static readonly Lock _lock = new();
    private static double _cacheTs;
    private static int _cacheOnline;
    private static int _cacheKnown;

    // Port of connection_screen.py:58 get_online
    // Static predicate (no per-call closure alloc); the 5 s render cache above
    // is untouched.
    private static readonly Func<Objects.GameObject, bool> IsPcFilter = static o => o.IsPc;
    public static (int online, int known) GetOnline()
    {
        var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds(); // Port of time.monotonic — now via TimeProvider
        lock (_lock)
        {
            if (now - _cacheTs < 5) return (_cacheOnline, _cacheKnown);
        }
        // Port of connection_screen.py:64 filter_by lambda x.is_pc
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

    // Port of connection_screen.py:72 _version
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

    // Port of connection_screen.py:79 render
    public static string Render(Session? session = null) => Render(AtherizSettings.Global, session);
    public static string Render(AtherizSettings? settings, Session? session = null)
    {
        settings ??= AtherizSettings.Global;
        var (online, known) = GetOnline(); // Port of connection_screen.py:80
        var version = GetVersion(); // Port of connection_screen.py:72
        var createText = CreateText(settings); // Port of connection_screen.py:86
        var guestText = GuestText(settings);

        // Build main screen with ANSI truecolor if not screenreader — Port of connection_screen.py:81-94
        bool isScreenReader = session is not null && session.ScreenReader; // Port of connection_screen.py:81 session.screenreader
        string full;
        if (isScreenReader)
        {
            // Port of connection_screen.py:82-88 SCREEN2
            full = string.Format(Screen2, version, known, online, createText, guestText);
        }
        else
        {
            // Port of connection_screen.py:89-94 SCREEN
            full = string.Format(Screen, version, known, online, createText, guestText);
        }

        // Byte-faithful to connection_screen.py:79-94 render — SCREEN/SCREEN2 only, no extra
        // header/footer/banner lines (removed 2026-09-04).

        // Port of utils.wrap_truecolor for non-screenreader — via GameUtils.WrapTruecolor
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
