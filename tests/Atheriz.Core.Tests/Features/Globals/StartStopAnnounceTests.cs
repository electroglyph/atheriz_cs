using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;

namespace Atheriz.Core.Tests.Features.Globals;

// Shutdown/reload announce + game-time stop share helpers: byte-identical
// messages in order, and the same ticker-resolution stop shape on both paths.
[Collection("Ported")]
public sealed class StartStopAnnounceTests
{
    private sealed class RecordingServerChannel : Channel
    {
        public readonly List<string> Seen = new();
        public override void Msg(string text) => Seen.Add(text);
    }

    private static RecordingServerChannel InstallServerChannel()
    {
        GlobalServices.Reset();
        ObjectRegistry.ClearAll();
        var chan = new RecordingServerChannel
        {
            Name = "server",
            Id = IdGenerator.GetUniqueId(),
        };
        ObjectRegistry.AddObject(chan);
        return chan;
    }

    private static bool HasOnTick(Atheriz.Core.Concurrency.AsyncTicker ticker) =>
        ticker.Slots.Values.Any(s => s.Coros.Any(d => d.Method.Name.Contains("OnTick")));

    [Fact]
    public void DoShutdown_WithServerChannel_AnnouncesShuttingDown()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.Reset();
        try
        {
            var chan = InstallServerChannel();
            var settings = new AtherizSettings
            {
                SavePath = env.TempPath,
                TimeSystemEnabled = false,
                AutosaveMinutes = 0,
                AutosaveOnShutdown = false,
            };
            StartStop.DoShutdown(settings);
            Assert.Equal(new[] { "Server is shutting down!" }, chan.Seen);
        }
        finally { StartStop.Reset(); }
    }

    [Fact]
    public void DoReload_WithServerChannel_AnnouncesReloadSequenceInOrder()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.Reset();
        try
        {
            var chan = InstallServerChannel();
            var ticker = GlobalServices.GetAsyncTicker();
            ticker.Clear();
            var settings = new AtherizSettings
            {
                SavePath = env.TempPath,
                TimeSystemEnabled = false,
                AutosaveMinutes = 0,
                AutosaveOnReload = false,
            };
            StartStop.DoReload(settings, ticker);
            Assert.Equal(new[] { "Server is reloading...", "Server reloaded" }, chan.Seen);
        }
        finally
        {
            StartStop.Reset();
            try { GlobalServices.TryGetTicker()?.Clear(); } catch { }
        }
    }

    [Fact]
    public void DoShutdown_WithStartedGameTime_StopsTickerRegistration()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.Reset();
        try
        {
            var ticker = GlobalServices.GetAsyncTicker();
            ticker.Clear();
            var gt = GlobalServices.GetGameTime();
            gt.Start(ticker);
            Assert.True(gt.Started);
            Assert.True(HasOnTick(ticker));
            var settings = new AtherizSettings
            {
                SavePath = env.TempPath,
                TimeSystemEnabled = true,
                AutosaveMinutes = 0,
                AutosaveOnShutdown = false,
            };
            StartStop.DoShutdown(settings, ticker: ticker);
            Assert.False(HasOnTick(ticker));
        }
        finally { StartStop.Reset(); }
    }

    [Fact]
    public void DoReload_WithStartedGameTime_RestartsAfterStop()
    {
        using var env = GlobalTestEnv.Enter();
        StartStop.Reset();
        try
        {
            var ticker = GlobalServices.GetAsyncTicker();
            ticker.Clear();
            var gt = GlobalServices.GetGameTime();
            gt.Start(ticker);
            Assert.True(HasOnTick(ticker));
            var settings = new AtherizSettings
            {
                SavePath = env.TempPath,
                TimeSystemEnabled = true,
                AutosaveMinutes = 0,
                AutosaveOnReload = false,
            };
            StartStop.DoReload(settings, ticker);
            var live = GlobalServices.TryGetGameTime();
            Assert.NotNull(live);
            Assert.True(live!.Started);
            Assert.True(HasOnTick(ticker));
        }
        finally
        {
            try { GlobalServices.TryGetGameTime()?.Stop(); } catch { }
            try { GlobalServices.TryGetTicker()?.Clear(); } catch { }
            StartStop.Reset();
        }
    }
}
