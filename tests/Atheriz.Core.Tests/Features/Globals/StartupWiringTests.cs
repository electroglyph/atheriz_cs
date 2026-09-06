using Atheriz.Core;
using Atheriz.Core.Globals;
using Atheriz.Core.Objects;
using Atheriz.Core.Plugins;
using Atheriz.Core.Settings;
using Atheriz.Core.Utils;

namespace Atheriz.Core.Tests.Features.Globals;

// Behavior pins for world startup wiring: initial setup creates the
// dashboard, button, channel, and admin objects; log rotation shifts
// generations; plugin load/unload roundtrips; settings defaults stay sane.
[Collection("Ported")]
public class StartupWiringTests
{
    [Fact]
    public void InitialSetup_Wires_DashboardButtonChannelAdmin()
    {
        // Initial setup creates the dashboard, button, channel, and admin objects.
        var tmp = Path.Combine(Path.GetTempPath(), "atheriz_initpin_" + Guid.NewGuid().ToString("N"));
        var save = Path.Combine(tmp, "save");
        var secret = Path.Combine(tmp, "secret");
        try
        {
            InitialSetup.DoSetup(save, "admin", "password123", secret);
            var dash = ObjectRegistry.FilterBy(o => o.Name == "A flashing dashboard");
            Assert.Single(dash);
            Assert.True(dash[0].IsItem);
            var button = ObjectRegistry.FilterBy(o => o.Name == "A big red button");
            Assert.Single(button);
            Assert.NotNull(button[0].ExternalCmdSet!.Get("push"));
            var chan = ObjectRegistry.FilterBy(o => o.IsChannel && o.Name == "Server");
            Assert.Single(chan);
            var admin = ObjectRegistry.FilterBy(o => o.IsAccount && o.Name == "admin");
            Assert.Single(admin);
            var hero = ObjectRegistry.FilterBy(o => o.IsPc && o.Name == "admin");
            Assert.Single(hero);
            Assert.Contains(chan[0].Id, hero[0].ChannelsSnapshot);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
            GlobalTestEnv.Enter().Dispose();
        }
    }

    [Fact]
    public void Logger_Rotate_ShiftsGenerations()
    {
        // 5-file rotation: r.log -> r.log.1 -> r.log.2, oldest dropped.
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_logrot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "r.log"), "A");
            File.WriteAllText(Path.Combine(dir, "r.log.1"), "B");
            AtherizLogger.Rotate(Path.Combine(dir, "r.log"));
            Assert.Equal("A", File.ReadAllText(Path.Combine(dir, "r.log.1")));
            Assert.Equal("B", File.ReadAllText(Path.Combine(dir, "r.log.2")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void PluginLoader_LoadUnload_Roundtrip()
    {
        // Plugin load success plus unload roundtrip: loader reports loaded then unloaded.
        var src = typeof(StartupWiringTests).Assembly.Location;
        var copy = Path.Combine(Path.GetTempPath(), "atheriz_plugin_" + Guid.NewGuid().ToString("N") + ".dll");
        File.Copy(src, copy);
        try
        {
            using var loader = new PluginLoader();
            loader.Load(copy);
            Assert.True(loader.IsLoaded);
            loader.Unload();
            Assert.False(loader.IsLoaded);
            Assert.Empty(loader.Replacements);
        }
        finally { try { File.Delete(copy); } catch { } }
    }

    [Fact]
    public void AtherizSettings_SpotDefaults_Sane()
    {
        var s = new AtherizSettings();
        Assert.NotEmpty(s.NetworkProtocols);
        Assert.NotEmpty(s.AllSymbols);
        Assert.False(string.IsNullOrEmpty(s.NsClosedDoor));
    }
}
