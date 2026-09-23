using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Objects;

// Login re-verifies under the write lock when the account moves
// mid-verify, so a rename landing during the PBKDF2 work cannot
// authenticate the stale name.
[Collection("Ported")]
public sealed class AccountLoginReverifyTests
{
    [Fact]
    public void Login_RenameDuringVerification_DoesNotAuthenticateOldName()
    {
        // Rename on another thread while Login is inside PBKDF2: the
        // re-verify must see the new name and refuse the old one.
        using var env = GlobalTestEnv.Enter();
        for (int i = 0; i < 3; i++)
        {
            string name = $"reverifyu{i}";
            var acc = Account.Create(name, "reverifypass");
            if (ObjectRegistry.Get(acc.Id).Count == 0)
            {
                try { ObjectRegistry.AddObject(acc); } catch { }
            }
            Assert.True(acc.Login(name, "reverifypass"));
            // Real Thread: the rename must land mid-PBKDF2. A pooled wait may
            // inline the login sequentially ahead of the rename, which would
            // fail loud (true instead of false) on pool timing luck.
            bool? second = null;
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try { second = acc.Login(name, "reverifypass"); }
                finally { done.Set(); }
            })
            { IsBackground = true };
            thread.Start();
            acc.Name = name + "_renamed";
            Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Assert.False(second ?? true);
            Assert.False(acc.LoggedIn);
        }
    }
}
