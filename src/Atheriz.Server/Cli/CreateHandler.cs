
namespace Atheriz.Server.Cli;

public static class CreateHandler
{
    public static async Task HandleCreateAsync(string[] a)
    {
        var settings = StopHandler.EffectiveSettingsValue;
        var port = ArgumentParser.ParsePort(a);
        var filtered = ArgumentParser.StripPortOptions(a);
        if (filtered.Length < 3)
        {
            Console.Error.WriteLine("Usage: atheriz create <accountname> <charactername> <password> [--port N]");
            Console.Error.WriteLine("atheriz: error: the following arguments are required: accountname, charactername, password");
            throw new CliExitException(2);
        }
        var accName = filtered[0]; var charName = filtered[1]; var pw = filtered[2];
        var portVal = port ?? settings.WebserverPort;
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
                if (status == "ok" || status == "error") return;
            }
            catch { Console.WriteLine(resp.Body); return; }
        }
        Console.WriteLine("No running server detected; creating directly against the database.");
        Console.WriteLine("Loading existing data...");
        var savePath = settings.SavePath;
        // A null admin response is "unreachable", not "not running". Refuse the
        // offline direct-DB writes when the target world's pid file names a
        // live verified server — same probe as `new --overwrite`.
        try
        {
            var pidFile = Path.Combine(savePath, "server.pid");
            if (Infrastructure.PidFile.IsLiveClaim(pidFile, out int ownerPid))
            {
                Console.WriteLine($"A live server owns this world (verified server.pid {ownerPid}); stop it first instead of offline create.");
                return;
            }
        }
        catch { }
        try { Atheriz.Core.Utils.PathGuards.GuardSavePath(savePath); } catch (Exception ex) { Console.WriteLine(ex.Message); return; }
        Directory.CreateDirectory(savePath);
        try
        {
            using var db = new Atheriz.Core.Persistence.AtherizDbContext(savePath);
            db.Database.EnsureCreated();
            Atheriz.Core.Globals.ObjectRegistry.LoadObjects(savePath);
        }
        catch (Exception ex) { Console.WriteLine($"Load failed: {ex.Message}"); return; }
        // Port of atheriz.py:1456-1459 offline path: at_char_create against the database.
        ServerEvents.AtCharCreate(accName, charName, pw);
    }
}
