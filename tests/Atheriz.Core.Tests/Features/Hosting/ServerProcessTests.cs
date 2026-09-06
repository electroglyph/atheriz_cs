using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using Atheriz.Core.Network;
using Atheriz.Core.Settings;
using Atheriz.Core.Tests;
using Atheriz.Core.Tests.Ported;
using Atheriz.Server.Cli;
using Atheriz.Server.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Atheriz.Server.Infrastructure;

namespace Atheriz.Core.Tests.Features.Hosting;

// Server process and template tooling: process termination, port detection, game scaffolding.
[Collection("Ported")]
public class ServerProcessTests
{
    // --- CLI/host ---

    [Fact]
    public async Task KillProcess_DoesTerminate()
    {
        // Behavior: KillProcessWithDots must actually terminate the process
        // instead of waiting ~5 s and returning while it is still alive.
        using var p = Process.Start("sleep", "30");
        Assert.NotNull(p);
        try
        {
            await ProcessHelper.KillProcessWithDots(p).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(p.HasExited, "KillProcessWithDots must terminate the process");
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(2000); } catch { }
        }
    }

    [Fact]
    public void PidFile_DetectsLoopbackListener()
    {
        // Pin: port-listen detection works both ways.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            Assert.True(PidFile.TryFindPidListeningOnPort(port, out int pid));
            Assert.Equal(Environment.ProcessId, pid);
        }
        finally { listener.Stop(); }
        Assert.False(PidFile.TryFindPidListeningOnPort(port, out _));
    }

    [Fact]
    public void GameTemplate_InvalidName_SaysCSharp_NotPython()
    {
        // The C# generator must reject invalid names naming
        // the actual language, not "a valid Python identifier".
        var sw = new StringWriter();
        var orig = Console.Out;
        Console.SetOut(sw);
        try { GameTemplateGenerator.CreateGameFolder("my-game"); }
        finally { Console.SetOut(orig); }
        var text = sw.ToString();
        Assert.DoesNotContain("Python identifier", text);
    }

    // --- Prompted create on closed stdin ---

    [Fact]
    public void GameTemplate_PromptedCreate_CompletesOnClosedStdin()
    {
        // Pin: with stdin at EOF, the existing-folder prompt aborts instead of
        // hanging. (A real terminal with no input blocks in ReadLine — that
        // needs a pty and is environmental, not unit-testable.)
        var dir = Path.Combine(Path.GetTempPath(), "atheriz_tmplin_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var origIn = Console.In;
        var origOut = Console.Out;
        var sb = new StringWriter();
        Console.SetIn(new StringReader(""));
        Console.SetOut(sb);
        try
        {
            var task = Task.Run(() => GameTemplateGenerator.CreateGameFolder(dir));
            Assert.True(task.Wait(TimeSpan.FromSeconds(10)), "prompted create must not hang on closed stdin");
            Assert.Contains("Aborted", sb.ToString());
        }
        finally
        {
            Console.SetIn(origIn);
            Console.SetOut(origOut);
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
