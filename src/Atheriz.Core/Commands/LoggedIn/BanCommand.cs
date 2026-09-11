// Port of atheriz/commands/loggedin/ban.py:279

namespace Atheriz.Core.Commands.LoggedIn;

/// <summary>Port of atheriz/commands/loggedin/ban.py:BanCommand</summary>
public sealed class BanCommand : Command
{
    public override string Key => "ban";
    public override string Desc => "Ban a player character, optionally their account and/or IP.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target").Help("Player character to ban (name or #id).");
        p.AddArgument("-r", "--reason").Help("Reason for the ban.");
        p.AddArgument("--account").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Ban the entire account and all its characters.");
        p.AddArgument("--ip").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Also ban the target's IP (requires an online target).");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var targetName = pa.GetString("target");
        if (string.IsNullOrWhiteSpace(targetName)) { caller.Msg(PrintHelp()); return; }
        var reason = pa.GetString("reason");
        bool wantAccount = pa.GetBool("account");
        bool ip = pa.GetBool("ip");
        if (!BanHelper.TryResolveBanPreamble(go, targetName, wantAccount, ip, "ban", "banning",
            out var target, out var acct, out var acctChars, out var host, out bool account)
            || target is null)
            return;
        List<GameObject> kickTargets;
        if (account && acct is not null)
        {
            kickTargets = acct is Account ? acctChars : [target];
            if (acct is Account ac) { ac.IsBanned = true; if (!string.IsNullOrEmpty(reason)) ac.BanReason = reason; }
            foreach (var c in kickTargets) { c.IsBanned = true; if (!string.IsNullOrEmpty(reason)) BanReasonHelper.SetBanReason(c, reason); }
        }
        else
        {
            kickTargets = [target];
            target.IsBanned = true;
            if (!string.IsNullOrEmpty(reason)) BanReasonHelper.SetBanReason(target, reason);
        }
        string? kickedIp = null;
        if (ip)
        {
            if (host is null) go.Msg("Target is not online; cannot ban IP.");
            else { ObjectRegistry.BanIp(host); kickedIp = host; }
        }
        List<string> failed = [];
        foreach (var t in kickTargets)
        {
            try
            {
                var sess = t.Session;
                var conn = sess?.Connection;
                if (conn is not null)
                {
                    string msg = "You have been banned." + (string.IsNullOrEmpty(reason) ? "" : $" Reason: {reason}");
                    conn.Msg(msg);
                    conn.Close();
                }
            }
            catch { failed.Add(t.Name); }
        }
        string scope = account && acct is not null ? "account" : "character";
        string outMsg = $"Banned {target.Name} ({scope}" + (string.IsNullOrEmpty(reason) ? "" : $", reason: {reason}") + ").";
        if (kickedIp is not null) outMsg += $" IP {kickedIp} banned until server restart.";
        if (failed.Count > 0) outMsg += $" Kick failed for: {string.Join(", ", failed)}.";
        go.Msg(outMsg);
    }
}

/// <summary>Port of atheriz/commands/loggedin/ban.py:UnbanCommand</summary>
public sealed class UnbanCommand : Command
{
    public override string Key => "unban";
    public override string Desc => "Unban a player character, optionally their account and/or IP.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument("target").Help("Player character to unban (name or #id).");
        p.AddArgument("--account").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Unban the entire account and all its characters.");
        p.AddArgument("--ip").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Also clear an IP ban for the target's host (requires an online target).");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var targetName = pa.GetString("target");
        if (string.IsNullOrWhiteSpace(targetName)) { caller.Msg(PrintHelp()); return; }
        bool wantAccount = pa.GetBool("account");
        bool ip = pa.GetBool("ip");
        if (!BanHelper.TryResolveBanPreamble(go, targetName, wantAccount, ip, "unban", "unbanning",
            out var target, out var acct, out var acctChars, out var host, out bool account)
            || target is null)
            return;
        if (account && acct is not null)
        {
            if (acct is Account ac) { ac.IsBanned = false; ac.BanReason = ""; }
            foreach (var c in acctChars) { c.IsBanned = false; BanReasonHelper.ClearBanReason(c); }
        }
        else { target.IsBanned = false; BanReasonHelper.ClearBanReason(target); }
        if (ip)
        {
            if (host is null) go.Msg("Target is not online; cannot clear IP ban by reference.");
            else ObjectRegistry.UnbanIp(host);
        }
        string scope = account && acct is not null ? "account" : "character";
        go.Msg($"Unbanned {target.Name} ({scope}).");
    }
}

internal static class BanReasonHelper
{
    internal static void SetBanReason(GameObject c, string reason)
    {
        c.BanReason = reason;
    }
    internal static void ClearBanReason(GameObject c)
    {
        c.BanReason = "";
        c.TryRemoveExtraJson("ban_reason");
    }
}
