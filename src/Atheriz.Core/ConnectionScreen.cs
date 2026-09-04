// Port of atheriz/connection_screen.py:1-95
using System.Reflection;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core;

// Port of atheriz/connection_screen.py:11-95 faithful welcome screen
public static class ConnectionScreen
{
    // Port of connection_screen.py:11 _guest_text
    private static string GuestText(AtherizSettings? s = null) => (s ?? AtherizSettings.Global).GuestEnabled ? "enter 'guest' to create a temporary character" : "";
    // Port of connection_screen.py:15 _create_text
    private static string CreateText(AtherizSettings? s = null) => (s ?? AtherizSettings.Global).AccountCreationEnabled ? "enter 'create' to make a new account" : "";

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
    private static readonly object _lock = new();
    private static double _cacheTs;
    private static int _cacheOnline;
    private static int _cacheKnown;

    // Port of connection_screen.py:58 get_online
    public static (int online, int known) GetOnline()
    {
        var now = global::Atheriz.Core.Utils.TimeProvider.MonotonicSeconds(); // Port of time.monotonic — now via TimeProvider
        lock (_lock)
        {
            if (now - _cacheTs < 5) return (_cacheOnline, _cacheKnown);
        }
        // Port of connection_screen.py:64 filter_by lambda x.is_pc
        var results = ObjectRegistry.FilterBy(o => o.IsPc);
        var online = results.Count(o => o.IsConnected);
        var known = results.Count;
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
    public static string Render(AtherizSettings settings, Session? session = null)
    {
        settings ??= AtherizSettings.Global;
        var (online, known) = GetOnline(); // Port of connection_screen.py:80
        var version = GetVersion(); // Port of connection_screen.py:72
        var createText = CreateText(settings); // Port of connection_screen.py:86
        var guestText = GuestText(settings);

        // Build main screen with ANSI truecolor if not screenreader — Port of connection_screen.py:81-94
        bool isScreenReader = session != null && session.ScreenReader; // Port of connection_screen.py:81 session.screenreader
        string raw;
        if (isScreenReader)
        {
            // Port of connection_screen.py:82-88 SCREEN2
            raw = string.Format(Screen2, version, known, online, createText, guestText);
        }
        else
        {
            // Port of connection_screen.py:89-94 SCREEN
            raw = string.Format(Screen, version, known, online, createText, guestText);
        }

        // Byte-faithful to connection_screen.py:79-94 render — SCREEN/SCREEN2 only, no extra
        // header/footer/banner lines (removed 2026-09-04 per audit Appendix B Q10).
        var full = raw;

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
