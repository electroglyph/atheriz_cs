// Port of atheriz/commands/loggedin/ban.py:279
using System.Text.Json;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;

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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null || pa.GetString("target") is not string targetName || string.IsNullOrWhiteSpace(targetName))
        { caller.Msg(PrintHelp()); return; }
        var reason = pa.GetString("reason");
        bool account = pa.GetBool("account");
        bool ip = pa.GetBool("ip");
        var target = BanHelper.ResolveTarget(go, targetName);
        if (target == null) return;
        if (target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot ban someone of equal or higher privilege."); return; }
        GameObject? acct = null;
        List<GameObject> acctChars = [];
        if (account)
        {
            acct = BanHelper.FindAccount(target);
            if (acct == null) { go.Msg($"Could not find the account owning {target.Name}; banning character only."); account = false; }
            else
            {
                var accObj = acct as Account;
                if (accObj != null)
                    acctChars = ObjectRegistry.Get(accObj.Characters.ToList());
                foreach (var ch in acctChars)
                    if (ch.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot ban someone of equal or higher privilege."); return; }
            }
        }
        string? host = null;
        if (ip) host = BanHelper.GetHost(target);
        List<GameObject> kickTargets;
        if (account && acct != null)
        {
            var acc2 = acct as Account;
            kickTargets = acc2 != null ? ObjectRegistry.Get(acc2.Characters.ToList()) : [target];
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
            if (host == null) go.Msg("Target is not online; cannot ban IP.");
            else { ObjectRegistry.BanIp(host); kickedIp = host; }
        }
        var failed = new List<string>();
        foreach (var t in kickTargets)
        {
            try
            {
                var sess = t.Session;
                var conn = sess?.Connection;
                if (conn != null)
                {
                    string msg = "You have been banned." + (string.IsNullOrEmpty(reason) ? "" : $" Reason: {reason}");
                    conn.Msg(msg);
                    conn.Close();
                }
            }
            catch { failed.Add(t.Name); }
        }
        string scope = account && acct != null ? "account" : "character";
        string outMsg = $"Banned {target.Name} ({scope}" + (string.IsNullOrEmpty(reason) ? "" : $", reason: {reason}") + ").";
        if (kickedIp != null) outMsg += $" IP {kickedIp} banned until server restart.";
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
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null || pa.GetString("target") is not string targetName || string.IsNullOrWhiteSpace(targetName))
        { caller.Msg(PrintHelp()); return; }
        bool account = pa.GetBool("account");
        bool ip = pa.GetBool("ip");
        var target = BanHelper.ResolveTarget(go, targetName);
        if (target == null) return;
        if (target.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot unban someone of equal or higher privilege."); return; }
        GameObject? acct = null;
        if (account)
        {
            acct = BanHelper.FindAccount(target);
            if (acct == null) { go.Msg($"Could not find the account owning {target.Name}; unbanning character only."); account = false; }
            else
            {
                var accChars = (acct as Account) != null ? ObjectRegistry.Get(((Account)acct).Characters.ToList()) : [];
                foreach (var ch in accChars) if (ch.PrivilegeLevel >= go.PrivilegeLevel) { go.Msg("You cannot unban someone of equal or higher privilege."); return; }
            }
        }
        string? host = null;
        if (ip) host = BanHelper.GetHost(target);
        if (account && acct != null)
        {
            if (acct is Account ac) { ac.IsBanned = false; ac.BanReason = ""; }
            var chars = (acct as Account) != null ? ObjectRegistry.Get(((Account)acct).Characters.ToList()) : [];
            foreach (var c in chars) { c.IsBanned = false; BanReasonHelper.ClearBanReason(c); }
        }
        else { target.IsBanned = false; BanReasonHelper.ClearBanReason(target); }
        if (ip)
        {
            if (host == null) go.Msg("Target is not online; cannot clear IP ban by reference.");
            else ObjectRegistry.UnbanIp(host);
        }
        string scope = account && acct != null ? "account" : "character";
        go.Msg($"Unbanned {target.Name} ({scope}).");
    }
    private static void ClearBanReason(GameObject c)
    {
        // Typed (F001): ban reason lives on the BanReason property (Account field-backed,
        // plain objects extra-backed); both legacy extra spellings are dropped too.
        c.BanReason = "";
        c.TryRemoveExtraJson("ban_reason");
        c.TryRemoveExtraJson("banReason");
    }
    private static void SetBanReason(GameObject c, string reason)
    {
        c.BanReason = reason;
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
