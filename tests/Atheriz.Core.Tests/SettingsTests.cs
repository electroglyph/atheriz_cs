using Atheriz.Core.Settings;

namespace Atheriz.Core.Tests;

public class SettingsTests
{
    [Fact]
    public void Defaults_Mirror_Python()
    {
        var s = new AtherizSettings();
        Assert.Equal("save", s.SavePath);
        Assert.Equal("secret", s.SecretPath);
        Assert.Equal("AtheriZ", s.ServerName);
        Assert.Equal(4444, s.TelnetPort);
        Assert.Equal(9999, s.WebserverPort);
        Assert.Equal(5, s.MaxCharacters);
        Assert.Equal(20, s.FuncparserMaxNesting);
        Assert.Equal(100, s.MaxSearchDepth);
        Assert.Equal(50, s.ChannelHistoryLimit);
        Assert.Equal("limbo", s.DefaultHome.Area);
    }

    [Fact]
    public void Derived_SecondsPerDay()
    {
        var s = new AtherizSettings();
        Assert.Equal(86400, s.SecondsPerDay);
        Assert.Equal(3600, s.SecondsPerHour);
        Assert.Equal(360, s.DaysPerYear);
    }
}
