using System.CommandLine;

namespace Atheriz.Server.Cli;

// Root command tree for the atheriz CLI. Every command is typed options and
// positional arguments with a Task<int> action — no string[] matcher, no
// exit-code static. Program exits with the returned code.
public static class AtherizCli
{
    private static Option<int?> PortOption() => new("--port", "-p")
    {
        Description = $"Override the webserver port (default: {AtherizSettings.Default.WebserverPort})"
    };

    private static Option<int?> TelnetPortOption() => new("--telnet-port")
    {
        Description = "Override the telnet port (default also honors ATHERIZ_TELNET_PORT)"
    };

    private static Option<string?> HostOption() => new("--host")
    {
        Description = "Override the host interface to bind to"
    };

    private static Option<bool> ForegroundOption() => new("--foreground", "-f")
    {
        Description = "Run the server in the foreground"
    };

    // Telnet default honors the environment when the flag is absent.
    public static int? TelnetPortOrEnv(int? flag)
    {
        if (flag is not null) return flag;
        var env = Environment.GetEnvironmentVariable("ATHERIZ_TELNET_PORT")
            ?? Environment.GetEnvironmentVariable("Atheriz__TelnetPort");
        return int.TryParse(env, out var ep) ? ep : null;
    }

    // Split glued short ports (-p1234 / -p=1234) into canonical tokens before
    // parsing, so every handler sees one shape. This is the only
    // pre-parse rewrite; everything else is System.CommandLine syntax.
    public static string[] NormalizeArgs(string[] a)
    {
        if (a.Any(v => IsGluedShortPort(v)))
        {
            var split = new List<string>(a.Length + 1);
            foreach (var v in a)
            {
                if (IsGluedShortPort(v))
                {
                    split.Add("-p");
                    split.Add(v[2] == '=' ? v.Substring(3) : v.Substring(2));
                }
                else split.Add(v);
            }
            return [.. split];
        }
        return a;
    }

    public static bool IsGluedShortPort(string v)
        => v.Length > 2 && v[0] == '-' && v[1] == 'p' && (v[2] == '=' || char.IsDigit(v[2]));

    public static RootCommand Build()
    {
        var root = new RootCommand("AtheriZ - Text-based multiplayer game server");

        var start = new Command("start", "Start the AtheriZ server");
        var startPort = PortOption(); var startHost = HostOption(); var startTelnet = TelnetPortOption(); var startFg = ForegroundOption();
        start.Options.Add(startPort); start.Options.Add(startHost); start.Options.Add(startTelnet); start.Options.Add(startFg);
        start.SetAction((ParseResult pr) => pr.GetValue(startFg)
            ? Hosting.ServerHost.RunForegroundAsync(
                pr.GetValue(startPort), pr.GetValue(startHost), TelnetPortOrEnv(pr.GetValue(startTelnet)))
            : DaemonSpawner.SpawnStart(Directory.GetCurrentDirectory(),
                pr.GetValue(startPort), pr.GetValue(startHost), TelnetPortOrEnv(pr.GetValue(startTelnet))));

        var stop = new Command("stop", "Stop the AtheriZ server");
        var stopPort = PortOption();
        stop.Options.Add(stopPort);
        stop.SetAction((ParseResult pr) => StopHandler.StopAsync(pr.GetValue(stopPort)));

        var restart = new Command("restart", "Restart the AtheriZ server");
        var restartPort = PortOption(); var restartHost = HostOption(); var restartTelnet = TelnetPortOption(); var restartFg = ForegroundOption();
        restart.Options.Add(restartPort); restart.Options.Add(restartHost); restart.Options.Add(restartTelnet); restart.Options.Add(restartFg);
        restart.SetAction((ParseResult pr) => RestartHandler.RestartAsync(
            pr.GetValue(restartPort), pr.GetValue(restartHost), TelnetPortOrEnv(pr.GetValue(restartTelnet)), pr.GetValue(restartFg)));

        var reload = new Command("reload", "Hot reload game logic");
        var reloadPort = PortOption();
        reload.Options.Add(reloadPort);
        reload.SetAction((ParseResult pr) => ReloadHandler.ReloadAsync(pr.GetValue(reloadPort)));

        var reset = new Command("reset", "Delete all game data and start fresh (asks for confirmation)");
        var resetPort = PortOption(); var resetTelnet = TelnetPortOption(); var resetFg = ForegroundOption();
        reset.Options.Add(resetPort); reset.Options.Add(resetTelnet); reset.Options.Add(resetFg);
        reset.SetAction((ParseResult pr) => ResetHandler.ResetAsync(pr.GetValue(resetPort), TelnetPortOrEnv(pr.GetValue(resetTelnet)), pr.GetValue(resetFg)));

        var create = new Command("create", "Create a new account and character");
        var accArg = new Argument<string>("accountname") { Description = "Account name" };
        var charArg = new Argument<string>("charactername") { Description = "Character name" };
        var pwArg = new Argument<string>("password") { Description = "Password" };
        var createPort = PortOption();
        create.Arguments.Add(accArg); create.Arguments.Add(charArg); create.Arguments.Add(pwArg);
        create.Options.Add(createPort);
        create.SetAction((ParseResult pr) => CreateHandler.CreateAsync(
            pr.GetValue(accArg)!, pr.GetValue(charArg)!, pr.GetValue(pwArg)!, pr.GetValue(createPort)));

        var @new = new Command("new", "Create a new game folder with template classes, then start the server");
        var folderArg = new Argument<string>("foldername") { Description = "Game folder to create" };
        var newPort = PortOption(); var newHost = HostOption(); var newTelnet = TelnetPortOption();
        var overwrite = new Option<bool>("--overwrite", "--force") { Description = "Overwrite an existing folder" };
        var newFg = ForegroundOption();
        @new.Arguments.Add(folderArg);
        @new.Options.Add(newPort); @new.Options.Add(newHost); @new.Options.Add(newTelnet); @new.Options.Add(overwrite); @new.Options.Add(newFg);
        @new.SetAction((ParseResult pr) => NewHandler.NewAsync(
            pr.GetValue(folderArg)!, pr.GetValue(newPort), pr.GetValue(newHost),
            TelnetPortOrEnv(pr.GetValue(newTelnet)), pr.GetValue(overwrite), pr.GetValue(newFg)));

        var test = new Command("test", "Run tests. Runs game tests by default, or core tests with 'test core'.");
        var testArgs = new Argument<string[]>("args") { Arity = ArgumentArity.ZeroOrMore, Description = "Passed to dotnet test" };
        test.Arguments.Add(testArgs);
        test.SetAction((ParseResult pr) => Task.FromResult(TestHandler.HandleTest(pr.GetValue(testArgs) ?? [])));

        root.Subcommands.Add(start); root.Subcommands.Add(stop); root.Subcommands.Add(restart);
        root.Subcommands.Add(reload); root.Subcommands.Add(reset); root.Subcommands.Add(create);
        root.Subcommands.Add(@new); root.Subcommands.Add(test);
        return root;
    }

    public static Task<int> InvokeAsync(string[] args)
    {
        if (args.Length == 0) args = ["--help"];
        return Build().Parse(NormalizeArgs(args)).InvokeAsync();
    }
}
