// Port of atheriz/commands/loggedin/spam.py:78
using System.IO;

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
        if (!CommandHelpers.RequirePuppet(caller, out var go)) return;
        var pa = args as GameArgumentParser.ParsedArgs;
        if (pa is null) { go.Msg("Usage: spam <count>"); return; }
        // No floor: count 0/negative runs an empty loop with the count
        // messages below (established behavior, not a refusal).
        _ = CommandHelpers.TryGetCount(pa, "count", 0, null, out int count);
        if (count > 1000) { go.Msg("Maximum count is 1000."); return; }
        go.Msg($"Creating {count} accounts and characters...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var settings = AtherizSettings.Global;
        var home = go.ResolveLocationObject();
        // Hoisted once: per-index names cannot self-collide within one run,
        // so the old per-iteration FilterBy saw the same answer every time.
        var existingNames = new HashSet<string>(
            ObjectRegistry.FilterBy(o => o.IsAccount).Select(o => o.Name),
            StringComparer.OrdinalIgnoreCase);
        List<(string a, string c)> created = [];
        for (int idx = 1; idx <= count; idx++)
        {
            string an = $"account{idx}";
            string pw = $"password{idx}";
            string cn = $"char{idx}";
            try
            {
                if (existingNames.Contains(an)) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                var account = Account.Create(an, pw);
                if (account is null) { go.Msg($"Account '{an}' already exists, skipping..."); continue; }
                var character = GameObject.Create(cn, "", isPc: true, isMapable: true);
                character.Symbol = "A";
                character.Home = Persistence.Dto.LocationRef.FromCoord(settings.DefaultHome);
                if (home is Node node) character.MoveTo(node);
                else if (home is not null) character.MoveTo(home);
                account.AddCharacter(character);
                ObjectRegistry.AddObject(character);
                created.Add((an, cn));
            }
            catch (InvalidOperationException) { go.Msg($"Account '{an}' already exists, skipping..."); }
            catch (Exception ex) { go.Msg($"Failed {an}: {ex.Message}"); }
        }
        // single save after the loop, not O(n) saves inside it.
        // Best-effort: spam's job is creating the accounts in-registry; a
        // bad save path reports instead of discarding the created accounts.
        try { ObjectRegistry.SaveObjects(settings.SavePath); }
        catch (Exception ex) { go.Msg($"Save failed: {ex.Message}"); }
        var credsFile = Path.Combine(settings.SavePath, "spam_accounts.txt");
        try
        {
            Directory.CreateDirectory(settings.SavePath);
            using var f = new StreamWriter(credsFile, false, System.Text.Encoding.UTF8);
            f.NewLine = "\n";
            // passwords are never persisted — account/character
            // names only (Python's plaintext password column removed).
            f.Write("# Account Name | Character Name\n");
            foreach (var (a, c) in created)
                f.Write($"{a}|{c}\n");
        }
        catch (Exception) { }
        sw.Stop();
        go.Msg($"Created {created.Count} accounts/chars in {sw.Elapsed.TotalMilliseconds} milliseconds. Names saved to {credsFile}");
    }
}