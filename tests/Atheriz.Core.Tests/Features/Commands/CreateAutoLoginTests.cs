using Atheriz.Core.Commands.UnloggedIn;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using System.Diagnostics;

namespace Atheriz.Core.Tests.Features.Commands;

// Sync `create` must leave the caller logged in: account bound, `logged_in`
// sent, and the character-selection wizard started, so a fresh account
// reaches its first character without typing `connect` first. Mirrors the
// sync `connect` tail; async `create` already awaited the same wizard.
[Collection("Ported")]
public sealed class CreateAutoLoginTests
{
    private static string SentText(TestConnection conn)
        => string.Join("\n", conn.Sent.SelectMany(s => s.Args).OfType<string>());

    private static async Task<TaskCompletionSource<string>> WaitForPromptAsync(TestConnection conn, TaskCompletionSource<string>? previous)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            var future = conn.Session!.InputFuture;
            if (future is not null && !ReferenceEquals(future, previous)) return future;
            await Task.Delay(10);
        }
        throw new TimeoutException("character selection prompt did not appear");
    }

    private static async Task<GameObject> WaitForPuppetAsync(TestConnection conn)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(5))
        {
            GameObject? puppet;
            lock (conn.Session!.Lock) puppet = conn.Session.Puppet;
            if (puppet is not null) return puppet;
            await Task.Delay(10);
        }
        throw new TimeoutException("character was not puppeted");
    }

    [Fact]
    public async Task Create_SyncRun_BindsAccountAndStartsSelectionWizard()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        try
        {
            using var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "autologin hunter22");

            Assert.NotNull(conn.Session.Account);
            Assert.Equal("autologin", conn.Session.Account!.Name);
            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.Contains("Account autologin created.", SentText(conn));

            var prompt = await WaitForPromptAsync(conn, null);
            Assert.Contains("select a character", SentText(conn).ToLowerInvariant());

            // Tidy end: puppet the session so the wizard loop exits on its own.
            var hero = GameObject.Create("autologin_cleanup", isPc: true);
            ObjectRegistry.AddObject(hero);
            Assert.True(SessionPuppetHelper.TryAttach(conn, hero));
            conn.Session.CancelPrompt(prompt);
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public async Task Create_SyncRun_FullFlow_PuppetsFirstCharacter()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        try
        {
            using var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "fullflow hunter22");

            var select = await WaitForPromptAsync(conn, null);
            select.TrySetResult("new");
            var namePrompt = await WaitForPromptAsync(conn, select);
            namePrompt.TrySetResult("AutoHero");
            var genderPrompt = await WaitForPromptAsync(conn, namePrompt);
            genderPrompt.TrySetResult("M");
            var descPrompt = await WaitForPromptAsync(conn, genderPrompt);
            descPrompt.TrySetResult("A fresh hero");

            var puppet = await WaitForPuppetAsync(conn);
            Assert.Equal("AutoHero", puppet.Name);
            var account = Assert.IsType<Account>(conn.Session.Account);
            Assert.Contains(account.Characters, id => id == puppet.Id);
            Assert.Contains("Character AutoHero created and puppeted.", SentText(conn));
        }
        finally { SaltProvider.Clear(); }
    }

    [Fact]
    public void Create_SyncRun_CharCreationDisabled_NoWizard()
    {
        using var env = GlobalTestEnv.Enter();
        SaltProvider.SetSalt("testsalt");
        var prev = AtherizSettings.Global.CharCreationEnabled;
        AtherizSettings.Global.CharCreationEnabled = false;
        try
        {
            using var conn = new TestConnection();
            new CreateAccountCommand().Run(conn, "nologinwiz hunter22");
            Assert.NotNull(conn.Session.Account);
            Assert.Contains(conn.Sent, s => s.Cmd == "logged_in");
            Assert.Null(conn.Session.InputFuture);
        }
        finally
        {
            AtherizSettings.Global.CharCreationEnabled = prev;
            SaltProvider.Clear();
        }
    }
}
