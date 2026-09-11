// Spam password retention: the created-accounts list keeps only names, so a
// created account still checks its password while the credentials file never
// sees it.
using Atheriz.Core.Commands.LoggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Commands;

[Collection("Ported")]
public sealed class SpamPasswordRetentionTests
{
    [Fact]
    public void SpamCommand_RunOnce_CreatesPasswordCheckedAccountWithoutPersistingPassword()
    {
        using var env = GlobalTestEnv.Enter();
        var origSave = AtherizSettings.Global.SavePath;
        var tmp = Path.Combine(env.TempPath, "spampersist");
        Directory.CreateDirectory(tmp);
        AtherizSettings.Global.SavePath = tmp;
        try
        {
            var admin = GameObject.Create("spam_root", privilege: Privilege.Admin);
            ObjectRegistry.AddObject(admin);
            admin.ClearMessages();

            var pa = new SpamCommand().Parser!.ParseArgs(["1"]);
            new SpamCommand().Run(admin, pa);

            var accounts = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name.Equals("account1", StringComparison.OrdinalIgnoreCase));
            Assert.Single(accounts);
            var created = (Account)accounts[0];
            Assert.True(created.CheckPassword("password1"));
            var creds = File.ReadAllText(Path.Combine(tmp, "spam_accounts.txt"));
            Assert.Contains("account1", creds);
            Assert.DoesNotContain("password1", creds);
            Assert.Contains("Created 1 accounts/chars", string.Join("\n", admin.PeekMessages()));
        }
        finally { AtherizSettings.Global.SavePath = origSave; }
    }
}
