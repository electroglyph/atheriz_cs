namespace Atheriz.Server.Cli;
public static class RestartHandler
{
    public static Task HandleRestartAsync(string[] a) => StopHandler.HandleRestartAsync(a);
}
