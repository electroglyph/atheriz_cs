// Port of atheriz/commands/unloggedin/screenreader.py:19 — also used loggedin via loggedin/cmdset.py import (faithful reuse)

namespace Atheriz.Core.Commands.UnloggedIn;

public sealed class ScreenReaderCommand : Command
{
    public override string Key => "screenreader";
    public override IReadOnlyList<string> Aliases => ["sr"];
    public override string Desc => "Toggle screenreader mode.";
    public override string Category => "Communication";
    public override bool UseParser => false;
    public override void Run(IMessageTarget caller, object? args)
    {
        // Typed only (F001): GameObject/Session/BaseConnection all expose Session
        // via ISessionProvider, so this branch handles every caller that has a
        // session; exotic doubles without the interface get no session.
        Session? sess = (caller as ISessionProvider)?.Session;
        if (sess is not null)
        {
            sess.ScreenReader = !sess.ScreenReader;
            try { sess.Connection?.SendCommand("screenreader", sess.ScreenReader); } catch (Exception) { }
            // Telnet/screen-reader callers get no other feedback: confirm the
            // new state on the message path, not just the control channel.
            try { caller.Msg($"Screenreader mode {(sess.ScreenReader ? "on" : "off")}."); } catch (Exception) { }
            return;
        }
    }
}
