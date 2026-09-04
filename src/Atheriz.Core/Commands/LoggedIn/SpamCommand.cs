// Port of atheriz/commands/loggedin/spam.py:78
using System.IO;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Commands;

namespace Atheriz.Core.Commands.LoggedIn;

public sealed class SpamCommand : Command
{
    public override string Key => "spam";
    public override string Desc => "Create multiple test accounts and characters.";
    public override bool Hide => true;
    public override string Category => "Admin";
    public override bool Access(IMessageTarget caller) => CommandPermissions.IsSuperUser(caller);
    protected override void SetupParser(GameArgumentParser p) { p.AddArgument("count", type: typeof(int), help: "Number of accounts to create"); }
    public override void Run(IMessageTarget caller, object? args)
    {
        if (caller is not GameObject go) { caller.Msg("You can't do that."); return; }
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa == null) { go.Msg("Usage: spam <count>"); return; }
        var countObj = pa["count"];
        int count = countObj is int i ? i : int.TryParse(countObj?.ToString(), out var parsed) ? parsed : 0;
        if (count > 1000) { go.Msg("Maximum count is 1000."); return; }
        go.Msg($"Creating {count} accounts and characters...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var settings = AtherizSettings.Default;
        var home = go.ResolveLocationObject();
        var created = new List<(string a, string p, string c)>();
        for (int idx = 1; idx <= count; idx++)
        {
            string an = $"account{idx}";
            string pw = $"password{idx}";
            string cn = $"char{idx}";
            try
            {
                var existing = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals(an, StringComparison.OrdinalIgnoreCase));
                if (existing.Count > 0) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                var account = Account.Create(an, pw);
                if (account == null) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                var character = GameObject.Create(cn, "", isPc: true, isMapable: true);
                character.Symbol = "A";
                character.Home = new Persistence.Dto.LocationRef.CoordLocation(settings.DefaultHome);
                if (home is Node node) character.MoveTo(node);
                else if (home != null) character.MoveTo(home);
                account.AddCharacter(character);
                ObjectRegistry.AddObject(account);
                ObjectRegistry.AddObject(character);
                ObjectRegistry.SaveObjects("save");
                created.Add((an, pw, cn));
            }
            catch (InvalidOperationException) { go.Msg($"Account '{an}' already exists, skipping..."); }
            catch (Exception ex) { go.Msg($"Failed {an}: {ex.Message}"); }
        }
        var credsFile = Path.Combine(settings.SavePath, "spam_accounts.txt");
        try
        {
            Directory.CreateDirectory(settings.SavePath);
            using var f = new StreamWriter(credsFile, false, System.Text.Encoding.UTF8);
            f.NewLine = "\n";
            f.Write("# Account Name | Password | Character Name\n");
            foreach (var (a, p, c) in created)
                f.Write($"{a}|{p}|{c}\n");
        }
        catch { }
        sw.Stop();
        go.Msg($"Created {created.Count} accounts/chars in {sw.Elapsed.TotalMilliseconds} milliseconds. Credentials saved to {credsFile}");
    }
}