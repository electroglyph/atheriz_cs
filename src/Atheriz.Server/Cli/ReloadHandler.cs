namespace Atheriz.Server.Cli;
public static class ReloadHandler
{
    public static Task HandleReloadAsync(string[] a) => StopHandler.HandleReloadAsync(a);
}
