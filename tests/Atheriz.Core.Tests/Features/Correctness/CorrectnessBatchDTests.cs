// Regression pins: setup-password handling, login hash rotation,
// repeat-alarm payload isolation, template-generator failure reporting.
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Atheriz.Core;
using Atheriz.Core.Concurrency;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Correctness;

[Collection("Ported")]
public class CorrectnessBatchDTests
{
    private const string FixedSalt = "testsalt";

    // The superuser password is verbatim — padded input must verify
    // as-typed, and the trimmed form must NOT verify.
    [Fact]
    public void DoSetup_PaddedPassword_VerbatimLogin()
    {
        using var env = GlobalTestEnv.Enter();
        var game = Path.Combine(Path.GetTempPath(), $"pwtrim_{Guid.NewGuid():N}");
        var orig = Directory.GetCurrentDirectory();
        try
        {
            Directory.CreateDirectory(game);
            var save = Path.Combine(game, "save");
            var secret = Path.Combine(game, "secret");
            InitialSetup.DoSetup(save, "pinadmin", " hunter22  ", secret);
            var account = ObjectRegistry.FilterBy(o => o.Name == "pinadmin").FirstOrDefault() as Account;
            Assert.NotNull(account);
            Assert.True(account!.CheckPassword(" hunter22  "));
            Assert.False(account.CheckPassword("hunter22"));
        }
        finally
        {
            try { Directory.SetCurrentDirectory(orig); } catch { }
            SaltProvider.Clear();
            try { Directory.Delete(game, true); } catch { }
        }
    }

    // A password rotation landing inside Login's PBKDF2 window must not
    // log in with the OLD password. The rotation is applied via reflection
    // (instant field write, no second PBKDF2) so it deterministically lands
    // inside the verify window; Login must still return false.
    [Fact]
    public void Login_RotationMidVerify_DoesNotLoginWithOldPassword()
    {
        using var env = GlobalTestEnv.Enter();
        ObjectRegistry.ClearAll();
        var acc = Account.Create("pinuser", "oldpassword1", FixedSalt);
        var newHash = Account.HashPassword("newpassword2", FixedSalt);
        var field = typeof(Account).GetField("_passwordHash", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            acc.SetPassword("oldpassword1", FixedSalt);
            // Real Thread: the rotation must land mid-PBKDF2. A pooled wait
            // may inline the login sequentially ahead of the rotation, which
            // would fail loud (true instead of false) on pool timing luck.
            bool? loginOk = null;
            using var done = new ManualResetEventSlim(false);
            var thread = new Thread(() =>
            {
                try { loginOk = acc.Login("pinuser", "oldpassword1", FixedSalt); }
                finally { done.Set(); }
            })
            { IsBackground = true };
            thread.Start();
            // Let Login snapshot and enter PBKDF2 (~100ms) before rotating:
            // the snapshot (microseconds) is done, the write-back is not.
            Thread.Sleep(15);
            field!.SetValue(acc, newHash);
            Assert.True(done.Wait(TimeSpan.FromSeconds(30)));
            Assert.True(thread.Join(TimeSpan.FromSeconds(30)));
            Assert.False(loginOk ?? true);
            Assert.False(acc.LoggedIn);
        }
        // The rotated password still logs in; the old one does not.
        Assert.True(acc.Login("pinuser", "newpassword2", FixedSalt));
        Assert.False(acc.Login("pinuser", "oldpassword1", FixedSalt));
    }

    private sealed class MutatingProbe : GameObject
    {
        private readonly object _gate = new();
        public int Fires;
        public bool SecondSawMutation;
        public override void AtAlarm(GameTime.GameTimeInfo time, Dictionary<string, JsonElement>? data)
        {
            lock (_gate)
            {
                Fires++;
                if (data is null) return;
                if (data.ContainsKey("injected")) SecondSawMutation = true;
                data["injected"] = JsonDocument.Parse("1").RootElement.Clone();
            }
        }
    }

    // Each repeat-alarm firing gets its own payload copy — firing N's
    // mutation must not be visible to firing N+1.
    [Fact]
    public void OnTick_RepeatAlarmFirings_GetIsolatedData()
    {
        using var env = GlobalTestEnv.Enter();
        var settings = new AtherizSettings { SavePath = env.TempPath };
        using var pool = new AsyncThreadPool(maxThreads: 4, queueLimit: 1000, reliefLimit: 0);
        var gt = new GameTime(settings, ticker: null, pool, autoLoad: false);
        gt.Ticks = 0;
        var probe = new MutatingProbe();
        probe.Name = "pinprobe";
        ObjectRegistry.AddObject(probe);
        using (var doc = JsonDocument.Parse("{\"seed\":1}"))
            gt.AddAlarm("?", "?", probe.Id, repeat: true,
                doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));

        gt.OnTick();
        for (int i = 0; i < 200 && Volatile.Read(ref probe.Fires) < 1; i++)
            Thread.Sleep(25);
        Assert.Equal(1, Volatile.Read(ref probe.Fires));

        gt.OnTick();
        for (int i = 0; i < 200 && Volatile.Read(ref probe.Fires) < 2; i++)
            Thread.Sleep(25);
        Assert.Equal(2, Volatile.Read(ref probe.Fires));
        Assert.False(probe.SecondSawMutation);
    }

    // A world-setup failure (here: invalid superuser password) must
    // report failure, not print the Success banner and return true.
    [Fact]
    public void CreateGameFolder_SetupFailure_ReturnsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "atheriz_pin_" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "pingame");
        var oldU = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME");
        var oldP = Environment.GetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", "pinadmin");
        Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", "x");
        try
        {
            Assert.False(GameTemplateGenerator.CreateGameFolder(target, "pingame", overwrite: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_USERNAME", oldU);
            Environment.SetEnvironmentVariable("ATHERIZ_SUPERUSER_PASSWORD", oldP);
            // Retry: a single swallowed delete leaks the whole ~400MB scaffold
            // into /tmp; repeated leaks fill the tmpfs until unrelated SQLite
            // tests fail with "disk is full".
            try
            {
                for (int attempt = 0; attempt < 3 && Directory.Exists(root); attempt++)
                    try { Directory.Delete(root, true); } catch { }
            }
            catch { }
        }
    }
}
