
namespace Atheriz.Server.Cli;

public static class CreateHandler
{
    public static async Task<int> CreateAsync(string accName, string charName, string pw, int? portOverride)
    {
        var settings = StopHandler.EffectiveSettingsValue;
        var portVal = portOverride ?? settings.WebserverPort;
        var tlsOn = !string.IsNullOrEmpty(settings.SslCertFile);
        var payload = JsonSerializer.Serialize(new { account_name = accName, char_name = charName, password = pw });
        // Any HTTP answer — even {status:"error"} — proves a live server owns
        // this world: print its message and return. The offline DB path runs
        // only when no token exists or the server cannot be reached, never
        // against a running server.
        var resp = await ShutdownClient.PostAdminWithTlsFallbackAsync(portVal, settings.SecretPath, "/_internal/create_account", payload, tlsOn).ConfigureAwait(false);
        if (resp is not null)
        {
            try
            {
                var status = resp.GetStatus("error");
                var msg = resp.GetMessage();
                Console.WriteLine(msg);
                // A live server answered: its verdict is the exit code
                // (ok->0, anything else->1), mirroring reload.
                return status == "ok" ? 0 : 1;
            }
            catch { Console.WriteLine(resp.Body); return 1; }
        }
        Console.WriteLine("No running server detected; creating directly against the database.");
        Console.WriteLine("Loading existing data...");
        var savePath = settings.SavePath;
        // A null admin response is "unreachable", not "not running". Refuse
        // the offline direct-DB writes when the target world's pid file
        // names a live verified server.
        try
        {
            var pidFile = Path.Combine(savePath, "server.pid");
            if (Infrastructure.PidFile.IsLiveClaim(pidFile, out int ownerPid))
            {
                Console.WriteLine($"A live server owns this world (verified server.pid {ownerPid}); stop it first instead of offline create.");
                return 1;
            }
        }
        catch { }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return 1; }
        Directory.CreateDirectory(savePath);
        try
        {
            using var db = new Atheriz.Core.Persistence.AtherizDbContext(savePath);
            db.Database.EnsureCreated();
            Atheriz.Core.Globals.ObjectRegistry.LoadObjects(savePath);
        }
        catch (Exception ex) { Console.WriteLine($"Load failed: {ex.Message}"); return 1; }
        ServerEvents.AtCharCreate(accName, charName, pw);
        return 0;
    }
}
