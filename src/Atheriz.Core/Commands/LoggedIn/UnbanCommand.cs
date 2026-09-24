namespace Atheriz.Core.Commands.LoggedIn;

public sealed class UnbanCommand : Command
{
    public override string Key => "unban";
    public override string Desc => "Unban a player character, optionally their account and/or IP.";
    public override string Category => "Building";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsBuilder(caller);
    protected override void SetupParser(GameArgumentParser p)
    {
        p.AddArgument(ParsedArgKeys.Target).Help("Player character to unban (name or #id).");
        p.AddArgument("--account").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Unban the entire account and all its characters.");
        p.AddArgument("--ip").Action(GameArgumentParser.ArgAction.StoreTrue).Help("Also clear an IP ban for the target's host (requires an online target).");
    }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        if (!this.RequireParsedArgs(caller, args, out var pa)) return;
        var targetName = pa.GetString(ParsedArgKeys.Target);
        if (string.IsNullOrWhiteSpace(targetName)) { caller.Msg(PrintHelp()); return; }
        bool wantAccount = pa.GetBool(ParsedArgKeys.Account);
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
