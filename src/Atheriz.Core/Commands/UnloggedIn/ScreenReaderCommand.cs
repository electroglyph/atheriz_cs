// Port of atheriz/commands/unloggedin/screenreader.py:19 — also used loggedin via loggedin/cmdset.py import (faithful reuse)
using Atheriz.Core.Network;
using Atheriz.Core.Objects;

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
        // via ISessionProvider; exotic doubles without the interface get no session.
        Session? sess = (caller as ISessionProvider)?.Session;
        if (sess != null)
        {
            sess.ScreenReader = !sess.ScreenReader;
            try { sess.Connection?.SendCommand("screenreader", sess.ScreenReader); } catch (Exception) { }
            return;
        }
        // fallback for GameObject
        if (caller is GameObject go && go.Session != null)
        {
            go.Session.ScreenReader = !go.Session.ScreenReader;
            try { go.Session.Connection?.SendCommand("screenreader", go.Session.ScreenReader); } catch (Exception) { }
        }
        else if (caller is BaseConnection bc && bc.Session != null)
        {
            bc.Session.ScreenReader = !bc.Session.ScreenReader;
            try { bc.Session.Connection?.SendCommand("screenreader", bc.Session.ScreenReader); } catch (Exception) { }
        }
    }
}
